using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonoMod.Utils;

#if NETSTANDARD
using static System.Reflection.IntrospectionExtensions;
using static System.Reflection.TypeExtensions;
#endif

namespace MonoMod {

    public delegate bool MethodParser(MonoModder modder, MethodBody body, Instruction instr, ref int instri);
    public delegate void MethodRewriter(MonoModder modder, MethodDefinition method);
    public delegate void MethodBodyRewriter(MonoModder modder, MethodBody body, Instruction instr, int instri);
    public delegate ModuleDefinition MissingDependencyResolver(MonoModder modder, ModuleDefinition main, string name, string fullName);
    public delegate void PostProcessor(MonoModder modder);
    public delegate void ModReadEventHandler(MonoModder modder, ModuleDefinition mod);

    public class RelinkMapEntry {
        public string Type;
        public string FindableID;

        public RelinkMapEntry() {
        }
        public RelinkMapEntry(string type, string findableID) {
            Type = type;
            FindableID = findableID;
        }
    }

    public enum DebugSymbolFormat {
        Auto,
        MDB,
        PDB
    }

    public class MonoModder : IDisposable {

        public static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;

        public static readonly Version Version = typeof(MonoModder).GetTypeInfo().Assembly.GetName().Version;

        // WasIDictionary and the _ IDictionaries are used when upgrading mods.

        [MonoMod__WasIDictionary__]
        public Dictionary<string, object> RelinkMap = new Dictionary<string, object>();
        public IDictionary<string, object> _RelinkMap { get { return RelinkMap; } set { RelinkMap = (Dictionary<string, object>) value; } }
        [MonoMod__WasIDictionary__]
        public Dictionary<string, ModuleDefinition> RelinkModuleMap = new Dictionary<string, ModuleDefinition>();
        public IDictionary<string, ModuleDefinition> _RelinkModuleMap { get { return RelinkModuleMap; } set { RelinkModuleMap = (Dictionary<string, ModuleDefinition>) value; } }
        public HashSet<string> SkipList = new HashSet<string>(EqualityComparer<string>.Default);

        [MonoMod__WasIDictionary__]
        public Dictionary<string, IMetadataTokenProvider> RelinkMapCache = new Dictionary<string, IMetadataTokenProvider>();
        public IDictionary<string, IMetadataTokenProvider> _RelinkMapCache { get { return RelinkMapCache; } set { RelinkMapCache = (Dictionary<string, IMetadataTokenProvider>) value; } }
        [MonoMod__WasIDictionary__]
        public Dictionary<string, TypeReference> RelinkModuleMapCache = new Dictionary<string, TypeReference>();
        public IDictionary<string, TypeReference> _RelinkModuleMapCache { get { return RelinkModuleMapCache; } set { RelinkModuleMapCache = (Dictionary<string, TypeReference>) value; } }

        public Dictionary<string, OpCode> ForceCallMap = new Dictionary<string, OpCode>();

        public ModReadEventHandler OnReadMod;
        public PostProcessor PostProcessors;

        [MonoMod__WasIDictionary__]
        public Dictionary<string, FastReflectionDelegate> CustomAttributeHandlers = new Dictionary<string, FastReflectionDelegate>();
        public IDictionary<string, FastReflectionDelegate> _CustomAttributeHandlers { get { return CustomAttributeHandlers; } set { CustomAttributeHandlers = (Dictionary<string, FastReflectionDelegate>) value; } }
        [MonoMod__WasIDictionary__]
        public Dictionary<string, FastReflectionDelegate> CustomMethodAttributeHandlers = new Dictionary<string, FastReflectionDelegate>();
        public IDictionary<string, FastReflectionDelegate> _CustomMethodAttributeHandlers { get { return CustomMethodAttributeHandlers; } set { CustomMethodAttributeHandlers = (Dictionary<string, FastReflectionDelegate>) value; } }

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

        [MonoMod__WasIDictionary__]
        public Dictionary<ModuleDefinition, List<ModuleDefinition>> DependencyMap = new Dictionary<ModuleDefinition, List<ModuleDefinition>>();
        public IDictionary<ModuleDefinition, List<ModuleDefinition>> _DependencyMap { get { return DependencyMap; } set { DependencyMap = (Dictionary<ModuleDefinition, List<ModuleDefinition>>) value; } }
        [MonoMod__WasIDictionary__]
        public Dictionary<string, ModuleDefinition> DependencyCache = new Dictionary<string, ModuleDefinition>();
        public IDictionary<string, ModuleDefinition> _DependencyCache { get { return DependencyCache; } set { DependencyCache = (Dictionary<string, ModuleDefinition>) value; } }

        public Func<ICustomAttributeProvider, TypeReference, bool> ShouldCleanupAttrib;

        public bool LogVerboseEnabled;
        public bool RelinkerCacheEnabled;
        public bool CleanupEnabled;
        public bool PublicEverything;

        public List<ModuleReference> Mods = new List<ModuleReference>();

        public bool Strict;
        public bool MissingDependencyThrow;
        public bool RemovePatchReferences;
        public bool PreventInline = false;
        public ReadingMode ReadingMode = ReadingMode.Immediate;
        public DebugSymbolFormat DebugSymbolOutputFormat = DebugSymbolFormat.Auto;

        public int CurrentRID = 0;

        protected IAssemblyResolver _assemblyResolver;
        public virtual IAssemblyResolver AssemblyResolver {
            get {
                if (_assemblyResolver == null) {
                    DefaultAssemblyResolver assemblyResolver = new DefaultAssemblyResolver();
                    foreach (string dir in DependencyDirs)
                        assemblyResolver.AddSearchDirectory(dir);
                    _assemblyResolver = assemblyResolver;
                }
                return _assemblyResolver;
            }
            set {
                _assemblyResolver = value;
            }
        }

        protected ReaderParameters _readerParameters;
        public virtual ReaderParameters ReaderParameters {
            get {
                if (_readerParameters == null) {
                    _readerParameters = new ReaderParameters(ReadingMode) {
                        AssemblyResolver = AssemblyResolver,
                        ReadSymbols = true
                    };
                }
                return _readerParameters;
            }
            set {
                _readerParameters = value;
            }
        }

        protected WriterParameters _writerParameters;
        public virtual WriterParameters WriterParameters {
            get {
                if (_writerParameters == null) {
                    bool pdb = DebugSymbolOutputFormat == DebugSymbolFormat.PDB;
                    bool mdb = DebugSymbolOutputFormat == DebugSymbolFormat.MDB;
                    if (DebugSymbolOutputFormat == DebugSymbolFormat.Auto) {
                        if (((int) PlatformHelper.Current & (int) Platform.Windows) == (int) Platform.Windows)
                            pdb = true;
                        else mdb = true;
                    }
                    _writerParameters = new WriterParameters() {
                        WriteSymbols = true,
                        SymbolWriterProvider =
#if !CECIL0_9
                            pdb ? new NativePdbWriterProvider() :
#else
                            pdb ? new PdbWriterProvider() :
#endif
                            mdb ? new MdbWriterProvider() :
                            (ISymbolWriterProvider) null
                    };
                }
                return _writerParameters;
            }
            set {
                _writerParameters = value;
            }
        }

