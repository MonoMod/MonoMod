using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.BaseLoader {
    public static class ModLoader {

        /// <summary>
        /// The path to the /Mods directory.
        /// </summary>
        public static string PathMods;
        /// <summary>
        /// The path to the /Mods/Cache directory.
        /// </summary>
        public static string PathCache;

        /// <summary>
        /// The path to the /Mods/blacklist.txt file.
        /// </summary>
        public static string PathBlacklist;
        internal static List<string> _Blacklist = new List<string>();
        /// <summary>
        /// The currently loaded mod blacklist.
        /// </summary>
        public static ReadOnlyCollection<string> Blacklist => _Blacklist.AsReadOnly();

        internal struct DelayedEntry { public ModMetadata Meta; public Action Callback; }
        internal static List<DelayedEntry> Delayed = new List<DelayedEntry>();
        internal static int DelayedLock;

        internal static void LoadAuto() {
            if (string.IsNullOrEmpty(PathMods))
                PathMods = Path.Combine(ModManager.PathGame, "Mods");
            Directory.CreateDirectory(PathMods);

            if (string.IsNullOrEmpty(PathCache))
                PathCache = Path.Combine(PathMods, "Cache");
            Directory.CreateDirectory(PathCache);

            if (string.IsNullOrEmpty(PathBlacklist))
                PathBlacklist = Path.Combine(PathMods, "blacklist.txt");
            if (File.Exists(PathBlacklist)) {
                _Blacklist = File.ReadAllLines(PathBlacklist).Select(l => (l.StartsWith("#") ? "" : l).Trim()).ToList();
            } else {
                using (StreamWriter writer = File.CreateText(PathBlacklist)) {
                    writer.WriteLine("# This is the blacklist. Lines starting with # are ignored.");
                    writer.WriteLine("ExampleFolder");
                    writer.WriteLine("SomeMod.zip");
                }
            }

            string[] files = Directory.GetFiles(PathMods);
            if (LoadFile != null) {
                for (int i = 0; i < files.Length; i++) {
                    string file = Path.GetFileName(files[i]);
                    if (_Blacklist.Contains(file))
                        continue;
                    LoadFile(file);
                }
            }
            files = Directory.GetDirectories(PathMods);
            for (int i = 0; i < files.Length; i++) {
                string file = Path.GetFileName(files[i]);
                if (file == "Cache" || _Blacklist.Contains(file))
                    continue;
                (LoadDir ?? DefaultLoadDir)(file);
            }
        }

        public static Action<string> LoadFile;
        public static Action<string> LoadDir;

        /// <summary>
        /// Load a mod from a directory at runtime.
        /// </summary>
        /// <param name="dir">The path to the mod directory.</param>
        public static void DefaultLoadDir(string dir) {
            if (!Directory.Exists(dir)) // Relative path?
                dir = Path.Combine(PathMods, dir);
            if (!Directory.Exists(dir)) // It just doesn't exist.
                return;

            Logger.Log(LogLevel.Verbose, "loader", $"Loading mod directory: {dir}");

            ModMetadata meta = null;
            ModMetadata[] multimetas = null;

            string metaPath = Path.Combine(dir, "metadata.yaml");
            if (File.Exists(metaPath))
                using (StreamReader reader = new StreamReader(metaPath)) {
                    try {
                        meta = YamlHelper.Deserializer.Deserialize<DirectoryModMetadata>(reader);
                        meta.Container = dir;
                        meta.PostParse();
                    } catch (Exception e) {
                        Logger.Log(LogLevel.Warn, "loader", $"Failed parsing metadata.yaml in {dir}: {e}");
                    }
                }

            metaPath = Path.Combine(dir, "multimetadata.yaml");
            if (File.Exists(metaPath))
                using (StreamReader reader = new StreamReader(metaPath)) {
                    try {
                        multimetas = YamlHelper.Deserializer.Deserialize<DirectoryModMetadata[]>(reader);
                        foreach (ModMetadata multimeta in multimetas) {
                            multimeta.Container = dir;
                            multimeta.PostParse();
                        }
                    } catch (Exception e) {
                        Logger.Log(LogLevel.Warn, "loader", $"Failed parsing multimetadata.yaml in {dir}: {e}");
                    }
                }

            ModContentSource contentMeta = new DirectoryModContent(dir);

            Action contentCrawl = () => {
                if (contentMeta == null)
                    return;
                ModContentManager.Crawl(contentMeta);
                contentMeta = null;
            };

            if (multimetas != null) {
                foreach (ModMetadata multimeta in multimetas) {
                    LoadModDelayed(multimeta, contentCrawl);
                }
            }

            LoadModDelayed(meta, contentCrawl);
        }

        /// <summary>
        /// Load a mod .dll given its metadata at runtime. Doesn't load the mod content.
        /// If required, loads the mod after all of its dependencies have been loaded.
        /// </summary>
        /// <param name="meta">Metadata of the mod to load.</param>
        /// <param name="callback">Callback to be executed after the mod has been loaded. Executed immediately if meta == null.</param>
        public static void LoadModDelayed(ModMetadata meta, Action callback) {
            if (meta == null) {
                callback?.Invoke();
                return;
            }

            foreach (ModMetadata dep in meta.Dependencies)
                if (!DependencyLoaded(dep)) {
                    Logger.Log(LogLevel.Info, "loader", $"Dependency {dep} of mod {meta} not loaded! Delaying.");
                    Delayed.Add(new DelayedEntry { Meta = meta, Callback = callback });
                    return;
                }

            LoadMod(meta);

            callback?.Invoke();
        }

        /// <summary>
        /// Load a mod .dll given its metadata at runtime. Doesn't load the mod content.
        /// </summary>
        /// <param name="meta">Metadata of the mod to load.</param>
        public static void LoadMod(ModMetadata meta) {
            if (meta == null)
                return;

            // Add an AssemblyResolve handler for all bundled libraries.
            AppDomain.CurrentDomain.AssemblyResolve += GenerateModAssemblyResolver(meta);

            // Load the actual assembly.
            Assembly asm = null;
            if (!string.IsNullOrEmpty(meta.DLL)) {
                if (meta.Prelinked && File.Exists(meta.DLL)) {
                    asm = Assembly.LoadFrom(meta.DLL);

                } else if (File.Exists(meta.DLL)) {
                    using (FileStream stream = File.OpenRead(meta.DLL))
                        asm = Relinker.GetRelinkedAssembly(meta, stream);

                } else {
                    string result;
                    Stream stream = meta.OpenStream(out result, meta.DLL);
                    if (stream != null) {
                        using (stream) {
                            if (meta.Prelinked) {
                                if (stream is MemoryStream) {
                                    asm = Assembly.Load(((MemoryStream) stream).GetBuffer());
                                } else {
                                    using (MemoryStream ms = new MemoryStream()) {
                                        byte[] buffer = new byte[2048];
                                        int read;
                                        while (0 < (read = stream.Read(buffer, 0, buffer.Length))) {
                                            ms.Write(buffer, 0, read);
                                        }
                                        asm = Assembly.Load(ms.ToArray());
                                    }
                                }
                            } else {
                                asm = Relinker.GetRelinkedAssembly(meta, stream);
                            }
                        }
                    } else {
                        throw new DllNotFoundException($"Cannot find DLL {meta.DLL} in mod {meta}");
                    }
                }
            }

            if (asm != null)
                LoadModAssembly(meta, asm);
        }

        /// <summary>
        /// Find and load all ModBases in the given assembly.
        /// </summary>
        /// <param name="meta">The mod metadata, preferably from the mod metadata.yaml file.</param>
        /// <param name="asm">The mod assembly, preferably relinked.</param>
        public static void LoadModAssembly(ModMetadata meta, Assembly asm) {
            if (meta != null)
                ModContentManager.Crawl(new AssemblyModContent(asm));

            Type[] types;
            try {
                types = asm.GetTypes();
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "loader", $"Failed reading assembly: {e}");
                e.LogDetailed();
                return;
            }
            for (int i = 0; i < types.Length; i++) {
                Type type = types[i];
                if (!typeof(ModBase).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                ModBase mod = (ModBase) type.GetConstructor(ModManager._EmptyTypeArray).Invoke(ModManager._EmptyObjectArray);
                if (meta != null)
                    mod.Metadata = meta;
                mod.Register();
            }
        }

        /// <summary>
        /// Checks if all dependencies are loaded.
        /// Can be used by mods manually to f.e. activate / disable functionality.
        /// </summary>
        /// <param name="meta">The metadata of the mod listing the dependencies.</param>
        /// <returns>True if the dependencies have already been loaded, false otherwise.</returns>
        public static bool DependenciesLoaded(ModMetadata meta) {
            foreach (ModMetadata dep in meta.Dependencies)
                if (!DependencyLoaded(dep))
                    return false;
            return true;
        }

        /// <summary>
        /// Checks if an dependency is loaded.
        /// Can be used by mods manually to f.e. activate / disable functionality.
        /// </summary>
        /// <param name="dep">Dependency to check for. Name and Version will be checked.</param>
        /// <returns>True if the dependency has already been loaded, false otherwise.</returns>
        public static bool DependencyLoaded(ModMetadata dep) {
            string depName = dep.Name;
            Version depVersion = dep.Version;

            foreach (ModBase other in ModManager._Mods) {
                ModMetadata meta = other.Metadata;
                if (meta.Name != depName)
                    continue;
                Version version = meta.Version;

                // Special case: Always true if version == 0.0.*
                if (version.Major == 0 && version.Minor == 0)
                    return true;
                // Major version, breaking changes, must match.
                if (version.Major != depVersion.Major)
                    return false;
                // Minor version, non-breaking changes, installed can't be lower than what we depend on.
                if (version.Minor < depVersion.Minor)
                    return false;
                return true;
            }

            return false;
        }

        private static ResolveEventHandler GenerateModAssemblyResolver(ModMetadata meta)
            => (sender, args) => {
                return meta.OpenAssembly(new AssemblyName(args.Name).Name + ".dll");
            };

    }
}
