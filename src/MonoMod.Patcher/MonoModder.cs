using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MonoMod.Utils;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;
using MonoMod.Cil;
using System.Globalization;

#if CECIL0_9
using InterfaceImplementation = Mono.Cecil.TypeReference;
#endif

namespace MonoMod
{

    public delegate bool MethodParser(MonoModder modder, MethodBody body, Instruction instr, ref int instri);
    public delegate void MethodRewriter(MonoModder modder, MethodDefinition method);
    public delegate void MethodBodyRewriter(MonoModder modder, MethodBody body, Instruction instr, int instri);
    public delegate ModuleDefinition MissingDependencyResolver(MonoModder modder, ModuleDefinition main, string name, string fullName);
    public delegate void PostProcessor(MonoModder modder);
    public delegate void ModReadEventHandler(MonoModder modder, ModuleDefinition mod);

    public class RelinkMapEntry
    {
        public string Type;
        public string FindableID;

        public RelinkMapEntry()
        {
        }
        public RelinkMapEntry(string type, string findableID)
        {
            Type = type;
            FindableID = findableID;
        }
    }

    public enum DebugSymbolFormat
    {
        Auto,
        MDB,
        PDB
    }

    public class MonoModder : IDisposable
    {

        public static readonly Version Version = typeof(MonoModder).Assembly.GetName().Version;

        public Dictionary<string, object> SharedData = new Dictionary<string, object>();

        public Dictionary<string, object> RelinkMap = new Dictionary<string, object>();
        public Dictionary<string, ModuleDefinition> RelinkModuleMap = new Dictionary<string, ModuleDefinition>();
        public HashSet<string> SkipList = new HashSet<string>(EqualityComparer<string>.Default);

        public Dictionary<string, IMetadataTokenProvider> RelinkMapCache = new Dictionary<string, IMetadataTokenProvider>();
        public Dictionary<string, TypeReference> RelinkModuleMapCache = new Dictionary<string, TypeReference>();

        public Dictionary<string, OpCode> ForceCallMap = new Dictionary<string, OpCode>();

        public ModReadEventHandler OnReadMod;
        public PostProcessor PostProcessors;

        public Dictionary<string, Action<object, object[]>> CustomAttributeHandlers = new Dictionary<string, Action<object, object[]>>() {
            // Dummy handlers for modifiers which should be preserved until cleanup.
            { "MonoMod.MonoModPublic", (_1, _2) => {} }
        };
        public Dictionary<string, Action<object, object[]>> CustomMethodAttributeHandlers = new Dictionary<string, Action<object, object[]>>();

        public MissingDependencyResolver MissingDependencyResolver;

        public MethodParser MethodParser;
        public MethodRewriter MethodRewriter;
        public MethodBodyRewriter MethodBodyRewriter;

        public Stream Input;
        public string InputPath;
        public Stream Output;
        public string OutputPath;
        public List<string> DependencyDirs = new List<string>();
        public ModuleDefinition Module;

        public Dictionary<ModuleDefinition, List<ModuleDefinition>> DependencyMap = new Dictionary<ModuleDefinition, List<ModuleDefinition>>();
        public Dictionary<string, ModuleDefinition> DependencyCache = new Dictionary<string, ModuleDefinition>();
        public Dictionary<ModuleDefinition, Dictionary<string, TypeReference>> ForwardedTypeCache = new Dictionary<ModuleDefinition, Dictionary<string, TypeReference>>();

        public Func<ICustomAttributeProvider, TypeReference, bool> ShouldCleanupAttrib;

        public bool LogVerboseEnabled;
        public bool CleanupEnabled;
        public bool PublicEverything;

        public List<ModuleReference> Mods = new List<ModuleReference>();

        public bool Strict;
        public bool MissingDependencyThrow;
        public bool RemovePatchReferences;
        public bool PreventInline;
        public bool? UpgradeMSCORLIB;

        public ReadingMode ReadingMode = ReadingMode.Immediate;
        public DebugSymbolFormat DebugSymbolOutputFormat = DebugSymbolFormat.Auto;

        public int CurrentRID = 0;

        protected IAssemblyResolver _assemblyResolver;
        public virtual IAssemblyResolver AssemblyResolver
        {
            get
            {
                if (_assemblyResolver == null)
                {
                    var assemblyResolver = new DefaultAssemblyResolver();
                    foreach (var dir in DependencyDirs)
                        assemblyResolver.AddSearchDirectory(dir);
                    _assemblyResolver = assemblyResolver;
                }
                return _assemblyResolver;
            }
            set => _assemblyResolver = value;
        }

        protected ReaderParameters _readerParameters;
        public virtual ReaderParameters ReaderParameters
        {
            get
            {
                if (_readerParameters == null)
                {
                    _readerParameters = new ReaderParameters(ReadingMode)
                    {
                        AssemblyResolver = AssemblyResolver,
                        ReadSymbols = true
                    };
                }
                return _readerParameters;
            }
            set => _readerParameters = value;
        }

        protected WriterParameters _writerParameters;
        public virtual WriterParameters WriterParameters
        {
            get
            {
                if (_writerParameters == null)
                {
                    var pdb = DebugSymbolOutputFormat == DebugSymbolFormat.PDB;
                    var mdb = DebugSymbolOutputFormat == DebugSymbolFormat.MDB;
                    if (DebugSymbolOutputFormat == DebugSymbolFormat.Auto)
                    {
                        if (PlatformDetection.OS.Is(OSKind.Windows))
                            pdb = true;
                        else
                            mdb = true;
                    }
                    _writerParameters = new WriterParameters()
                    {
                        WriteSymbols = true,
                        SymbolWriterProvider =
#if !CECIL0_9
                            pdb ? new NativePdbWriterProvider() :
#else
                            pdb ? new PdbWriterProvider() :
#endif
                            mdb ? new MdbWriterProvider() :
                            (ISymbolWriterProvider)null
                    };
                }
                return _writerParameters;
            }
            set => _writerParameters = value;
        }

        public bool GACEnabled;

        private string[] _GACPathsNone = new string[0];
        protected string[] _GACPaths;
        public string[] GACPaths
        {
            get
            {
                // .NET Core doesn't have a GAC.
                // .NET Framework does have a GAC but only on Windows.
                // The GAC might still be relevant when patching a Framework assembly with Core.
                if (!GACEnabled)
                    return _GACPathsNone;

                if (_GACPaths != null)
                    return _GACPaths;


                if (PlatformDetection.Runtime is not RuntimeKind.Mono)
                {
                    // C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xml
                    var path = Environment.GetEnvironmentVariable("windir");
                    if (string.IsNullOrEmpty(path))
                        return _GACPaths = _GACPathsNone;

                    path = Path.Combine(path, "Microsoft.NET");
                    path = Path.Combine(path, "assembly");
                    _GACPaths = new string[] {
                        Path.Combine(path, "GAC_32"),
                        Path.Combine(path, "GAC_64"),
                        Path.Combine(path, "GAC_MSIL")
                    };

                }
                else
                {
                    var paths = new List<string>();
                    var gac = Path.Combine(
                        Path.GetDirectoryName(
                            Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName)
                        ),
                        "gac"
                    );
                    if (Directory.Exists(gac))
                        paths.Add(gac);

                    var prefixesEnv = Environment.GetEnvironmentVariable("MONO_GAC_PREFIX");
                    if (!string.IsNullOrEmpty(prefixesEnv))
                    {
                        var prefixes = prefixesEnv.Split(Path.PathSeparator);
                        foreach (var prefix in prefixes)
                        {
                            if (string.IsNullOrEmpty(prefix))
                                continue;

                            var path = prefix;
                            path = Path.Combine(path, "lib");
                            path = Path.Combine(path, "mono");
                            path = Path.Combine(path, "gac");
                            if (Directory.Exists(path) && !paths.Contains(path))
                                paths.Add(path);
                        }
                    }

                    _GACPaths = paths.ToArray();
                }

                return _GACPaths;
            }
            set
            {
                GACEnabled = true;
                _GACPaths = value;
            }
        }

