using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using Mono.Collections.Generic;

namespace MonoMod {
    public class MonoMod {

        public FileInfo In;
        public DirectoryInfo Dir;
        public FileInfo Out;

        public ModuleDefinition Module;
        public List<ModuleDefinition> Dependencies = new List<ModuleDefinition>();
        public MethodDefinition Entry;

        public MonoMod() {
        }

        public MonoMod(string input) : this(new FileInfo(input)) {
        }

        public MonoMod(FileInfo input) {
            In = input;
        }

        public static void Main(string[] args) {
            Console.WriteLine("MonoMod "+System.Reflection.Assembly.GetCallingAssembly().GetName().Version);

            if (args.Length != 1) {
                Console.WriteLine("No valid arguments (executable path) passed.");
                return;
            }

            MonoMod mm = new MonoMod(args[0]);

            mm.Patch();
        }

        public void Patch() {
            if (Dir == null) {
                Dir = In.Directory;
            }
            if (Out == null) {
                Out = new FileInfo(In.FullName.Substring(0, In.FullName.Length-4)+".mm.exe");
            }

            Console.WriteLine("Patching "+In.Name+" ...");

            Console.WriteLine("Reading assembly as Mono.Cecil ModuleDefinition and AssemblyDefinition...");
            Module = ModuleDefinition.ReadModule(In.FullName);

            Console.WriteLine("Reading module dependencies...");
            for (int mi = 0; mi < Module.ModuleReferences.Count; mi++) {
                LoadDependency(Module.ModuleReferences[mi].Name);
            }

            Console.WriteLine("Reading assembly dependencies...");
            for (int mi = 0; mi < Module.AssemblyReferences.Count; mi++) {
                LoadDependency(Module.AssemblyReferences[mi].Name);
            }

            Dependencies.Remove(Module);

            Console.WriteLine("Reading main EntryPoint MethodDefinition...");
            Entry = Module.EntryPoint;

            Console.WriteLine("Replacing main EntryPoint...");
            MethodDefinition entryOld = Entry;
            Entry = PatchEntry(entryOld);
            if (Entry != null) {
                entryOld.DeclaringType.Methods.Add(Entry);
                Module.EntryPoint = Entry;
            }

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

            Console.WriteLine("Writing to output file...");
            Module.Write(Out.FullName);

            Console.WriteLine("Done.");
        }

        public void PatchModule(ModuleDefinition mod, List<TypeDefinition> types) {
            Module.AssemblyReferences.Add(mod.Assembly.Name);

            for (int i = 0; i < mod.Types.Count; i++) {
                PatchType(mod.Types[i], types);
            }
        }

        public void PatchType(TypeDefinition type, List<TypeDefinition> types) {
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

                FieldDefinition newField = new FieldDefinition(field.Name, field.Attributes, FindType(field.FieldType));
                newField.InitialValue = field.InitialValue;
                origTypeResolved.Fields.Add(newField);
            }

            types.Add(type);
        }

