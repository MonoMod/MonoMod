using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using Mono.Collections.Generic;

namespace MonoMod {
    public class MonoMod {

        public readonly static List<BlacklistItem> GlobalBlacklist = new List<BlacklistItem>() {
            new BlacklistItem("Assembly-CSharp", "globalGameState.DemoVersion"), //MegaSphere first alpha demo
            new BlacklistItem("Assembly-CSharp", "globalGameState.isDemo"), //MegaSphere first alpha demo
        };

        private readonly static List<BlacklistItem> loadedBlacklist = new List<BlacklistItem>();

        public FileInfo In;
        public DirectoryInfo Dir;
        public FileInfo Out;

        public ModuleDefinition Module;
        public List<ModuleDefinition> Dependencies = new List<ModuleDefinition>();
        public MethodDefinition Entry;

        public MonoMod() {
        }

        public MonoMod(string input)
            : this(new FileInfo(input)) {
        }

        public MonoMod(FileInfo input)
            : this() {
            In = input;
            Dir = input.Directory;
            Out = new FileInfo(input.FullName.Substring(0, input.FullName.Length-4)+".mm.exe");
        }

        public static void Main(string[] args) {
            Console.WriteLine("MonoMod "+System.Reflection.Assembly.GetCallingAssembly().GetName().Version);

            if (args.Length != 1) {
                Console.WriteLine("No valid arguments (executable path) passed.");
                return;
            }

            MonoMod mm = new MonoMod(args[0]);

            mm.Read();
            mm.AutoPatch();
            mm.Write();
        }

        /// <summary>
        /// Reads the main assembly to mod.
        /// </summary>
        /// <param name="loadDependencies">If set to <c>true</c> load dependencies when not already loaded.</param>
        public virtual void Read(bool loadDependencies = true) {
            if (Module == null) {
                Console.WriteLine("Reading assembly as Mono.Cecil ModuleDefinition and AssemblyDefinition...");
                Module = ModuleDefinition.ReadModule(In.FullName);
                LoadBlacklist(Module);
            }

            if (loadDependencies && Dependencies.Count == 0 && Dir != null) {
                Console.WriteLine("Reading module dependencies...");
                for (int mi = 0; mi < Module.ModuleReferences.Count; mi++) {
                    LoadDependency(Module.ModuleReferences[mi].Name);
                }

                Console.WriteLine("Reading assembly dependencies...");
                for (int mi = 0; mi < Module.AssemblyReferences.Count; mi++) {
                    LoadDependency(Module.AssemblyReferences[mi].Name);
                }

                Dependencies.Remove(Module);
            }

            //TODO make this return a status code or something
        }

        /// <summary>
        /// Write the modded module to the given file or the default output.
        /// </summary>
        /// <param name="output">Output file. If none given, Out will be used.</param>
        public virtual void Write(FileInfo output = null) {
            if (output == null) {
                output = Out;
            }

            PatchWasHere();

            Console.WriteLine("Writing to output file...");
            Module.Write(output.FullName);

            //TODO make this return a status code or something
        }