        protected string[] _GACPaths;
        public string[] GACPaths {
            get {
                if (_GACPaths != null)
                    return _GACPaths;

                if (!IsMono) {
                    // C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xml
                    string path = Path.Combine(Environment.GetEnvironmentVariable("windir"), "Microsoft.NET");
                    path = Path.Combine(path, "assembly");
                    _GACPaths = new string[] {
                        Path.Combine(path, "GAC_32"),
                        Path.Combine(path, "GAC_64"),
                        Path.Combine(path, "GAC_MSIL")
                    };

                } else {
                    List<string> paths = new List<string>();
                    string gac = Path.Combine(
                        Path.GetDirectoryName(
                            Path.GetDirectoryName(typeof(object).GetTypeInfo().Module.FullyQualifiedName)
                        ),
                        "gac"
                    );
                    if (Directory.Exists(gac))
                        paths.Add(gac);

                    string prefixesEnv = Environment.GetEnvironmentVariable("MONO_GAC_PREFIX");
                    if (!string.IsNullOrEmpty(prefixesEnv)) {
                        string[] prefixes = prefixesEnv.Split(Path.PathSeparator);
                        foreach (string prefix in prefixes) {
                            if (string.IsNullOrEmpty(prefix))
                                continue;

                            string path = prefix;
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
            set {
                _GACPaths = value;
            }
        }

        public MonoModder() {
            MethodParser = DefaultParser;

            MissingDependencyResolver = DefaultMissingDependencyResolver;

            PostProcessors += DefaultPostProcessor;

            string dependencyDirsEnv = Environment.GetEnvironmentVariable("MONOMOD_DEPDIRS");
            if (!string.IsNullOrEmpty(dependencyDirsEnv)) {
                foreach (string dir in dependencyDirsEnv.Split(Path.PathSeparator).Select(dir => dir.Trim())) {
                    (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(dir);
                    DependencyDirs.Add(dir);
                }
            }
            LogVerboseEnabled = Environment.GetEnvironmentVariable("MONOMOD_LOG_VERBOSE") == "1";
            RelinkerCacheEnabled = Environment.GetEnvironmentVariable("MONOMOD_RELINKER_CACHED") == "1";
            CleanupEnabled = Environment.GetEnvironmentVariable("MONOMOD_CLEANUP") != "0";
            PublicEverything = Environment.GetEnvironmentVariable("MONOMOD_PUBLIC_EVERYTHING") == "1";
            PreventInline = Environment.GetEnvironmentVariable("MONOMOD_PREVENTINLINE") == "1";
            Strict = Environment.GetEnvironmentVariable("MONOMOD_STRICT") == "1";
            MissingDependencyThrow = Environment.GetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW") != "0";
            RemovePatchReferences = Environment.GetEnvironmentVariable("MONOMOD_DEPENDENCY_REMOVE_PATCH") != "0";

            MonoModRulesManager.Register(this);
        }

        public virtual void ClearCaches(bool all = false, bool shareable = false, bool moduleSpecific = false) {
            if (all || shareable) {
#if !CECIL0_9
                foreach (KeyValuePair<string, ModuleDefinition> dep in DependencyCache)
                    dep.Value.Dispose();
#endif
                DependencyCache.Clear();
            }

            if (all || moduleSpecific) {
                RelinkMapCache.Clear();
                RelinkModuleMapCache.Clear();
            }
        }

        public virtual void Dispose() {
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

        public virtual void Log(object value) {
            Log(value.ToString());
        }
        public virtual void Log(string text) {
            Console.Write("[MonoMod] ");
            Console.WriteLine(text);
        }

        public virtual void LogVerbose(object value) {
            if (!LogVerboseEnabled)
                return;
            Log(value);
        }
        public virtual void LogVerbose(string text) {
            if (!LogVerboseEnabled)
                return;
            Log(text);
        }

        /// <summary>
        /// Reads the main module from the Input stream / InputPath file to Module.
        /// </summary>
        public virtual void Read() {
            if (Module == null) {
                if (Input != null) {
                    Log("Reading input stream into module.");
                    Module = MonoModExt.ReadModule(Input, GenReaderParameters(true));
                } else if (InputPath != null) {
                    Log("Reading input file into module.");
                    (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(Path.GetDirectoryName(InputPath));
                    DependencyDirs.Add(Path.GetDirectoryName(InputPath));
                    Module = MonoModExt.ReadModule(InputPath, GenReaderParameters(true, InputPath));
                }

                string modsEnv = Environment.GetEnvironmentVariable("MONOMOD_MODS");
                if (!string.IsNullOrEmpty(modsEnv)) {
                    foreach (string path in modsEnv.Split(Path.PathSeparator).Select(path => path.Trim())) {
                        ReadMod(path);
                    }
                }
            }
        }

        public virtual void MapDependencies() {
            foreach (ModuleDefinition mod in Mods)
                MapDependencies(mod);
            MapDependencies(Module);
        }
        public virtual void MapDependencies(ModuleDefinition main) {
            if (DependencyMap.ContainsKey(main))
                return;
            DependencyMap[main] = new List<ModuleDefinition>();

            foreach (AssemblyNameReference dep in main.AssemblyReferences)
                MapDependency(main, dep);
        }
        public virtual void MapDependency(ModuleDefinition main, AssemblyNameReference depRef) {
            MapDependency(main, depRef.Name, depRef.FullName, depRef);
        }
        public virtual void MapDependency(ModuleDefinition main, string name, string fullName = null, AssemblyNameReference depRef = null) {
            List<ModuleDefinition> mapped;
            if (!DependencyMap.TryGetValue(main, out mapped))
                DependencyMap[main] = mapped = new List<ModuleDefinition>();

            ModuleDefinition dep;
            if (fullName != null && (
                DependencyCache.TryGetValue(fullName, out dep) ||
                DependencyCache.TryGetValue(fullName + " [RT:" + main.RuntimeVersion + "]", out dep)
            )) {
                LogVerbose($"[MapDependency] {main.Name} -> {dep.Name} (({fullName}), ({name})) from cache");
                mapped.Add(dep);
                MapDependencies(dep);
                return;
            }

            if (DependencyCache.TryGetValue(name, out dep) ||
                DependencyCache.TryGetValue(name + " [RT:" + main.RuntimeVersion + "]", out dep)
            ) {
                LogVerbose($"[MapDependency] {main.Name} -> {dep.Name} ({name}) from cache");
                mapped.Add(dep);
                MapDependencies(dep);
                return;
            }

            // Used to fix Mono.Cecil.pdb being loaded instead of Mono.Cecil.Pdb.dll
            string ext = Path.GetExtension(name).ToLowerInvariant();
            bool nameRisky = ext == "pdb" || ext == "mdb";

            string path = null;
            foreach (string depDir in DependencyDirs) {
                path = Path.Combine(depDir, name + ".dll");
                if (!File.Exists(path))
                    path = Path.Combine(depDir, name + ".exe");
                if (!File.Exists(path) && !nameRisky)
                    path = Path.Combine(depDir, name);
                if (File.Exists(path)) break;
                else path = null;
            }

            // If we've got an AssemblyNameReference, use it to resolve the module.
            if (path == null && depRef != null) {
                try {
                    dep = AssemblyResolver.Resolve(depRef)?.MainModule;
                } catch { }
                if (dep != null)
#if !CECIL0_9
                    path = dep.FileName;
#else
                    path = dep.FullyQualifiedName;
#endif
            }

            // Manually check in GAC
            if (path == null) {
                foreach (string gacpath in GACPaths) {
                    path = Path.Combine(gacpath, name);

                    if (Directory.Exists(path)) {
                        string[] versions = Directory.GetDirectories(path);
                        int highest = 0;
                        int highestIndex = 0;
                        for (int i = 0; i < versions.Length; i++) {
                            string versionPath = versions[i];
                            if (versionPath.StartsWith(path))
                                versionPath = versionPath.Substring(path.Length + 1);
                            Match versionMatch = Regex.Match(versionPath, "\\d+");
                            if (!versionMatch.Success) {
                                continue;
                            }
                            int version = int.Parse(versionMatch.Value);
                            if (version > highest) {
                                highest = version;
                                highestIndex = i;
                            }
                            // Maybe check minor versions?
                        }
                        path = Path.Combine(versions[highestIndex], name + ".dll");
                        break;
                    } else {
                        path = null;
                    }
                }
            }

            // Try to use the AssemblyResolver with the full name (or even partial name).
            if (path == null) {
                try {
                    dep = AssemblyResolver.Resolve(new AssemblyNameReference(fullName ?? name, new Version(0, 0, 0, 0)))?.MainModule;
                } catch { }
                if (dep != null)
#if !CECIL0_9
                    path = dep.FileName;
#else
                    path = dep.FullyQualifiedName;
#endif
            }

#if !NETSTANDARD
            // Check if available in GAC
            // Note: This is a fallback as MonoMod depends on a low version of the .NET Framework.
            // This unfortunately breaks ReflectionOnlyLoad on targets higher than the MonoMod target.
            if (path == null && fullName != null) {
                System.Reflection.Assembly asm = null;
                try {
                    asm = System.Reflection.Assembly.ReflectionOnlyLoad(fullName);
                } catch { }
                path = asm?.Location;
            }
#endif

            if (dep == null) {
                if (path != null && File.Exists(path)) {
                    dep = MonoModExt.ReadModule(path, GenReaderParameters(false, path));
                } else if ((dep = MissingDependencyResolver?.Invoke(this, main, name, fullName)) == null) {
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
        public virtual ModuleDefinition DefaultMissingDependencyResolver(MonoModder mod, ModuleDefinition main, string name, string fullName) {
            if (MissingDependencyThrow && Environment.GetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW") == "0") {
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
        /// <param name="output">Output stream. If none given, default Output will be used.</param>
        public virtual void Write(Stream output = null, string outputPath = null) {
            output = output ?? Output;
            outputPath = outputPath ?? OutputPath;

            PatchRefsInType(PatchWasHere());

            if (output != null) {
                Log("[Write] Writing modded module into output stream.");
                Module.Write(output, WriterParameters);
            } else {
                Log("[Write] Writing modded module into output file.");
                Module.Write(outputPath, WriterParameters);
            }
        }

        public virtual ReaderParameters GenReaderParameters(bool mainModule, string path = null) {
            ReaderParameters _rp = ReaderParameters;
            ReaderParameters rp = new ReaderParameters(_rp.ReadingMode);
            rp.AssemblyResolver = _rp.AssemblyResolver;
            rp.MetadataResolver = _rp.MetadataResolver;
#if !CECIL0_9
            rp.MetadataImporterProvider = _rp.MetadataImporterProvider;
            rp.ReflectionImporterProvider = _rp.ReflectionImporterProvider;
#endif
            rp.SymbolStream = _rp.SymbolStream;
            rp.SymbolReaderProvider = _rp.SymbolReaderProvider;
            rp.ReadSymbols = _rp.ReadSymbols;

            if (path != null && !File.Exists(path + ".mdb") && !File.Exists(Path.ChangeExtension(path, "pdb")))
                rp.ReadSymbols = false;

            return rp;
        }


        public virtual void ReadMod(string path) {
            if (Directory.Exists(path)) {
                Log($"[ReadMod] Loading mod dir: {path}");
                string mainName = Module.Name.Substring(0, Module.Name.Length - 3);
                string mainNameSpaceless = mainName.Replace(" ", "");
                if (!DependencyDirs.Contains(path)) {
                    (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(path);
                    DependencyDirs.Add(path);
                }
                foreach (string modFile in Directory.GetFiles(path))
                    if ((Path.GetFileName(modFile).StartsWith(mainName) ||
                        Path.GetFileName(modFile).StartsWith(mainNameSpaceless)) &&
                        modFile.ToLower().EndsWith(".mm.dll"))
                        ReadMod(modFile);
                return;
            }

            Log($"[ReadMod] Loading mod: {path}");
            ModuleDefinition mod = MonoModExt.ReadModule(path, GenReaderParameters(false, path));
            string dir = Path.GetDirectoryName(path);
            if (!DependencyDirs.Contains(dir)) {
                (AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(dir);
                DependencyDirs.Add(dir);
            }
            Mods.Add(mod);
            OnReadMod?.Invoke(this, mod);
        }
        public virtual void ReadMod(Stream stream) {
            Log($"[ReadMod] Loading mod: stream#{(uint) stream.GetHashCode()}");
            ModuleDefinition mod = MonoModExt.ReadModule(stream, GenReaderParameters(false));
            Mods.Add(mod);
            OnReadMod?.Invoke(this, mod);
        }

        public virtual void ParseRules(ModuleDefinition mod) {
            TypeDefinition rulesType = mod.GetType("MonoMod.MonoModRules");
            Type rulesTypeMMILRT = null;
            if (rulesType != null) {
                rulesTypeMMILRT = MonoModRulesManager.ExecuteRules(this, rulesType);
                // Finally, remove the type, otherwise it'll easily conflict with other mods' rules.
                mod.Types.Remove(rulesType);
            }

            // Rule parsing pass: Check for MonoModHook and similar attributes
            foreach (TypeDefinition type in mod.Types)
                ParseRulesInType(type, rulesTypeMMILRT);
        }

        public virtual void ParseRulesInType(TypeDefinition type, Type rulesTypeMMILRT = null) {
            string typeName = type.GetPatchFullName();

            if (!type.MatchingConditionals(Module))
                return;

            CustomAttribute caHandler;

            caHandler = type.GetMMAttribute("CustomAttributeAttribute");
            if (caHandler != null)
                CustomAttributeHandlers[type.FullName] = rulesTypeMMILRT.GetMethod((string) caHandler.ConstructorArguments[0].Value).GetFastDelegate();

            caHandler = type.GetMMAttribute("CustomMethodAttributeAttribute");
            if (caHandler != null)
                CustomMethodAttributeHandlers[type.FullName] = rulesTypeMMILRT.GetMethod((string) caHandler.ConstructorArguments[0].Value).GetFastDelegate();

            CustomAttribute hook;

            for (hook = type.GetMMAttribute("Hook"); hook != null; hook = type.GetNextMMAttribute("Hook"))
                ParseLinkFrom(type, hook);
            for (hook = type.GetMMAttribute("LinkFrom"); hook != null; hook = type.GetNextMMAttribute("LinkFrom"))
                ParseLinkFrom(type, hook);
            for (hook = type.GetMMAttribute("LinkTo"); hook != null; hook = type.GetNextMMAttribute("LinkTo"))
                ParseLinkTo(type, hook);

            if (type.HasMMAttribute("Ignore"))
                return;

            foreach (MethodDefinition method in type.Methods) {
                if (!method.MatchingConditionals(Module))
                    continue;

                for (hook = method.GetMMAttribute("Hook"); hook != null; hook = method.GetNextMMAttribute("Hook"))
                    ParseLinkFrom(method, hook);
                for (hook = method.GetMMAttribute("LinkFrom"); hook != null; hook = method.GetNextMMAttribute("LinkFrom"))
                    ParseLinkFrom(method, hook);
                for (hook = method.GetMMAttribute("LinkTo"); hook != null; hook = method.GetNextMMAttribute("LinkTo"))
                    ParseLinkTo(method, hook);

                if (method.HasMMAttribute("ForceCall"))
                    ForceCallMap[method.GetFindableID()] = OpCodes.Call;
                else if (method.HasMMAttribute("ForceCallvirt"))
                    ForceCallMap[method.GetFindableID()] = OpCodes.Callvirt;
            }

            foreach (FieldDefinition field in type.Fields) {
                if (!field.MatchingConditionals(Module))
                    continue;

                for (hook = field.GetMMAttribute("Hook"); hook != null; hook = field.GetNextMMAttribute("Hook"))
                    ParseLinkFrom(field, hook);
                for (hook = field.GetMMAttribute("LinkFrom"); hook != null; hook = field.GetNextMMAttribute("LinkFrom"))
                    ParseLinkFrom(field, hook);
                for (hook = field.GetMMAttribute("LinkTo"); hook != null; hook = field.GetNextMMAttribute("LinkTo"))
                    ParseLinkTo(field, hook);
            }

            foreach (PropertyDefinition prop in type.Properties) {
                if (!prop.MatchingConditionals(Module))
                    continue;

                for (hook = prop.GetMMAttribute("Hook"); hook != null; hook = prop.GetNextMMAttribute("Hook"))
                    ParseLinkFrom(prop, hook);
                for (hook = prop.GetMMAttribute("LinkFrom"); hook != null; hook = prop.GetNextMMAttribute("LinkFrom"))
                    ParseLinkFrom(prop, hook);
                for (hook = prop.GetMMAttribute("LinkTo"); hook != null; hook = prop.GetNextMMAttribute("LinkTo"))
                    ParseLinkTo(prop, hook);
            }

            foreach (TypeDefinition nested in type.NestedTypes)
                ParseRulesInType(nested, rulesTypeMMILRT);
        }

        public virtual void ParseLinkFrom(MemberReference target, CustomAttribute hook) {
            string from = (string) hook.ConstructorArguments[0].Value;

            object to;
            if (target is TypeReference)
                to = ((TypeReference) target).GetPatchFullName();
            else if (target is MethodReference)
                to = new RelinkMapEntry(
                    ((MethodReference) target).DeclaringType.GetPatchFullName(),
                    ((MethodReference) target).GetFindableID(withType: false)
                );
            else if (target is FieldReference)
                to = new RelinkMapEntry(
                    ((FieldReference) target).DeclaringType.GetPatchFullName(),
                    ((FieldReference) target).Name
                );
            else if (target is PropertyReference)
                to = new RelinkMapEntry(
                    ((PropertyReference) target).DeclaringType.GetPatchFullName(),
                    ((PropertyReference) target).Name
                );
            else
                return;

            RelinkMap[from] = to;
        }

        public virtual void ParseLinkTo(MemberReference from, CustomAttribute hook) {
            string fromID = (from as MethodReference)?.GetFindableID() ?? from.GetPatchFullName();
            if (hook.ConstructorArguments.Count == 1)
                RelinkMap[fromID] = (string) hook.ConstructorArguments[0].Value;
            else
                RelinkMap[fromID] = new RelinkMapEntry(
                    (string) hook.ConstructorArguments[0].Value,
                    (string) hook.ConstructorArguments[1].Value
                );
        }

        public virtual void RunCustomAttributeHandlers(ICustomAttributeProvider cap) {
            if (!cap.HasCustomAttributes)
                return;

            foreach (CustomAttribute attrib in cap.CustomAttributes) {
                FastReflectionDelegate handler;
                if (CustomAttributeHandlers.TryGetValue(attrib.AttributeType.FullName, out handler))
                    handler?.Invoke(null, cap, attrib);
                if (cap is MethodReference && CustomMethodAttributeHandlers.TryGetValue(attrib.AttributeType.FullName, out handler))
                    handler?.Invoke(null, (MethodDefinition) cap, attrib);
            }
        }


        /// <summary>
        /// Automatically mods the module, loading Input, writing the modded module to Output.
        /// </summary>
        public virtual void AutoPatch() {
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

            if (PostProcessors != null) {
                Delegate[] pps = PostProcessors.GetInvocationList();
                for (int i = 0; i < pps.Length; i++) {
                    Log($"[PostProcessor] PostProcessor pass #{i + 1}");
                    ((PostProcessor) pps[i])?.Invoke(this);
                }
            }
        }

        public virtual IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            try {
                // TODO: Handle mtp being deleted but being hooked in a better, Strict-compatible way.
                return PostRelinker(
                    MainRelinker(mtp, context) ?? mtp,
                    context
                );
            } catch (Exception e) {
                throw new RelinkFailedException(null, e, mtp, context);
            }
        }
        public virtual IMetadataTokenProvider MainRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
             if (mtp is TypeReference) {
                TypeReference type = (TypeReference) mtp;

                // Type isn't coming from a mod module - just return the original.
                if (!Mods.Contains(type.Module))
                    return Module.ImportReference(type);

                // Type **refrence** is coming from a mod module - resolve it just to be safe.
                type = type.SafeResolve() ?? type;
                TypeReference found = FindTypeDeep(type.GetPatchFullName());

                if (found == null) {
                    if (RelinkMap.ContainsKey(type.FullName))
                        return null; // Let the post-relinker handle this.
                    throw new RelinkTargetNotFoundException(mtp, context);
                }
                return Module.ImportReference(found);
            }

            if (mtp is FieldReference || mtp is MethodReference || mtp is PropertyReference || mtp is EventReference)
                // Don't relink those. It'd be useful to f.e. link to member B instead of member A.
                // MonoModExt already handles the default "deep" relinking.
                return Module.ImportReference(mtp);

            throw new InvalidOperationException($"MonoMod default relinker can't handle metadata token providers of the type {mtp.GetType()}");
        }
        public virtual IMetadataTokenProvider PostRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            // The post relinker doesn't care if it can't handle a specific metadata token provider type; Just run ResolveRelinkTarget
            return ResolveRelinkTarget(mtp) ?? mtp;
        }

        public virtual IMetadataTokenProvider ResolveRelinkTarget(IMetadataTokenProvider mtp, bool relink = true, bool relinkModule = true) {
            string name;
            string nameAlt = null;
            if (mtp is TypeReference) {
                name = ((TypeReference) mtp).FullName;
            } else if (mtp is MethodReference) {
                name = ((MethodReference) mtp).GetFindableID(withType: true);
                nameAlt = ((MethodReference) mtp).GetFindableID(simple: true);
            } else if (mtp is FieldReference) {
                name = ((FieldReference) mtp).FullName;
            } else if (mtp is PropertyReference) {
                name = ((PropertyReference) mtp).FullName;
            } else
                return null;

            if (RelinkMapCache.TryGetValue(name, out IMetadataTokenProvider cached))
                return cached;

            object val;
            if (relink && (
                RelinkMap.TryGetValue(name, out val) ||
                (nameAlt != null && RelinkMap.TryGetValue(nameAlt, out val))
            )) {
                // If the value already is a mtp, import and cache the imported reference.
                if (val is IMetadataTokenProvider)
                    return RelinkMapCache[name] = Module.ImportReference((IMetadataTokenProvider) val);

                if (val is RelinkMapEntry) {
                    string typeName = ((RelinkMapEntry) val).Type as string;
                    string findableID = ((RelinkMapEntry) val).FindableID as string;

                    TypeDefinition type = FindTypeDeep(typeName)?.SafeResolve();
                    if (type == null)
                        return RelinkMapCache[name] = ResolveRelinkTarget(mtp, false, relinkModule);

                    val =
                        type.FindMethod(findableID) ??
                        type.FindField(findableID) ??
                        type.FindProperty(findableID) ??
                        (object) null
                    ;
                    if (val == null) {
                        if (Strict)
                            throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} ({typeName}, {findableID}) (remap: {mtp})", mtp);
                        else
                            return null;
                    }
                    return RelinkMapCache[name] = Module.ImportReference((IMetadataTokenProvider) val);
                }

                if (val is string && mtp is TypeReference) {
                    IMetadataTokenProvider found = FindTypeDeep((string) val);
                    if (found == null) {
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
                    return RelinkMapCache[name] = (IMetadataTokenProvider) val;

                throw new InvalidOperationException($"MonoMod doesn't support RelinkMap value of type {val.GetType()} (remap: {mtp})");
            }


            if (relinkModule && mtp is TypeReference) {
                TypeReference type;
                if (RelinkModuleMapCache.TryGetValue(name, out type))
                    return type;
                type = (TypeReference) mtp;

                ModuleDefinition scope;
                if (RelinkModuleMap.TryGetValue(type.Scope.Name, out scope)) {
                    TypeReference found = scope.GetType(type.FullName);
                    if (found == null) {
                        if (Strict)
                            throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} {type.FullName} (remap: {mtp})", mtp);
                        else
                            return null;
                    }
                    return RelinkModuleMapCache[name] = Module.ImportReference(found);
                }

                return RelinkModuleMapCache[name] = Module.ImportReference(type);
            }

            return null;
        }


        public virtual bool DefaultParser(MonoModder mod, MethodBody body, Instruction instr, ref int instri) {
            return true;
        }


        public virtual TypeReference FindType(string name)
            => FindType(Module, name, new Stack<ModuleDefinition>()) ?? Module.GetType(name, false);
        public virtual TypeReference FindType(string name, bool runtimeName)
            => FindType(Module, name, new Stack<ModuleDefinition>()) ?? Module.GetType(name, runtimeName);
        protected virtual TypeReference FindType(ModuleDefinition main, string fullName, Stack<ModuleDefinition> crawled) {
            TypeReference type;
            if ((type = main.GetType(fullName, false)) != null)
                return type;
            if (fullName.StartsWith("<PrivateImplementationDetails>/"))
                return null;
            if (crawled.Contains(main))
                return null;
            crawled.Push(main);
            foreach (ModuleDefinition dep in DependencyMap[main])
                if (!(RemovePatchReferences && dep.Assembly.Name.Name.EndsWith(".mm")) && (type = FindType(dep, fullName, crawled)) != null)
                    return type;
            return null;
        }
        public virtual TypeReference FindTypeDeep(string name) {
            TypeReference type = FindType(name, false);
            if (type != null)
                return type;

            // Check in the dependencies of the mod modules.
            Stack<ModuleDefinition> crawled = new Stack<ModuleDefinition>();
            // Set type to null so that an actual break condition exists
            type = null;
            foreach (ModuleDefinition mod in Mods)
                foreach (ModuleDefinition dep in DependencyMap[mod])
                    if ((type = FindType(dep, name, crawled)) != null) {
                        // Type may come from a dependency. If the assembly reference is missing, add.
                        AssemblyNameReference dllRef = type.Scope as AssemblyNameReference;
                        if (dllRef != null && !Module.AssemblyReferences.Any(n => n.Name == dllRef.Name))
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
        public virtual void PrePatchModule(ModuleDefinition mod) {
            foreach (TypeDefinition type in mod.Types)
                PrePatchType(type);

            foreach (ModuleReference @ref in mod.ModuleReferences)
                if (!Module.ModuleReferences.Contains(@ref))
                    Module.ModuleReferences.Add(@ref);

            foreach (Resource res in mod.Resources)
                if (res is EmbeddedResource) 
                    Module.Resources.Add(new EmbeddedResource(
                        res.Name.StartsWith(mod.Assembly.Name.Name) ?
                            Module.Assembly.Name.Name + res.Name.Substring(mod.Assembly.Name.Name.Length) :
                            res.Name,
                        res.Attributes,
                        ((EmbeddedResource) res).GetResourceData()
                    ));
        }

        /// <summary>
        /// Patches the type (adds new types).
        /// </summary>
        /// <param name="type">Type to patch into the input module.</param>
        public virtual void PrePatchType(TypeDefinition type, bool forceAdd = false) {
            string typeName = type.GetPatchFullName();

            // Fix legacy issue: Copy / inline any used modifiers.
            if ((type.Namespace != "MonoMod" && type.HasMMAttribute("Ignore")) || SkipList.Contains(typeName) || !type.MatchingConditionals(Module))
                return;
            // ... Except MonoModRules
            if (type.FullName == "MonoMod.MonoModRules" && !forceAdd)
                return;

            // Check if type exists in target module or dependencies.
            TypeReference targetType = forceAdd ? null : Module.GetType(typeName, false); // For PrePatch, we need to check in the target assembly only
            TypeDefinition targetTypeDef = targetType?.SafeResolve();
            if (type.HasMMAttribute("Replace") || type.HasMMAttribute("Remove")) {
                if (targetTypeDef != null) {
                    if (targetTypeDef.DeclaringType == null)
                        Module.Types.Remove(targetTypeDef);
                    else
                        targetTypeDef.DeclaringType.NestedTypes.Remove(targetTypeDef);
                }
                if (type.HasMMAttribute("Remove"))
                    return;
            } else if (targetType != null) {
                PrePatchNested(type);
                return;
            }

            // Add the type.
            LogVerbose($"[PrePatchType] Adding {typeName} to the target module.");

            TypeDefinition newType = new TypeDefinition(type.Namespace, type.Name, type.Attributes, type.BaseType);

            foreach (GenericParameter genParam in type.GenericParameters)
                newType.GenericParameters.Add(genParam.Clone());

            foreach (var interf in type.Interfaces)
                newType.Interfaces.Add(interf);

            newType.ClassSize = type.ClassSize;
            if (type.DeclaringType != null) {
                // The declaring type is existing as this is being called nestedly.
                newType.DeclaringType = type.DeclaringType.Relink(Relinker, newType).Resolve();
                newType.DeclaringType.NestedTypes.Add(newType);
            } else {
                Module.Types.Add(newType);
            }
            newType.PackingSize = type.PackingSize;
            newType.SecurityDeclarations.AddRange(type.SecurityDeclarations);

            // When adding MonoModAdded, try to reuse the just added MonoModAdded.
            newType.AddAttribute(GetMonoModAddedCtor());

            targetType = newType;
            
            PrePatchNested(type);
        }

        protected virtual void PrePatchNested(TypeDefinition type) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PrePatchType(type.NestedTypes[i]);
            }
        }
#endregion

#region Patch Pass
        /// <summary>
        /// Patches the module (adds new type members).
        /// </summary>
        /// <param name="mod">Mod to patch into the input module.</param>
        public virtual void PatchModule(ModuleDefinition mod) {
            foreach (TypeDefinition type in mod.Types)
                if (
                    (type.Namespace == "MonoMod" || type.Namespace.StartsWith("MonoMod.")) &&
                    type.BaseType.FullName == "System.Attribute"
                   )
                    PatchType(type);

            foreach (TypeDefinition type in mod.Types)
                if (!(
                    (type.Namespace == "MonoMod" || type.Namespace.StartsWith("MonoMod.")) &&
                    type.BaseType.FullName == "System.Attribute"
                   ))
                    PatchType(type);
        }

        /// <summary>
        /// Patches the type (adds new members).
        /// </summary>
        /// <param name="type">Type to patch into the input module.</param>
        public virtual void PatchType(TypeDefinition type) {
            string typeName = type.GetPatchFullName();

            TypeReference targetType = Module.GetType(typeName, false);
            if (targetType == null) return; // Type should've been added or removed accordingly.
            TypeDefinition targetTypeDef = targetType?.SafeResolve();

            if ((type.Namespace != "MonoMod" && type.HasMMAttribute("Ignore")) || // Fix legacy issue: Copy / inline any used modifiers.
                SkipList.Contains(typeName) ||
                !type.MatchingConditionals(Module)) {

                if (type.HasMMAttribute("Ignore") && targetTypeDef != null) {
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

            HashSet<MethodDefinition> propMethods = new HashSet<MethodDefinition>(); // In the Patch pass, prop methods exist twice.
            foreach (PropertyDefinition prop in type.Properties)
                PatchProperty(targetTypeDef, prop, propMethods);

            HashSet<MethodDefinition> eventMethods = new HashSet<MethodDefinition>(); // In the Patch pass, prop methods exist twice.
            foreach (EventDefinition eventdef in type.Events)
                PatchEvent(targetTypeDef, eventdef, eventMethods);

            foreach (MethodDefinition method in type.Methods)
                if (!propMethods.Contains(method) && !eventMethods.Contains(method))
                    PatchMethod(targetTypeDef, method);

            if (type.HasMMAttribute("EnumReplace")) {
                for (int ii = 0; ii < targetTypeDef.Fields.Count;) {
                    if (targetTypeDef.Fields[ii].Name == "value__") {
                        ii++;
                        continue;
                    }

                    targetTypeDef.Fields.RemoveAt(ii);
                }
            }

            foreach (FieldDefinition field in type.Fields)
                PatchField(targetTypeDef, field);

            PatchNested(type);
        }

        protected virtual void PatchNested(TypeDefinition type) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PatchType(type.NestedTypes[i]);
            }
        }

        public virtual void PatchProperty(TypeDefinition targetType, PropertyDefinition prop, HashSet<MethodDefinition> propMethods = null) {
            if (!prop.MatchingConditionals(Module))
                return;

            MethodDefinition addMethod;

            PropertyDefinition targetProp = targetType.FindProperty(prop.Name);
            string backingName = $"<{prop.Name}>__BackingField";
            FieldDefinition backing = prop.DeclaringType.FindField(backingName);
            FieldDefinition targetBacking = targetType.FindField(backingName);

            // Cheap fix: Apply the mod property attributes on the mod backing field.
            // Causes the field to be ignored / replaced / ... in its own patch pass further below.
            if (backing != null)
                foreach (CustomAttribute attrib in prop.CustomAttributes)
                    backing.CustomAttributes.Add(attrib.Clone());

            if (prop.HasMMAttribute("Ignore")) {
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

            if (prop.HasMMAttribute("Remove") || prop.HasMMAttribute("Replace")) {
                if (targetProp != null) {
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
                if (prop.HasMMAttribute("Remove"))
                    return;
            }

            if (targetProp == null) {
                // Add missing property
                PropertyDefinition newProp = targetProp = new PropertyDefinition(prop.Name, prop.Attributes, prop.PropertyType);
                newProp.AddAttribute(GetMonoModAddedCtor());

                foreach (ParameterDefinition param in prop.Parameters)
                    newProp.Parameters.Add(param.Clone());

                newProp.DeclaringType = targetType;
                targetType.Properties.Add(newProp);

                if (backing != null) {
                    FieldDefinition newBacking = targetBacking = new FieldDefinition(backingName, backing.Attributes, backing.FieldType);
                    targetType.Fields.Add(newBacking);
                }
            }

            foreach (CustomAttribute attrib in prop.CustomAttributes)
                targetProp.CustomAttributes.Add(attrib.Clone());

            MethodDefinition getter = prop.GetMethod;
            if (getter != null &&
                (addMethod = PatchMethod(targetType, getter)) != null) {
                targetProp.GetMethod = addMethod;
                propMethods?.Add(getter);
            }

            MethodDefinition setter = prop.SetMethod;
            if (setter != null &&
                (addMethod = PatchMethod(targetType, setter)) != null) {
                targetProp.SetMethod = addMethod;
                propMethods?.Add(setter);
            }

            foreach (MethodDefinition method in prop.OtherMethods)
                if ((addMethod = PatchMethod(targetType, method)) != null) {
                    targetProp.OtherMethods.Add(addMethod);
                    propMethods?.Add(method);
                }
        }

        public virtual void PatchEvent(TypeDefinition targetType, EventDefinition srcEvent, HashSet<MethodDefinition> propMethods = null) {
            MethodDefinition patched;
            EventDefinition targetEvent = targetType.FindEvent(srcEvent.Name);
            string backingName = $"<{srcEvent.Name}>__BackingField";
            FieldDefinition backing = srcEvent.DeclaringType.FindField(backingName);
            FieldDefinition targetBacking = targetType.FindField(backingName);

            // Cheap fix: Apply the mod property attributes on the mod backing field.
            // Causes the field to be ignored / replaced / ... in its own patch pass further below.
            if (backing != null)
                foreach (CustomAttribute attrib in srcEvent.CustomAttributes)
                    backing.CustomAttributes.Add(attrib.Clone());

            if (srcEvent.HasMMAttribute("Ignore")) {
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

            if (srcEvent.HasMMAttribute("Remove") || srcEvent.HasMMAttribute("Replace")) {
                if (targetEvent != null) {
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
                if (srcEvent.HasMMAttribute("Remove"))
                    return;
            }

            if (targetEvent == null) {
                // Add missing event
                EventDefinition newEvent = targetEvent = new EventDefinition(srcEvent.Name, srcEvent.Attributes, srcEvent.EventType);
                newEvent.AddAttribute(GetMonoModAddedCtor());

                newEvent.DeclaringType = targetType;
                targetType.Events.Add(newEvent);

                if (backing != null) {
                    FieldDefinition newBacking = new FieldDefinition(backingName, backing.Attributes, backing.FieldType);
                    targetType.Fields.Add(newBacking);
                }
            }

            foreach (CustomAttribute attrib in srcEvent.CustomAttributes)
                targetEvent.CustomAttributes.Add(attrib.Clone());

            MethodDefinition adder = srcEvent.AddMethod;
            if (adder != null &&
                (patched = PatchMethod(targetType, adder)) != null) {
                targetEvent.AddMethod = patched;
                propMethods?.Add(adder);
            }

            MethodDefinition remover = srcEvent.RemoveMethod;
            if (remover != null &&
                (patched = PatchMethod(targetType, remover)) != null) {
                targetEvent.RemoveMethod = patched;
                propMethods?.Add(remover);
            }

            MethodDefinition invoker = srcEvent.InvokeMethod;
            if (invoker != null &&
                (patched = PatchMethod(targetType, invoker)) != null) {
                targetEvent.InvokeMethod = patched;
                propMethods?.Add(invoker);
            }

            foreach (MethodDefinition method in srcEvent.OtherMethods)
                if ((patched = PatchMethod(targetType, method)) != null) {
                    targetEvent.OtherMethods.Add(patched);
                    propMethods?.Add(method);
                }
        }


        public virtual void PatchField(TypeDefinition targetType, FieldDefinition field) {
            TypeDefinition type = field.DeclaringType;
            string typeName = type.GetPatchFullName();

            if (field.HasMMAttribute("NoNew") || SkipList.Contains(typeName + "::" + field.Name) || !field.MatchingConditionals(Module))
                return;

            if (field.HasMMAttribute("Remove") || field.HasMMAttribute("Replace")) {
                FieldDefinition targetField = targetType.FindField(field.Name);
                if (targetField != null)
                    targetType.Fields.Remove(targetField);
                if (field.HasMMAttribute("Remove"))
                    return;
            }

            FieldDefinition existingField = type.FindField(field.Name);

            if (type.HasMMAttribute("Ignore") && existingField != null) {
                // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                foreach (CustomAttribute attrib in field.CustomAttributes)
                    if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                        existingField.CustomAttributes.Add(attrib.Clone());
                return;
            }

            if (targetType.HasField(field))
                return;

            FieldDefinition newField = new FieldDefinition(field.Name, field.Attributes, field.FieldType);
            newField.AddAttribute(GetMonoModAddedCtor());
            newField.InitialValue = field.InitialValue;
            if (field.HasConstant)
                newField.Constant = field.Constant;
            foreach (CustomAttribute attrib in field.CustomAttributes)
                newField.CustomAttributes.Add(attrib.Clone());
            targetType.Fields.Add(newField);
        }

        public virtual MethodDefinition PatchMethod(TypeDefinition targetType, MethodDefinition method) {
            if (method.Name.StartsWith("orig_") || method.HasMMAttribute("Original"))
                // Ignore original method stubs
                return null;

            if (!AllowedSpecialName(method, targetType) || !method.MatchingConditionals(Module))
                // Ignore ignored methods
                return null;

            string typeName = targetType.GetPatchFullName();

            if (SkipList.Contains(method.GetFindableID(type: typeName)))
                return null;

            // If the method's a MonoModConstructor method, just update its attributes to make it look like one.
            if (method.HasMMAttribute("Constructor")) {
                // Add MonoModOriginalName as the orig name data gets lost otherwise.
                if (!method.IsSpecialName && !method.HasMMAttribute("OriginalName")) {
                    CustomAttribute origNameAttrib = new CustomAttribute(GetMonoModOriginalNameCtor());
                    origNameAttrib.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.String, "orig_" + method.Name));
                    method.AddAttribute(origNameAttrib);
                }

                method.Name = method.IsStatic ? ".cctor" : ".ctor";
                method.IsSpecialName = true;
                method.IsRuntimeSpecialName = true;
            }

            MethodDefinition existingMethod = targetType.FindMethod(method.GetFindableID(type: typeName));
            MethodDefinition origMethod = targetType.FindMethod(method.GetFindableID(type: typeName, name: method.GetOriginalName()));

            if (method.HasMMAttribute("Ignore")) {
                // MonoModIgnore is a special case, as registered custom attributes should still be applied.
                if (existingMethod != null)
                    foreach (CustomAttribute attrib in method.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName) ||
                            CustomMethodAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            existingMethod.CustomAttributes.Add(attrib.Clone());
                return null;
            }

            if (existingMethod == null && method.HasMMAttribute("NoNew"))
                return null;

            if (method.HasMMAttribute("Remove")) {
                if (existingMethod != null)
                    targetType.Methods.Remove(existingMethod);
                return null;
            }

            if (method.HasMMAttribute("Replace")) {
                method.Name = method.GetPatchName();
                if (existingMethod != null) {
                    existingMethod.CustomAttributes.Clear();
                    existingMethod.Attributes = method.Attributes;
                    existingMethod.IsPInvokeImpl = method.IsPInvokeImpl;
                    existingMethod.ImplAttributes = method.ImplAttributes;
                }

            } else if (existingMethod != null && origMethod == null) {
                origMethod = existingMethod.Clone();
                origMethod.Name = method.GetOriginalName();
                origMethod.Attributes = existingMethod.Attributes & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName;
                origMethod.MetadataToken = GetMetadataToken(TokenType.Method);
                origMethod.IsVirtual = false; // Fix overflow when calling orig_ method, but orig_ method already defined higher up

                origMethod.Overrides.Clear();
                foreach (MethodReference @override in method.Overrides)
                    origMethod.Overrides.Add(@override);

                origMethod.AddAttribute(GetMonoModOriginalCtor());

                // Check if we've got custom attributes on our own orig_ method.
                MethodDefinition modOrigMethod = method.DeclaringType.FindMethod(method.GetFindableID(name: method.GetOriginalName()));
                if (modOrigMethod != null)
                    foreach (CustomAttribute attrib in modOrigMethod.CustomAttributes)
                        if (CustomAttributeHandlers.ContainsKey(attrib.AttributeType.FullName) ||
                            CustomMethodAttributeHandlers.ContainsKey(attrib.AttributeType.FullName))
                            origMethod.CustomAttributes.Add(attrib.Clone());

                targetType.Methods.Add(origMethod);
            }

            // Fix for .cctor not linking to orig_.cctor
            if (origMethod != null && method.IsConstructor && method.IsStatic && method.HasBody && !method.HasMMAttribute("Constructor")) {
                Collection<Instruction> instructions = method.Body.Instructions;
                ILProcessor ilProcessor = method.Body.GetILProcessor();
                ilProcessor.InsertBefore(instructions[instructions.Count - 1], ilProcessor.Create(OpCodes.Call, origMethod));
            }

            if (existingMethod != null) {
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

            } else {
                MethodDefinition clone = new MethodDefinition(method.Name, method.Attributes, Module.TypeSystem.Void);
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

                clone.AddAttribute(GetMonoModAddedCtor());

                targetType.Methods.Add(clone);

                method = clone;
            }

            if (origMethod != null) {
                CustomAttribute origNameAttrib = new CustomAttribute(GetMonoModOriginalNameCtor());
                origNameAttrib.ConstructorArguments.Add(new CustomAttributeArgument(Module.TypeSystem.String, origMethod.Name));
                method.AddAttribute(origNameAttrib);
            }

            return method;
        }
#endregion

#region PatchRefs Pass
        public virtual void PatchRefs() {
            if (Environment.GetEnvironmentVariable("MONOMOD_LEGACY_RELINKMAP") == "1") {
                _SplitUpgrade();
            }

            string mscorlibUpgradeStr = Environment.GetEnvironmentVariable("MONOMOD_MSCORLIB_UPGRADE");
            bool mscorlibUpdate;
            if (!string.IsNullOrEmpty(mscorlibUpgradeStr)) {
                mscorlibUpdate = mscorlibUpgradeStr == "1";
            } else {
                // Check if the assembly depends on mscorlib 2.0.5.0, possibly Unity.
                // If so, upgrade to that version (or away to an even higher version).
                Version fckUnity = new Version(2, 0, 5, 0);
                mscorlibUpdate = Module.AssemblyReferences.Any(x => x.Version == fckUnity);
            }

            if (mscorlibUpdate) {
                // Attempt to remap and remove redundant mscorlib references.
                // Subpass 1: Find newest referred version.
                List<AssemblyNameReference> mscorlibDeps = new List<AssemblyNameReference>();
                for (int i = 0; i < Module.AssemblyReferences.Count; i++) {
                    AssemblyNameReference dep = Module.AssemblyReferences[i];
                    if (dep.Name == "mscorlib") {
                        mscorlibDeps.Add(dep);
                    }
                }
                if (mscorlibDeps.Count > 1) {
                    // Subpass 2: Apply changes if found.
                    AssemblyNameReference maxmscorlib = mscorlibDeps.OrderByDescending(x => x.Version).First();
                    if (DependencyCache.TryGetValue(maxmscorlib.FullName, out ModuleDefinition mscorlib)) {
                        for (int i = 0; i < Module.AssemblyReferences.Count; i++) {
                            AssemblyNameReference dep = Module.AssemblyReferences[i];
                            if (dep.Name == "mscorlib" && maxmscorlib.Version > dep.Version) {
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

        // Private because this method isn't here to stay.
        private void _SplitUpgrade() {
            // This is required to stay compatible with mods created before splitting MonoMod into pieces.

            // Only run if the mod refers to MonoMod <= 18.03.* and if MonoModExt is no longer present in MonoMod.
            if (FindType("MonoMod.MonoModExt") != null)
                return;
            bool requiresUpgrade = false;
            List<ModuleReference> modules = new List<ModuleReference>(Mods);
            modules.Add(Module);
            foreach (ModuleDefinition mod in modules) {
                for (int i = 0; i < mod.AssemblyReferences.Count; i++) {
                    AssemblyNameReference dep = mod.AssemblyReferences[i];
                    if (dep.Name == "MonoMod") {
                        if (dep.Version.Major < 18 || (dep.Version.Major == 18 && dep.Version.Minor <= 3)) {
                            requiresUpgrade = true;
                        }
                        break;
                    }
                }
                if (requiresUpgrade)
                    break;
            }
            if (!requiresUpgrade)
                return;

            Log("[UpgradeSplit] Upgrading from MonoMod pre-18.03 to 18.04+");
            Log("[UpgradeSplit] THIS STEP WILL BE REMOVED IN A FUTURE RELEASE.");
            Log("[UpgradeSplit] It is only meant to preserve compatibility with mods during the transition to a \"split\" MonoMod.");

            string root = Path.GetDirectoryName(DependencyCache["MonoMod"]
#if !CECIL0_9
                .FileName
#else
                .FullyQualifiedName
#endif
            );

            bool found = false;
            // Don't compact this, otherwise it'll only run until the first "true" upgrade.
            found |= _SplitUpgrade("MonoMod");
            found |= _SplitUpgrade("MonoMod.Utils");
            found |= _SplitUpgrade("MonoMod.RuntimeDetour");
            if (!found) {
                Log("[UpgradeSplit] No MonoMod \"split\" upgrade targets found. Upgrade skipped.");
                return;
            }
        }

        private bool _SplitUpgrade(string split) {
            bool missingDependencyThrow = MissingDependencyThrow;
            MissingDependencyThrow = false;
            MapDependency(Module, split);
            MissingDependencyThrow = missingDependencyThrow;

            ModuleDefinition splitModule;
            if (!DependencyCache.TryGetValue(split, out splitModule)) {
                Log($"[UpgradeSplit] {split} doesn't exist, skipping it.");
                return false;
            }

            _SplitUpgrade(splitModule);
            return true;
        }

        private void _SplitUpgrade(ModuleDefinition split) {
            Log($"[UpgradeSplit] Upgrading to split {split.Name}");

            foreach (TypeDefinition type in split.Types)
                _SplitUpgrade(type);
        }

        private void _SplitUpgrade(TypeDefinition type) {
            CustomAttribute typeAttribOldName = type.GetMMAttribute("__OldName__");
            string typeOldName = typeAttribOldName == null ? null : (string) typeAttribOldName.ConstructorArguments[0].Value;

            RelinkMap[type.FullName] = type;
            if (typeOldName != null)
                RelinkMap[typeOldName] = type;

            foreach (FieldDefinition field in type.Fields) {
                if (field.HasMMAttribute("__WasIDictionary__")) {
                    // MonoMod moved from IDictionary to Dictionary, but provides proxies for old mods.
                    // Relink from old field type + new field name => proxy property.
                    GenericInstanceType fieldType = (GenericInstanceType) field.FieldType;
                    GenericInstanceType fieldTypeOld = new GenericInstanceType(
                        FindTypeDeep("System.Collections.Generic.IDictionary`2")
                    );
                    fieldTypeOld.GenericArguments.AddRange(fieldType.GenericArguments);
                    RelinkMap[$"{fieldTypeOld} {type.FullName}::{field.Name}"] = type.FindProperty($"_{field.Name}");
                    if (typeOldName != null)
                        RelinkMap[$"{fieldTypeOld} {typeOldName}::{field.Name}"] = type.FindProperty($"_{field.Name}");
                }
            }

            foreach (TypeDefinition nested in type.NestedTypes)
                _SplitUpgrade(nested);
        }

        public virtual void PatchRefs(ModuleDefinition mod) {
            foreach (TypeDefinition type in mod.Types)
                PatchRefsInType(type);
        }

        public virtual void PatchRefsInType(TypeDefinition type) {
            LogVerbose($"[VERBOSE] [PatchRefsInType] Patching refs in {type}");

            if (type.BaseType != null)
                type.BaseType = type.BaseType.Relink(Relinker, type);

            // Don't foreach when modifying the collection
            for (int i = 0; i < type.Interfaces.Count; i++) {
#if !CECIL0_9
                InterfaceImplementation interf = type.Interfaces[i];
                InterfaceImplementation newInterf = new InterfaceImplementation(interf.InterfaceType.Relink(Relinker, type));
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
            for (int i = 0; i < type.CustomAttributes.Count; i++)
                type.CustomAttributes[i] = type.CustomAttributes[i].Relink(Relinker, type);

            foreach (PropertyDefinition prop in type.Properties) {
                prop.PropertyType = prop.PropertyType.Relink(Relinker, type);
                // Don't foreach when modifying the collection
                for (int i = 0; i < prop.CustomAttributes.Count; i++)
                    prop.CustomAttributes[i] = prop.CustomAttributes[i].Relink(Relinker, type);
            }

            foreach (EventDefinition eventDef in type.Events) {
                eventDef.EventType = eventDef.EventType.Relink(Relinker, type);
                for (int i = 0; i < eventDef.CustomAttributes.Count; i++)
                    eventDef.CustomAttributes[i] = eventDef.CustomAttributes[i].Relink(Relinker, type);
            }

            foreach (MethodDefinition method in type.Methods)
                PatchRefsInMethod(method);

            foreach (FieldDefinition field in type.Fields) {
                field.FieldType = field.FieldType.Relink(Relinker, type);
                // Don't foreach when modifying the collection
                for (int i = 0; i < field.CustomAttributes.Count; i++)
                    field.CustomAttributes[i] = field.CustomAttributes[i].Relink(Relinker, type);
            }

            for (int i = 0; i < type.NestedTypes.Count; i++)
                PatchRefsInType(type.NestedTypes[i]);
        }

        public virtual void PatchRefsInMethod(MethodDefinition method) {
            if ((!AllowedSpecialName(method) && !method.IsConstructor) ||
                method.HasMMAttribute("Ignore") ||
                SkipList.Contains(method.GetFindableID()) ||
                !method.MatchingConditionals(Module))
                // Ignore ignored methods
                return;

            LogVerbose($"[VERBOSE] [PatchRefsInMethod] Patching refs in {method}");

            // Don't foreach when modifying the collection
            for (int i = 0; i < method.GenericParameters.Count; i++)
                method.GenericParameters[i] = method.GenericParameters[i].Relink(Relinker, method);

            foreach (ParameterDefinition param in method.Parameters) {
                param.ParameterType = param.ParameterType.Relink(Relinker, method);
                for (int i = 0; i < param.CustomAttributes.Count; i++)
                    param.CustomAttributes[i] = param.CustomAttributes[i].Relink(Relinker, method);
            }

            for (int i = 0; i < method.CustomAttributes.Count; i++)
                method.CustomAttributes[i] = method.CustomAttributes[i].Relink(Relinker, method);

            for (int i = 0; i < method.Overrides.Count; i++)
                method.Overrides[i] = method.Overrides[i].Relink(Relinker, method);

            method.ReturnType = method.ReturnType.Relink(Relinker, method);

            if (method.Body == null) return;

            foreach (VariableDefinition var in method.Body.Variables)
                var.VariableType = var.VariableType.Relink(Relinker, method);

            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                if (handler.CatchType != null)
                    handler.CatchType = handler.CatchType.Relink(Relinker, method);

            MethodRewriter?.Invoke(this, method);

            Dictionary<TypeReference, VariableDefinition> tmpAddrLocMap = new Dictionary<TypeReference, VariableDefinition>();

            MethodBody body = method.Body;

            for (int instri = 0; method.HasBody && instri < body.Instructions.Count; instri++) {
                Instruction instr = body.Instructions[instri];
                object operand = instr.Operand;

                // MonoMod-specific in-code flag setting / ...

                // TODO: Split out the MonoMod inline parsing.

                if (!MethodParser(this, body, instr, ref instri))
                    continue;

                // Before relinking, check for an existing forced call opcode mapping.
                OpCode forceCall = default(OpCode);
                bool hasForceCall = operand is MethodReference && (
                    ForceCallMap.TryGetValue((operand as MethodReference).GetFindableID(), out forceCall) ||
                    ForceCallMap.TryGetValue((operand as MethodReference).GetFindableID(simple: true), out forceCall)
                );

                // General relinking
                if (!(operand is ParameterDefinition) && operand is IMetadataTokenProvider)
                    operand = ((IMetadataTokenProvider) operand).Relink(Relinker, method);

                // Check again after relinking.
                if (!hasForceCall && operand is MethodReference) {
                    OpCode forceCallRelinked;
                    bool hasForceCallRelinked = 
                        ForceCallMap.TryGetValue((operand as MethodReference).GetFindableID(), out forceCallRelinked) ||
                        ForceCallMap.TryGetValue((operand as MethodReference).GetFindableID(simple: true), out forceCallRelinked)
                    ;
                    // If a relinked force call exists, prefer it over the existing forced call opcode.
                    // Otherwise keep the existing forced call opcode.
                    if (hasForceCallRelinked) {
                        forceCall = forceCallRelinked;
                        hasForceCall = true;
                    }
                }

                // patch_ constructor fix: If referring to itself, refer to the original constructor.
                if (instr.OpCode == OpCodes.Call && operand is MethodReference &&
                    (((MethodReference) operand).Name == ".ctor" ||
                     ((MethodReference) operand).Name == ".cctor") &&
                    ((MethodReference) operand).FullName == method.FullName) {
                    // ((MethodReference) operand).Name = method.GetOriginalName();
                    // Above could be enough, but what about the metadata token?
                    operand = method.DeclaringType.FindMethod(method.GetFindableID(name: method.GetOriginalName()));
                }

                // .ctor -> static method reference fix: newobj -> call
                if ((instr.OpCode == OpCodes.Newobj || instr.OpCode == OpCodes.Newarr) && operand is MethodReference &&
                    ((MethodReference) operand).Name != ".ctor") {
                    instr.OpCode = ((MethodReference) operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;

                // field -> property reference fix: ld(s)fld(a) / st(s)fld(a) <-> call get / set
                } else if ((instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Stfld || instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldsflda || instr.OpCode == OpCodes.Stsfld) && operand is PropertyReference) {
                    PropertyDefinition prop = ((PropertyReference) operand).Resolve();
                    if (instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldsflda)
                        operand = prop.GetMethod;
                    else {
                        operand = prop.SetMethod;
                    }
                    if (instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Ldsflda)
                        body.AppendGetAddr(instr, prop.PropertyType, tmpAddrLocMap);
                    instr.OpCode = ((MethodReference) operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;

                // field <-> method reference fix: ld(s)fld / st(s)fld <-> call
                } else if ((instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldflda || instr.OpCode == OpCodes.Stfld) && operand is MethodReference) {
                    if (instr.OpCode == OpCodes.Ldflda)
                        body.AppendGetAddr(instr, ((PropertyReference) operand).PropertyType, tmpAddrLocMap);
                    instr.OpCode = ((MethodReference) operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;

                } else if ((instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldsflda || instr.OpCode == OpCodes.Stsfld) && operand is MethodReference) {
                    if (instr.OpCode == OpCodes.Ldsflda)
                        body.AppendGetAddr(instr, ((PropertyReference) operand).PropertyType, tmpAddrLocMap);
                    instr.OpCode = OpCodes.Call;

                } else if ((instr.OpCode == OpCodes.Callvirt || instr.OpCode == OpCodes.Call) && operand is FieldReference) {
                    // Setters don't return anything.
                    TypeReference returnType = ((MethodReference) instr.Operand).ReturnType;
                    bool set = returnType == null || returnType.MetadataType == MetadataType.Void;
                    // This assumption is dangerous.
                    bool instance = ((MethodReference) instr.Operand).HasThis;
                    if (instance)
                        instr.OpCode = set ? OpCodes.Stfld : OpCodes.Ldfld;
                    else
                        instr.OpCode = set ? OpCodes.Stsfld : OpCodes.Ldsfld;
                    // TODO: When should we emit ldflda / ldsflda?
                }

                // "general" static method <-> virtual method reference fix: call <-> callvirt
                else if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) && operand is MethodReference) {
                    if (hasForceCall) {
                        instr.OpCode = forceCall;
                    } else if (!body.IsBaseMethodCall(operand as MethodReference)) {
                        instr.OpCode = ((MethodReference) operand).IsCallvirt() ? OpCodes.Callvirt : OpCodes.Call;
                    }
                }

                // Reference importing
                if (operand is IMetadataTokenProvider)
                    operand = method.Module.ImportReference((IMetadataTokenProvider) operand);

                instr.Operand = operand;

                MethodBodyRewriter?.Invoke(this, body, instr, instri);
            }
        }

#endregion

#region Cleanup Pass
        public virtual void Cleanup(bool all = false) {
            for (int i = 0; i < Module.Types.Count; i++) {
                TypeDefinition type = Module.Types[i];
                if (all && (type.Namespace.StartsWith("MonoMod") || type.Name.StartsWith("MonoMod"))) {
                    Log($"[Cleanup] Removing type {type.Name}");
                    Module.Types.RemoveAt(i);
                    i--;
                    continue;
                }
                CleanupType(type, all: all);
            }

            Collection<AssemblyNameReference> deps = Module.AssemblyReferences;
            for (int i = deps.Count - 1; i > -1; --i)
                if ((all && deps[i].Name.StartsWith("MonoMod")) ||
                    (RemovePatchReferences && deps[i].Name.EndsWith(".mm")))
                    deps.RemoveAt(i);
        }

        public virtual void CleanupType(TypeDefinition type, bool all = false) {
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

        public virtual void Cleanup(ICustomAttributeProvider cap, bool all = false) {
            Collection<CustomAttribute> attribs = cap.CustomAttributes;
            for (int i = attribs.Count - 1; i > -1; --i) {
                TypeReference attribType = attribs[i].AttributeType;
                if (ShouldCleanupAttrib?.Invoke(cap, attribType) ?? (
                    (attribType.Scope.Name == "MonoMod" || attribType.Scope.Name == "MonoMod.exe" || attribType.Scope.Name == "MonoMod.dll") ||
                    (attribType.FullName.StartsWith("MonoMod.MonoMod"))
                )) {
                    attribs.RemoveAt(i);
                }
            }
        }

#endregion

#region Default PostProcessor Pass
        public virtual void DefaultPostProcessor(MonoModder modder) {
            foreach (TypeDefinition type in Module.Types)
                DefaultPostProcessType(type);

            if (CleanupEnabled)
                Cleanup(all: Environment.GetEnvironmentVariable("MONOMOD_CLEANUP_ALL") == "1");
        }

        public virtual void DefaultPostProcessType(TypeDefinition type) {
            if (PublicEverything || type.HasMMAttribute("Public"))
                type.SetPublic(true);

            RunCustomAttributeHandlers(type);

            foreach (EventDefinition eventDef in type.Events) {
                if (PublicEverything || eventDef.HasMMAttribute("Public")) {
                    eventDef.SetPublic(true);
                    eventDef.AddMethod?.SetPublic(true);
                    eventDef.RemoveMethod?.SetPublic(true);
                    foreach (MethodDefinition method in eventDef.OtherMethods)
                        method.SetPublic(true);
                }

                RunCustomAttributeHandlers(eventDef);
            }

            foreach (PropertyDefinition prop in type.Properties) {
                if (PublicEverything || prop.HasMMAttribute("Public")) {
                    prop.SetPublic(true);
                    prop.GetMethod?.SetPublic(true);
                    prop.SetMethod?.SetPublic(true);
                    foreach (MethodDefinition method in prop.OtherMethods)
                        method.SetPublic(true);
                }

                RunCustomAttributeHandlers(prop);
            }

            foreach (MethodDefinition method in type.Methods) {
                if (PublicEverything || method.HasMMAttribute("Public"))
                    method.SetPublic(true);

                if (PreventInline && method.HasBody) {
                    method.NoInlining = true;
                    // Remove AggressiveInlining
                    method.ImplAttributes &= (MethodImplAttributes) 0x0100;
                }

                method.ConvertShortLongOps();

                RunCustomAttributeHandlers(method);
            }

            foreach (FieldDefinition field in type.Fields) {
                if (PublicEverything || field.HasMMAttribute("Public"))
                    field.SetPublic(true);

                RunCustomAttributeHandlers(field);
            }


            foreach (TypeDefinition nested in type.NestedTypes)
                DefaultPostProcessType(nested);
        }
#endregion

#region MonoMod injected types
        public virtual TypeDefinition PatchWasHere() {
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "WasHere") {
                    LogVerbose("[PatchWasHere] Type MonoMod.WasHere already existing");
                    return Module.Types[ti];
                }
            }
            LogVerbose("[PatchWasHere] Adding type MonoMod.WasHere");
            TypeDefinition wasHere = new TypeDefinition("MonoMod", "WasHere", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.TypeSystem.Object
            };
            Module.Types.Add(wasHere);
            return wasHere;
        }

        protected MethodDefinition _mmOriginalCtor;
        public virtual MethodReference GetMonoModOriginalCtor() {
            if (_mmOriginalCtor != null && _mmOriginalCtor.Module != Module) {
                _mmOriginalCtor = null;
            }
            if (_mmOriginalCtor != null) {
                return _mmOriginalCtor;
            }

            TypeDefinition attrType = null;
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModOriginal") {
                    attrType = Module.Types[ti];
                    for (int mi = 0; mi < attrType.Methods.Count; mi++) {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic) {
                            continue;
                        }
                        return _mmOriginalCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModOriginal] Adding MonoMod.MonoModOriginal");
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModOriginal", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.ImportReference(typeof(Attribute))
            };
            _mmOriginalCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmOriginalCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmOriginalCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmOriginalCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, Module.ImportReference(
                typeof(Attribute).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)[0]
            )));
            _mmOriginalCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmOriginalCtor);
            Module.Types.Add(attrType);
            return _mmOriginalCtor;
        }

        protected MethodDefinition _mmOriginalNameCtor;
        public virtual MethodReference GetMonoModOriginalNameCtor() {
            if (_mmOriginalNameCtor != null && _mmOriginalNameCtor.Module != Module) {
                _mmOriginalNameCtor = null;
            }
            if (_mmOriginalNameCtor != null) {
                return _mmOriginalNameCtor;
            }

            TypeDefinition attrType = null;
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModOriginalName") {
                    attrType = Module.Types[ti];
                    for (int mi = 0; mi < attrType.Methods.Count; mi++) {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic) {
                            continue;
                        }
                        return _mmOriginalNameCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModOriginalName] Adding MonoMod.MonoModOriginalName");
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModOriginalName", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.ImportReference(typeof(Attribute))
            };
            _mmOriginalNameCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmOriginalNameCtor.Parameters.Add(new ParameterDefinition("n", ParameterAttributes.None, Module.TypeSystem.String));
            _mmOriginalNameCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmOriginalNameCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmOriginalNameCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, Module.ImportReference(
                typeof(Attribute).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)[0]
            )));
            _mmOriginalNameCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmOriginalNameCtor);
            Module.Types.Add(attrType);
            return _mmOriginalNameCtor;
        }

        protected MethodDefinition _mmAddedCtor;
        public virtual MethodReference GetMonoModAddedCtor() {
            if (_mmAddedCtor != null && _mmAddedCtor.Module != Module) {
                _mmAddedCtor = null;
            }
            if (_mmAddedCtor != null) {
                return _mmAddedCtor;
            }

            TypeDefinition attrType = null;
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModAdded") {
                    attrType = Module.Types[ti];
                    for (int mi = 0; mi < attrType.Methods.Count; mi++) {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic) {
                            continue;
                        }
                        return _mmAddedCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModAdded] Adding MonoMod.MonoModAdded");
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModAdded", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.ImportReference(typeof(Attribute))
            };
            _mmAddedCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmAddedCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmAddedCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmAddedCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, Module.ImportReference(
                typeof(Attribute).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)[0]
            )));
            _mmAddedCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attrType.Methods.Add(_mmAddedCtor);
            Module.Types.Add(attrType);
            return _mmAddedCtor;
        }

        protected MethodDefinition _mmPatchCtor;
        public virtual MethodReference GetMonoModPatchCtor() {
            if (_mmPatchCtor != null && _mmPatchCtor.Module != Module) {
                _mmPatchCtor = null;
            }
            if (_mmPatchCtor != null) {
                return _mmPatchCtor;
            }

            TypeDefinition attrType = null;
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "MonoModPatch") {
                    attrType = Module.Types[ti];
                    for (int mi = 0; mi < attrType.Methods.Count; mi++) {
                        if (!attrType.Methods[mi].IsConstructor || attrType.Methods[mi].IsStatic) {
                            continue;
                        }
                        return _mmPatchCtor = attrType.Methods[mi];
                    }
                }
            }
            LogVerbose("[MonoModPatch] Adding MonoMod.MonoModPatch");
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModPatch", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.ImportReference(typeof(Attribute))
            };
            _mmPatchCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmPatchCtor.Parameters.Add(new ParameterDefinition("name", ParameterAttributes.None, Module.TypeSystem.String));
            _mmPatchCtor.MetadataToken = GetMetadataToken(TokenType.Method);
            _mmPatchCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            _mmPatchCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, Module.ImportReference(
                typeof(Attribute).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)[0]
            )));
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
        public virtual MetadataToken GetMetadataToken(TokenType type) {
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
            if (Module.FileName == null) {
                ++CurrentRID;
            } else
#endif
            {
                try {
                    while (Module.LookupToken(CurrentRID | (int) type) != null) {
                        ++CurrentRID;
                    }
                } catch {
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
        public virtual bool AllowedSpecialName(MethodDefinition method, TypeDefinition targetType = null) {
            if (method.HasMMAttribute("Added") || method.DeclaringType.HasMMAttribute("Added") ||
                (targetType?.HasMMAttribute("Added") ?? false)) {
                return true;
            }

            // HOW NOT TO SOLVE ISSUES:
            // if (method.IsConstructor)
            //     return true; // We don't give a f**k anymore.

            // The legacy behaviour is required to not break anything. It's very, very finnicky.
            // In retrospect, taking the above "fix" into consideration, it was bound to fail as soon
            // as other ignored members were accessed from the new constructors.
            if (method.IsConstructor && (method.HasCustomAttributes || method.IsStatic)) {
                if (method.IsStatic)
                    return true;
                // Overriding the constructor manually is generally a horrible idea, but who knows where it may be used.
                if (method.HasMMAttribute("Constructor")) return true;
            }

            if (method.IsGetter || method.IsSetter)
                return true;

            if (method.Name.StartsWith("op_"))
                return true;

            return !method.IsRuntimeSpecialName; // Formerly SpecialName. If something breaks, blame UnderRail.
        }
#endregion

    }
}
