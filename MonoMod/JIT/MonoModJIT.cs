using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace MonoMod.JIT
{
    /// <summary>
    /// Class that does black magic at runtime.
    /// </summary>
    public class MonoModJIT : MonoMod {

        private readonly Dictionary<MethodDefinition, DynamicMethodDelegate> CacheParsed = new Dictionary<MethodDefinition, DynamicMethodDelegate>();
        private readonly Dictionary<Type, TypeDefinition> CacheTypeDefs = new Dictionary<Type, TypeDefinition>();
        private readonly Dictionary<TypeDefinition, TypeDefinition> CacheTypeDefs_ = new Dictionary<TypeDefinition, TypeDefinition>();
        private readonly Dictionary<MethodInfo, MethodDefinition> CacheMethodDefs = new Dictionary<MethodInfo, MethodDefinition>();
        private readonly Dictionary<MethodDefinition, MethodDefinition> CacheMethodDefs_ = new Dictionary<MethodDefinition, MethodDefinition>();
        private readonly static Dictionary<string, Type> CacheTypes = new Dictionary<string, Type>(128);
        private readonly static Dictionary<string, Type> CachePrefoundTypes = new Dictionary<string, Type>(1024);

        private byte[] OriginalChecksum;
        private ModuleDefinition OriginalModule;
        private bool IsPatched;
        private bool IsParsed;

        public Assembly PatchedAssembly;

        public MonoModJIT(Assembly assembly)
            : base(assembly.Location) {
            Out = new FileInfo(assembly.Location.Substring(0, assembly.Location.Length-4) + ".mmc");
            Read(true);
        }

        public override void Read(bool loadDependencies = true) {
            base.Read(loadDependencies);

            OriginalModule = Module;

            if (Out.Exists) {
                Module = ModuleDefinition.ReadModule(Out.FullName);
            }

            CheckChecksum();
        }

        public void CheckChecksum() {
            if (OriginalChecksum == null) {
                using (MD5 md5 = MD5.Create()) {
                    using (FileStream stream = File.OpenRead(In.FullName)) {
                        OriginalChecksum = md5.ComputeHash(stream);
                    }
                }
            }
            
            if (Module == OriginalModule || IsPatched) {
                return;
            }
            
            byte[] checksum = GetData("Checksum");
            if (checksum == null) {
                return;
            }
            
            bool match = true;
            for (int i = 0; i < checksum.Length && match; i++) {
                match = checksum[i] == OriginalChecksum[i];
            }
            
            if (!match) {
                //Revert to the previously loaded module
                Module = OriginalModule;
            }
            IsPatched = match;
        }
        
        public void WriteChecksum() {
            SetData("Checksum", OriginalChecksum);
        }
        
        public MethodDefinition GetDataMethod() {
            TypeDefinition wasHere = Module.GetType("MonoMod.WasHere");
            MethodDefinition jitData = null;
            for (int i = 0; i < wasHere.Methods.Count; i++) {
                if (wasHere.Methods[i].Name == "MonoModJIT_Data") {
                    jitData = wasHere.Methods[i];
                    break;
                }
            }
            return jitData;
        }
        
        public byte[] GetData(string name) {
            MethodDefinition jitData = GetDataMethod();
            if (jitData == null) {
                return null;
            }
            
            for (int i = 0; i < jitData.Body.Instructions.Count; i++) {
                Instruction instr = jitData.Body.Instructions[i];
                if (instr.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr) {
                    string operand = (string) instr.Operand;
                    if (operand.StartsWith(name + ":")) {
                        char[] chars = ((string) instr.Operand).Substring(name.Length + 1).ToCharArray();
                        byte[] data = new byte[chars.Length];
                        Buffer.BlockCopy(chars, 0, data, 0, data.Length);
                        return data;
                    }
                }
            }
            return null;
        }
        
        public void SetData(string name, byte[] data) {
            MethodDefinition jitData = GetDataMethod();
            if (jitData == null) {
                jitData = new MethodDefinition("MonoModJIT_Data", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, Module.Import(typeof(void)));
                jitData.Body = new Mono.Cecil.Cil.MethodBody(jitData);
                jitData.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
                
                Module.GetType("MonoMod.WasHere").Methods.Add(jitData);
            }
            
            char[] chars = new char[data.Length];
            Buffer.BlockCopy(data, 0, chars, 0, chars.Length);
            string newstr = name + ":" + new string(chars);
            
            for (int i = 0; i < jitData.Body.Instructions.Count; i++) {
                Instruction instr = jitData.Body.Instructions[i];
                if (instr.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr) {
                    if (((string) instr.Operand).StartsWith(name + ":")) {
                        instr.Operand = newstr;
                        return;
                    }
                }
            }
            
            ILProcessor il = jitData.Body.GetILProcessor();
            il.InsertBefore(jitData.Body.Instructions[jitData.Body.Instructions.Count - 1], il.Create(Mono.Cecil.Cil.OpCodes.Ldstr, newstr));
        }

        public override TypeDefinition PatchWasHere() {
            TypeDefinition wasHere = base.PatchWasHere();

            WriteChecksum();

            return wasHere;
        }
        
        public override void AutoPatch(bool read = true, bool write = true) {
            //Reading outside of AutoPatch disables modding. Ironically modding twice can kill MonoModJIT
            if (!IsPatched) {
                base.AutoPatch(false, write);
                IsPatched = true;
            }
            
            if (!IsParsed) {
                CacheParsed.Clear();
                CacheTypeDefs.Clear();
                CacheTypeDefs_.Clear();
                CacheMethodDefs.Clear();
                CacheMethodDefs_.Clear();
                
                AutoParse();
                IsParsed = true;
            }
        }

        public TypeDefinition GetTypeDefinition(Type type) {
            TypeDefinition def;
            if (CacheTypeDefs.TryGetValue(type, out def)) {
                return def;
            }

            Type highest = type;
            int count = 1;
            while ((highest = highest.DeclaringType) != null) {
                count++;
            }
            Type[] path = new Type[count];
            highest = path[0] = type;
            for (int i = 1; (highest = highest.DeclaringType) != null; i++) {
                path[i] = highest;
            }

            for (int i = 0; i < path.Length; i++) {
                if (def == null) {
                    def = Module.GetType(path[i].FullName);
                    continue;
                }

                for (int ii = 0; ii < def.NestedTypes.Count; ii++) {
                    if (def.NestedTypes[ii].Name == path[i].Name) {
                        //Probably check for more than that
                        def = def.NestedTypes[ii];
                        break;
                    }
                }
            }

            CacheTypeDefs[type] = def;
            return def;
        }
        
        public TypeDefinition GetTypeDefinition(TypeDefinition type) {
            TypeDefinition def;
            if (CacheTypeDefs_.TryGetValue(type, out def)) {
                return def;
            }

            TypeDefinition highest = type;
            int count = 1;
            while ((highest = highest.DeclaringType) != null) {
                count++;
            }
            TypeDefinition[] path = new TypeDefinition[count];
            highest = path[0] = type;
            for (int i = 1; (highest = highest.DeclaringType) != null; i++) {
                path[i] = highest;
            }

            for (int i = 0; i < path.Length; i++) {
                if (def == null) {
                    def = Module.GetType(path[i].FullName);
                    continue;
                }

                for (int ii = 0; ii < def.NestedTypes.Count; ii++) {
                    if (def.NestedTypes[ii].Name == path[i].Name) {
                        //Probably check for more than that
                        def = def.NestedTypes[ii];
                        break;
                    }
                }
            }

            CacheTypeDefs_[type] = def;
            return def;
        }

        public MethodDefinition GetMethodDefinition(MethodInfo info) {
            MethodDefinition def;
            if (CacheMethodDefs.TryGetValue(info, out def)) {
                return def;
            }

            TypeDefinition type = GetTypeDefinition(info.DeclaringType);
            for (int i = 0; i < type.Methods.Count; i++) {
                if (type.Methods[i].Name == info.Name && type.Methods[i].Parameters.Count == info.GetParameters().Length) {
                    //Probably check for more than that
                    def = type.Methods[i];
                    break;
                }
            }

            CacheMethodDefs[info] = def;
            return def;
        }
        
        public MethodDefinition GetMethodDefinition(MethodDefinition info) {
            MethodDefinition def;
            if (CacheMethodDefs_.TryGetValue(info, out def)) {
                return def;
            }

            TypeDefinition type = GetTypeDefinition(info.DeclaringType);
            for (int i = 0; i < type.Methods.Count; i++) {
                if (type.Methods[i].Name == info.Name && type.Methods[i].Parameters.Count == info.Parameters.Count) {
                    //Probably check for more than that
                    def = type.Methods[i];
                    break;
                }
            }

            CacheMethodDefs_[info] = def;
            return def;
        }

        public MethodDefinition GetPatched(MethodInfo method) {
            AutoPatch();
            //also clears the cache if required for the returning GetMethodDefinition to work

            return GetMethodDefinition(method);
        }

        public MethodDefinition GetPatched(MethodDefinition method) {
            if (MonoMod.HasAttribute(method, "JIT.MonoModJITPatched")) {
                return method;
            }

            AutoPatch();
            //also clears the cache if required for the returning GetMethodDefinition to work

            return GetMethodDefinition(method);
        }
        
        public void AutoParse() {
            byte[] data = new byte[0];
            using (MemoryStream ms = new MemoryStream()) {
                Module.Write(ms);
                data = new byte[ms.Length];
                ms.Seek(0, SeekOrigin.Begin);
                ms.Read(data, 0, data.Length);
            }
            PatchedAssembly = Assembly.Load(data);
        }
        
        private Type[] getTypesFromParams(Collection<ParameterDefinition> params_) {
            Type[] paramTypes = new Type[params_.Count];
            for (int pi = 0; pi < params_.Count; pi++) {
                paramTypes[pi] = FindTypeJIT(params_[pi].ParameterType);
            }
            return paramTypes;
        }

        public DynamicMethodDelegate GetParsed(MethodInfo method) {
            return GetParsed(GetPatched(method));
        }

        public DynamicMethodDelegate GetParsed(MethodDefinition method) {
            method = GetPatched(method);
            
            DynamicMethodDelegate dmd;
            if (CacheParsed.TryGetValue(method, out dmd)) {
                return dmd;
            }
            
            AutoParse();
            
            Type type = FindTypeJIT(method.DeclaringType);
            dmd = type.GetMethod(method.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                null, getTypesFromParams(method.Parameters), null).GetDelegate();
            
            CacheParsed[method] = dmd;

            return dmd;
        }
        
        private Type FindTypeJIT(TypeReference typeRef) {
            string name = typeRef.FullName;
            Type type_ = null;
            if (CacheTypes.TryGetValue(name, out type_)) {
                return type_;
            }
            if (CachePrefoundTypes.TryGetValue(name, out type_)) {
                CacheTypes[name] = type_;
                return type_;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Assembly> delayedAssemblies = new List<Assembly>();

            foreach (Assembly assembly in assemblies) {
                if (assembly.GetName().Name.EndsWith(".mm")) {
                    delayedAssemblies.Add(assembly);
                    continue;
                }
                try {
                    Type[] types = assembly.GetTypes();
                    foreach (Type type in types) {
                        if ((type.Name == name && type.FullName.EndsWith("."+name)) || name == type.FullName) {
                            CacheTypes[name] = type;
                            return type;
                        }
                        CachePrefoundTypes[type.FullName] = CachePrefoundTypes[type.Name] = type;
                    }
                } catch (ReflectionTypeLoadException e) {
                    Log("Failed searching a type in MonoModJIT's FindTypeJIT.");
                    Log("Assembly: " + assembly.GetName().Name);
                    Log(e.Message);
                    foreach (Exception le in e.LoaderExceptions) {
                        Log(le.Message);
                    }
                }
            }

            foreach (Assembly assembly in delayedAssemblies) {
                try {
                    Type[] types = assembly.GetTypes();
                    foreach (Type type in types) {
                        if ((type.Name == name && type.FullName.EndsWith("."+name)) || name == type.FullName) {
                            CacheTypes[name] = type;
                            return type;
                        }
                    }
                } catch (ReflectionTypeLoadException e) {
                    Log("Failed searching a type in MonoModJIT's FindTypeJIT.");
                    Log("Assembly: " + assembly.GetName().Name);
                    Log(e.Message);
                    foreach (Exception le in e.LoaderExceptions) {
                        Log(le.Message);
                    }
                }
            }

            CacheTypes[name] = null;
            return null;
        }
        
        protected override void Log(string txt) {
            if (Logger != null) {
                Logger(txt);
            }
            if (DefaultLogger != null) {
                DefaultLogger(txt);
            }
            //default: nop (originally Console.WriteLine)
        }

    }
}