        /// <summary>
        /// Runs some basic optimization (f.e. disables NoOptimization, removes nops)
        /// </summary>
        public virtual void Optimize() {
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                TypeDefinition type = Module.Types[ti];
                for (int mi = 0; mi < type.Methods.Count; mi++) {
                    MethodDefinition method = type.Methods[mi];

                    method.NoOptimization = false;

                    if (method.HasBody) {
                        List<int> nops = new List<int>();
                        for (int i = 0; i < method.Body.Instructions.Count; i++) {
                            Instruction instruction = method.Body.Instructions[i];
                            if (instruction.OpCode == OpCodes.Nop) {
                                method.Body.Instructions.RemoveAt(i);
                                nops.Add(i);
                                i = i-1 < 0 ? 0 : i-1;

                                for (int ii = method.Body.Instructions.Count - 1; i <= ii; ii--) {
                                    Instruction next = method.Body.Instructions[ii];
                                    next.Offset--;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Automatically mods the module, loading In, based on the files in Dir and writes it to Out.
        /// If Dir and Out are not set, it will use the input file to create Dir and Out.
        /// </summary>
        public virtual void AutoPatch(bool read = true, bool write = true) {
            if (read) {
                if (Dir == null) {
                    Dir = In.Directory;
                }
            }
            if (Out == null) {
                Out = new FileInfo(In.FullName.Substring(0, In.FullName.Length-4)+".mm.exe");
            }

            Console.WriteLine("Patching "+In.Name+" ...");

            Read(true);

            Console.WriteLine("Replacing main EntryPoint...");
            Entry = PatchEntry(Module.EntryPoint);

            string fileName = In.Name.Substring(0, In.Name.LastIndexOf("."));
            Console.WriteLine("Scanning for files matching "+fileName+".*.mm.dll ...");
            List<TypeDefinition> types = new List<TypeDefinition>();
            foreach (FileInfo f in Dir.GetFiles()) {
                if (f.Name.StartsWith(fileName) && f.Name.ToLower().EndsWith(".mm.dll")) {
                    Console.WriteLine("Found "+f.Name+" , reading...");
                    ModuleDefinition mod = ModuleDefinition.ReadModule(f.FullName);
                    PatchModule(mod, types);
                }
            }
            Console.WriteLine("Patching / fixing references...");
            foreach (FileInfo f in Dir.GetFiles()) {
                if (f.Name.StartsWith(fileName) && f.Name.ToLower().EndsWith(".mm.dll")) {
                    PatchRefs(types);
                }
            }

            Optimize();

            Console.WriteLine("Done.");
        }

        /// <summary>
        /// Patches the module and adds the patched types to the given list.
        /// </summary>
        /// <param name="mod">Mod to patch into the input module.</param>
        /// <param name="types">Type list containing all patched types.</param>
        public virtual void PatchModule(ModuleDefinition mod, List<TypeDefinition> types) {
            Module.AssemblyReferences.Add(mod.Assembly.Name);

            for (int i = 0; i < mod.Types.Count; i++) {
                PatchType(mod.Types[i], types);
            }
        }

        /// <summary>
        /// Patches the type and adds it to the given list if it's actually patched.
        /// </summary>
        /// <param name="type">Type to patch into the input module.</param>
        /// <param name="types">Type list containing all patched types.</param>
        public virtual void PatchType(TypeDefinition type, List<TypeDefinition> types) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PatchType(type.NestedTypes[i], types);
            }

            string typeName = type.FullName;
            Console.WriteLine("T: " + typeName);

            typeName = RemovePrefixes(typeName, type.Name);

            if (type.Attributes.HasFlag(TypeAttributes.NotPublic) &&
                type.Attributes.HasFlag(TypeAttributes.Interface)) {
                Console.WriteLine("Type is a private interface; ignore...");
                return;
            }

            if (HasAttribute(type, "MonoModIgnore")) {
                return;
            }

            TypeReference origType = Module.GetType(typeName, true);
            if (origType == null) {
                //TODO still required?
                /*if (!type.Name.StartsWith("patch_")) {
                    Module.Types.Add(Module.Import(type).Resolve());
                }*/
                return;
            }

            origType = Module.GetType(typeName, false);
            if (origType == null) {
                return;
            }

            TypeDefinition origTypeResolved = origType.Resolve();

            IsBlacklisted(origTypeResolved.Module.Name, origTypeResolved.FullName, HasAttribute(origTypeResolved, "MonoModBlacklisted"));

            if (type.Name.StartsWith("remove_") || HasAttribute(type, "MonoModRemove")) {
                Module.Types.Remove(origTypeResolved);
                return;
            }

            type = Module.Import(type).Resolve();

            for (int ii = 0; ii < type.Methods.Count; ii++) {
                MethodDefinition method = type.Methods[ii];
                Console.WriteLine("M: "+method.FullName);

                if (!AllowedSpecialName(method) || HasAttribute(method, "MonoModIgnore")) {
                    continue;
                }

                method = Module.Import(method).Resolve();
                PatchMethod(method);
            }

            for (int ii = 0; ii < type.Fields.Count; ii++) {
                FieldDefinition field = type.Fields[ii];
                /*if (field.Attributes.HasFlag(FieldAttributes.SpecialName)) {
                        continue;
                    }*/

                bool hasField = false;
                for (int iii = 0; iii < origTypeResolved.Fields.Count; iii++) {
                    if (origTypeResolved.Fields[iii].Name == field.Name) {
                        hasField = true;
                        break;
                    }
                }
                if (hasField) {
                    continue;
                }
                Console.WriteLine("F: "+field.FullName);

                FieldDefinition newField = new FieldDefinition(field.Name, field.Attributes, FindType(field.FieldType, type));
                newField.InitialValue = field.InitialValue;
                origTypeResolved.Fields.Add(newField);
            }

            types.Add(type);
        }

        /// <summary>
        /// Patches the given method into the input module.
        /// </summary>
        /// <param name="method">Method to patch in.</param>
        public virtual void PatchMethod(MethodDefinition method) {
            if (method.Name.StartsWith("orig_")) {
                Console.WriteLine(method.Name + " is an orig_ method; ignoring...");
                return;
            }

            Console.WriteLine("Patching "+method.Name+" ...");

            Console.WriteLine("Checking for already existing methods...");

            TypeDefinition origType = Module.GetType(RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType.Name));

            MethodDefinition origMethod = null; //original method that is going to be changed if existing (f.e. X)
            MethodDefinition origMethodOrig = null; //orig_ method (f.e. orig_X)

            //TODO the orig methods of replace_ methods can't be found
            for (int i = 0; i < origType.Methods.Count; i++) {
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                    origMethod = origType.Methods[i];
                }
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName.Replace(method.Name, "orig_"+method.Name), method.DeclaringType.Name)) {
                    origMethodOrig = origType.Methods[i];
                }
            }

            if (origMethod != null && origMethodOrig == null) {
                IsBlacklisted(origMethod.Module.Name, origMethod.DeclaringType.FullName+"."+origMethod.Name, HasAttribute(origMethod, "MonoModBlacklisted"));
                if (method.Name.StartsWith("replace_") || HasAttribute(method, "MonoModReplace")) {
                    Console.WriteLine("Method existing; replacing...");
                } else {
                    Console.WriteLine("Method existing; creating copy...");

                    MethodDefinition copy = new MethodDefinition("orig_"+origMethod.Name, origMethod.Attributes & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName, origMethod.ReturnType);
                    copy.DeclaringType = origMethod.DeclaringType;
                    copy.MetadataToken = origMethod.MetadataToken;
                    copy.Body = origMethod.Body;

                    for (int i = 0; i < origMethod.Parameters.Count; i++) {
                        copy.Parameters.Add(origMethod.Parameters[i]);
                    }

                    for (int i = 0; i < origMethod.GenericParameters.Count; i++) {
                        copy.GenericParameters.Add(new GenericParameter(origMethod.GenericParameters[i].Name, copy));
                    }

                    origType.Methods.Add(copy);
                    origMethodOrig = copy;
                    Console.WriteLine("Added copy of original method to "+copy.FullName);
                }
            } else if (origMethod != null) {
                Console.WriteLine("Prefixed method existing; ignoring...");
            }

            //fix for .cctor not linking to orig_.cctor
            if (origMethodOrig != null && origMethod.IsConstructor && origMethod.IsStatic) {
                Collection<Instruction> instructions = method.Body.Instructions;
                ILProcessor ilProcessor = method.Body.GetILProcessor();
                ilProcessor.InsertBefore(instructions[instructions.Count - 1], ilProcessor.Create(OpCodes.Call, origMethodOrig));
            }

            for (int i = 0; method.HasBody && i < method.Body.Variables.Count; i++) {
                //TODO debug! (Import crashes)
                method.Body.Variables[i].VariableType = FindType(method.Body.Variables[i].VariableType, method);
            }

            Console.WriteLine("Storing method to main module...");

            if (origMethod != null) {
                origMethod.Body = method.Body;
                method = origMethod;
            } else {
                MethodAttributes attribs;
                if (origMethodOrig != null) {
                    attribs = origMethodOrig.Attributes;
                } else {
                    attribs = method.Attributes;
                }
                MethodDefinition clone = new MethodDefinition(method.Name, attribs, Module.Import(typeof(void)));
                origType.Methods.Add(clone);
                clone.DeclaringType = origType;
                for (int i = 0; i < method.GenericParameters.Count; i++) {
                    clone.GenericParameters.Add(new GenericParameter(method.GenericParameters[i].Name, origType));
                }
                for (int i = 0; i < method.Parameters.Count; i++) {
                    clone.Parameters.Add(new ParameterDefinition(FindType(method.Parameters[i].ParameterType, clone)));
                }
                clone.ReturnType = FindType(method.ReturnType, clone);
                clone.Body = method.Body;
                method = clone;
            }

            if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) {
                Console.WriteLine("Finding property...");

                PropertyDefinition property = null;
                for (int i = 0; i < origType.Properties.Count; i++) {
                    if (method.Name.EndsWith(origType.Properties[i].Name)) {
                        property = origType.Properties[i];
                        if (method.Name.StartsWith("get_")) {
                            property.PropertyType = method.ReturnType;
                        }
                        break;
                    }
                }

                if (property == null) {
                    Console.WriteLine("Property not found; creating new property...");
                    property = new PropertyDefinition(method.Name.Substring(4), PropertyAttributes.None, method.ReturnType);
                    origType.Properties.Add(property);
                }

                if (method.Name.StartsWith("get_")) {
                    Console.WriteLine("Replacing getter...");
                    property.GetMethod = method;
                } else {
                    Console.WriteLine("Replacing setter...");
                    property.SetMethod = method;
                }
            }
        }

