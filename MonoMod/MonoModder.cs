using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using StringInject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoMod {
    public class MonoModder : IDisposable {

        public static Action<string> DefaultLogger;
        public Action<string> Logger;

        public static Dictionary<string, object> Data = new Dictionary<string, object>() {
            { "Platform",           (PlatformHelper.Current & ~Platform.X64).ToString() },
            { "PlatformPrefix",     (PlatformHelper.Current & ~Platform.X64).ToString().ToLowerInvariant() + "_" },
            { "Arch",               (PlatformHelper.Current & Platform.X64).ToString() },
            { "Architecture",       (PlatformHelper.Current & Platform.X64).ToString() },
            { "ArchPrefix",         (PlatformHelper.Current & Platform.X64).ToString().ToLowerInvariant() + "_" },
            { "ArchitecturePrefix", (PlatformHelper.Current & Platform.X64).ToString().ToLowerInvariant() + "_" }
        };

        public Dictionary<string, object> RelinkMap = new Dictionary<string, object>();
        public Dictionary<string, ModuleDefinition> RelinkModuleMap = new Dictionary<string, ModuleDefinition>();
        public HashSet<string> SkipList = new HashSet<string>();

        public Relinker Relinker;
        public Relinker MainRelinker;
        public Relinker PostRelinker;

        public MissingDependencyResolver OnMissingDependency;

        public Stream Input;
        public Stream Output;
        public ModuleDefinition Module;
        public Dictionary<ModuleDefinition, List<ModuleDefinition>> DependencyMap = new Dictionary<ModuleDefinition, List<ModuleDefinition>>();
        public Dictionary<string, ModuleDefinition> DependencyCache = new Dictionary<string, ModuleDefinition>();
        public List<string> DependencyDirs = new List<string>();

        public List<ModuleDefinition> Mods = new List<ModuleDefinition>();

        public Dictionary<string, MMILProxyDelegate> MMILProxyMap = new Dictionary<string, MMILProxyDelegate>();

        public int CurrentRID = 0;

        public bool SkipOptimization = false;

        public ReadingMode ReadingMode = ReadingMode.Deferred;

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
                        AssemblyResolver = AssemblyResolver
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
                    _writerParameters = new WriterParameters() {
                        // WriteSymbols = true
                    };
                }
                return _writerParameters;
            }
            set {
                _writerParameters = value;
            }
        }

        protected string _GACPath;
        public string GACPath {
            get {
                if (_GACPath == null) {
                    string os;
                    System.Reflection.PropertyInfo property_platform = typeof(Environment).GetProperty("Platform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (property_platform != null) {
                        // For mono, get from
                        // static extern PlatformID Platform
                        os = property_platform.GetValue(null, null).ToString().ToLower();
                    } else {
                        // For .NET, use default value
                        os = Environment.OSVersion.Platform.ToString().ToLower();
                        // .NET also prefixes the version with a v
                    }
                    if (os.Contains("win")) {
                        // C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xml
                        _GACPath = Path.Combine(Environment.GetEnvironmentVariable("windir"), "Microsoft.NET", "assembly", "GAC_MSIL");

                        /*} else if (os.Contains("mac") || os.Contains("osx")) {
                        // TODO test GAC path for Mono on Mac
                        // should be <prefix>/lib/mono/gac, too, but what's prefix on Mac?
                    } else if (os.Contains("lin") || os.Contains("unix")) {*/
                        // For now let's just pretend it's the same as with Linux...
                    } else if (os.Contains("mac") || os.Contains("osx") || os.Contains("lin") || os.Contains("unix")) {
                        // <prefix>/lib/mono/gac

                        Process which = new Process();
                        which.StartInfo.FileName = "which";
                        which.StartInfo.Arguments = "mono";
                        which.StartInfo.CreateNoWindow = true;
                        which.StartInfo.RedirectStandardOutput = true;
                        which.StartInfo.UseShellExecute = false;
                        which.EnableRaisingEvents = true;

                        StringBuilder whichOutputBuilder = new StringBuilder();

                        which.OutputDataReceived += new DataReceivedEventHandler(
                            delegate (object sender, DataReceivedEventArgs e) {
                                whichOutputBuilder.Append(e.Data);
                            }
                        );
                        which.Start();
                        which.BeginOutputReadLine();
                        which.WaitForExit();
                        which.CancelOutputRead();

                        _GACPath = Directory.GetParent(whichOutputBuilder.ToString().Trim()).Parent.FullName;
                        _GACPath = Path.Combine(_GACPath, "lib", "mono", "gac");
                    }
                }

                return _GACPath;
            }
            set {
                _GACPath = value;
            }
        }

        public MonoModder() {
            Relinker = DefaultRelinker;
            MainRelinker = DefaultMainRelinker;
            PostRelinker = DefaultPostRelinker;

            OnMissingDependency = DefaultMissingDependencyResolver;

            MMILProxy.FillMap(this, MMILProxyMap);
        }

        public void SetupLegacy() {
            ReadingMode = ReadingMode.Immediate;
        }

        public virtual void Dispose() {
            Module?.Dispose();
            Module = null;

            AssemblyResolver?.Dispose();
            AssemblyResolver = null;

            foreach (ModuleDefinition mod in Mods)
                mod?.Dispose();

            foreach (List<ModuleDefinition> dependencies in DependencyMap.Values)
                foreach (ModuleDefinition dep in dependencies)
                    dep?.Dispose();
            DependencyMap.Clear();

            Input?.Dispose();
            Output?.Dispose();
        }

        public virtual void Log(object obj) {
            Log(obj.ToString());
        }

        public virtual void Log(string txt) {
            if (Logger != null) {
                Logger(txt);
                return;
            }
            if (DefaultLogger != null) {
                DefaultLogger(txt);
                return;
            }
            Console.Write("[MonoMod] ");
            Console.WriteLine(txt);
        }

        /// <summary>
        /// Reads the main module from the Input stream to Module.
        /// </summary>
        public virtual void Read(bool loadDependencies = true) {
            if (Module == null) {
                Log("Reading input stream into module.");
                Module = ModuleDefinition.ReadModule(Input, GenReaderParameters(true));
            }

            if (loadDependencies) MapDependencies(Module);
        }

        public virtual void MapDependencies(ModuleDefinition main) {
            if (DependencyMap.ContainsKey(main)) return;
            DependencyMap[main] = new List<ModuleDefinition>();

            foreach (AssemblyNameReference dep in main.AssemblyReferences)
                MapDependency(main, dep);
        }
        public virtual void MapDependency(ModuleDefinition main, AssemblyNameReference dep) {
            MapDependency(main, dep.Name, dep.FullName);
        }
        public virtual void MapDependency(ModuleDefinition main, string name, string fullName = null) {
            if ((fullName != null && fullName == Module.Assembly.Name.FullName) || name == Module.Assembly.Name.Name || Module.Assembly.Name.Name.EndsWith(".mm"))
                return; // Don't load / map the main module or other mods!

            ModuleDefinition dep;
            if ((fullName != null && DependencyCache.TryGetValue(fullName, out dep)) ||
                                     DependencyCache.TryGetValue(name    , out dep)) {
                Log($"[MapDependency] {main.Name} -> {dep.Name} (({fullName}), ({name})) from cache");
                DependencyMap[main].Add(dep);
                return;
            }

            string path = null;
            foreach (string depDir in DependencyDirs) {
                path = Path.Combine(depDir, name + ".dll");
                if (!File.Exists(path))
                    path = Path.Combine(depDir, name + ".exe");
                if (!File.Exists(path))
                    path = Path.Combine(depDir, name);
                if (File.Exists(path)) break;
                else path = null;
            }

            // Check if available in GAC
            if (path == null && fullName != null) {
                System.Reflection.Assembly asm = null;
                try {
                    asm = System.Reflection.Assembly.ReflectionOnlyLoad(fullName);
                } catch { }
                path = asm?.Location;
            }

            // Manually check in GAC
            if (path == null && fullName == null) {
                path = Path.Combine(GACPath, name);

                if (Directory.Exists(path)) {
                    string[] versions = Directory.GetDirectories(path);
                    int highest = 0;
                    int highestIndex = 0;
                    for (int i = 0; i < versions.Length; i++) {
                        Match versionMatch = Regex.Match(versions[i].Substring(path.Length + 1), "\\d+");
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
                } else {
                    path = null;
                }
            }

            if (path != null && File.Exists(path)) {
                dep = ModuleDefinition.ReadModule(path, GenReaderParameters(false));
            } else if ((dep = OnMissingDependency?.Invoke(main, name, fullName)) == null) return;
            
            Log($"[MapDependency] {main.Name} -> {dep.Name} (({fullName}), ({name})) loaded");
            DependencyMap[main].Add(dep);
            DependencyCache[fullName] = dep;
            DependencyCache[name] = dep;
            MapDependencies(dep);
        }
        public virtual ModuleDefinition DefaultMissingDependencyResolver(ModuleDefinition main, string name, string fullName) {
            throw new InvalidOperationException($"MonoMod cannot map dependency {main.Name} -> (({fullName}), ({name})) - not found");
        }

        /// <summary>
        /// Write the modded module to the given stream or the default output.
        /// </summary>
        /// <param name="output">Output stream. If none given, default Output will be used.</param>
        public virtual void Write(Stream output = null) {
            output = output ?? Output;

            PatchWasHere();

            Log("[Write] Writing modded module into output stream.");
            Module.Write(output, WriterParameters);
        }

        public virtual ReaderParameters GenReaderParameters(bool mainModule) {
            ReaderParameters _rp = ReaderParameters;
            ReaderParameters rp = new ReaderParameters(_rp.ReadingMode);
            rp.AssemblyResolver = _rp.AssemblyResolver;
            rp.MetadataResolver = _rp.MetadataResolver;
            rp.MetadataImporterProvider = _rp.MetadataImporterProvider;
            rp.ReflectionImporterProvider = _rp.ReflectionImporterProvider;
            rp.SymbolStream = _rp.SymbolStream;
            rp.SymbolReaderProvider = _rp.SymbolReaderProvider;
            rp.ReadSymbols = _rp.ReadSymbols;

            // TODO debug symbol support

            return rp;
        }


        public virtual void ReadMod(string path) {
            if (Directory.Exists(path)) {
                Log($"[ReadMod] Loading mod dir: {path}");
                string mainName = Module.Name.Substring(0, Module.Name.Length - 3);
                DependencyDirs.Add(path);
                foreach (string modFile in Directory.GetFiles(path))
                    if (Path.GetFileName(modFile).StartsWith(mainName) && modFile.ToLower().EndsWith(".mm.dll"))
                        ReadMod(modFile);
                return;
            }

            Log($"[ReadMod] Loading mod: {path}");
            ModuleDefinition mod = ModuleDefinition.ReadModule(path, GenReaderParameters(false));
            MapDependencies(mod);
            ParseRules(mod);
            Mods.Add(mod);
        }
        public virtual void ReadMod(Stream stream) {
            Mods.Add(ModuleDefinition.ReadModule(stream, GenReaderParameters(false)));
        }


        public virtual void ParseRules(ModuleDefinition mod) {
            // Rule parsing pre-pass: Check for MonoModHook and similar attributes
            foreach (TypeDefinition type in mod.Types)
                ParseRulesInType(type);

            TypeDefinition rulesType = mod.GetType("MonoMod.MonoModRules");
            if (rulesType == null) return;

            // Check the constructor for all main rules
            MethodDefinition cctor = rulesType.FindMethod("System.Void .cctor()");
            if (cctor != null && cctor.HasBody) {
                bool matchingPlatformIL = true;
                for (int instri = 0; instri < cctor.Body.Instructions.Count; instri++) {
                    Instruction instr = cctor.Body.Instructions[instri];

                    // Temporarily enable matching platform, otherwise the platform data gets lost.
                    // Check the next one as the array size is before the newarr.
                    if (instr.Next?.OpCode == OpCodes.Newarr && (
                        (instr.Next?.Operand as TypeReference)?.FullName == "Platform" ||
                        (instr.Next?.Operand as TypeReference)?.FullName == "int"
                    )) {
                        matchingPlatformIL = true;
                    }

                    if (instr.Operand is MethodReference) {
                        MethodReference mr = (MethodReference) instr.Operand;

                        if (mr.DeclaringType.FullName == "MMIL") {
                            if (mr.Name == "OnPlatform")
                                matchingPlatformIL = cctor.ParseOnPlatform(ref instri);
                            // Right now nothing else to parse.
                        }

                        if (mr.DeclaringType.FullName == "MMIL/Rule") {
                            if (mr.Name == "RelinkModule") {
                                MMILRT.Rule.RelinkModule(
                                    this,
                                    (string) cctor.Body.Instructions[instri - 2].Operand,
                                    (string) cctor.Body.Instructions[instri - 1].Operand
                                );
                            }

                            if (mr.Name == "RelinkType") {
                                MMILRT.Rule.RelinkType(
                                    this,
                                    (string) cctor.Body.Instructions[instri - 2].Operand,
                                    (string) cctor.Body.Instructions[instri - 1].Operand
                                );
                            }

                            if (mr.Name == "RelinkMember") {
                                MMILRT.Rule.RelinkMember(
                                    this,
                                    (string) cctor.Body.Instructions[instri - 3].Operand,
                                    (string) cctor.Body.Instructions[instri - 2].Operand,
                                    (string) cctor.Body.Instructions[instri - 1].Operand
                                );
                                /*MMILProxyMap["System.Void MMIL/Rule::RelinkMember(System.String,System.String,System.String)"](
                                    (string) cctor.Body.Instructions[instri - 3].Operand,
                                    (string) cctor.Body.Instructions[instri - 2].Operand,
                                    (string) cctor.Body.Instructions[instri - 1].Operand
                                );*/
                            }


                            if (mr.Name == "Patch") {
                                MMILRT.Rule.Patch(
                                    this,
                                    (string) cctor.Body.Instructions[instri - 2].Operand,
                                    cctor.Body.Instructions[instri - 1].GetInt() == 1
                                );
                            }

                        }

                    }

                    if (!matchingPlatformIL) {
                        cctor.Body.Instructions.RemoveAt(instri);
                        instri = Math.Max(0, instri - 1);
                        cctor.Body.UpdateOffsets(instri, -1);
                        continue;
                    }

                }
            }

            // Finally, remove the type, otherwise it'll easily conflict with other mods' rules.
            mod.Types.Remove(rulesType);
        }

        public virtual void ParseRulesInType(TypeDefinition type) {
            string typeName = RemovePrefixes(type.FullName, type);

            if (type.HasMMAttribute("Ignore") || !type.MatchingConditionals())
                return;

            CustomAttribute hook;

            hook = type.GetMMAttribute("Hook");
            if (hook != null)
                ParseHook(type, hook);

            foreach (MethodDefinition method in type.Methods) {
                if (method.HasMMAttribute("Ignore") || !method.MatchingConditionals())
                    continue;

                hook = method.GetMMAttribute("Hook");
                if (hook != null)
                    ParseHook(method, hook);
            }

            foreach (FieldDefinition field in type.Fields) {
                if (field.HasMMAttribute("Ignore") || !field.MatchingConditionals())
                    continue;

                hook = field.GetMMAttribute("Hook");
                if (hook != null)
                    ParseHook(field, hook);
            }

        }

        public virtual void ParseHook(IMetadataTokenProvider target, CustomAttribute hook) {
            string from = (string) hook.ConstructorArguments[0].Value;

            object to;
            if (target is TypeReference)
                to = ((TypeReference) target).FullName;
            else if (target is MethodReference)
                to = Tuple.Create(
                    ((MethodReference) target).DeclaringType.FullName,
                    ((MethodReference) target).GetFindableID(withType: false)
                );
            else if (target is FieldReference)
                to = Tuple.Create(
                    ((FieldReference) target).DeclaringType.FullName,
                    ((FieldReference) target).Name
                );
            else
                return;

            RelinkMap[from] = to;
        }

        /// <summary>
        /// Automatically mods the module, loading Input, writing the modded module to Output.
        /// </summary>
        public virtual void AutoPatch() {
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

            Log("[AutoPatch] Optimization pass");
            Optimize();
        }

        /// <summary>
        /// Runs some basic optimization (f.e. disables NoOptimization, removes nops)
        /// </summary>
        public virtual void Optimize() {
            if (SkipOptimization) return;
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                TypeDefinition type = Module.Types[ti];
                for (int mi = 0; mi < type.Methods.Count; mi++) {
                    MethodDefinition method = type.Methods[mi];

                    method.NoInlining = false;
                    method.NoOptimization = false;

                    if (method.HasBody) {
                        for (int instri = 0; instri < method.Body.Instructions.Count; instri++) {
                            Instruction instruction = method.Body.Instructions[instri];
                            if (instruction.OpCode == OpCodes.Nop) {
                                method.Body.Instructions.RemoveAt(instri);
                                instri = Math.Max(0, instri - 1);
                                method.Body.UpdateOffsets(instri, -1);
                            }
                        }
                    }
                }
            }
        }


        public virtual MemberReference GetLinkToRef(ICustomAttributeProvider orig, IGenericParameterProvider context) {
            CustomAttribute linkto = orig?.GetMMAttribute("LinkTo");
            if (linkto == null) return null;

            TypeDefinition type = null;
            MemberReference member = null;
            for (int i = 0; i < linkto.ConstructorArguments.Count; i++) {
                CustomAttributeArgument arg = linkto.ConstructorArguments[i];
                object value = arg.Value;

                if (i == 0) { // Type
                    if (value is string)
                        type = FindTypeDeep((string) value).Resolve();
                    else if (value is TypeReference)
                        type = Relink((TypeReference) value, context).Resolve();

                } else if (i == 1) { // Member
                    if (orig is MethodReference)
                        member =
                            type.FindMethod((string) value) ??
                            type.FindMethod(((MethodReference) orig).GetFindableID(name: (string) value, type: type.FullName));
                    // Microoptimization with the type: type.FullName above:
                    // Instead of waiting until the 4th pass, just use the type name once and return in the 3rd pass.
                    else if (orig is FieldReference)
                        member = type.FindField((string) value);
                }

            }

            if (type == null)
                throw new InvalidOperationException($"Could not resolve MonoModLinkTo on {orig}: Type not found.");
            if (orig is TypeReference)
                return Module.ImportReference(type);

            if (member == null)
                throw new InvalidOperationException($"Could not resolve MonoModLinkTo on {orig}: Member not found.");
            if (orig is MethodReference)
                return Module.ImportReference((MethodReference) member);
            if (orig is FieldReference)
                return Module.ImportReference((FieldReference) member);

            throw new InvalidOperationException($"Cannot link from {orig} - unknown type {orig.GetType()}");
        }

        public virtual IMetadataTokenProvider DefaultRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            // LinkTo bypasses all relinking maps.
            ICustomAttributeProvider cap = mtp as ICustomAttributeProvider;
            if (cap == null) // TODO This increases the PatchRefs pass time and could be optimized.
                if (mtp is TypeReference)
                    cap = ((TypeReference) mtp).Resolve() as ICustomAttributeProvider;
                else if (mtp is MethodReference)
                    cap = ((MethodReference) mtp).Resolve() as ICustomAttributeProvider;
                else if (mtp is FieldReference)
                    cap = ((FieldReference) mtp).Resolve() as ICustomAttributeProvider;

            if (cap?.GetMMAttribute("LinkTo") != null)
                return GetLinkToRef(cap, context);

            return PostRelinker(
                MainRelinker(mtp, context),
                context);
        }
        public virtual IMetadataTokenProvider DefaultMainRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
             if (mtp is TypeReference) {
                TypeReference type = (TypeReference) mtp;

                // Type isn't coming from a mod module - just return the original.
                if (!Mods.Contains(type.Module))
                    return type;

                return Module.ImportReference(FindTypeDeep(RemovePrefixes(type.FullName, type)));
            }

            if (mtp is FieldReference || mtp is MethodReference)
                // Don't relink those. It'd be useful to f.e. link to member B instead of member A.
                // MonoModExt already handles the default "deep" relinking.
                return mtp;

            throw new InvalidOperationException($"MonoMod default relinker can't handle metadata token providers of the type {mtp.GetType()}");
        }
        public virtual IMetadataTokenProvider DefaultPostRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            
            // The post relinker doesn't care if it can't handle a specific metadata token provider type; Just run ResolveRelinkTarget
            return ResolveRelinkTarget(mtp) ?? mtp;
        }

        public virtual IMetadataTokenProvider Relink(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            return mtp.Relink(Relinker, context);
        }
        public virtual TypeReference Relink(TypeReference type, IGenericParameterProvider context) {
            return type.Relink(Relinker, context);
        }
        public virtual MethodReference Relink(MethodReference method, IGenericParameterProvider context) {
            return method.Relink(Relinker, context);
        }
        public virtual CustomAttribute Relink(CustomAttribute attrib, IGenericParameterProvider context) {
            return attrib.Relink(Relinker, context);
        }

        public virtual IMetadataTokenProvider ResolveRelinkTarget(IMetadataTokenProvider mtp, bool relink = true, bool relinkModule = true) {
            string name;
            if (mtp is TypeReference)
                name = ((TypeReference) mtp).FullName;
            else if (mtp is MethodReference)
                name = ((MethodReference) mtp).GetFindableID(withType: true);
            else if (mtp is FieldReference)
                name = ((FieldReference) mtp).FullName;
            else
                return null;

            object val;
            if (relink && RelinkMap.TryGetValue(name, out val)) {
                if (val is Tuple<string, string>) {
                    Tuple<string, string> tuple = (Tuple<string, string>) val;
                    string typeName = tuple.Item1 as string;
                    TypeDefinition type = FindTypeDeep(typeName)?.Resolve();
                    if (type == null)
                        return ResolveRelinkTarget(mtp, false, relinkModule);

                    if (mtp is MethodReference)
                        val = type.FindMethod(tuple.Item2);
                    else if (mtp is FieldReference)
                        val = type.FindField(tuple.Item2);
                    else
                        throw new InvalidOperationException($"MonoMod doesn't support RelinkMap member type {val.GetType()} with Tuple<string, string>");
                    // Store the result
                    return (IMetadataTokenProvider) (RelinkMap[name] = val = Module.ImportReference((IMetadataTokenProvider) val));
                }

                if (val is string && mtp is TypeReference)
                    // Store the result
                    RelinkMap[name] = val = Module.ImportReference(
                        ResolveRelinkTarget(FindTypeDeep((string) val), false, relinkModule)
                    );

                if (val is IMetadataTokenProvider)
                    return (IMetadataTokenProvider) val;

                throw new InvalidOperationException($"MonoMod doesn't support RelinkMap value of type {val.GetType()}");
            }


            if (relinkModule && mtp is TypeReference) {
                TypeReference type = (TypeReference) mtp;
                ModuleDefinition scope;
                if (!RelinkModuleMap.TryGetValue(type.Scope.Name, out scope))
                    return type;
                type.Scope = scope;
                return Module.ImportReference(type);
            }

            return null;
        }

        public virtual TypeReference FindType(string name)
            => FindType(Module, name, new Stack<ModuleDefinition>()) ?? Module.GetType(name, true);
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
                if ((type = FindType(dep, fullName, crawled)) != null)
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
        public virtual void PrePatchType(TypeDefinition type) {
            string typeName = RemovePrefixes(type.FullName, type);

            // Fix legacy issue: Copy / inline any used modifiers.
            if ((type.Namespace != "MonoMod" && type.HasMMAttribute("Ignore")) || SkipList.Contains(typeName) || !type.MatchingConditionals())
                return;

            // Check if type exists in target module or dependencies.
            TypeReference targetType = FindTypeDeep(typeName);
            TypeDefinition targetTypeDef = targetType?.Resolve();
            if (targetType != null) {
                if (targetTypeDef != null && (type.Name.StartsWith("remove_") || type.HasMMAttribute("Remove")))
                    if (targetTypeDef.DeclaringType == null)
                        Module.Types.Remove(targetTypeDef);
                    else
                        targetTypeDef.DeclaringType.Resolve().NestedTypes.Remove(targetTypeDef);
                else
                    PrePatchNested(type);
                return;
            }

            // Add the type.
            Log($"[PrePatchType] Adding {typeName} to the target module.");

            TypeDefinition newType = new TypeDefinition(type.Namespace, type.Name, type.Attributes, type.BaseType);
            newType.AddAttribute(GetMonoModAddedCtor());

            foreach (GenericParameter genParam in type.GenericParameters)
                newType.GenericParameters.Add(genParam.Clone());

            foreach (InterfaceImplementation interf in type.Interfaces)
                newType.Interfaces.Add(interf);

            newType.ClassSize = type.ClassSize;
            if (type.DeclaringType != null) {
                // The declaring type is existing as this is being called nestedly.
                newType.DeclaringType = Relink(type.DeclaringType, newType).Resolve();
                newType.DeclaringType.NestedTypes.Add(newType);
            } else {
                Module.Types.Add(newType);
            }
            newType.PackingSize = type.PackingSize;
            newType.SecurityDeclarations.AddRange(type.SecurityDeclarations);

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
            MethodDefinition addMethod;

            string typeName = RemovePrefixes(type.FullName, type);

            if (type.HasMMAttribute("Ignore") ||
                SkipList.Contains(typeName) ||
                !type.MatchingConditionals()) {
                PatchNested(type);
                return;
            }

            TypeReference targetType = Module.GetType(typeName, false);
            if (targetType == null) return; // Type should've been added or removed accordingly.
            TypeDefinition targetTypeDef = targetType?.Resolve();

            if (typeName == type.FullName)
                Log($"[PatchType] Patching type {typeName}");
            else
                Log($"[PatchType] Patching type {typeName} (prefixed: {type.FullName})");

            // Add "new" custom attributes
            foreach (CustomAttribute attrib in type.CustomAttributes)
                if (!targetTypeDef.HasCustomAttribute(attrib.AttributeType.FullName))
                    targetTypeDef.CustomAttributes.Add(attrib.Clone());

            Stack<MethodDefinition> propMethods = new Stack<MethodDefinition>(); // In the Patch pass, prop methods exist twice.
            foreach (PropertyDefinition prop in type.Properties) {
                PropertyDefinition targetProp = targetTypeDef.FindProperty(prop.Name);
                if (targetProp == null) {
                    // Add missing property
                    PropertyDefinition newProp = targetProp = new PropertyDefinition(prop.Name, prop.Attributes, prop.PropertyType);
                    newProp.AddAttribute(GetMonoModAddedCtor());

                    foreach (ParameterDefinition param in prop.Parameters)
                        newProp.Parameters.Add(param.Clone());

                    foreach (CustomAttribute attrib in prop.CustomAttributes)
                        newProp.CustomAttributes.Add(attrib.Clone());

                    newProp.DeclaringType = targetTypeDef;
                    targetTypeDef.Properties.Add(newProp);
                }

                MethodDefinition getter = prop.GetMethod;
                if (getter != null &&
                    (addMethod = PatchMethod(targetTypeDef, getter)) != null) {
                    targetProp.GetMethod = addMethod;
                    propMethods.Push(getter);
                }

                MethodDefinition setter = prop.SetMethod;
                if (setter != null &&
                    (addMethod = PatchMethod(targetTypeDef, setter)) != null) {
                    targetProp.SetMethod = addMethod;
                    propMethods.Push(setter);
                }

                foreach (MethodDefinition method in prop.OtherMethods)
                    if ((addMethod = PatchMethod(targetTypeDef, method)) != null) {
                        targetProp.OtherMethods.Add(addMethod);
                        propMethods.Push(method);
                    }
            }

            foreach (MethodDefinition method in type.Methods)
                if (!propMethods.Contains(method))
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

            foreach (FieldDefinition field in type.Fields) {
                if (field.HasMMAttribute("Ignore") || field.HasMMAttribute("NoNew") || SkipList.Contains(typeName + "::" + field.Name) || !field.MatchingConditionals())
                    continue;

                if (field.HasMMAttribute("Remove") || field.HasMMAttribute("Replace")) {
                    FieldDefinition targetField = targetTypeDef.FindField(field.Name);
                    if (targetField != null)
                        targetTypeDef.Fields.Remove(targetField);
                    if (field.HasMMAttribute("Remove"))
                        continue;
                }

                if (targetTypeDef.HasField(field))
                    continue;

                FieldDefinition newField = new FieldDefinition(field.Name, field.Attributes, field.FieldType);
                newField.AddAttribute(GetMonoModAddedCtor());
                newField.InitialValue = field.InitialValue;
                newField.Constant = field.Constant;
                foreach (CustomAttribute attrib in field.CustomAttributes)
                    newField.CustomAttributes.Add(attrib.Clone());
                targetTypeDef.Fields.Add(newField);
            }

            PatchNested(type);
        }

        protected virtual void PatchNested(TypeDefinition type) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PatchType(type.NestedTypes[i]);
            }
        }

        public virtual MethodDefinition PatchMethod(TypeDefinition targetType, MethodDefinition method) {
            if (method.Name.StartsWith("orig_") || method.HasMMAttribute("Original"))
                // Ignore original method stubs
                return null;

            if (!AllowedSpecialName(method, targetType) || method.HasMMAttribute("Ignore") || !method.MatchingConditionals())
                // Ignore ignored methods
                return null;

            string typeName = RemovePrefixes(targetType.FullName, targetType);
            if (SkipList.Contains(method.GetFindableID(type: typeName)))
                return null;

            MethodDefinition existingMethod = targetType.FindMethod(method.GetFindableID(type: typeName));
            MethodDefinition origMethod = targetType.FindMethod(method.GetFindableID(type: typeName, name: method.GetOriginalName()));

            if (existingMethod == null && method.HasMMAttribute("NoNew"))
                return null;

            // If the method's a MonoModConstructor method, just update its attributes to make it look like one.
            if (method.HasMMAttribute("Constructor")) {
                method.Name = method.IsStatic ? ".cctor" : ".ctor";
                method.IsSpecialName = true;
                method.IsRuntimeSpecialName = true;
            }

            if (method.Name.StartsWith("replace_") || method.HasMMAttribute("Replace")) {
                method.Name = RemovePrefixes(method.Name);
                if (existingMethod != null) {
                    existingMethod.CustomAttributes.Clear();
                    existingMethod.Attributes = method.Attributes;
                    existingMethod.IsPInvokeImpl = method.IsPInvokeImpl;
                    existingMethod.ImplAttributes = method.ImplAttributes;
                }

            } else if (existingMethod != null && origMethod == null) {
                origMethod = new MethodDefinition(method.GetOriginalName(), existingMethod.Attributes & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName, existingMethod.ReturnType);
                origMethod.DeclaringType = existingMethod.DeclaringType;
                origMethod.MetadataToken = GetMetadataToken(TokenType.Method);
                origMethod.Body = existingMethod.Body.Clone(origMethod);
                origMethod.Attributes = existingMethod.Attributes & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName;
                origMethod.ImplAttributes = existingMethod.ImplAttributes;
                origMethod.IsManaged = existingMethod.IsManaged;
                origMethod.IsIL = existingMethod.IsIL;
                origMethod.IsNative = existingMethod.IsNative;
                origMethod.PInvokeInfo = existingMethod.PInvokeInfo;
                origMethod.IsPreserveSig = existingMethod.IsPreserveSig;
                origMethod.IsInternalCall = existingMethod.IsInternalCall;
                origMethod.IsPInvokeImpl = existingMethod.IsPInvokeImpl;

                origMethod.IsVirtual = false; // Fix overflow when calling orig_ method, but orig_ method already defined higher up

                foreach (GenericParameter genParam in existingMethod.GenericParameters)
                    origMethod.GenericParameters.Add(genParam.Clone());

                foreach (ParameterDefinition param in existingMethod.Parameters)
                    origMethod.Parameters.Add(param);

                foreach (CustomAttribute attrib in existingMethod.CustomAttributes)
                    origMethod.CustomAttributes.Add(attrib.Clone());

                foreach (MethodReference @override in method.Overrides)
                    origMethod.Overrides.Add(@override);

                origMethod.AddAttribute(GetMonoModOriginalCtor());

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

                method = existingMethod;

            } else {
                MethodDefinition clone = new MethodDefinition(method.Name, method.Attributes, Module.TypeSystem.Void);
                clone.MetadataToken = GetMetadataToken(TokenType.Method);
                clone.CallingConvention = method.CallingConvention;
                clone.ExplicitThis = method.ExplicitThis;
                clone.MethodReturnType = method.MethodReturnType;
                clone.NoInlining = method.NoInlining;
                clone.NoOptimization = method.NoOptimization;
                clone.Attributes = method.Attributes;
                clone.ImplAttributes = method.ImplAttributes;
                clone.SemanticsAttributes = method.SemanticsAttributes;
                clone.DeclaringType = targetType;
                clone.ReturnType = method.ReturnType;
                clone.Body = method.Body.Clone(clone);
                clone.IsManaged = method.IsManaged;
                clone.IsIL = method.IsIL;
                clone.IsNative = method.IsNative;
                clone.PInvokeInfo = method.PInvokeInfo;
                clone.IsPreserveSig = method.IsPreserveSig;
                clone.IsInternalCall = method.IsInternalCall;
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
            foreach (TypeDefinition type in Module.Types)
                PatchRefsInType(type);
        }

        public virtual void PatchRefsInType(TypeDefinition type) {
            if (type.BaseType != null) type.BaseType = Relink(type.BaseType, type);

            // Don't foreach when modifying the collection
            for (int i = 0; i < type.Interfaces.Count; i++) {
                InterfaceImplementation interf = type.Interfaces[i];
                InterfaceImplementation newInterf = new InterfaceImplementation(Relink(interf.InterfaceType, type));
                foreach (CustomAttribute attrib in interf.CustomAttributes)
                    newInterf.CustomAttributes.Add(Relink(attrib, type));
                type.Interfaces[i] = newInterf;
            }

            foreach (CustomAttribute attrib in type.CustomAttributes)
                Relink(attrib, type);

            foreach (PropertyDefinition prop in type.Properties) {
                prop.PropertyType = Relink(prop.PropertyType, type);
                // Don't foreach when modifying the collection
                for (int i = 0; i < prop.CustomAttributes.Count; i++)
                    prop.CustomAttributes[i] = Relink(prop.CustomAttributes[i], type);
            }

            foreach (MethodDefinition method in type.Methods)
                PatchRefsInMethod(method);

            foreach (FieldDefinition field in type.Fields) {
                field.FieldType = Relink(field.FieldType, type);
                foreach (CustomAttribute attrib in field.CustomAttributes)
                    Relink(attrib, type);
            }

            PatchRefsInTypeNested(type);
        }

        protected virtual void PatchRefsInTypeNested(TypeDefinition type) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PatchRefsInType(type.NestedTypes[i]);
            }
        }

        public virtual void PatchRefsInMethod(MethodDefinition method) {
            if ((!AllowedSpecialName(method) && !method.IsConstructor) ||
                method.HasMMAttribute("Ignore") ||
                SkipList.Contains(method.GetFindableID()) ||
                !method.MatchingConditionals())
                // Ignore ignored methods
                return;

            // Don't foreach when modifying the collection
            for (int i = 0; i < method.GenericParameters.Count; i++)
                method.GenericParameters[i] = (GenericParameter) Relink(method.GenericParameters[i], method);

            for (int i = 0; i < method.Parameters.Count; i++)
                method.Parameters[i] = (ParameterDefinition) Relink(method.Parameters[i], method);

            for (int i = 0; i < method.CustomAttributes.Count; i++)
                method.CustomAttributes[i] = Relink(method.CustomAttributes[i], method);

            for (int i = 0; i < method.Overrides.Count; i++)
                method.Overrides[i] = Relink(method.Overrides[i], method);

            method.ReturnType = Relink(method.ReturnType, method);

            if (method.Body == null) return;

            foreach (VariableDefinition var in method.Body.Variables)
                var.VariableType = Relink(var.VariableType, method);

            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                if (handler.CatchType != null)
                    handler.CatchType = Relink(handler.CatchType, method);

            bool publicAccess = false;
            bool matchingPlatformIL = true;

            for (int instri = 0; method.HasBody && instri < method.Body.Instructions.Count; instri++) {
                Instruction instr = method.Body.Instructions[instri];
                object operand = instr.Operand;

                // MonoMod-specific in-code flag setting / ...

                // Temporarily enable matching platform, otherwise the platform data gets lost.
                // Check the next one as the array size is before the newarr.
                if (instr.Next?.OpCode == OpCodes.Newarr && (
                    (instr.Next?.Operand as TypeReference)?.FullName == "Platform" ||
                    (instr.Next?.Operand as TypeReference)?.FullName == "int"
                )) {
                    matchingPlatformIL = true;
                }

                if (operand is MethodReference && (
                    ((MethodReference) operand).DeclaringType.FullName == "MonoMod.MonoModInline" ||
                    ((MethodReference) operand).DeclaringType.FullName == "MMIL"
                )) {
                    MethodReference mr = ((MethodReference) operand);

                    // General MMIL
                    if (mr.Name == "DisablePublicAccess") {
                        publicAccess = false;
                    } else if (mr.Name == "EnablePublicAccess") {
                        publicAccess = true;
                    }

                    if (mr.Name == "OnPlatform")
                        method.ParseOnPlatform(ref instri);

                    // Keep the method reference as modded mods may still require these.
                }

                if (!matchingPlatformIL) {
                    method.Body.Instructions.RemoveAt(instri);
                    instri = Math.Max(0, instri - 1);
                    method.Body.UpdateOffsets(instri, -1);
                    continue;
                }

                // General relinking
                if (operand is IMetadataTokenProvider) operand = Relink((IMetadataTokenProvider) operand, method);

                // patch_ constructor fix: If referring to itself, refer to the original constructor.
                if (instr.OpCode == OpCodes.Call && operand is MethodReference &&
                    (((MethodReference) operand).Name == ".ctor" ||
                     ((MethodReference) operand).Name == ".cctor") &&
                    ((MethodReference) operand).FullName == method.FullName) {
                    // ((MethodReference) operand).Name = method.GetOriginalName();
                    // Above could be enough, but what about the metadata token?
                    operand = method.DeclaringType.FindMethod(method.GetFindableID(name: method.GetOriginalName()));
                }

                // Reference importing
                if (operand is IMetadataTokenProvider) {
                    operand = Module.ImportReference((IMetadataTokenProvider) operand);
                    if (publicAccess)
                        ((IMetadataTokenProvider) operand).SetPublic(true);
                }

                instr.Operand = operand;
            }
        }

        #endregion

        #region MonoMod injected types
        public virtual TypeDefinition PatchWasHere() {
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "WasHere") {
                    Log("[PatchWasHere] Type MonoMod.WasHere already existing");
                    return Module.Types[ti];
                }
            }
            Log("[PatchWasHere] Adding type MonoMod.WasHere");
            TypeDefinition wasHere = new TypeDefinition("MonoMod", "WasHere", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.ImportReference(typeof(object))
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
            Log("[MonoModOriginal] Adding MonoMod.MonoModOriginal");
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
                        return _mmOriginalCtor = attrType.Methods[mi];
                    }
                }
            }
            Log("[MonoModOriginalName] Adding MonoMod.MonoModOriginalName");
            attrType = attrType ?? new TypeDefinition("MonoMod", "MonoModOriginalName", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.ImportReference(typeof(Attribute))
            };
            _mmOriginalCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Module.TypeSystem.Void
            );
            _mmOriginalCtor.Parameters.Add(new ParameterDefinition("n", ParameterAttributes.None, Module.TypeSystem.String));
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
            Log("[MonoModAdded] Adding MonoMod.MonoModAdded");
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
        #endregion


        #region Helper methods
        /// <summary>
        /// Creates a new non-conflicting MetadataToken.
        /// </summary>
        /// <param name="type">The type of the new token.</param>
        /// <returns>A MetadataToken with an unique RID for the target module.</returns>
        public virtual MetadataToken GetMetadataToken(TokenType type) {
            while (Module.LookupToken(CurrentRID | (int) type) != null) {
                ++CurrentRID;
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

            return !method.Attributes.HasFlag(MethodAttributes.SpecialName);
        }

        /// <summary>
        /// Removes all MonoMod prefixes from the given string and its type definition.
        /// </summary>
        /// <returns>str without prefixes.</returns>
        /// <param name="str">String to remove the prefixes from or the string containing strPrefixed.</param>
        /// <param name="strPrefixed">String to remove the prefixes from when part of str.</param>
        public virtual string RemovePrefixes(string str, TypeReference type) {
            for (TypeReference type_ = type; type_ != null; type_ = type_.DeclaringType) {
                str = RemovePrefixes(str, type_.Name);
            }
            return str;
        }
        /// <summary>
        /// Removes all MonoMod prefixes from the given string.
        /// </summary>
        /// <returns>str without prefixes.</returns>
        /// <param name="str">String to remove the prefixes from or the string containing strPrefixed.</param>
        /// <param name="strPrefixed">String to remove the prefixes from when part of str.</param>
        public virtual string RemovePrefixes(string str, string strPrefixed = null) {
            strPrefixed = strPrefixed ?? str;
            str = RemovePrefix(str, "patch_", strPrefixed);
            str = RemovePrefix(str, "remove_", strPrefixed);
            str = RemovePrefix(str, "replace_", strPrefixed);
            return str;
        }
        /// <summary>
        /// Removes the prefix from the given string.
        /// </summary>
        /// <returns>str without prefix.</returns>
        /// <param name="str">String to remove the prefixes from or the string containing strPrefixed.</param>
        /// <param name="prefix">Prefix.</param>
        /// <param name="strPrefixed">String to remove the prefixes from when part of str.</param>
        public static string RemovePrefix(string str, string prefix, string strPrefixed = null) {
            strPrefixed = strPrefixed ?? str;
            if (strPrefixed.StartsWith(prefix)) {
                return str.Replace(strPrefixed, strPrefixed.Substring(prefix.Length));
            }
            return str;
        }
        #endregion

    }
}