        public MonoModder()
        {
            MethodParser = DefaultParser;

            MissingDependencyResolver = DefaultMissingDependencyResolver;

            PostProcessors += DefaultPostProcessor;

            var dependencyDirsEnv = Environment.GetEnvironmentVariable("MONOMOD_DEPDIRS");
            if (!string.IsNullOrEmpty(dependencyDirsEnv))
            {
                foreach (var dir in dependencyDirsEnv.Split(Path.PathSeparator).Select(dir => dir.Trim()))
                {
                    (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(dir);
                    DependencyDirs.Add(dir);
                }
            }
            LogVerboseEnabled = Environment.GetEnvironmentVariable("MONOMOD_LOG_VERBOSE") == "1";
            CleanupEnabled = Environment.GetEnvironmentVariable("MONOMOD_CLEANUP") != "0";
            PublicEverything = Environment.GetEnvironmentVariable("MONOMOD_PUBLIC_EVERYTHING") == "1";
            PreventInline = Environment.GetEnvironmentVariable("MONOMOD_PREVENTINLINE") == "1";
            Strict = Environment.GetEnvironmentVariable("MONOMOD_STRICT") == "1";
            MissingDependencyThrow = Environment.GetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW") != "0";
            RemovePatchReferences = Environment.GetEnvironmentVariable("MONOMOD_DEPENDENCY_REMOVE_PATCH") != "0";

            var envDebugSymbolFormat = Environment.GetEnvironmentVariable("MONOMOD_DEBUG_FORMAT");
            if (envDebugSymbolFormat != null)
            {
                envDebugSymbolFormat = envDebugSymbolFormat.ToLower(CultureInfo.InvariantCulture);
                if (envDebugSymbolFormat == "pdb")
                    DebugSymbolOutputFormat = DebugSymbolFormat.PDB;
                else if (envDebugSymbolFormat == "mdb")
                    DebugSymbolOutputFormat = DebugSymbolFormat.MDB;
            }

            var upgradeMSCORLIBStr = Environment.GetEnvironmentVariable("MONOMOD_MSCORLIB_UPGRADE");
            UpgradeMSCORLIB = string.IsNullOrEmpty(upgradeMSCORLIBStr) ? (bool?)null : (upgradeMSCORLIBStr != "0");

            GACEnabled =
#if NETFRAMEWORK
                Environment.GetEnvironmentVariable("MONOMOD_GAC_ENABLED") != "0";
#else
                Environment.GetEnvironmentVariable("MONOMOD_GAC_ENABLED") == "1";
#endif

            MonoModRulesManager.Register(this);
        }

        public virtual void ClearCaches(bool all = false, bool shareable = false, bool moduleSpecific = false)
        {
            if (all || shareable)
            {
#if !CECIL0_9
                foreach (KeyValuePair<string, ModuleDefinition> dep in DependencyCache)
                    dep.Value.Dispose();
#endif
                DependencyCache.Clear();
                ForwardedTypeCache.Clear();
            }

            if (all || moduleSpecific)
            {
                RelinkMapCache.Clear();
                RelinkModuleMapCache.Clear();
            }
        }

        public virtual void Dispose()
        {
            ClearCaches(all: true);

#if !CECIL0_9
            Module?.Dispose();
#endif
            Module = null;

#if !CECIL0_9
            AssemblyResolver?.Dispose();
#endif
            AssemblyResolver = null;

#if !CECIL0_9
            foreach (ModuleDefinition mod in Mods)
                mod?.Dispose();

            foreach (List<ModuleDefinition> dependencies in DependencyMap.Values)
                foreach (ModuleDefinition dep in dependencies)
                    dep?.Dispose();
#endif
            DependencyMap.Clear();

            Input?.Dispose();
            Output?.Dispose();
        }

        public virtual void Log(object value)
        {
            Log(value.ToString());
        }
        public virtual void Log(string text)
        {
            Console.Write("[MonoMod] ");
            Console.WriteLine(text);
        }

        public virtual void LogVerbose(object value)
        {
            if (!LogVerboseEnabled)
                return;
            Log(value);
        }
        public virtual void LogVerbose(string text)
        {
            if (!LogVerboseEnabled)
                return;
            Log(text);
        }

        private static ModuleDefinition _ReadModule(Stream input, ReaderParameters args)
        {
            if (args.ReadSymbols)
            {
                try
                {
                    return ModuleDefinition.ReadModule(input, args);
                }
                catch
                {
                    args.ReadSymbols = false;
                    input.Seek(0, SeekOrigin.Begin);
                }
            }
            return ModuleDefinition.ReadModule(input, args);
        }

        private static ModuleDefinition _ReadModule(string input, ReaderParameters args)
        {
            if (args.ReadSymbols)
            {
                try
                {
                    return ModuleDefinition.ReadModule(input, args);
                }
                catch
                {
                    args.ReadSymbols = false;
                }
            }
            return ModuleDefinition.ReadModule(input, args);
        }

        /// <summary>
        /// Reads the main module from the Input stream / InputPath file to Module.
        /// </summary>
        public virtual void Read()
        {
            if (Module == null)
            {
                if (Input != null)
                {
                    Log("Reading input stream into module.");
                    Module = _ReadModule(Input, GenReaderParameters(true));
                }
                else if (InputPath != null)
                {
                    Log("Reading input file into module.");
                    (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(Path.GetDirectoryName(InputPath));
                    DependencyDirs.Add(Path.GetDirectoryName(InputPath));
                    Module = _ReadModule(InputPath, GenReaderParameters(true, InputPath));
                }

                var modsEnv = Environment.GetEnvironmentVariable("MONOMOD_MODS");
                if (!string.IsNullOrEmpty(modsEnv))
                {
                    foreach (var path in modsEnv.Split(Path.PathSeparator).Select(path => path.Trim()))
                    {
                        ReadMod(path);
                    }
                }
            }
        }

        public virtual void MapDependencies()
        {
            foreach (ModuleDefinition mod in Mods)
                MapDependencies(mod);
            MapDependencies(Module);
        }
        public virtual void MapDependencies(ModuleDefinition main)
        {
            if (DependencyMap.ContainsKey(main))
                return;
            DependencyMap[main] = new List<ModuleDefinition>();

            foreach (AssemblyNameReference dep in main.AssemblyReferences)
                MapDependency(main, dep);
        }
        public virtual void MapDependency(ModuleDefinition main, AssemblyNameReference depRef)
        {
            MapDependency(main, depRef.Name, depRef.FullName, depRef);
        }
        public virtual void MapDependency(ModuleDefinition main, string name, string fullName = null, AssemblyNameReference depRef = null)
        {
            if (!DependencyMap.TryGetValue(main, out List<ModuleDefinition> mapped))
                DependencyMap[main] = mapped = new List<ModuleDefinition>();

            if (!main.Name.StartsWith("System") && name.StartsWith("System.Private"))
                return;

            if (fullName != null && (
                DependencyCache.TryGetValue(fullName, out ModuleDefinition dep) ||
                DependencyCache.TryGetValue(fullName + " [RT:" + main.RuntimeVersion + "]", out dep)
            ))
            {
                LogVerbose($"[MapDependency] {main.Name} -> {dep.Name} (({fullName}), ({name})) from cache");
                mapped.Add(dep);
                MapDependencies(dep);
                return;
            }

            if (DependencyCache.TryGetValue(name, out dep) ||
                DependencyCache.TryGetValue(name + " [RT:" + main.RuntimeVersion + "]", out dep)
            )
            {
                LogVerbose($"[MapDependency] {main.Name} -> {dep.Name} ({name}) from cache");
                mapped.Add(dep);
                MapDependencies(dep);
                return;
            }

            // Used to fix Mono.Cecil.pdb being loaded instead of Mono.Cecil.Pdb.dll
            var ext = Path.GetExtension(name).ToLower(CultureInfo.InvariantCulture);
            var nameRisky = ext == "pdb" || ext == "mdb";

            string path = null;
            foreach (var depDir in DependencyDirs)
            {
                path = Path.Combine(depDir, name + ".dll");
                if (!File.Exists(path))
                    path = Path.Combine(depDir, name + ".exe");
                if (!File.Exists(path) && !nameRisky)
                    path = Path.Combine(depDir, name);
                if (File.Exists(path)) break;
                else path = null;
            }

            // If we've got an AssemblyNameReference, use it to resolve the module.
            if (path == null && depRef != null)
            {
                try
                {
                    dep = AssemblyResolver.Resolve(depRef)?.MainModule;
                }
                catch { }
                if (dep != null)
#if !CECIL0_9
                    path = dep.FileName;
#else
                    path = dep.FullyQualifiedName;
#endif
            }

            // Manually check in GAC
            if (path == null)
            {
                foreach (var gacpath in GACPaths)
                {
                    path = Path.Combine(gacpath, name);

                    if (Directory.Exists(path))
                    {
                        var versions = Directory.GetDirectories(path);
                        var highest = 0;
                        var highestIndex = 0;
                        for (var i = 0; i < versions.Length; i++)
                        {
                            var versionPath = versions[i];
                            if (versionPath.StartsWith(path, StringComparison.Ordinal))
                                versionPath = versionPath.Substring(path.Length + 1);
                            Match versionMatch = Regex.Match(versionPath, "\\d+");
                            if (!versionMatch.Success)
                            {
                                continue;
                            }
                            var version = int.Parse(versionMatch.Value, CultureInfo.InvariantCulture);
                            if (version > highest)
                            {
                                highest = version;
                                highestIndex = i;
                            }
                            // Maybe check minor versions?
                        }
                        path = Path.Combine(versions[highestIndex], name + ".dll");
                        break;
                    }
                    else
                    {
                        path = null;
                    }
                }
            }

            // Try to use the AssemblyResolver with the full name (or even partial name).
            if (path == null)
            {
                try
                {
                    dep = AssemblyResolver.Resolve(AssemblyNameReference.Parse(fullName ?? name))?.MainModule;
                }
                catch { }
                if (dep != null)
#if !CECIL0_9
                    path = dep.FileName;
#else
                    path = dep.FullyQualifiedName;
#endif
            }

            if (dep == null)
            {
                if (path != null && File.Exists(path))
                {
                    dep = _ReadModule(path, GenReaderParameters(false, path));
                }
                else if ((dep = MissingDependencyResolver?.Invoke(this, main, name, fullName)) == null)
                {
                    return;
                }
            }

            LogVerbose($"[MapDependency] {main.Name} -> {dep.Name} (({fullName}), ({name})) loaded");
            mapped.Add(dep);
            if (fullName == null)
                fullName = dep.Assembly.FullName;
            DependencyCache[fullName] = dep;
            DependencyCache[name] = dep;
            MapDependencies(dep);
        }
        public virtual ModuleDefinition DefaultMissingDependencyResolver(MonoModder mod, ModuleDefinition main, string name, string fullName)
        {
            if (MissingDependencyThrow && Environment.GetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW") == "0")
            {
                Log("[MissingDependencyResolver] [WARNING] Use MMILRT.Modder.MissingDependencyThrow instead of setting the env var MONOMOD_DEPENDENCY_MISSING_THROW");
                MissingDependencyThrow = false;
            }
            if (MissingDependencyThrow ||
                Strict
            )
                throw new RelinkTargetNotFoundException($"MonoMod cannot map dependency {main.Name} -> (({fullName}), ({name})) - not found", main);
            return null;
        }

        /// <summary>
        /// Write the modded module to the given stream or the default output.
        /// </summary>
        /// <param name="output">Output stream. If none given, outputPath or default Output will be used.</param>
        /// <param name="outputPath">Output path. If none given, output or default OutputPath will be used.</param>
        public virtual void Write(Stream output = null, string outputPath = null)
        {
            output = output ?? Output;
            outputPath = outputPath ?? OutputPath;

            PatchRefsInType(PatchWasHere());

            if (output != null)
            {
                Log("[Write] Writing modded module into output stream.");
                Module.Write(output, WriterParameters);
            }
            else
            {
                Log("[Write] Writing modded module into output file.");
                Module.Write(outputPath, WriterParameters);
            }
        }

        public virtual ReaderParameters GenReaderParameters(bool mainModule, string path = null)
        {
            ReaderParameters _rp = ReaderParameters;
            var rp = new ReaderParameters(_rp.ReadingMode);
            rp.AssemblyResolver = _rp.AssemblyResolver;
            rp.MetadataResolver = _rp.MetadataResolver;
#if !CECIL0_9
            rp.InMemory = _rp.InMemory;
            rp.MetadataImporterProvider = _rp.MetadataImporterProvider;
            rp.ReflectionImporterProvider = _rp.ReflectionImporterProvider;
            rp.ThrowIfSymbolsAreNotMatching = _rp.ThrowIfSymbolsAreNotMatching;
            rp.ApplyWindowsRuntimeProjections = _rp.ApplyWindowsRuntimeProjections;
#endif
            rp.SymbolStream = _rp.SymbolStream;
            rp.SymbolReaderProvider = _rp.SymbolReaderProvider;
            rp.ReadSymbols = _rp.ReadSymbols;

            if (path != null && !File.Exists(path + ".mdb") && !File.Exists(Path.ChangeExtension(path, "pdb")))
                rp.ReadSymbols = false;

            return rp;
        }


        public virtual void ReadMod(string path)
        {
            if (Directory.Exists(path))
            {
                Log($"[ReadMod] Loading mod dir: {path}");
                var mainName = Module.Name.Substring(0, Module.Name.Length - 3);
                var mainNameSpaceless = mainName.Replace(" ", "", StringComparison.Ordinal);
                if (!DependencyDirs.Contains(path))
                {
                    (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(path);
                    DependencyDirs.Add(path);
                }
                foreach (var modFile in Directory.GetFiles(path))
                    if ((Path.GetFileName(modFile).StartsWith(mainName, StringComparison.Ordinal) ||
                        Path.GetFileName(modFile).StartsWith(mainNameSpaceless, StringComparison.Ordinal)) &&
                        modFile.ToLower(CultureInfo.InvariantCulture).EndsWith(".mm.dll", StringComparison.Ordinal))
                        ReadMod(modFile);
                return;
            }

            Log($"[ReadMod] Loading mod: {path}");
            ModuleDefinition mod = _ReadModule(path, GenReaderParameters(false, path));
            var dir = Path.GetDirectoryName(path);
            if (!DependencyDirs.Contains(dir))
            {
                (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(dir);
                DependencyDirs.Add(dir);
            }
            Mods.Add(mod);
            OnReadMod?.Invoke(this, mod);
        }
        public virtual void ReadMod(Stream stream)
        {
            Log($"[ReadMod] Loading mod: stream#{(uint)stream.GetHashCode()}");
            ModuleDefinition mod = _ReadModule(stream, GenReaderParameters(false));
            Mods.Add(mod);
            OnReadMod?.Invoke(this, mod);
        }

        public virtual void ParseRules(ModuleDefinition mod)
        {
            TypeDefinition rulesType = mod.GetType("MonoMod.MonoModRules");
            Type rulesTypeMMILRT = null;
            if (rulesType != null)
            {
                rulesTypeMMILRT = MonoModRulesManager.ExecuteRules(this, rulesType);
                // Finally, remove the type, otherwise it'll easily conflict with other mods' rules.
                mod.Types.Remove(rulesType);
            }

            // Rule parsing pass: Check for MonoModHook and similar attributes
            foreach (TypeDefinition type in mod.Types)
                ParseRulesInType(type, rulesTypeMMILRT);
        }

        public virtual void ParseRulesInType(TypeDefinition type, Type rulesTypeMMILRT = null)
        {
            var typeName = type.GetPatchFullName();

            if (!MatchingConditionals(type, Module))
                return;

            CustomAttribute handler;

            handler = type.GetCustomAttribute("MonoMod.MonoModCustomAttributeAttribute");
            if (handler != null)
            {
                System.Reflection.MethodInfo method = rulesTypeMMILRT.GetMethod((string)handler.ConstructorArguments[0].Value);
                CustomAttributeHandlers[type.FullName] = (self, args) => method.Invoke(self, args);
            }

            handler = type.GetCustomAttribute("MonoMod.MonoModCustomMethodAttributeAttribute");
            if (handler != null)
            {
                System.Reflection.MethodInfo method = rulesTypeMMILRT.GetMethod((string)handler.ConstructorArguments[0].Value);
                System.Reflection.ParameterInfo[] argInfos = method.GetParameters();
                if (argInfos.Length == 2 && argInfos[0].ParameterType.IsCompatible(typeof(ILContext)))
                {
                    CustomMethodAttributeHandlers[type.FullName] = (self, args) =>
                    {
                        var il = new ILContext((MethodDefinition)args[0]);
                        il.Invoke(_ =>
                        {
                            method.Invoke(self, new object[] {
                                il,
                                args[1]
                            });
                        });
                        if (il.IsReadOnly)
                            il.Dispose();
                    };
                }
                else
                {
                    CustomMethodAttributeHandlers[type.FullName] = (self, args) => method.Invoke(self, args);
                }
            }

            CustomAttribute hook;

            for (hook = type.GetCustomAttribute("MonoMod.MonoModHook"); hook != null; hook = type.GetNextCustomAttribute("MonoMod.MonoModHook"))
                ParseLinkFrom(type, hook);
            for (hook = type.GetCustomAttribute("MonoMod.MonoModLinkFrom"); hook != null; hook = type.GetNextCustomAttribute("MonoMod.MonoModLinkFrom"))
                ParseLinkFrom(type, hook);
            for (hook = type.GetCustomAttribute("MonoMod.MonoModLinkTo"); hook != null; hook = type.GetNextCustomAttribute("MonoMod.MonoModLinkTo"))
                ParseLinkTo(type, hook);

            if (type.HasCustomAttribute("MonoMod.MonoModIgnore"))
                return;

            foreach (MethodDefinition method in type.Methods)
            {
                if (!MatchingConditionals(method, Module))
                    continue;

                for (hook = method.GetCustomAttribute("MonoMod.MonoModHook"); hook != null; hook = method.GetNextCustomAttribute("MonoMod.MonoModHook"))
                    ParseLinkFrom(method, hook);
                for (hook = method.GetCustomAttribute("MonoMod.MonoModLinkFrom"); hook != null; hook = method.GetNextCustomAttribute("MonoMod.MonoModLinkFrom"))
                    ParseLinkFrom(method, hook);
                for (hook = method.GetCustomAttribute("MonoMod.MonoModLinkTo"); hook != null; hook = method.GetNextCustomAttribute("MonoMod.MonoModLinkTo"))
                    ParseLinkTo(method, hook);

                if (method.HasCustomAttribute("MonoMod.MonoModForceCall"))
                    ForceCallMap[method.GetID()] = OpCodes.Call;
                else if (method.HasCustomAttribute("MonoMod.MonoModForceCallvirt"))
                    ForceCallMap[method.GetID()] = OpCodes.Callvirt;
            }

            foreach (FieldDefinition field in type.Fields)
            {
                if (!MatchingConditionals(field, Module))
                    continue;

                for (hook = field.GetCustomAttribute("MonoMod.MonoModHook"); hook != null; hook = field.GetNextCustomAttribute("MonoMod.MonoModHook"))
                    ParseLinkFrom(field, hook);
                for (hook = field.GetCustomAttribute("MonoMod.MonoModLinkFrom"); hook != null; hook = field.GetNextCustomAttribute("MonoMod.MonoModLinkFrom"))
                    ParseLinkFrom(field, hook);
                for (hook = field.GetCustomAttribute("MonoMod.MonoModLinkTo"); hook != null; hook = field.GetNextCustomAttribute("MonoMod.MonoModLinkTo"))
                    ParseLinkTo(field, hook);
            }

            foreach (PropertyDefinition prop in type.Properties)
            {
                if (!MatchingConditionals(prop, Module))
                    continue;

                for (hook = prop.GetCustomAttribute("MonoMod.MonoModHook"); hook != null; hook = prop.GetNextCustomAttribute("MonoMod.MonoModHook"))
                    ParseLinkFrom(prop, hook);
                for (hook = prop.GetCustomAttribute("MonoMod.MonoModLinkFrom"); hook != null; hook = prop.GetNextCustomAttribute("MonoMod.MonoModLinkFrom"))
                    ParseLinkFrom(prop, hook);
                for (hook = prop.GetCustomAttribute("MonoMod.MonoModLinkTo"); hook != null; hook = prop.GetNextCustomAttribute("MonoMod.MonoModLinkTo"))
                    ParseLinkTo(prop, hook);
            }

            foreach (TypeDefinition nested in type.NestedTypes)
                ParseRulesInType(nested, rulesTypeMMILRT);
        }

        public virtual void ParseLinkFrom(MemberReference target, CustomAttribute hook)
        {
            var from = (string)hook.ConstructorArguments[0].Value;

            object to;
            if (target is TypeReference)
                to = ((TypeReference)target).GetPatchFullName();
            else if (target is MethodReference)
                to = new RelinkMapEntry(
                    ((MethodReference)target).DeclaringType.GetPatchFullName(),
                    ((MethodReference)target).GetID(withType: false)
                );
            else if (target is FieldReference)
                to = new RelinkMapEntry(
                    ((FieldReference)target).DeclaringType.GetPatchFullName(),
                    ((FieldReference)target).Name
                );
            else if (target is PropertyReference)
                to = new RelinkMapEntry(
                    ((PropertyReference)target).DeclaringType.GetPatchFullName(),
                    ((PropertyReference)target).Name
                );
            else
                return;

            RelinkMap[from] = to;
        }

        public virtual void ParseLinkTo(MemberReference from, CustomAttribute hook)
        {
            var fromID = (from as MethodReference)?.GetID() ?? from.GetPatchFullName();
            if (hook.ConstructorArguments.Count == 1)
                RelinkMap[fromID] = (string)hook.ConstructorArguments[0].Value;
            else
                RelinkMap[fromID] = new RelinkMapEntry(
                    (string)hook.ConstructorArguments[0].Value,
                    (string)hook.ConstructorArguments[1].Value
                );
        }

        public virtual void RunCustomAttributeHandlers(ICustomAttributeProvider cap)
        {
            if (!cap.HasCustomAttributes)
                return;

            foreach (CustomAttribute attrib in cap.CustomAttributes.ToArray())
            {
                if (CustomAttributeHandlers.TryGetValue(attrib.AttributeType.FullName, out Action<object, object[]> handler))
                    handler?.Invoke(null, new object[] { cap, attrib });
                if (cap is MethodReference && CustomMethodAttributeHandlers.TryGetValue(attrib.AttributeType.FullName, out handler))
                    handler?.Invoke(null, new object[] { (MethodDefinition)cap, attrib });
            }
        }


        /// <summary>
        /// Automatically mods the module, loading Input, writing the modded module to Output.
        /// </summary>
        public virtual void AutoPatch()
        {
            Log("[AutoPatch] Parsing rules in loaded mods");
            foreach (ModuleDefinition mod in Mods)
                ParseRules(mod);

            /* WHY PRE-PATCH?
             * Custom attributes and other stuff refering to possibly new types
             * 1. could access yet undefined types that need to be copied over
             * 2. need to be copied over themselves anyway, regardless if new type or not
             * To define the order of origMethoding (first types, then references), PrePatch does
             * the "type addition" job by creating stub types, which then get filled in
             * the Patch pass.
             */

            Log("[AutoPatch] PrePatch pass");
            foreach (ModuleDefinition mod in Mods)
                PrePatchModule(mod);

            Log("[AutoPatch] Patch pass");
            foreach (ModuleDefinition mod in Mods)
                PatchModule(mod);

            /* The PatchRefs pass fixes all references referring to stuff
             * possibly added in the PrePatch or Patch passes.
             */

            Log("[AutoPatch] PatchRefs pass");
            PatchRefs();

            if (PostProcessors != null)
            {
                Delegate[] pps = PostProcessors.GetInvocationList();
                for (var i = 0; i < pps.Length; i++)
                {
                    Log($"[PostProcessor] PostProcessor pass #{i + 1}");
                    ((PostProcessor)pps[i])?.Invoke(this);
                }
            }
        }

        public virtual IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context)
        {
            try
            {
                // TODO: Handle mtp being deleted but being hooked in a better, Strict-compatible way.
                IMetadataTokenProvider relinked = PostRelinker(
                    MainRelinker(mtp, context) ?? mtp,
                    context
                );
                if (relinked == null)
                    throw new RelinkTargetNotFoundException(mtp, context);
                return relinked;
            }
            catch (Exception e)
            {
                throw new RelinkFailedException(null, e, mtp, context);
            }
        }
        public virtual IMetadataTokenProvider MainRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context)
        {
            if (mtp is TypeReference type)
            {
                // Type is coming from the input module - return the original.
                if (type.Module == Module)
                    return type;

                // Type isn't coming from a mod module - import the original.
                if (type.Module != null && !Mods.Contains(type.Module))
                    return Module.ImportReference(type);

                // Don't resolve references to system libraries
                // Doing so will bypass system reference assemblies like System.Runtime and cause a lot of jank in general 
                if (type.Scope.Name.StartsWith("System."))
                    return Module.ImportReference(type);

                // Type **reference** is coming from a mod module - resolve it just to be safe.
                type = type.SafeResolve() ?? type;
                TypeReference found = FindTypeDeep(type.GetPatchFullName());

                if (found == null)
                {
                    if (RelinkMap.ContainsKey(type.FullName))
                        return null; // Let the post-relinker handle this.
                    throw new RelinkTargetNotFoundException(mtp, context);
                }
                return Module.ImportReference(found);
            }

            if (mtp is FieldReference || mtp is MethodReference || mtp is PropertyReference || mtp is EventReference || mtp is CallSite)
                // Don't relink those. It'd be useful to f.e. link to member B instead of member A.
                // MonoModExt already handles the default "deep" relinking.
                return Module.ImportReference(mtp);

            throw new InvalidOperationException($"MonoMod default relinker can't handle metadata token providers of the type {mtp.GetType()}");
        }
        public virtual IMetadataTokenProvider PostRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context)
        {
            // The post relinker doesn't care if it can't handle a specific metadata token provider type; Just run ResolveRelinkTarget
            return ResolveRelinkTarget(mtp) ?? mtp;
        }

        public virtual IMetadataTokenProvider ResolveRelinkTarget(IMetadataTokenProvider mtp, bool relink = true, bool relinkModule = true)
        {
            string name;
            string nameAlt = null;
            if (mtp is TypeReference)
            {
                name = ((TypeReference)mtp).FullName;
            }
            else if (mtp is MethodReference)
            {
                name = ((MethodReference)mtp).GetID(withType: true);
                nameAlt = ((MethodReference)mtp).GetID(simple: true);
            }
            else if (mtp is FieldReference)
            {
                name = ((FieldReference)mtp).FullName;
            }
            else if (mtp is PropertyReference)
            {
                name = ((PropertyReference)mtp).FullName;
            }
            else
                return null;

            if (RelinkMapCache.TryGetValue(name, out IMetadataTokenProvider cached))
                return cached;

            if (relink && (
                RelinkMap.TryGetValue(name, out var val) ||
                (nameAlt != null && RelinkMap.TryGetValue(nameAlt, out val))
            ))
            {
                // If the value already is a mtp, import and cache the imported reference.
                if (val is IMetadataTokenProvider)
                    return RelinkMapCache[name] = Module.ImportReference((IMetadataTokenProvider)val);

                if (val is RelinkMapEntry)
                {
                    var typeName = ((RelinkMapEntry)val).Type as string;
                    var findableID = ((RelinkMapEntry)val).FindableID as string;

                    TypeDefinition type = FindTypeDeep(typeName)?.SafeResolve();
                    if (type == null)
                        return RelinkMapCache[name] = ResolveRelinkTarget(mtp, false, relinkModule);

                    val =
                        type.FindMethod(findableID) ??
                        type.FindField(findableID) ??
                        type.FindProperty(findableID) ??
                        (object)null
                    ;
                    if (val == null)
                    {
                        if (Strict)
                            throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} ({typeName}, {findableID}) (remap: {mtp})", mtp);
                        else
                            return null;
                    }
                    return RelinkMapCache[name] = Module.ImportReference((IMetadataTokenProvider)val);
                }

                if (val is string && mtp is TypeReference)
                {
                    IMetadataTokenProvider found = FindTypeDeep((string)val);
                    if (found == null)
                    {
                        if (Strict)
                            throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} {val} (remap: {mtp})", mtp);
                        else
                            return null;
                    }
                    val = Module.ImportReference(
                        ResolveRelinkTarget(found, false, relinkModule) ?? found
                    );
                }

                if (val is IMetadataTokenProvider)
                    return RelinkMapCache[name] = (IMetadataTokenProvider)val;

                throw new InvalidOperationException($"MonoMod doesn't support RelinkMap value of type {val.GetType()} (remap: {mtp})");
            }


            if (relinkModule && mtp is TypeReference)
            {
                if (RelinkModuleMapCache.TryGetValue(name, out TypeReference type))
                    return type;
                type = (TypeReference)mtp;

                if (RelinkModuleMap.TryGetValue(type.Scope.Name, out ModuleDefinition scope))
                {
                    TypeReference found = scope.GetType(type.FullName);
                    if (found == null)
                    {
                        if (Strict)
                            throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} {type.FullName} (remap: {mtp})", mtp);
                        else
                            return null;
                    }
                    return RelinkModuleMapCache[name] = Module.ImportReference(found);
                }

                // Value types (i.e. enums) as custom attribute parameters aren't marked as value types.
                // To prevent that and other issues from popping up, don't cache the default.
                return Module.ImportReference(type);
            }