        /// <summary>
        /// Patches the references in all of the given types.
        /// </summary>
        /// <param name="types">Types to patch.</param>
        public virtual void PatchRefs(List<TypeDefinition> types) {
            foreach (TypeDefinition type in types) {
                if (type == null) {
                    continue;
                }
                string typeName = type.FullName;
                Console.WriteLine("TR: "+typeName);

                typeName = RemovePrefixes(typeName, type.Name);

                TypeDefinition origType = Module.GetType(typeName);
                IsBlacklisted(origType.Module.Name, origType.FullName, HasAttribute(origType, "MonoModBlacklisted"));
                for (int ii = 0; ii < type.Methods.Count; ii++) {
                    MethodDefinition method = type.Methods[ii];

                    if (!AllowedSpecialName(method) || HasAttribute(method, "MonoModIgnore")) {
                        continue;
                    }

                    for (int iii = 0; iii < origType.Methods.Count; iii++) {
                        MethodDefinition origMethod = origType.Methods[iii];
                        if (origMethod.FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                            method = origMethod;
                            Console.WriteLine("MR: "+method.FullName);
                            PatchRefsInMethod(method);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Patches the references in method.
        /// </summary>
        /// <param name="method">Method to patch.</param>
        public virtual void PatchRefsInMethod(MethodDefinition method) {
            if (method.Name.StartsWith("orig_")) {
                Console.WriteLine(method.Name + " is an orig_ method; ignoring...");
                return;
            }

            Console.WriteLine("Patching references in "+method.Name+" ...");

            Console.WriteLine("Checking for original methods...");

            TypeDefinition origType = Module.GetType(RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType.Name));

            MethodDefinition origMethodOrig = null; //orig_ method (f.e. orig_X)

            for (int i = 0; i < origType.Methods.Count; i++) {
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName.Replace(method.Name, "orig_"+method.Name), method.DeclaringType.Name)) {
                    origMethodOrig = origType.Methods[i];
                }
            }

            if (origMethodOrig != null) {
                IsBlacklisted(origMethodOrig.Module.Name, method.DeclaringType+"."+RemovePrefixes(method.Name), HasAttribute(origMethodOrig, "MonoModBlacklisted"));
                Console.WriteLine("Prefixed method existing; ignoring...");
            }

            Console.WriteLine("Modifying method body...");
            for (int i = 0; method.HasBody && i < method.Body.Instructions.Count; i++) {
                Instruction instruction = method.Body.Instructions[i];
                object operand = instruction.Operand;

                if (operand is MethodReference) {
                    MethodReference methodCalled = (MethodReference) operand;
                    if (methodCalled.FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                        operand = method;
                    } else {
                        MethodReference findMethod = FindMethod(methodCalled, method, false);

                        if (origMethodOrig != null && methodCalled.FullName == origMethodOrig.FullName) {
                            Console.WriteLine("Found call to the original method; linking...");
                            findMethod = origMethodOrig;
                        }

                        if (findMethod == null) {
                            try {
                                findMethod = Module.Import(methodCalled);
                            } catch {
                                //uh. generic type failed importing?
                            }
                        }

                        if (findMethod == null && methodCalled.IsGenericInstance) {
                            GenericInstanceMethod genericMethodCalled = ((GenericInstanceMethod) methodCalled);
                            Console.WriteLine("Calling method: " + genericMethodCalled.FullName);
                            Console.WriteLine("Element method: " + genericMethodCalled.ElementMethod.FullName);
                            GenericInstanceMethod genericMethod = new GenericInstanceMethod(FindMethod(genericMethodCalled.ElementMethod, method, true));

                            for (int gi = 0; gi < genericMethodCalled.GenericArguments.Count; gi++) {
                                Console.WriteLine("Generic argument: " + genericMethodCalled.GenericArguments[gi]);
                                //genericMethod.GenericArguments.Add(genericMethodCalled.GenericArguments[gi]);
                                for (int gii = 0; gii < method.GenericParameters.Count; gii++) {
                                    GenericParameter genericParam = method.GenericParameters[gii];
                                    Console.WriteLine("Checking against: " + genericParam.FullName);
                                    if (genericParam.FullName == genericMethodCalled.GenericArguments[gi].FullName) {
                                        Console.WriteLine("Success!");
                                        genericMethod.GenericArguments.Add(genericParam);
                                        break;
                                    }
                                }
                            }

                            findMethod = genericMethod;
                        }
                        
                        MethodDefinition findMethodDef = findMethod == null ? null : findMethod.Resolve();
                        if (findMethodDef != null) {
                            IsBlacklisted(findMethod.Module.Name, findMethod.DeclaringType.FullName+"."+findMethod.Name, HasAttribute(findMethodDef, "MonoModBlacklisted"));
                            //Quite untested - fixes invalid IL when calling virtual methods when not virtual in patch
                            if (findMethodDef.Attributes.HasFlag(MethodAttributes.Virtual)) {
                                instruction.OpCode = OpCodes.Callvirt;
                            }
                        }
                        
                        operand = findMethod ?? Module.Import(methodCalled);
                    }
                }

                if (operand is FieldReference) {
                    FieldReference field = (FieldReference) operand;

                    TypeReference findTypeRef = FindType(field.DeclaringType, method, false);
                    TypeDefinition findType = findTypeRef == null ? null : findTypeRef.Resolve();
                    if (findType != null) {
                        for (int ii = 0; ii < findType.Fields.Count; ii++) {
                            if (findType.Fields[ii].Name == field.Name) {
                                field = findType.Fields[ii];
                                if (field.Module != Module) {
                                    field = Module.Import(field);
                                    //Console.WriteLine("F: ref->dep: "+field.FullName);
                                } else {
                                    //Console.WriteLine("F: ref->in: "+field.FullName);
                                }
                                break;
                            }
                        }
                    }

                    if (field == operand && findType != null) {
                        //Console.WriteLine("F: new: " + field.FullName);
                        FieldDefinition oldField = null;
                        TypeDefinition oldType = (TypeDefinition) field.DeclaringType;
                        for (int ii = 0; ii < oldType.Fields.Count; ii++) {
                            if (oldType.Fields[ii].Name == field.Name) {
                                oldField = oldType.Fields[ii];
                                break;
                            }
                        }
                        if (oldField != null) {
                            FieldDefinition newField = new FieldDefinition(field.Name, oldField.Attributes, FindType(oldField.FieldType, method));
                            newField.InitialValue = oldField.InitialValue;
                            findType.Fields.Add(newField);
                        }
                    }

                    if (field == operand) {
                        field = new FieldReference(field.Name, FindType(field.FieldType, method), FindType(field.DeclaringType, method));
                    }

                    if (field != null) {
                        IsBlacklisted(field.Module.Name, field.DeclaringType.FullName+"."+field.Name);
                    }
                    operand = field;
                }

                if (operand is TypeReference) {
                    operand = FindType((TypeReference) operand, method);
                }

                instruction.Operand = operand;
            }

            for (int i = 0; method.HasBody && i < method.Body.Variables.Count; i++) {
                method.Body.Variables[i].VariableType = FindType(method.Body.Variables[i].VariableType, method);
            }

            if (method.ReturnType.IsGenericParameter) {
                method.ReturnType = FindTypeGeneric(method.ReturnType, method);

                /*
                TypeReference returnType = method.ReturnType;

                Console.WriteLine("Generic param wanted: " + returnType.FullName);
                Console.WriteLine("Method: " + method.FullName);
                for (int gi = 0; gi < method.GenericParameters.Count; gi++) {
                    GenericParameter genericParam = method.GenericParameters[gi];
                    Console.WriteLine("Checking against: " + genericParam.FullName);
                    if (genericParam.FullName == returnType.FullName) {
                        Console.WriteLine("Success!");
                        method.ReturnType = Module.Import(genericParam);
                        break;
                    }
                }
                */
            }

            for (int ei = 0; method.HasBody && ei < method.Body.ExceptionHandlers.Count; ei++) {
                if (method.Body.ExceptionHandlers[ei].CatchType == null) {
                    continue;
                }
                method.Body.ExceptionHandlers[ei].CatchType = FindType(method.Body.ExceptionHandlers[ei].CatchType, method);
            }
        }

        /// <summary>
        /// Finds a type in the input module based on a type in any other module.
        /// </summary>
        /// <returns>The found type or either null or the imported type.</returns>
        /// <param name="type">Type to find.</param>
        /// <param name="context">Context containing some info.</param>
        /// <param name="fallbackToImport">If set to <c>true</c> this method returns the type to find as imported in the input module.</param>
        public virtual TypeReference FindType(TypeReference type, MemberReference context = null, bool fallbackToImport = true) {
            if (type == null) {
                Console.WriteLine("ERROR: Can't find null type!");
                Console.WriteLine(Environment.StackTrace);
                return null;
            }
            string typeName = RemovePrefixes(type.FullName, type.Name);
            TypeReference foundType = Module.GetType(typeName);
            if (foundType == null && type.IsByReference) {
                foundType = FindType(type.GetElementType(), context, fallbackToImport);
            }
            if (foundType == null && type.IsArray) {
                //TODO return proper array?
                foundType = FindType(type.GetElementType(), context, fallbackToImport);
            }
            if (foundType == null && context != null && type.IsGenericParameter) {
                foundType = FindTypeGeneric(type, context, fallbackToImport);
            }
            if (foundType == null && context != null && type.IsGenericInstance) {
                foundType = new GenericInstanceType(FindType(type.GetElementType(), context, fallbackToImport));
                foreach (TypeReference genericArgument in ((GenericInstanceType) type).GenericArguments) {
                    ((GenericInstanceType) foundType).GenericArguments.Add(FindType(genericArgument, context));
                }
            }
            if (foundType == null) {
                foreach (ModuleDefinition dependency in Dependencies) {
                    foundType = dependency.GetType(typeName);
                    if (foundType != null) {
                        return Module.Import(foundType);
                    }
                }
            }
            if (foundType != null && foundType.IsDefinition) {
                IsBlacklisted(foundType.Module.Name, foundType.FullName, HasAttribute(foundType.Resolve(), "MonoModBlacklisted"));
            }
            if (type.IsGenericParameter) {
                return foundType ?? (fallbackToImport ? type : null);
            }
            return foundType ?? (fallbackToImport ? Module.Import(type) : null);
        }

        /// <summary>
        /// Finds a generic type / generic parameter based on a generic parameter type in the context.
        /// </summary>
        /// <returns>The found type or either null or the imported type.</returns>
        /// <param name="type">Type to find.</param>
        /// <param name="context">Context containing the param.</param>
        /// <param name="fallbackToImport">If set to <c>true</c> this method returns the type to find as imported in the input module.</param>
        public virtual TypeReference FindTypeGeneric(TypeReference type, MemberReference context, bool fallbackToImport = true) {
            if (context is MethodReference) {
                for (int gi = 0; gi < ((MethodReference) context).GenericParameters.Count; gi++) {
                    GenericParameter genericParam = ((MethodReference) context).GenericParameters[gi];
                    if (genericParam.FullName == type.FullName) {
                        //TODO variables hate me, import otherwise
                        return genericParam;
                    }
                }
            }
            if (context is TypeReference) {
                for (int gi = 0; gi < ((TypeReference) context).GenericParameters.Count; gi++) {
                    GenericParameter genericParam = ((TypeReference) context).GenericParameters[gi];
                    if (genericParam.FullName == type.FullName) {
                        //TODO variables hate me, import otherwise
                        return genericParam;
                    }
                }
            }
            if (context.DeclaringType != null) {
                return FindTypeGeneric(type, context.DeclaringType, fallbackToImport);
            }
            return fallbackToImport ? type : null;
        }

        /// <summary>
        /// Finds a method in the input module based on a method in any other module.
        /// </summary>
        /// <returns>The found method or either null or the imported method.</returns>
        /// <param name="method">Method to find.</param>
        /// <param name="context">Context containing some info.</param>
        /// <param name="fallbackToImport">If set to <c>true</c> this method returns the method to find as imported in the input module.</param>
        public virtual MethodReference FindMethod(MethodReference method, MemberReference context, bool fallbackToImport) {
            TypeReference findTypeRef = FindType(method.DeclaringType, context, true);
            TypeDefinition findType = findTypeRef == null ? null : findTypeRef.Resolve();

            if (findType != null) {
                bool typeMismatch = findType.FullName != RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType.Name);
                
                string methodName = RemovePrefixes(method.FullName, method.DeclaringType.Name);
                methodName = methodName.Substring(methodName.IndexOf(" ") + 1);
                for (int ii = 0; ii < findType.Methods.Count; ii++) {
                    MethodReference foundMethod = findType.Methods[ii];
                    string foundMethodName = foundMethod.FullName;
                    foundMethodName = foundMethodName.Replace(findType.FullName, findTypeRef.FullName);
                    foundMethodName = foundMethodName.Substring(foundMethodName.IndexOf(" ") + 1);
                    //TODO find a better way to compare methods / fix comparing return types
                    
                    if (methodName == foundMethodName ||
                        methodName == ReplaceGenerics(foundMethodName, foundMethod, findType)) {
                        IsBlacklisted(foundMethod.Module.Name, foundMethod.DeclaringType.FullName+"."+foundMethod.Name, HasAttribute(foundMethod.Resolve(), "MonoModBlacklisted"));
                        
                        if (typeMismatch && method.DeclaringType.IsGenericInstance) {
                            //TODO test return type context
                            MethodReference genMethod = new MethodReference(method.Name, FindType(method.ReturnType, context), findTypeRef);
                            genMethod.CallingConvention = method.CallingConvention;
                            genMethod.HasThis = method.HasThis;
                            genMethod.ExplicitThis = method.ExplicitThis;
                            for (int i = 0; i < method.GenericParameters.Count; i++) {
                                genMethod.GenericParameters.Add(new GenericParameter(method.GenericParameters[i].Name, genMethod));
                            }
                            for (int i = 0; i < method.Parameters.Count; i++) {
                                genMethod.Parameters.Add(new ParameterDefinition(FindType(method.Parameters[i].ParameterType, genMethod)));
                            }
                            
                            foundMethod = Module.Import(genMethod);
                        }
                        
                        if (foundMethod.Module != Module) {
                            foundMethod = Module.Import(foundMethod);
                        }
                        
                        return foundMethod;
                    }
                }
            }
            
            //For anyone trying to find out why / when no method gets found: Take this!
            /*
            Console.WriteLine("debug a: " + method.FullName);
            Console.WriteLine("debug b: " + findTypeRef);
            Console.WriteLine("debug c: " + findType);
            */

            return fallbackToImport ? Module.Import(method) : null;
        }

        /// <summary>
        /// Loads a dependency and adds it to Dependencies. Requires the field Dir to be set.
        /// </summary>
        /// <param name="dependency">Dependency to load.</param>
        public virtual void LoadDependency(string dependency) {
            FileInfo dependencyFile = new FileInfo(Dir.FullName+Path.DirectorySeparatorChar+dependency+".dll");
            if (!dependencyFile.Exists) {
                dependencyFile = new FileInfo(Dir.FullName+Path.DirectorySeparatorChar+dependency+".exe");
            }
            if (!dependencyFile.Exists) {
                dependencyFile = new FileInfo(Dir.FullName+Path.DirectorySeparatorChar+dependency);
            }
            if (!dependencyFile.Exists) {
                Console.WriteLine("WARNING: Dependency \""+dependency+"\" not found; ignoring...");
                return;
            }
            ModuleDefinition module = ModuleDefinition.ReadModule(dependencyFile.FullName);
            Dependencies.Add(module);
            LoadBlacklist(module);
            Console.WriteLine("Dependency \""+dependency+"\" loaded.");
        }

        /// <summary>
        /// Loads the blacklist from the module.
        /// </summary>
        /// <param name="module">Module to load the blacklist from.</param>
        public void LoadBlacklist(ModuleDefinition module) {
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                TypeDefinition type = Module.Types[ti];
                if (type.Name != "MonoModBlacklist") {
                    continue;
                }
                for (int ii = 0; ii < type.Methods.Count; ii++) {
                    MethodDefinition method = type.Methods[ii];
                    if (method.Name != ".cctor") {
                        continue;
                    }
                    for (int i = 0; i < method.Body.Instructions.Count; i++) {
                        Instruction instruction = method.Body.Instructions[i];
                        if (instruction.OpCode != OpCodes.Ldstr) {
                            continue;
                        }
                        string item_ = (string) instruction.Operand;
                        int splitIndex = item_.IndexOf(":");
                        BlacklistItem item = new BlacklistItem(item_.Substring(0, splitIndex), item_.Substring(splitIndex + 1));
                        loadedBlacklist.Add(item);
                    }
                }
            }
        }

