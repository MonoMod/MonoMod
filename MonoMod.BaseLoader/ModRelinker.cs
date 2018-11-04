using Mono.Cecil;
using MonoMod;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MonoMod.BaseLoader {
    public static class Relinker {

        /// <summary>
        /// The hasher used by Relinker.
        /// </summary>
        public static readonly HashAlgorithm ChecksumHasher = MD5.Create();

        /// <summary>
        /// The current entry assembly checksum.
        /// </summary>
        public static string GameChecksum { get; internal set; }

        internal static readonly Dictionary<string, ModuleDefinition> StaticRelinkModuleCache;
        internal static ModuleDefinition RuntimeRuleContainer;

        static Relinker() {
            Assembly entry = Assembly.GetEntryAssembly();

            StaticRelinkModuleCache = new Dictionary<string, ModuleDefinition>() {
                { "MonoMod", ModuleDefinition.ReadModule(typeof(MonoModder).Assembly.Location, new ReaderParameters(ReadingMode.Immediate)) },
                { entry.GetName().Name, ModuleDefinition.ReadModule(entry.Location, new ReaderParameters(ReadingMode.Immediate)) }
            };

            string mod = Path.Combine(
                Path.GetDirectoryName(entry.Location),
                Path.GetFileNameWithoutExtension(entry.Location) + ".Mod.mm.dll"
            );
            if (File.Exists(mod)) {
                RuntimeRuleContainer = ModuleDefinition.ReadModule(mod, new ReaderParameters(ReadingMode.Immediate));
            }
        }

        private static Dictionary<string, ModuleDefinition> _SharedRelinkModuleMap;
        public static Dictionary<string, ModuleDefinition> SharedRelinkModuleMap {
            get {
                if (_SharedRelinkModuleMap != null)
                    return _SharedRelinkModuleMap;

                _SharedRelinkModuleMap = new Dictionary<string, ModuleDefinition>();
                string[] entries = Directory.GetFiles(ModManager.PathGame);
                for (int i = 0; i < entries.Length; i++) {
                    string path = entries[i];
                    string name = Path.GetFileName(path);
                    string nameNeutral = name.Substring(0, Math.Max(0, name.Length - 4));
                    if (name.EndsWith(".mm.dll")) {
                        if (name.StartsWith("Celeste."))
                            _SharedRelinkModuleMap[nameNeutral] = StaticRelinkModuleCache["Celeste"];
                        else {
                            Logger.Log(LogLevel.Warn, "relinker", $"Found unknown {name}");
                            int dot = name.IndexOf('.');
                            if (dot < 0)
                                continue;
                            string nameRelinkedNeutral = name.Substring(0, dot);
                            string nameRelinked = nameRelinkedNeutral + ".dll";
                            string pathRelinked = Path.Combine(Path.GetDirectoryName(path), nameRelinked);
                            if (!File.Exists(pathRelinked))
                                continue;
                            ModuleDefinition relinked;
                            if (!StaticRelinkModuleCache.TryGetValue(nameRelinkedNeutral, out relinked)) {
                                relinked = ModuleDefinition.ReadModule(pathRelinked, new ReaderParameters(ReadingMode.Immediate));
                                StaticRelinkModuleCache[nameRelinkedNeutral] = relinked;
                            }
                            Logger.Log(LogLevel.Verbose, "relinker", $"Remapped to {nameRelinked}");
                            _SharedRelinkModuleMap[nameNeutral] = relinked;
                        }
                    }
                }
                return _SharedRelinkModuleMap;
            }
        }

        private static Dictionary<string, object> _SharedRelinkMap;
        public static Dictionary<string, object> SharedRelinkMap {
            get {
                if (_SharedRelinkMap != null)
                    return _SharedRelinkMap;

                _SharedRelinkMap = new Dictionary<string, object>();


                return _SharedRelinkMap;
            }
        }

        private static MonoModder _Modder;
        public static MonoModder Modder {
            get {
                if (_Modder != null)
                    return _Modder;

                _Modder = new MonoModder() {
                    CleanupEnabled = false,
                    RelinkModuleMap = SharedRelinkModuleMap,
                    RelinkMap = SharedRelinkMap,
                    DependencyDirs = {
                        ModManager.PathGame
                    },
                    ReaderParameters = {
                        SymbolReaderProvider = new RelinkerSymbolReaderProvider()
                    }
                };

                return _Modder;
            }
            set {
                _Modder = value;
            }
        }

        /// <summary>
        /// Relink a .dll to point towards the game's assembly at runtime, then load it.
        /// </summary>
        /// <param name="meta">The mod metadata, used for caching, among other things.</param>
        /// <param name="stream">The stream to read the .dll from.</param>
        /// <param name="depResolver">An optional dependency resolver.</param>
        /// <param name="checksumsExtra">Any optional checksums. If you're running this at runtime, pass at least Relinker.GetChecksum(Metadata)</param>
        /// <param name="prePatch">An optional step executed before patching, but after MonoMod has loaded the input assembly.</param>
        /// <returns>The loaded, relinked assembly.</returns>
        public static Assembly GetRelinkedAssembly(ModMetadata meta, Stream stream,
            MissingDependencyResolver depResolver = null, string[] checksumsExtra = null, Action<MonoModder> prePatch = null) {
            string cachedPath = GetCachedPath(meta);
            string cachedChecksumPath = cachedPath.Substring(0, cachedPath.Length - 4) + ".sum";

            string[] checksums = new string[2 + (checksumsExtra?.Length ?? 0)];
            if (GameChecksum == null)
                GameChecksum = GetChecksum(Assembly.GetAssembly(typeof(Utils.Relinker)).Location);
            checksums[0] = GameChecksum;

            checksums[1] = GetChecksum(meta);

            if (checksumsExtra != null)
                for (int i = 0; i < checksumsExtra.Length; i++) {
                    checksums[i + 2] = checksumsExtra[i];
                }

            if (File.Exists(cachedPath) && File.Exists(cachedChecksumPath) &&
                ChecksumsEqual(checksums, File.ReadAllLines(cachedChecksumPath))) {
                Logger.Log(LogLevel.Verbose, "relinker", $"Loading cached assembly for {meta}");
                try {
                    return Assembly.LoadFrom(cachedPath);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, "relinker", $"Failed loading {meta}");
                    e.LogDetailed();
                    return null;
                }
            }

            if (depResolver == null)
                depResolver = GenerateModDependencyResolver(meta);

            try {
                MonoModder modder = Modder;

                modder.Input = stream;
                modder.OutputPath = cachedPath;
                modder.MissingDependencyResolver = depResolver;

                string symbolPath;
                modder.ReaderParameters.SymbolStream = meta.OpenStream(out symbolPath, meta.DLL.Substring(0, meta.DLL.Length - 4) + ".pdb", meta.DLL + ".mdb");
                modder.ReaderParameters.ReadSymbols = modder.ReaderParameters.SymbolStream != null;
                if (modder.ReaderParameters.SymbolReaderProvider != null &&
                    modder.ReaderParameters.SymbolReaderProvider is RelinkerSymbolReaderProvider) {
                    ((RelinkerSymbolReaderProvider) modder.ReaderParameters.SymbolReaderProvider).Format =
                        string.IsNullOrEmpty(symbolPath) ? DebugSymbolFormat.Auto :
                        symbolPath.EndsWith(".mdb") ? DebugSymbolFormat.MDB :
                        symbolPath.EndsWith(".pdb") ? DebugSymbolFormat.PDB :
                        DebugSymbolFormat.Auto;
                }

                modder.Read();

                modder.ReaderParameters.ReadSymbols = false;

                if (modder.ReaderParameters.SymbolReaderProvider != null &&
                    modder.ReaderParameters.SymbolReaderProvider is RelinkerSymbolReaderProvider) {
                    ((RelinkerSymbolReaderProvider) modder.ReaderParameters.SymbolReaderProvider).Format = DebugSymbolFormat.Auto;
                }

                modder.MapDependencies();

                if (RuntimeRuleContainer != null) {
                    modder.ParseRules(RuntimeRuleContainer);
                    RuntimeRuleContainer = null;
                }

                prePatch?.Invoke(modder);

                modder.AutoPatch();

                modder.Write();
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "relinker", $"Failed relinking {meta}");
                e.LogDetailed();
                return null;
            } finally {
                Modder.ClearCaches(moduleSpecific: true);
#if !LEGACY
                Modder.Module.Dispose();
#endif
                Modder.Module = null;
                Modder.ReaderParameters.SymbolStream?.Dispose();
            }

            if (File.Exists(cachedChecksumPath)) {
                File.Delete(cachedChecksumPath);
            }
            File.WriteAllLines(cachedChecksumPath, checksums);

            Logger.Log(LogLevel.Verbose, "relinker", $"Loading assembly for {meta}");
            try {
                return Assembly.LoadFrom(cachedPath);
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "relinker", $"Failed loading {meta}");
                e.LogDetailed();
                return null;
            }
        }


        private static MissingDependencyResolver GenerateModDependencyResolver(ModMetadata meta)
            => (mod, main, name, fullName) => {
                string result;
                Stream stream = meta.OpenStream(out result, name + ".dll");
                if (stream == null)
                    return null;
                using (stream) {
                    return ModuleDefinition.ReadModule(stream, mod.GenReaderParameters(false));
                }
            };

        /// <summary>
        /// Get the cached path of a given mod's relinked .dll
        /// </summary>
        /// <param name="meta">The mod metadata.</param>
        /// <returns>The full path to the cached relinked .dll</returns>
        public static string GetCachedPath(ModMetadata meta)
            => Path.Combine(ModLoader.PathCache, meta.Name + "." + Path.GetFileNameWithoutExtension(meta.DLL) + ".dll");

        /// <summary>
        /// Get the checksum for a given mod's .dll or the containing container
        /// </summary>
        /// <param name="meta">The mod metadata.</param>
        /// <returns>A checksum to be used with other Relinker methods.</returns>
        public static string GetChecksum(ModMetadata meta) {
            string path = meta.DLL;
            if (!File.Exists(path))
                path = meta.Container;
            return GetChecksum(path);
        }
        /// <summary>
        /// Get the checksum for a given path.
        /// </summary>
        /// <param name="path">The filepath.</param>
        /// <returns>A checksum to be used with other Relinker methods.</returns>
        public static string GetChecksum(string path) {
            using (FileStream fs = File.OpenRead(path))
                return ChecksumHasher.ComputeHash(fs).ToHexadecimalString();
        }

        /// <summary>
        /// Determine if both checksum collections are equal.
        /// </summary>
        /// <param name="a">The first checksum array.</param>
        /// <param name="b">The second checksum array.</param>
        /// <returns>True if the contents of both arrays match, false otherwise.</returns>
        public static bool ChecksumsEqual(string[] a, string[] b) {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Trim() != b[i].Trim())
                    return false;
            return true;
        }

    }
}