        public void PatchMethod(MethodDefinition method) {
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
                if (method.Name.StartsWith("replace_") || HasAttribute(method, "MonoModReplace")) {
                    Console.WriteLine("Method existing; replacing...");
                } else {
                    Console.WriteLine("Method existing; creating copy...");

                    MethodDefinition copy = new MethodDefinition("orig_"+origMethod.Name, origMethod.Attributes, origMethod.ReturnType);
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
            if (origMethod != null && origMethod.IsConstructor && origMethod.IsStatic) {
                Collection<Instruction> instructions = method.Body.Instructions;
                ILProcessor ilProcessor = method.Body.GetILProcessor();
                ilProcessor.InsertBefore(instructions[instructions.Count - 1], ilProcessor.Create(OpCodes.Call, origMethodOrig));
            }

            for (int i = 0; i < method.Body.Variables.Count; i++) {
                method.Body.Variables[i].VariableType = FindType(method.Body.Variables[i].VariableType);
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
                MethodDefinition clone = new MethodDefinition(method.Name, attribs, FindType(method.ReturnType));
                for (int i = 0; i < method.Parameters.Count; i++) {
                    clone.Parameters.Add(new ParameterDefinition(FindType(method.Parameters[i].ParameterType)));
                }
                for (int i = 0; i < method.GenericParameters.Count; i++) {
                    clone.GenericParameters.Add(new GenericParameter(method.GenericParameters[i].Name, clone));
                }
                clone.Body = method.Body;
                origType.Methods.Add(clone);
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

        public void PatchRefs(List<TypeDefinition> types) {
            foreach (TypeDefinition type in types) {
                if (type == null) {
                    continue;
                }
                string typeName = type.FullName;
                Console.WriteLine("TR: "+typeName);

                typeName = RemovePrefixes(typeName, type.Name);

                TypeDefinition origType = Module.GetType(typeName);
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

        public void PatchRefsInMethod(MethodDefinition method) {
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
                Console.WriteLine("Prefixed method existing; ignoring...");
            }

            Console.WriteLine("Modifying method body...");
            for (int i = 0; i < method.Body.Instructions.Count; i++) {
                Instruction instruction = method.Body.Instructions[i];
                object operand = instruction.Operand;

                if (operand is MethodReference) {
                    MethodReference methodCalled = (MethodReference) operand;
                    if (methodCalled.FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                        operand = method;
                    } else {
                        MethodReference findMethod = FindMethod(methodCalled, false);

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
                            GenericInstanceMethod genericMethod = new GenericInstanceMethod(FindMethod(genericMethodCalled.ElementMethod, true));

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

                        operand = findMethod ?? Module.Import(methodCalled);
                    }
                }

                if (operand is FieldReference) {
                    FieldReference field = (FieldReference) operand;

                    TypeReference findTypeRef = FindType(field.DeclaringType, false);
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
                            FieldDefinition newField = new FieldDefinition(field.Name, oldField.Attributes, FindType(oldField.FieldType));
                            newField.InitialValue = oldField.InitialValue;
                            findType.Fields.Add(newField);
                        }
                    }

                    if (field == operand) {
                        field = new FieldReference(field.Name, FindType(field.FieldType), FindType(field.DeclaringType));
                    }

                    operand = field;
                }

                if (operand is TypeReference) {
                    if (((TypeReference) operand).IsGenericParameter) {
                        Console.WriteLine("Generic param wanted: " + ((TypeReference) operand).FullName);
                        Console.WriteLine("Method: " + method.FullName);
                        for (int gi = 0; gi < method.GenericParameters.Count; gi++) {
                            GenericParameter genericParam = method.GenericParameters[gi];
                            Console.WriteLine("Checking against: " + genericParam.FullName);
                            if (genericParam.FullName == ((TypeReference) operand).FullName) {
                                Console.WriteLine("Success!");
                                operand = Module.Import(genericParam);
                                break;
                            }
                        }
                    } else {
                        operand = FindType((TypeReference) operand);
                    }
                }

                instruction.Operand = operand;

                if (instruction.ToString().Contains("System.Exception") || instruction.ToString().Contains("catch")) {
                    Console.WriteLine("lolwut " + instruction);
                    Console.WriteLine(instruction.Operand.GetType().FullName);
                }
            }

            for (int i = 0; i < method.Body.Variables.Count; i++) {
                if (method.Body.Variables[i].VariableType.IsGenericParameter) {
                    TypeReference variableType = method.Body.Variables[i].VariableType;

                    Console.WriteLine("Generic param wanted: " + variableType.FullName);
                    Console.WriteLine("Method: " + method.FullName);
                    for (int gi = 0; gi < method.GenericParameters.Count; gi++) {
                        GenericParameter genericParam = method.GenericParameters[gi];
                        Console.WriteLine("Checking against: " + genericParam.FullName);
                        if (genericParam.FullName == variableType.FullName) {
                            Console.WriteLine("Success!");
                            method.Body.Variables[i].VariableType = Module.Import(genericParam);
                            break;
                        }
                    }
                } else {
                    method.Body.Variables[i].VariableType = FindType(method.Body.Variables[i].VariableType);
                }
            }

            if (method.ReturnType.IsGenericParameter) {
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
            }

            for (int ei = 0; ei < method.Body.ExceptionHandlers.Count; ei++) {
                if (method.Body.ExceptionHandlers[ei].CatchType == null) {
                    continue;
                }
                method.Body.ExceptionHandlers[ei].CatchType = FindType(method.Body.ExceptionHandlers[ei].CatchType, true);
            }
        }

        public TypeReference FindType(TypeReference type, bool fallbackToImport = true) {
            if (type == null) {
                Console.WriteLine("ERROR: Can't find null type!");
                Console.WriteLine(Environment.StackTrace);
                return null;
            }
            string typeName = RemovePrefixes(type.FullName, type.Name);
            TypeReference foundType = Module.GetType(typeName);
            if (foundType == null) {
                foreach (ModuleDefinition dependency in Dependencies) {
                    foundType = dependency.GetType(typeName);
                    if (foundType != null) {
                        return Module.Import(foundType);
                    }
                }
            }
            if (type.IsGenericParameter) {
                return foundType ?? (fallbackToImport ? type : null);
            }
            return foundType ?? (fallbackToImport ? Module.Import(type) : null);
        }

        public MethodReference FindMethod(MethodReference method, bool fallbackToImport) {
            TypeReference findTypeRef = FindType(method.DeclaringType, false);
            TypeDefinition findType = findTypeRef == null ? null : findTypeRef.Resolve();

            if (method != null && findType != null) {
                for (int ii = 0; ii < findType.Methods.Count; ii++) {
                    if (findType.Methods[ii].FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                        MethodReference foundMethod = findType.Methods[ii];
                        if (foundMethod.Module != Module) {
                            foundMethod = Module.Import(foundMethod);
                        }
                        return foundMethod;
                    }
                }
            }

            return fallbackToImport ? Module.Import(method) : null;
        }

        public void LoadDependency(string dependency) {
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
            Dependencies.Add(ModuleDefinition.ReadModule(dependencyFile.FullName));
            Console.WriteLine("Dependency \""+dependency+"\" loaded.");
        }

        public MethodDefinition PatchEntry(MethodDefinition entryOld) {
            if (entryOld == null) {
                Console.WriteLine("Entry point not found; skipping...");
                return null;
            }

            Console.WriteLine("M:"+entryOld.Name);

            entryOld.Name = "orig_"+entryOld.Name;

            MethodAttributes attribs = MethodAttributes.Public | MethodAttributes.Static;
            MethodDefinition entry = new MethodDefinition("Main", attribs, Module.Import(typeof(void)));
            entry.Parameters.Add(new ParameterDefinition(Module.Import(typeof(string[]))));

            MethodBody body = new MethodBody(entry);
            ILProcessor processor = body.GetILProcessor();

            processor.Emit(OpCodes.Ldstr, "MonoMod "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            processor.Emit(OpCodes.Call, Module.Import(typeof(Console).GetMethod("WriteLine", new Type[] {typeof(string)})));


            processor.Emit(OpCodes.Ldarg_0);
            processor.Emit(OpCodes.Call, entryOld);

            processor.Emit(OpCodes.Ret);


            entry.Body = body;

            return entry;
        }

        public static string RemovePrefixes(string str, string strPrefixed = null) {
            strPrefixed = strPrefixed ?? str;
            str = RemovePrefix(str, "patch_", strPrefixed);
            str = RemovePrefix(str, "remove_", strPrefixed);
            str = RemovePrefix(str, "replace_", strPrefixed);
            return str;
        }

        public static string RemovePrefix(string str, string prefix, string strPrefixed = null) {
            strPrefixed = strPrefixed ?? str;
            if (strPrefixed.StartsWith(prefix)) {
                return str.Replace(strPrefixed, strPrefixed.Substring(prefix.Length));
            }
            return str;
        }

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

        public static bool HasAttribute(MethodDefinition method, string attribute) {
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

        public static bool HasAttribute(TypeDefinition type, string attribute) {
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

    }
}