        /// <summary>
        /// Patches the (entry) method to output the used MonoMod version in the console.
        /// </summary>
        /// <returns>The new entry method.</returns>
        /// <param name="entryOld">The old entry method.</param>
        public virtual MethodDefinition PatchEntry(MethodDefinition entryOld) {
            if (entryOld == null) {
                Console.WriteLine("Entry point not found; skipping...");
                return null;
            }

            Console.WriteLine("M:"+entryOld.Name);

            entryOld.Name = "orig_"+entryOld.Name;

            MethodDefinition entry = new MethodDefinition("Main", MethodAttributes.Public | MethodAttributes.Static, Module.Import(typeof(void)));
            entry.Parameters.Add(new ParameterDefinition(Module.Import(typeof(string[]))));

            MethodBody body = new MethodBody(entry);
            ILProcessor processor = body.GetILProcessor();

            processor.Emit(OpCodes.Ldstr, "MonoMod "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            processor.Emit(OpCodes.Call, Module.Import(typeof(Console).GetMethod("WriteLine", new Type[] {typeof(string)})));

            processor.Emit(OpCodes.Ldarg_0);
            processor.Emit(OpCodes.Call, entryOld);

            processor.Emit(OpCodes.Ret);

            entry.Body = body;

            entryOld.DeclaringType.Methods.Add(entry);
            Module.EntryPoint = entry;

            return entry;
        }

        /// <summary>
        /// Patches the type MonoMod.WasHere into the output module if it didn't exist yet.
        /// </summary>
        public virtual TypeDefinition PatchWasHere() {
            Console.WriteLine("Checking if MonoMod already was there...");
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "WasHere") {
                    Console.WriteLine("MonoMod was there.");
                    return Module.Types[ti].Resolve();
                }
            }
            Console.WriteLine("Adding MonoMod.WasHere");
            TypeDefinition wasHere = new TypeDefinition("MonoMod", "WasHere", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.Import(typeof(object))
            };
            Module.Types.Add(wasHere);
            return wasHere;
        }

        /// <summary>
        /// Removes all MonoMod prefixes from the given string.
        /// </summary>
        /// <returns>The prefixes.</returns>
        /// <param name="str">String to remove the prefixes from or the string containing strPrefixed.</param>
        /// <param name="strPrefixed">String to remove the prefixes from when part of str.</param>
        public static string RemovePrefixes(string str, string strPrefixed = null) {
            strPrefixed = strPrefixed ?? str;
            str = RemovePrefix(str, "patch_", strPrefixed);
            str = RemovePrefix(str, "remove_", strPrefixed);
            str = RemovePrefix(str, "replace_", strPrefixed);
            return str;
        }

        /// <summary>
        /// Removes the prefix from the given string.
        /// </summary>
        /// <returns>The prefix.</returns>
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
        
        public static string ReplaceGenerics(string str, MethodReference method, TypeReference type) {
            if (!type.HasGenericParameters) {
                return str;
            }
            for (int i = 0; i < type.GenericParameters.Count; i++) {
                str = str.Replace(type.GenericParameters[i].Name, "!"+i);
            }
            for (int i = 0; i < method.GenericParameters.Count; i++) {
                str = str.Replace(method.GenericParameters[i].Name, "!!"+i);
            }
            return str;
        }

        /// <summary>
        /// Checks if the method has a special name that is "allowed" to be patched.
        /// </summary>
        /// <returns><c>true</c> if the special name used in the method is allowed, <c>false</c> otherwise.</returns>
        /// <param name="method">Method to check.</param>
        public static bool AllowedSpecialName(MethodDefinition method) {
            if (method.IsConstructor && (method.HasCustomAttributes || method.IsStatic)) {
                if (method.IsStatic) {
                    return true;
                }
                //Overriding the constructor manually is generally a horrible idea, but who knows where it may be used.
                foreach (CustomAttribute attrib in method.CustomAttributes) {
                    if (attrib.AttributeType.FullName == "MonoMod.MonoModConstructor") {
                        return true;
                    }
                }
            }

            return !method.Attributes.HasFlag(MethodAttributes.SpecialName);
        }

        /// <summary>
        /// Determines if the method has got a specific MonoMod attribute.
        /// </summary>
        /// <returns><c>true</c> if the method contains the given MonoMod attribute, <c>false</c> otherwise.</returns>
        /// <param name="method">Method.</param>
        /// <param name="attribute">Attribute.</param>
        public static bool HasAttribute(MethodDefinition method, string attribute) {
            if (method == null) {
                return false;
            }
            if (!method.HasCustomAttributes) {
                return false;
            }
            foreach (CustomAttribute attrib in method.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod." + attribute) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines if the type has got a specific MonoMod attribute.
        /// </summary>
        /// <returns><c>true</c> if the type contains the given MonoMod attribute, <c>false</c> otherwise.</returns>
        /// <param name="type">Type.</param>
        /// <param name="attribute">Attribute.</param>
        public static bool HasAttribute(TypeDefinition type, string attribute) {
            if (type == null) {
                return false;
            }
            if (!type.HasCustomAttributes) {
                return false;
            }
            foreach (CustomAttribute attrib in type.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod." + attribute) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines if the full name is in any of the blacklists.
        /// </summary>
        /// <returns><c>true</c> if the full name is in a blacklist, <c>false</c> otherwise.</returns>
        /// <param name="assemblyName">Assembly name.</param>
        /// <param name="fullName">Full name.</param>
        public static bool IsBlacklisted(string assemblyName, string fullName, bool hasBlacklistedAttr = false, bool throwWhenBlacklisted = true) {
            if (assemblyName.EndsWith(".dll")) {
                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
            }
            if (hasBlacklistedAttr) {
                if (throwWhenBlacklisted) {
                    throw new AccessViolationException("Keep yar' dirtay fingars away! " + assemblyName + ":" + fullName);
                }
                return true;
            }
            foreach (BlacklistItem item in GlobalBlacklist) {
                if (item.AssemblyName == assemblyName && item.FullName == fullName) {
                    if (throwWhenBlacklisted) {
                        throw new AccessViolationException("Keep yar' dirtay fingars away! " + assemblyName + ":" + fullName);
                    }
                    return true;
                }
            }
            foreach (BlacklistItem item in loadedBlacklist) {
                if (item.AssemblyName == assemblyName && item.FullName == fullName) {
                    if (throwWhenBlacklisted) {
                        throw new AccessViolationException("Keep yar' dirtay fingars away! " + assemblyName + ":" + fullName);
                    }
                    return true;
                }
            }
            return false;
        }

    }
}