            return null;
        }


        public virtual bool DefaultParser(MonoModder mod, MethodBody body, Instruction instr, ref int instri)
        {
            return true;
        }


        public virtual TypeReference FindType(string name)
            => FindType(Module, name, new Stack<ModuleDefinition>()) ?? Module.GetType(name, false);
        public virtual TypeReference FindType(string name, bool runtimeName)
            => FindType(Module, name, new Stack<ModuleDefinition>()) ?? Module.GetType(name, runtimeName);
        protected virtual TypeReference FindType(ModuleDefinition main, string fullName, Stack<ModuleDefinition> crawled)
        {
            TypeReference type;
            if ((type = main.GetType(fullName, false)) != null)
                return type;
            if (fullName.StartsWith("<PrivateImplementationDetails>/", StringComparison.Ordinal))
                return null;

            if (crawled.Contains(main))
                return null;
            crawled.Push(main);

            TypeReference CrawlDependencies(bool crawlPrivateSystemLibs = false)
            {
                foreach (ModuleDefinition dep in DependencyMap[main])
                {
                    if (RemovePatchReferences && dep.Assembly.Name.Name.EndsWith(".mm", StringComparison.Ordinal))
                        continue;

                    if (!crawlPrivateSystemLibs && dep.Assembly.Name.Name.StartsWith("System.Private"))
                        continue;

                    if (FindType(dep, fullName, crawled) is { } typeRef)
                        return typeRef;
                }
                return null;
            }

            if (main.HasExportedTypes)
            {
                if (!ForwardedTypeCache.TryGetValue(main, out Dictionary<string, TypeReference> forwardedTypes))
                    ForwardedTypeCache.Add(main, forwardedTypes = new Dictionary<string, TypeReference>());

                if (!forwardedTypes.TryGetValue(fullName, out TypeReference forwardedType))
                {
                    if (main.ExportedTypes.FirstOrDefault(t => t.FullName == fullName) is { } exportedType)
                    {
                        TypeReference crawledType = CrawlDependencies(true);
                        if (crawledType != null)
                            forwardedType = new TypeReference(exportedType.Namespace, exportedType.Name, main, main.Assembly.Name, crawledType.IsValueType);
                        else
                            forwardedType = new TypeReference(exportedType.Namespace, exportedType.Name, main, main.Assembly.Name);
                    }
                    forwardedTypes.Add(fullName, forwardedType);
                }

                if (forwardedType != null)
                    return forwardedType;
            }

            return CrawlDependencies();
        }
        public virtual TypeReference FindTypeDeep(string name)
        {
            TypeReference type = FindType(name, false);
            if (type != null)
                return type;

            // Check in the dependencies of the mod modules.
            var crawled = new Stack<ModuleDefinition>();
            // Set type to null so that an actual break condition exists
            type = null;
            foreach (ModuleDefinition mod in Mods)
                foreach (ModuleDefinition dep in DependencyMap[mod])
                    if ((type = FindType(dep, name, crawled)) != null)
                    {
                        // Type may come from a dependency. If the assembly reference is missing, add.
                        if (type.Scope is AssemblyNameReference dllRef && !Module.AssemblyReferences.Any(n => n.Name == dllRef.Name))
                            Module.AssemblyReferences.Add(dllRef);
                        return Module.ImportReference(type);
                    }

            return null;
        }

        #region Pre-Patch Pass
        /// <summary>
        /// Pre-Patches the module (adds new types, module references, resources, ...).
        /// </summary>
        /// <param name="mod">Mod to patch into the input module.</param>
        public virtual void PrePatchModule(ModuleDefinition mod)
        {
            foreach (TypeDefinition type in mod.Types)
                PrePatchType(type);

            foreach (ModuleReference @ref in mod.ModuleReferences)
                if (!Module.ModuleReferences.Contains(@ref))
                    Module.ModuleReferences.Add(@ref);

            foreach (Resource res in mod.Resources)
                if (res is EmbeddedResource)
                    Module.Resources.Add(new EmbeddedResource(
                        res.Name.StartsWith(mod.Assembly.Name.Name, StringComparison.Ordinal) ?
                            Module.Assembly.Name.Name + res.Name.Substring(mod.Assembly.Name.Name.Length) :
                            res.Name,
                        res.Attributes,
                        ((EmbeddedResource)res).GetResourceData()
                    ));
        }

        /// <summary>
        /// Patches the type (adds new types).
        /// </summary>
        /// <param name="type">Type to patch into the input module.</param>
        /// <param name="forceAdd">Forcibly add the type, no matter if it already exists or if it's MonoModRules.</param>
        public virtual void PrePatchType(TypeDefinition type, bool forceAdd = false)
        {
            var typeName = type.GetPatchFullName();

            // Fix legacy issue: Copy / inline any used modifiers.
            if ((type.Namespace != "MonoMod" && type.HasCustomAttribute("MonoMod.MonoModIgnore")) || SkipList.Contains(typeName) || !MatchingConditionals(type, Module))
                return;
            // ... Except MonoModRules
            if (type.FullName == "MonoMod.MonoModRules" && !forceAdd)
                return;

            // Check if type exists in target module or dependencies.
            TypeReference targetType = forceAdd ? null : Module.GetType(typeName, false); // For PrePatch, we need to check in the target assembly only
            TypeDefinition targetTypeDef = targetType?.SafeResolve();
            if (type.HasCustomAttribute("MonoMod.MonoModReplace") || type.HasCustomAttribute("MonoMod.MonoModRemove"))
            {
                if (targetTypeDef != null)
                {
                    if (targetTypeDef.DeclaringType == null)
                        Module.Types.Remove(targetTypeDef);
                    else
                        targetTypeDef.DeclaringType.NestedTypes.Remove(targetTypeDef);
                }
                if (type.HasCustomAttribute("MonoMod.MonoModRemove"))
                    return;
            }
            else if (targetType != null)
            {
                PrePatchNested(type);
                return;
            }

            // Add the type.
            LogVerbose($"[PrePatchType] Adding {typeName} to the target module.");

            var newType = new TypeDefinition(type.Namespace, type.Name, type.Attributes, type.BaseType);

            foreach (GenericParameter genParam in type.GenericParameters)
                newType.GenericParameters.Add(genParam.Clone());

            foreach (InterfaceImplementation interf in type.Interfaces)
                newType.Interfaces.Add(interf);

            newType.ClassSize = type.ClassSize;
            if (type.DeclaringType != null)
            {
                // The declaring type is existing as this is being called nestedly.
                newType.DeclaringType = type.DeclaringType.Relink(Relinker, newType).Resolve();
                newType.DeclaringType.NestedTypes.Add(newType);
            }
            else
            {
                Module.Types.Add(newType);
            }
            newType.PackingSize = type.PackingSize;
            newType.SecurityDeclarations.AddRange(type.SecurityDeclarations);

            // When adding MonoModAdded, try to reuse the just added MonoModAdded.
            newType.CustomAttributes.Add(new CustomAttribute(GetMonoModAddedCtor()));

            targetType = newType;

            PrePatchNested(type);
        }

        protected virtual void PrePatchNested(TypeDefinition type)
        {
            for (var i = 0; i < type.NestedTypes.Count; i++)
            {
                PrePatchType(type.NestedTypes[i]);
            }
        }
        #endregion

        #region Patch Pass
        /// <summary>
        /// Patches the module (adds new type members).
        /// </summary>
        /// <param name="mod">Mod to patch into the input module.</param>
        public virtual void PatchModule(ModuleDefinition mod)
        {
            foreach (TypeDefinition type in mod.Types)
                if (
                    (type.Namespace == "MonoMod" || type.Namespace.StartsWith("MonoMod.", StringComparison.Ordinal)) &&
                    type.BaseType.FullName == "System.Attribute"
                   )
                    PatchType(type);

            foreach (TypeDefinition type in mod.Types)
                if (!(
                    (type.Namespace == "MonoMod" || type.Namespace.StartsWith("MonoMod.", StringComparison.Ordinal)) &&
                    type.BaseType.FullName == "System.Attribute"
                   ))
                    PatchType(type);
        }

        /// <summary>
        /// Patches the type (adds new members).
        /// </summary>
        /// <param name="type">Type to patch into the input module.</param>
        public virtual void PatchType(TypeDefinition type)
        {
            var typeName = type.GetPatchFullName();

            TypeReference targetType = Module.GetType(typeName, false);
            if (targetType == null) return; // Type should've been added or removed accordingly.
            TypeDefinition targetTypeDef = targetType?.SafeResolve();

            if ((type.Namespace != "MonoMod" && type.HasCustomAttribute("MonoMod.MonoModIgnore")) || // Fix legacy issue: Copy / inline any used modifiers.
                SkipList.Contains(typeName) ||
                !MatchingConditionals(type, Module))
            {

                if (type.HasCustomAttribute("MonoMod.MonoModIgnore") && targetTypeDef != null)
                {
                    // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                    foreach (CustomAttribute attrib in type.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            targetTypeDef.CustomAttributes.Add(attrib.Clone());
                }

                PatchNested(type);
                return;
            }

            if (typeName == type.FullName)
                LogVerbose($"[PatchType] Patching type {typeName}");
            else
                LogVerbose($"[PatchType] Patching type {typeName} (prefixed: {type.FullName})");

            // Add "new" custom attributes
            foreach (CustomAttribute attrib in type.CustomAttributes)
                if (!targetTypeDef.HasCustomAttribute(attrib.AttributeType.FullName))
                    targetTypeDef.CustomAttributes.Add(attrib.Clone());

            var propMethods = new HashSet<MethodDefinition>(); // In the Patch pass, prop methods exist twice.
            foreach (PropertyDefinition prop in type.Properties)
                PatchProperty(targetTypeDef, prop, propMethods);

            var eventMethods = new HashSet<MethodDefinition>(); // In the Patch pass, prop methods exist twice.
            foreach (EventDefinition eventdef in type.Events)
                PatchEvent(targetTypeDef, eventdef, eventMethods);

            foreach (MethodDefinition method in type.Methods)
                if (!propMethods.Contains(method) && !eventMethods.Contains(method))
                    PatchMethod(targetTypeDef, method);

            if (type.HasCustomAttribute("MonoMod.MonoModEnumReplace"))
            {
                for (var ii = 0; ii < targetTypeDef.Fields.Count;)
                {
                    if (targetTypeDef.Fields[ii].Name == "value__")
                    {
                        ii++;
                        continue;
                    }

                    targetTypeDef.Fields.RemoveAt(ii);
                }
            }

            if (type.IsSequentialLayout)
                targetTypeDef.IsSequentialLayout = true;

            if (type.IsExplicitLayout)
                targetTypeDef.IsExplicitLayout = true;

            if (type.HasLayoutInfo)
            {
                targetTypeDef.PackingSize = type.PackingSize;
                targetTypeDef.ClassSize = type.ClassSize;
            }

            foreach (FieldDefinition field in type.Fields)
                PatchField(targetTypeDef, field);

            PatchNested(type);
        }

        protected virtual void PatchNested(TypeDefinition type)
        {
            for (var i = 0; i < type.NestedTypes.Count; i++)
            {
                PatchType(type.NestedTypes[i]);
            }
        }

        public virtual void PatchProperty(TypeDefinition targetType, PropertyDefinition prop, HashSet<MethodDefinition> propMethods = null)
        {
            if (!MatchingConditionals(prop, Module))
                return;

            prop.Name = prop.GetPatchName();

            MethodDefinition addMethod;

            PropertyDefinition targetProp = targetType.FindProperty(prop.Name);
            var backingName = $"<{prop.Name}>__BackingField";
            FieldDefinition backing = prop.DeclaringType.FindField(backingName);
            FieldDefinition targetBacking = targetType.FindField(backingName);

            if (prop.HasCustomAttribute("MonoMod.MonoModIgnore"))
            {
                // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                if (targetProp != null)
                    foreach (CustomAttribute attrib in prop.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            targetProp.CustomAttributes.Add(attrib.Clone());
                if (backing != null)
                    backing.DeclaringType.Fields.Remove(backing); // Otherwise the backing field gets added anyway
                if (prop.GetMethod != null)
                    propMethods?.Add(prop.GetMethod);
                if (prop.SetMethod != null)
                    propMethods?.Add(prop.SetMethod);
                foreach (MethodDefinition method in prop.OtherMethods)
                    propMethods?.Add(method);
                return;
            }

            if (prop.HasCustomAttribute("MonoMod.MonoModRemove") || prop.HasCustomAttribute("MonoMod.MonoModReplace"))
            {
                if (targetProp != null)
                {
                    targetType.Properties.Remove(targetProp);
                    if (targetBacking != null)
                        targetType.Fields.Remove(targetBacking);
                    if (targetProp.GetMethod != null)
                        targetType.Methods.Remove(targetProp.GetMethod);
                    if (targetProp.SetMethod != null)
                        targetType.Methods.Remove(targetProp.SetMethod);
                    foreach (MethodDefinition method in targetProp.OtherMethods)
                        targetType.Methods.Remove(method);
                }
                if (prop.HasCustomAttribute("MonoMod.MonoModRemove"))
                    return;
            }

            if (targetProp == null)
            {
                // Add missing property
                PropertyDefinition newProp = targetProp = new PropertyDefinition(prop.Name, prop.Attributes, prop.PropertyType);
                newProp.CustomAttributes.Add(new CustomAttribute(GetMonoModAddedCtor()));

                foreach (ParameterDefinition param in prop.Parameters)
                    newProp.Parameters.Add(param.Clone());

                newProp.DeclaringType = targetType;
                targetType.Properties.Add(newProp);

                if (backing != null)
                {
                    FieldDefinition newBacking = targetBacking = new FieldDefinition(backingName, backing.Attributes, backing.FieldType);
                    targetType.Fields.Add(newBacking);
                }
            }

            foreach (CustomAttribute attrib in prop.CustomAttributes)
                targetProp.CustomAttributes.Add(attrib.Clone());

            MethodDefinition getter = prop.GetMethod;
            if (getter != null &&
                (addMethod = PatchMethod(targetType, getter)) != null)
            {
                targetProp.GetMethod = addMethod;
                propMethods?.Add(getter);
            }

            MethodDefinition setter = prop.SetMethod;
            if (setter != null &&
                (addMethod = PatchMethod(targetType, setter)) != null)
            {
                targetProp.SetMethod = addMethod;
                propMethods?.Add(setter);
            }

            foreach (MethodDefinition method in prop.OtherMethods)
                if ((addMethod = PatchMethod(targetType, method)) != null)
                {
                    targetProp.OtherMethods.Add(addMethod);
                    propMethods?.Add(method);
                }
        }

        public virtual void PatchEvent(TypeDefinition targetType, EventDefinition srcEvent, HashSet<MethodDefinition> propMethods = null)
        {
            srcEvent.Name = srcEvent.GetPatchName();

            MethodDefinition patched;
            EventDefinition targetEvent = targetType.FindEvent(srcEvent.Name);
            var backingName = $"<{srcEvent.Name}>__BackingField";
            FieldDefinition backing = srcEvent.DeclaringType.FindField(backingName);
            FieldDefinition targetBacking = targetType.FindField(backingName);

            if (srcEvent.HasCustomAttribute("MonoMod.MonoModIgnore"))
            {
                // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                if (targetEvent != null)
                    foreach (CustomAttribute attrib in srcEvent.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            targetEvent.CustomAttributes.Add(attrib.Clone());
                if (backing != null)
                    backing.DeclaringType.Fields.Remove(backing); // Otherwise the backing field gets added anyway
                if (srcEvent.AddMethod != null)
                    propMethods?.Add(srcEvent.AddMethod);
                if (srcEvent.RemoveMethod != null)
                    propMethods?.Add(srcEvent.RemoveMethod);
                if (srcEvent.InvokeMethod != null)
                    propMethods?.Add(srcEvent.InvokeMethod);
                foreach (MethodDefinition method in srcEvent.OtherMethods)
                    propMethods?.Add(method);
                return;
            }

            if (srcEvent.HasCustomAttribute("MonoMod.MonoModRemove") || srcEvent.HasCustomAttribute("MonoMod.MonoModReplace"))
            {
                if (targetEvent != null)
                {
                    targetType.Events.Remove(targetEvent);
                    if (targetBacking != null)
                        targetType.Fields.Remove(targetBacking);
                    if (targetEvent.AddMethod != null)
                        targetType.Methods.Remove(targetEvent.AddMethod);
                    if (targetEvent.RemoveMethod != null)
                        targetType.Methods.Remove(targetEvent.RemoveMethod);
                    if (targetEvent.InvokeMethod != null)
                        targetType.Methods.Remove(targetEvent.InvokeMethod);
                    if (targetEvent.OtherMethods != null)
                        foreach (MethodDefinition method in targetEvent.OtherMethods)
                            targetType.Methods.Remove(method);
                }
                if (srcEvent.HasCustomAttribute("MonoMod.MonoModRemove"))
                    return;
            }

            if (targetEvent == null)
            {
                // Add missing event
                EventDefinition newEvent = targetEvent = new EventDefinition(srcEvent.Name, srcEvent.Attributes, srcEvent.EventType);
                newEvent.CustomAttributes.Add(new CustomAttribute(GetMonoModAddedCtor()));

                newEvent.DeclaringType = targetType;
                targetType.Events.Add(newEvent);

                if (backing != null)
                {
                    var newBacking = new FieldDefinition(backingName, backing.Attributes, backing.FieldType);
                    targetType.Fields.Add(newBacking);
                }
            }

            foreach (CustomAttribute attrib in srcEvent.CustomAttributes)
                targetEvent.CustomAttributes.Add(attrib.Clone());

            MethodDefinition adder = srcEvent.AddMethod;
            if (adder != null &&
                (patched = PatchMethod(targetType, adder)) != null)
            {
                targetEvent.AddMethod = patched;
                propMethods?.Add(adder);
            }

            MethodDefinition remover = srcEvent.RemoveMethod;
            if (remover != null &&
                (patched = PatchMethod(targetType, remover)) != null)
            {
                targetEvent.RemoveMethod = patched;
                propMethods?.Add(remover);
            }

            MethodDefinition invoker = srcEvent.InvokeMethod;
            if (invoker != null &&
                (patched = PatchMethod(targetType, invoker)) != null)
            {
                targetEvent.InvokeMethod = patched;
                propMethods?.Add(invoker);
            }

            foreach (MethodDefinition method in srcEvent.OtherMethods)
                if ((patched = PatchMethod(targetType, method)) != null)
                {
                    targetEvent.OtherMethods.Add(patched);
                    propMethods?.Add(method);
                }
        }


        public virtual void PatchField(TypeDefinition targetType, FieldDefinition field)
        {
            var typeName = field.DeclaringType.GetPatchFullName();

            if (field.HasCustomAttribute("MonoMod.MonoModNoNew") || SkipList.Contains(typeName + "::" + field.Name) || !MatchingConditionals(field, Module))
                return;

            field.Name = field.GetPatchName();

            if (field.HasCustomAttribute("MonoMod.MonoModRemove") || field.HasCustomAttribute("MonoMod.MonoModReplace"))
            {
                FieldDefinition targetField = targetType.FindField(field.Name);
                if (targetField != null)
                    targetType.Fields.Remove(targetField);
                if (field.HasCustomAttribute("MonoMod.MonoModRemove"))
                    return;
            }

            FieldDefinition existingField = targetType.FindField(field.Name);

            if (field.HasCustomAttribute("MonoMod.MonoModIgnore") && existingField != null)
            {
                // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                foreach (CustomAttribute attrib in field.CustomAttributes)
                    if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                        existingField.CustomAttributes.Add(attrib.Clone());
                return;
            }

            if (existingField == null)
            {
                existingField = new FieldDefinition(field.Name, field.Attributes, field.FieldType);
                existingField.CustomAttributes.Add(new CustomAttribute(GetMonoModAddedCtor()));
                existingField.InitialValue = field.InitialValue;
                if (field.HasConstant)
                    existingField.Constant = field.Constant;
                targetType.Fields.Add(existingField);
            }

            if (field.HasLayoutInfo)
                existingField.Offset = field.Offset;

            if (field.HasMarshalInfo)
                existingField.MarshalInfo = field.MarshalInfo;

            foreach (CustomAttribute attrib in field.CustomAttributes)
                existingField.CustomAttributes.Add(attrib.Clone());
        }

        public virtual MethodDefinition PatchMethod(TypeDefinition targetType, MethodDefinition method)
        {
            if (method.Name.StartsWith("orig_", StringComparison.Ordinal) || method.HasCustomAttribute("MonoMod.MonoModOriginal"))
                // Ignore original method stubs
                return null;

            if (!AllowedSpecialName(method, targetType) || !MatchingConditionals(method, Module))
                // Ignore ignored methods
                return null;

            var typeName = targetType.GetPatchFullName();

            if (SkipList.Contains(method.GetID(type: typeName)))
                return null;

            // Back in the day when patch_ was the only available alternative, this only affected replacements.
            method.Name = method.GetPatchName();

            // If the method's a MonoModConstructor method, just update its attributes to make it look like one.
            if (method.HasCustomAttribute("MonoMod.MonoModConstructor"))
            {
                // Add MonoModOriginalName as the orig name data gets lost otherwise.
                if (!method.IsSpecialName && !method.HasCustomAttribute("MonoMod.MonoModOriginalName"))
                {
                    var origNameAttrib = new CustomAttribute(GetMonoModOriginalNameCtor());
                    origNameAttrib.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.String, "orig_" + method.Name));
                    method.CustomAttributes.Add(origNameAttrib);
                }

                method.Name = method.IsStatic ? ".cctor" : ".ctor";
                method.IsSpecialName = true;
                method.IsRuntimeSpecialName = true;
            }

            MethodDefinition existingMethod = targetType.FindMethod(method.GetID(type: typeName));
            MethodDefinition origMethod = targetType.FindMethod(method.GetID(type: typeName, name: method.GetOriginalName()));

            if (method.HasCustomAttribute("MonoMod.MonoModIgnore"))
            {
                // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                if (existingMethod != null)
                    foreach (CustomAttribute attrib in method.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName) ||
                            CustomMethodAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            existingMethod.CustomAttributes.Add(attrib.Clone());
                return null;
            }

            if (existingMethod == null && method.HasCustomAttribute("MonoMod.MonoModNoNew"))
                return null;

            if (method.HasCustomAttribute("MonoMod.MonoModRemove"))
            {
                if (existingMethod != null)
                    targetType.Methods.Remove(existingMethod);
                return null;
            }

            if (method.HasCustomAttribute("MonoMod.MonoModReplace"))
            {
                if (existingMethod != null)
                {
                    existingMethod.CustomAttributes.Clear();
                    existingMethod.Attributes = method.Attributes;
                    existingMethod.IsPInvokeImpl = method.IsPInvokeImpl;
                    existingMethod.ImplAttributes = method.ImplAttributes;
                }

            }
            else if (existingMethod != null && origMethod == null)
            {
                origMethod = existingMethod.Clone();
                origMethod.Name = method.GetOriginalName();
                origMethod.Attributes = existingMethod.Attributes & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName;
                origMethod.MetadataToken = GetMetadataToken(TokenType.Method);
                origMethod.IsVirtual = false; // Fix overflow when calling orig_ method, but orig_ method already defined higher up

                origMethod.Overrides.Clear();
                foreach (MethodReference @override in method.Overrides)
                    origMethod.Overrides.Add(@override);

                origMethod.CustomAttributes.Add(new CustomAttribute(GetMonoModOriginalCtor()));

                // Check if we've got custom attributes on our own orig_ method.
                MethodDefinition modOrigMethod = method.DeclaringType.FindMethod(method.GetID(name: method.GetOriginalName()));
                if (modOrigMethod != null)
                    foreach (CustomAttribute attrib in modOrigMethod.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName) ||
                            CustomMethodAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            origMethod.CustomAttributes.Add(attrib.Clone());

                targetType.Methods.Add(origMethod);
            }

            // Fix for .cctor not linking to orig_.cctor
            if (origMethod != null && method.IsConstructor && method.IsStatic && method.HasBody && !method.HasCustomAttribute("MonoMod.MonoModConstructor"))
            {
                Collection<Instruction> instructions = method.Body.Instructions;
                ILProcessor ilProcessor = method.Body.GetILProcessor();
                ilProcessor.InsertBefore(instructions[instructions.Count - 1], ilProcessor.Create(OpCodes.Call, origMethod));
            }

            if (existingMethod != null)
            {
                existingMethod.Body = method.Body.Clone(existingMethod);
                existingMethod.IsManaged = method.IsManaged;
                existingMethod.IsIL = method.IsIL;
                existingMethod.IsNative = method.IsNative;
                existingMethod.PInvokeInfo = method.PInvokeInfo;
                existingMethod.IsPreserveSig = method.IsPreserveSig;
                existingMethod.IsInternalCall = method.IsInternalCall;
                existingMethod.IsPInvokeImpl = method.IsPInvokeImpl;

                foreach (CustomAttribute attrib in method.CustomAttributes)
                    existingMethod.CustomAttributes.Add(attrib.Clone());

                method = existingMethod;

            }
            else
            {
                var clone = new MethodDefinition(method.Name, method.Attributes, Module.TypeSystem.Void);
                clone.MetadataToken = GetMetadataToken(TokenType.Method);
                clone.CallingConvention = method.CallingConvention;
                clone.ExplicitThis = method.ExplicitThis;
                clone.MethodReturnType = method.MethodReturnType;
                clone.Attributes = method.Attributes;
                clone.ImplAttributes = method.ImplAttributes;
                clone.SemanticsAttributes = method.SemanticsAttributes;
                clone.DeclaringType = targetType;
                clone.ReturnType = method.ReturnType;
                clone.Body = method.Body.Clone(clone);
                clone.PInvokeInfo = method.PInvokeInfo;
                clone.IsPInvokeImpl = method.IsPInvokeImpl;

                foreach (GenericParameter genParam in method.GenericParameters)
                    clone.GenericParameters.Add(genParam.Clone());

                foreach (ParameterDefinition param in method.Parameters)
                    clone.Parameters.Add(param.Clone());

                foreach (CustomAttribute attrib in method.CustomAttributes)
                    clone.CustomAttributes.Add(attrib.Clone());

                foreach (MethodReference @override in method.Overrides)
                    clone.Overrides.Add(@override);

                clone.CustomAttributes.Add(new CustomAttribute(GetMonoModAddedCtor()));

                targetType.Methods.Add(clone);

                method = clone;
            }

            if (origMethod != null)
            {
                var origNameAttrib = new CustomAttribute(GetMonoModOriginalNameCtor());
                origNameAttrib.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.String, origMethod.Name));
                method.CustomAttributes.Add(origNameAttrib);
            }

            return method;
        }
        #endregion

        #region PatchRefs Pass
        public virtual void PatchRefs()
        {
            if (UpgradeMSCORLIB == null)
            {
                // Check if the assembly depends on mscorlib 2.0.5.0, possibly Unity.
                // If so, upgrade to that version (or away to an even higher version).
                var fckUnity = new Version(2, 0, 5, 0);
                UpgradeMSCORLIB = Module.AssemblyReferences.Any(x => x.Version == fckUnity);
            }

            if (UpgradeMSCORLIB.Value)
            {
                // Attempt to remap and remove redundant mscorlib references.
                // Subpass 1: Find newest referred version.
                var mscorlibDeps = new List<AssemblyNameReference>();
                for (var i = 0; i < Module.AssemblyReferences.Count; i++)
                {
                    AssemblyNameReference dep = Module.AssemblyReferences[i];
                    if (dep.Name == "mscorlib")
                    {
                        mscorlibDeps.Add(dep);
                    }
                }
                if (mscorlibDeps.Count > 1)
                {
                    // Subpass 2: Apply changes if found.
                    AssemblyNameReference maxmscorlib = mscorlibDeps.OrderByDescending(x => x.Version).First();
                    if (DependencyCache.TryGetValue(maxmscorlib.FullName, out ModuleDefinition mscorlib))
                    {
                        for (var i = 0; i < Module.AssemblyReferences.Count; i++)
                        {
                            AssemblyNameReference dep = Module.AssemblyReferences[i];
                            if (dep.Name == "mscorlib" && maxmscorlib.Version > dep.Version)
                            {
                                LogVerbose("[PatchRefs] Removing and relinking duplicate mscorlib: " + dep.Version);
                                RelinkModuleMap[dep.FullName] = mscorlib;
                                Module.AssemblyReferences.RemoveAt(i);
                                --i;
                            }
                        }
                    }
                }
            }

            foreach (TypeDefinition type in Module.Types)
                PatchRefsInType(type);
        }

        public virtual void PatchRefs(ModuleDefinition mod)
        {
            foreach (TypeDefinition type in mod.Types)
                PatchRefsInType(type);
        }

        public virtual void PatchRefsInType(TypeDefinition type)
        {
            LogVerbose($"[VERBOSE] [PatchRefsInType] Patching refs in {type}");

            if (type.BaseType != null)
                type.BaseType = type.BaseType.Relink(Relinker, type);

            // Don't foreach when modifying the collection
            for (var i = 0; i < type.GenericParameters.Count; i++)
            {
                type.GenericParameters[i] = type.GenericParameters[i].Relink(Relinker, type);
                for (var j = 0; j < type.GenericParameters[i].CustomAttributes.Count; j++)
                    PatchRefsInCustomAttribute(type.GenericParameters[i].CustomAttributes[j], type);
            }

            // Don't foreach when modifying the collection
            for (var i = 0; i < type.Interfaces.Count; i++)
            {
#if !CECIL0_9
                InterfaceImplementation interf = type.Interfaces[i];
                var newInterf = new InterfaceImplementation(interf.InterfaceType.Relink(Relinker, type));
                foreach (CustomAttribute attrib in interf.CustomAttributes)
                    newInterf.CustomAttributes.Add(attrib.Relink(Relinker, type));
                type.Interfaces[i] = newInterf;
#else
                TypeReference interf = type.Interfaces[i];
                TypeReference newInterf = interf.Relink(Relinker, type);
                type.Interfaces[i] = newInterf;
#endif
            }

            // Don't foreach when modifying the collection
            for (var i = 0; i < type.CustomAttributes.Count; i++)
                PatchRefsInCustomAttribute(type.CustomAttributes[i] = type.CustomAttributes[i].Relink(Relinker, type), type);

            foreach (PropertyDefinition prop in type.Properties)
            {
                prop.PropertyType = prop.PropertyType.Relink(Relinker, type);
                // Don't foreach when modifying the collection
                for (var i = 0; i < prop.CustomAttributes.Count; i++)
                    prop.CustomAttributes[i] = prop.CustomAttributes[i].Relink(Relinker, type);
            }

            foreach (EventDefinition eventDef in type.Events)
            {
                eventDef.EventType = eventDef.EventType.Relink(Relinker, type);
                for (var i = 0; i < eventDef.CustomAttributes.Count; i++)
                    eventDef.CustomAttributes[i] = eventDef.CustomAttributes[i].Relink(Relinker, type);
            }

            foreach (MethodDefinition method in type.Methods)
                PatchRefsInMethod(method);

            foreach (FieldDefinition field in type.Fields)
            {
                field.FieldType = field.FieldType.Relink(Relinker, type);
                // Don't foreach when modifying the collection
                for (var i = 0; i < field.CustomAttributes.Count; i++)
                    field.CustomAttributes[i] = field.CustomAttributes[i].Relink(Relinker, type);
            }

            for (var i = 0; i < type.NestedTypes.Count; i++)
                PatchRefsInType(type.NestedTypes[i]);
        }

        public virtual void PatchRefsInMethod(MethodDefinition method)
        {
            LogVerbose($"[VERBOSE] [PatchRefsInMethod] Patching refs in {method}");

            // Don't foreach when modifying the collection
            for (var i = 0; i < method.GenericParameters.Count; i++)
                method.GenericParameters[i] = method.GenericParameters[i].Relink(Relinker, method);

            foreach (ParameterDefinition param in method.Parameters)
            {
                param.ParameterType = param.ParameterType.Relink(Relinker, method);
                for (var i = 0; i < param.CustomAttributes.Count; i++)
                    PatchRefsInCustomAttribute(param.CustomAttributes[i] = param.CustomAttributes[i].Relink(Relinker, method), method);
            }

            for (var i = 0; i < method.CustomAttributes.Count; i++)
                PatchRefsInCustomAttribute(method.CustomAttributes[i] = method.CustomAttributes[i].Relink(Relinker, method), method);

            for (var i = 0; i < method.Overrides.Count; i++)
                method.Overrides[i] = (MethodReference)method.Overrides[i].Relink(Relinker, method);

            method.ReturnType = method.ReturnType.Relink(Relinker, method);

            for (var i = 0; i < method.MethodReturnType.CustomAttributes.Count; i++)
                PatchRefsInCustomAttribute(method.MethodReturnType.CustomAttributes[i] = method.MethodReturnType.CustomAttributes[i].Relink(Relinker, method), method);

            foreach (CustomDebugInformation dbgInfo in method.CustomDebugInformations)
                if (dbgInfo is AsyncMethodBodyDebugInformation asyncDbgInfo)
                    for (var i = 0; i < asyncDbgInfo.ResumeMethods.Count; i++)
                        asyncDbgInfo.ResumeMethods[i] = ((MethodReference)asyncDbgInfo.ResumeMethods[i].Relink(Relinker, method)).Resolve();

            if (method.Body == null) return;

            foreach (VariableDefinition var in method.Body.Variables)
                var.VariableType = var.VariableType.Relink(Relinker, method);

            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                if (handler.CatchType != null)
                    handler.CatchType = handler.CatchType.Relink(Relinker, method);

            MethodRewriter?.Invoke(this, method);

            var tmpAddrLocMap = new Dictionary<TypeReference, VariableDefinition>();

            MethodBody body = method.Body;

            for (var instri = 0; method.HasBody && instri < body.Instructions.Count; instri++)
            {
                Instruction instr = body.Instructions[instri];
                var operand = instr.Operand;

                // MonoMod-specific in-code flag setting / ...

                // TODO: Split out the MonoMod inline parsing.

                if (!MethodParser(this, body, instr, ref instri))
                    continue;

                // Before relinking, check for an existing forced call opcode mapping.
                OpCode forceCall = default;
                var hasForceCall = operand is MethodReference && (
                    ForceCallMap.TryGetValue((operand as MethodReference).GetID(), out forceCall) ||
                    ForceCallMap.TryGetValue((operand as MethodReference).GetID(simple: true), out forceCall)
                );

                // General relinking
                if (!(operand is ParameterDefinition) && operand is IMetadataTokenProvider)
                    operand = ((IMetadataTokenProvider)operand).Relink(Relinker, method);

                // Check again after relinking.
                if (!hasForceCall && operand is MethodReference)
                {
                    var hasForceCallRelinked =
                        ForceCallMap.TryGetValue((operand as MethodReference).GetID(), out OpCode forceCallRelinked) ||
                        ForceCallMap.TryGetValue((operand as MethodReference).GetID(simple: true), out forceCallRelinked)
                    ;
                    // If a relinked force call exists, prefer it over the existing forced call opcode.
                    // Otherwise keep the existing forced call opcode.
                    if (hasForceCallRelinked)
                    {
                        forceCall = forceCallRelinked;
                        hasForceCall = true;
                    }
                }

                // patch_ constructor fix: If referring to itself, refer to the original constructor.
                if (instr.OpCode == OpCodes.Call && operand is MethodReference &&
                    (((MethodReference)operand).Name == ".ctor" ||
                     ((MethodReference)operand).Name == ".cctor") &&
                    ((MethodReference)operand).FullName == method.FullName)
                {
                    // ((MethodReference) operand).Name = method.GetOriginalName();
                    // Above could be enough, but what about the metadata token?
                    operand = method.DeclaringType.FindMethod(method.GetID(name: method.GetOriginalName()));
                }

                // .ctor -> static method reference fix: newobj -> call
                if ((instr.OpCode == OpCodes.Newobj || instr.OpCode == OpCodes.Newarr) && operand is MethodReference &&
                    ((MethodReference)operand).Name != ".ctor")
                {
                    instr.OpCode = ((MethodReference)operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;

                    // field -> property reference fix: ld(s)fld(a) / st(s)fld(a) <-> call get / set
                }
                else if ((instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Stfld || instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldsflda || instr.OpCode == OpCodes.Stsfld) && operand is PropertyReference)
                {
                    PropertyDefinition prop = ((PropertyReference)operand).Resolve();
                    if (instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldsflda)
                        operand = prop.GetMethod;
                    else
                    {
                        operand = prop.SetMethod;
                    }
                    if (instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Ldsflda)
                        body.AppendGetAddr(instr, prop.PropertyType, tmpAddrLocMap);
                    instr.OpCode = ((MethodReference)operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;

                    // field <-> method reference fix: ld(s)fld / st(s)fld <-> call
                }
                else if ((instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Stfld) && operand is MethodReference)
                {
                    if (instr.OpCode == OpCodes.Ldflda)
                        body.AppendGetAddr(instr, ((PropertyReference)operand).PropertyType, tmpAddrLocMap);
                    instr.OpCode = ((MethodReference)operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;

                }
                else if ((instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldsflda || instr.OpCode == OpCodes.Stsfld) && operand is MethodReference)
                {
                    if (instr.OpCode == OpCodes.Ldsflda)
                        body.AppendGetAddr(instr, ((PropertyReference)operand).PropertyType, tmpAddrLocMap);
                    instr.OpCode = OpCodes.Call;

                }
                else if ((instr.OpCode == OpCodes.Callvirt || instr.OpCode == OpCodes.Call) && operand is FieldReference)
                {
                    // Setters don't return anything.
                    TypeReference returnType = ((MethodReference)instr.Operand).ReturnType;
                    var set = returnType == null || returnType.MetadataType == MetadataType.Void;
                    // This assumption is dangerous.
                    var instance = ((MethodReference)instr.Operand).HasThis;
                    if (instance)
                        instr.OpCode = set ? OpCodes.Stfld : OpCodes.Ldfld;
                    else
                        instr.OpCode = set ? OpCodes.Stsfld : OpCodes.Ldsfld;
                    // TODO: When should we emit ldflda / ldsflda?
                }

                // "general" static method <-> virtual method reference fix: call <-> callvirt
                else if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) && operand is MethodReference)
                {
                    if (hasForceCall)
                    {
                        instr.OpCode = forceCall;
                    }
                    else if ((operand as MethodReference)?.SafeResolve() != null && !body.IsBaseMethodCall(operand as MethodReference))
                    {
                        instr.OpCode = ((MethodReference)operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;
                    }
                }

                // Reference importing
                if (operand is IMetadataTokenProvider)
                    operand = method.Module.ImportReference((IMetadataTokenProvider)operand);

                instr.Operand = operand;

                MethodBodyRewriter?.Invoke(this, body, instr, instri);
            }
        }

        public virtual void PatchRefsInCustomAttribute(CustomAttribute attr, IGenericParameterProvider ctx)
        {
            // Try to resolve the method reference to work around Mono weirdness
            if (attr.Constructor.DeclaringType?.Scope == Module)
            {
                TypeDefinition resolvedType = attr.Constructor.DeclaringType?.SafeResolve();
                if (resolvedType != null)
                    attr.Constructor = resolvedType.FindMethod(attr.Constructor.GetID());
            }

            // Relink attribute arguments
            for (var i = 0; i < attr.ConstructorArguments.Count; i++)
            {
                CustomAttributeArgument arg = attr.ConstructorArguments[i];
                if (arg.Value is IMetadataTokenProvider mtp)
                {
                    attr.ConstructorArguments[i] = new CustomAttributeArgument(arg.Type, mtp.Relink(Relinker, ctx));
                }
            }

            for (var i = 0; i < attr.Properties.Count; i++)
            {
                CustomAttributeNamedArgument arg = attr.Properties[i];
                if (arg.Argument.Value is IMetadataTokenProvider mtp)
                {
                    attr.Properties[i] = new CustomAttributeNamedArgument(arg.Name, new CustomAttributeArgument(arg.Argument.Type, mtp.Relink(Relinker, ctx)));
                }
            }
        }

        #endregion

        #region Cleanup Pass
        public virtual void Cleanup(bool all = false)
        {
            for (var i = 0; i < Module.Types.Count; i++)
            {
                TypeDefinition type = Module.Types[i];
                if (all && (type.Namespace.StartsWith("MonoMod", StringComparison.Ordinal) || type.Name.StartsWith("MonoMod", StringComparison.Ordinal)))
                {
                    Log($"[Cleanup] Removing type {type.Name}");
                    Module.Types.RemoveAt(i);
                    i--;
                    continue;
                }
                CleanupType(type, all: all);
            }

            Collection<AssemblyNameReference> deps = Module.AssemblyReferences;
            for (var i = deps.Count - 1; i > -1; --i)
                if ((all && deps[i].Name.StartsWith("MonoMod", StringComparison.Ordinal)) ||
                    (RemovePatchReferences && deps[i].Name.EndsWith(".mm", StringComparison.Ordinal)))
                    deps.RemoveAt(i);
        }

        public virtual void CleanupType(TypeDefinition type, bool all = false)
        {
            Cleanup(type, all: all);


            foreach (PropertyDefinition prop in type.Properties)
                Cleanup(prop, all: all);

            foreach (MethodDefinition method in type.Methods)
                Cleanup(method, all: all);

            foreach (FieldDefinition field in type.Fields)
                Cleanup(field, all: all);

            foreach (EventDefinition eventDef in type.Events)
                Cleanup(eventDef, all: all);


            foreach (TypeDefinition nested in type.NestedTypes)
                CleanupType(nested, all: all);
        }

        public virtual void Cleanup(ICustomAttributeProvider cap, bool all = false)
        {
            Collection<CustomAttribute> attribs = cap.CustomAttributes;
            for (var i = attribs.Count - 1; i > -1; --i)
            {
                TypeReference attribType = attribs[i].AttributeType;
                if (ShouldCleanupAttrib?.Invoke(cap, attribType) ?? (
                    (attribType.Scope.Name == "MonoMod" || attribType.Scope.Name == "MonoMod.exe" || attribType.Scope.Name == "MonoMod.dll") ||
                    (attribType.FullName.StartsWith("MonoMod.MonoMod", StringComparison.Ordinal))
                ))
                {
                    attribs.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Default PostProcessor Pass
        public virtual void DefaultPostProcessor(MonoModder modder)
        {
            foreach (TypeDefinition type in Module.Types.ToArray())
                DefaultPostProcessType(type);

            if (CleanupEnabled)
                Cleanup(all: Environment.GetEnvironmentVariable("MONOMOD_CLEANUP_ALL") == "1");
        }

        public virtual void DefaultPostProcessType(TypeDefinition type)
        {
            if (PublicEverything || type.HasCustomAttribute("MonoMod.MonoModPublic"))
                type.SetPublic(true);

            RunCustomAttributeHandlers(type);

            foreach (EventDefinition eventDef in type.Events.ToArray())
            {
                if (PublicEverything || eventDef.HasCustomAttribute("MonoMod.MonoModPublic"))
                {
                    eventDef.SetPublic(true);
                    eventDef.AddMethod?.SetPublic(true);
                    eventDef.RemoveMethod?.SetPublic(true);
                    foreach (MethodDefinition method in eventDef.OtherMethods)
                        method.SetPublic(true);
                }

                RunCustomAttributeHandlers(eventDef);
            }

            foreach (PropertyDefinition prop in type.Properties.ToArray())
            {
                if (PublicEverything || prop.HasCustomAttribute("MonoMod.MonoModPublic"))
                {
                    prop.SetPublic(true);
                    prop.GetMethod?.SetPublic(true);
                    prop.SetMethod?.SetPublic(true);
                    foreach (MethodDefinition method in prop.OtherMethods)
                        method.SetPublic(true);
                }

                RunCustomAttributeHandlers(prop);
            }

            foreach (MethodDefinition method in type.Methods.ToArray())
            {
                if (PublicEverything || method.HasCustomAttribute("MonoMod.MonoModPublic"))
                    method.SetPublic(true);

                if (PreventInline && method.HasBody)
                {
                    method.NoInlining = true;
                    // Remove AggressiveInlining
                    method.ImplAttributes &= (MethodImplAttributes)0x0100;
                }

                method.FixShortLongOps();

                RunCustomAttributeHandlers(method);
            }

            foreach (FieldDefinition field in type.Fields.ToArray())
            {
                if (PublicEverything || field.HasCustomAttribute("MonoMod.MonoModPublic"))
                    field.SetPublic(true);

                RunCustomAttributeHandlers(field);
            }


            foreach (TypeDefinition nested in type.NestedTypes.ToArray())
                DefaultPostProcessType(nested);
        }
        #endregion

        #region MonoMod injected types
        public virtual TypeDefinition PatchWasHere()
        {
            for (var ti = 0; ti < Module.Types.Count; ti++)
            {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "WasHere")
                {
                    LogVerbose("[PatchWasHere] Type MonoMod.WasHere already existing");
                    return Module.Types[ti];
                }
            }
            LogVerbose("[PatchWasHere] Adding type MonoMod.WasHere");
            var wasHere = new TypeDefinition("MonoMod", "WasHere", TypeAttributes.Public | TypeAttributes.Class)
            {
                BaseType = Module.TypeSystem.Object
            };
            Module.Types.Add(wasHere);
            return wasHere;
        }

        protected MethodDefinition _mmOriginalCtor;
        public virtual MethodReference GetMonoModOriginalCtor()
        {
            if (_mmOriginalCtor != null && _mmOriginalCtor.Module != Module)
            {
                _mmOriginalCtor = null;
            }
            if (_mmOriginalCtor != null)
            {
                return _mmOriginalCtor;
            }

            TypeDefinition attrType = null;
            for (var ti = 0; ti < Module.Types.Count; ti++)
            {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModOriginal")
                {
                    attrType = Module.Types[ti];
                    for (var mi = 0; mi < attrType.Methods.Count; mi++)
                    {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic)
                        {
                            continue;
                        }
                        return _mmOriginalCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModOriginal] Adding MonoMod.MonoModOriginal");
            TypeReference tr_Attribute = FindType("System.Attribute");
            if (tr_Attribute != null)
            {
                tr_Attribute = Module.ImportReference(tr_Attribute);
            }
            else
            {
                tr_Attribute = Module.ImportReference(typeof(Attribute));
            }
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModOriginal", TypeAttributes.Public | TypeAttributes.Class)
            {
                BaseType = tr_Attribute
            };
            _mmOriginalCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmOriginalCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmOriginalCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmOriginalCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(
                ".ctor", Module.TypeSystem.Void, tr_Attribute)
            {
                HasThis = false
            }));
            _mmOriginalCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmOriginalCtor);
            Module.Types.Add(attrType);
            return _mmOriginalCtor;
        }

        protected MethodDefinition _mmOriginalNameCtor;
        public virtual MethodReference GetMonoModOriginalNameCtor()
        {
            if (_mmOriginalNameCtor != null && _mmOriginalNameCtor.Module != Module)
            {
                _mmOriginalNameCtor = null;
            }
            if (_mmOriginalNameCtor != null)
            {
                return _mmOriginalNameCtor;
            }

            TypeDefinition attrType = null;
            for (var ti = 0; ti < Module.Types.Count; ti++)
            {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModOriginalName")
                {
                    attrType = Module.Types[ti];
                    for (var mi = 0; mi < attrType.Methods.Count; mi++)
                    {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic)
                        {
                            continue;
                        }
                        return _mmOriginalNameCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModOriginalName] Adding MonoMod.MonoModOriginalName");
            TypeReference tr_Attribute = FindType("System.Attribute");
            if (tr_Attribute != null)
            {
                tr_Attribute = Module.ImportReference(tr_Attribute);
            }
            else
            {
                tr_Attribute = Module.ImportReference(typeof(Attribute));
            }
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModOriginalName", TypeAttributes.Public | TypeAttributes.Class)
            {
                BaseType = tr_Attribute
            };
            _mmOriginalNameCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmOriginalNameCtor.Parameters.Add(new ParameterDefinition("n", ParameterAttributes.None, Module.TypeSystem.String));
            _mmOriginalNameCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmOriginalNameCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmOriginalNameCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(
                ".ctor", Module.TypeSystem.Void, tr_Attribute)
            {
                HasThis = false
            }));
            _mmOriginalNameCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmOriginalNameCtor);
            Module.Types.Add(attrType);
            return _mmOriginalNameCtor;
        }

        protected MethodDefinition _mmAddedCtor;
        public virtual MethodReference GetMonoModAddedCtor()
        {
            if (_mmAddedCtor != null && _mmAddedCtor.Module != Module)
            {
                _mmAddedCtor = null;
            }
            if (_mmAddedCtor != null)
            {
                return _mmAddedCtor;
            }

            TypeDefinition attrType = null;
            for (var ti = 0; ti < Module.Types.Count; ti++)
            {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModAdded")
                {
                    attrType = Module.Types[ti];
                    for (var mi = 0; mi < attrType.Methods.Count; mi++)
                    {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic)
                        {
                            continue;
                        }
                        return _mmAddedCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModAdded] Adding MonoMod.MonoModAdded");
            TypeReference tr_Attribute = FindType("System.Attribute");
            if (tr_Attribute != null)
            {
                tr_Attribute = Module.ImportReference(tr_Attribute);
            }
            else
            {
                tr_Attribute = Module.ImportReference(typeof(Attribute));
            }
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModAdded", TypeAttributes.Public | TypeAttributes.Class)
            {
                BaseType = tr_Attribute
            };
            _mmAddedCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmAddedCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmAddedCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmAddedCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(
                ".ctor", Module.TypeSystem.Void, tr_Attribute)
            {
                HasThis = false
            }));
            _mmAddedCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmAddedCtor);
            Module.Types.Add(attrType);
            return _mmAddedCtor;
        }

        protected MethodDefinition _mmPatchCtor;
        public virtual MethodReference GetMonoModPatchCtor()
        {
            if (_mmPatchCtor != null && _mmPatchCtor.Module != Module)
            {
                _mmPatchCtor = null;
            }
            if (_mmPatchCtor != null)
            {
                return _mmPatchCtor;
            }

            TypeDefinition attrType = null;
            for (var ti = 0; ti < Module.Types.Count; ti++)
            {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModPatch")
                {
                    attrType = Module.Types[ti];
                    for (var mi = 0; mi < attrType.Methods.Count; mi++)
                    {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic)
                        {
                            continue;
                        }
                        return _mmPatchCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModPatch] Adding MonoMod.MonoModPatch");
            TypeReference tr_Attribute = FindType("System.Attribute");
            if (tr_Attribute != null)
            {
                tr_Attribute = Module.ImportReference(tr_Attribute);
            }
            else
            {
                tr_Attribute = Module.ImportReference(typeof(Attribute));
            }
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModPatch", TypeAttributes.Public | TypeAttributes.Class)
            {
                BaseType = tr_Attribute
            };
            _mmPatchCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmPatchCtor.Parameters.Add(new ParameterDefinition("name", ParameterAttributes.None, Module.TypeSystem.String));
            _mmPatchCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmPatchCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmPatchCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(
                ".ctor", Module.TypeSystem.Void, tr_Attribute)
            {
                HasThis = false
            }));
            _mmPatchCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmPatchCtor);
            Module.Types.Add(attrType);
            return _mmPatchCtor;
        }
        #endregion


        #region Helper methods
        /// <summary>
        /// Creates a new non-conflicting MetadataToken.
        /// </summary>
        /// <param name="type">The type of the new token.</param>
        /// <returns>A MetadataToken with an unique RID for the target module.</returns>
        public virtual MetadataToken GetMetadataToken(TokenType type)
        {
            /* Notes:
             * 
             * Mono.Cecil does a great job fixing tokens anyway.
             * 
             * The ModuleDef must be constructed with a reader, thus
             * from an image, as that is the only way a MetadataReader
             * gets assigned to the ModuleDef.
             * 
             * At the same time, the module has only got a file name when
             * it has been passed on from the image it has been created from.
             * 
             * Creating an image from a name-less stream results in an empty string.
             */
#if !CECIL0_9
            if (Module.FileName == null)
            {
                ++CurrentRID;
            }
            else
#endif
            {
                try
                {
                    while (Module.LookupToken(CurrentRID | (int)type) != null)
                    {
                        ++CurrentRID;
                    }
                }
                catch
                {
                    ++CurrentRID;
                }
            }
            return new MetadataToken(type, CurrentRID);
        }

        /// <summary>
        /// Checks if the method has a special name that is "allowed" to be patched.
        /// </summary>
        /// <returns><c>true</c> if the special name used in the method is allowed, <c>false</c> otherwise.</returns>
        /// <param name="method">Method to check.</param>
        /// <param name="targetType">Type to which the method will be added.</param>
        public virtual bool AllowedSpecialName(MethodDefinition method, TypeDefinition targetType = null)
        {
            if (method.HasCustomAttribute("MonoMod.MonoModAdded") || method.DeclaringType.HasCustomAttribute("MonoMod.MonoModAdded") ||
                (targetType?.HasCustomAttribute("MonoMod.MonoModAdded") ?? false))
            {
                return true;
            }

            // HOW NOT TO SOLVE ISSUES:
            // if (method.IsConstructor)
            //     return true; // We don't give a f**k anymore.

            // The legacy behaviour is required to not break anything. It's very, very finnicky.
            // In retrospect, taking the above "fix" into consideration, it was bound to fail as soon
            // as other ignored members were accessed from the new constructors.
            if (method.IsConstructor && (method.HasCustomAttributes || method.IsStatic))
            {
                if (method.IsStatic)
                    return true;
                // Overriding the constructor manually is generally a horrible idea, but who knows where it may be used.
                if (method.HasCustomAttribute("MonoMod.MonoModConstructor")) return true;
            }

            if (method.IsGetter || method.IsSetter)
                return true;

            if (method.Name.StartsWith("op_", StringComparison.Ordinal))
                return true;

            return !method.IsRuntimeSpecialName; // Formerly SpecialName. If something breaks, blame UnderRail.
        }

        public virtual bool MatchingConditionals(ICustomAttributeProvider cap, ModuleDefinition module)
            => MatchingConditionals(cap, module.Assembly.Name);
        public virtual bool MatchingConditionals(ICustomAttributeProvider cap, AssemblyNameReference asmName = null)
        {
            if (cap == null)
                return true;
            if (!cap.HasCustomAttributes)
                return true;

            var status = true;
            foreach (CustomAttribute attrib in cap.CustomAttributes)
            {
                if (attrib.AttributeType.FullName == "MonoMod.MonoModOnPlatform")
                {
                    var plats = (CustomAttributeArgument[])attrib.ConstructorArguments[0].Value;
                    for (var i = 0; i < plats.Length; i++)
                    {
                        if (PlatformDetection.OS.Is((OSKind)plats[i].Value))
                        {
                            // status &= true;
                            continue;
                        }
                    }
                    status &= plats.Length == 0;
                    continue;
                }

                if (attrib.AttributeType.FullName == "MonoMod.MonoModIfFlag")
                {
                    var flag = (string)attrib.ConstructorArguments[0].Value;
                    bool value;
                    if (!SharedData.TryGetValue(flag, out var valueObj) || !(valueObj is bool))
                        if (attrib.ConstructorArguments.Count == 2)
                            value = (bool)attrib.ConstructorArguments[1].Value;
                        else
                            value = true;
                    else
                        value = (bool)valueObj;
                    status &= value;
                    continue;
                }

                if (attrib.AttributeType.FullName == "MonoMod.MonoModTargetModule")
                {
                    var name = (string)attrib.ConstructorArguments[0].Value;
                    status &= asmName.Name == name || asmName.FullName == name;
                    continue;
                }
            }

            return status;
        }
        #endregion

    }
}
