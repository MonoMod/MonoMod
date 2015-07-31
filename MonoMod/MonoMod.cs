/* Enable compilation of generic method reference resolution
 * 
 * When fixing / patching the references in methods, patch the references to other
 * generic methods more deeply by importing (TODO: finding) the element method
 * instead of simply importing the generic method. Should fix references to generic methods
 * across assemblies. MonoMod will additionally print out a warning.
 * 
 * This is still early WIP and thus not really supported in release environments.
 * It is enabled by default, as it has been partially "fixed" (doesn't break working references anymore; effect on broken references unknown).
 * 
 * 0x0ade
 */
#define GENERIC_METHOD_REFERENCE

/* Enable handling of generic types when finding them in FindType
 * 
 * When finding a type, it is possible that the type that is being searched is a generic type.
 * In case this is true, MonoMod will print out a warning and instead of importing the type when
 * the type has not been found, FindType will return null.
 * 
 * This is still early WIP and thus not really supported in release environments.
 * It is enabled by default, as it currently helps debugging issues with generic references.
 * 
 * 0x0ade
 */
#define GENERIC_TYPE_IMPORT

/* Enable handling of generic types (f.e. type parameters) when referencing them in methods.
 * 
 * Similar to GENERIC_METHOD_REFERENCE, but doesn't seem to break as much and with types, not methods.
 * 
 * This is still early WIP and thus not really supported in release environments.
 * It is enabled by default, as it currently helps debugging issues with generic references.
 * 
 * 0x0ade
 */
#define GENERIC_TYPE_REFERENCE

/* Enable handling of generic typed variables when referencing them in methods.
 * 
 * Similar to GENERIC_METHOD_REFERENCE, but doesn't seem to break as much and with types, not methods.
 * 
 * This is still early WIP and thus not really supported in release environments.
 * It is enabled by default, as it currently helps debugging issues with generic references.
 * 
 * 0x0ade
 */
#define GENERIC_TYPE_VARIABLE

/* Enable handling of generic typed returns in methods.
 * 
 * Similar to GENERIC_METHOD_REFERENCE, but doesn't seem to break as much and with types, not methods.
 * 
 * This is still early WIP and thus not really supported in release environments.
 * It is enabled by default, as it currently helps debugging issues with generic references.
 * 
 * 0x0ade
 */
#define GENERIC_TYPE_RETURN

/* Enable handling of generic parameters as operands in methods.
 * 
 * Similar to GENERIC_METHOD_REFERENCE, but doesn't seem to break as much and with types, not methods.
 * 
 * This is still early WIP and thus not really supported in release environments.
 * It is disabled by default, as it would activate reference code that - when ran - will never reach execution.
 * 
 * 0x0ade
 */
//#define GENERIC_PARAM

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
            for (int i = 0; i < Module.ModuleReferences.Count; i++) {
                LoadDependency(Module.ModuleReferences[i].Name);
            }

            Console.WriteLine("Reading assembly dependencies...");
            for (int i = 0; i < Module.AssemblyReferences.Count; i++) {
                LoadDependency(Module.AssemblyReferences[i].Name);
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

            string fileName = In.Name.Substring(0, In.Name.IndexOf("."));
            Console.WriteLine("Scanning for files matching "+fileName+".*.mm.dll ...");
            foreach (FileInfo f in Dir.GetFiles()) {
                if (f.Name.StartsWith(fileName) && f.Name.ToLower().EndsWith(".mm.dll")) {
                    Console.WriteLine("Found "+f.Name+" , reading...");
                    ModuleDefinition mod = ModuleDefinition.ReadModule(f.FullName);
                    PatchModule(mod);
                }
            }

            Console.WriteLine("Writing to output file...");
            Module.Write(Out.FullName);

            Console.WriteLine("Done.");
        }

        public void PatchModule(ModuleDefinition mod) {
            Module.AssemblyReferences.Add(mod.Assembly.Name);

            TypeDefinition[] types = new TypeDefinition[mod.Types.Count];
            for (int i = 0; i < mod.Types.Count; i++) {
                TypeDefinition type = mod.Types[i];
                string typeName = type.FullName;
                Console.WriteLine("T: " + typeName);

                typeName = RemovePrefixes(typeName, type.Name);

                if (type.Attributes.HasFlag(TypeAttributes.NotPublic) &&
                    type.Attributes.HasFlag(TypeAttributes.Interface)) {
                    Console.WriteLine("Type is a private interface; ignore...");
                    continue;
                }

                if (HasIgnoreAttribute(type)) {
                    continue;
                }

                TypeReference origType = Module.GetType(typeName, true);
                if (origType == null) {
                    if (!type.Name.StartsWith("patch_")) {
                        Module.Types.Add(Module.Import(type).Resolve());
                    }
                    continue;
                }

                origType = Module.GetType(typeName, false);
                if (origType == null) {
                    continue;
                }

                TypeDefinition origTypeResolved = origType.Resolve();

                type = Module.Import(type).Resolve();

                for (int ii = 0; ii < type.Methods.Count; ii++) {
                    MethodDefinition method = type.Methods[ii];
                    Console.WriteLine("M: "+method.FullName);

                    if (method.Attributes.HasFlag(MethodAttributes.SpecialName) || HasIgnoreAttribute(method)) {
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

                    FieldAttributes attribs = field.Attributes;
                    FieldDefinition newField = new FieldDefinition(field.Name, attribs, FindType(field.FieldType));
                    newField.InitialValue = field.InitialValue;
                    origTypeResolved.Fields.Add(newField);
                }

                types[i] = type;
            }

            for (int i = 0; i < types.Length; i++) {
                TypeDefinition type = types[i];
                if (type == null) {
                    continue;
                }
                string typeName = type.FullName;
                Console.WriteLine("TR: "+typeName);

                typeName = RemovePrefixes(typeName, type.Name);

                TypeDefinition origType = Module.GetType(typeName);
                for (int ii = 0; ii < type.Methods.Count; ii++) {
                    MethodDefinition method = type.Methods[ii];

                    if (method.Attributes.HasFlag(MethodAttributes.SpecialName) || HasIgnoreAttribute(method)) {
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

        public void PatchMethod(MethodDefinition method) {
            MethodBody body = method.Body;

            if (method.Name.StartsWith("orig_")) {
                Console.WriteLine(method.Name + " is an orig_ method; ignoring...");
                return;
            }


            Console.WriteLine("Patching "+method.Name+" ...");

            Console.WriteLine("Checking for already existing methods...");

            TypeDefinition origType = Module.GetType(RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType.Name));

            MethodDefinition origMethod = null; //original method that is going to be changed if existing (f.e. X)
            MethodDefinition origMethodOrig = null; //orig_ method (f.e. orig_X)

            for (int i = 0; i < origType.Methods.Count; i++) {
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                    origMethod = origType.Methods[i];
                }
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName.Replace(method.Name, "orig_"+method.Name), method.DeclaringType.Name)) {
                    origMethodOrig = origType.Methods[i];
                }
            }

            if (origMethod != null && origMethodOrig == null) {
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
            } else if (origMethod != null) {
                Console.WriteLine("Prefixed method existing; ignoring...");
            }

            for (int i = 0; i < body.Variables.Count; i++) {
                body.Variables[i].VariableType = FindType(body.Variables[i].VariableType);
            }

            Console.WriteLine("Storing method to main module...");

            if (origMethod != null) {
                origMethod.Body = body;
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

        public void PatchRefsInMethod(MethodDefinition method) {
            if (method.Name.StartsWith("orig_")) {
                Console.WriteLine(method.Name + " is an orig_ method; ignoring...");
                return;
            }

            MethodBody body = method.Body;

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

            for (int i = 0; i < body.Instructions.Count; i++) {
                Instruction instruction = body.Instructions[i];
                Object operand = instruction.Operand;

                if (operand is MethodReference) {
                    MethodReference methodCalled = (MethodReference) operand;
                    if (methodCalled.FullName == RemovePrefixes(method.FullName, method.DeclaringType.Name)) {
                        instruction.Operand = method;
                    } else {
                        MethodReference findMethod = FindMethod(methodCalled, false);

                        if (origMethodOrig != null && methodCalled.FullName == origMethodOrig.FullName) {
                            Console.WriteLine("Found call to the original method; linking...");
                            findMethod = origMethodOrig;
                        }

                        #if GENERIC_METHOD_REFERENCE
                        if (findMethod == null) {
                            try {
                                findMethod = Module.Import(methodCalled);
                            } catch (Exception e) {
                                Console.WriteLine("WARNING: Generic method instance could not be directly imported!");
                                Console.WriteLine(e);
                            }
                        }

                        if (findMethod == null && methodCalled.IsGenericInstance) {
                            Console.WriteLine("WARNING: GENERIC_METHOD_REFERENCE currently being tested extensively in the devbuilds - use with care!");
                            Console.WriteLine(Environment.StackTrace);

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
                        #endif

                        instruction.Operand = findMethod ?? Module.Import(methodCalled);
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
                            FieldAttributes attribs = oldField.Attributes;
                            TypeReference type = FindType(oldField.FieldType);
                            FieldDefinition newField = new FieldDefinition(field.Name, attribs, type);
                            newField.InitialValue = oldField.InitialValue;
                            findType.Fields.Add(newField);
                        }
                    }

                    if (field == operand) {
                        field = new FieldReference(field.Name, FindType(field.FieldType), FindType(field.DeclaringType));
                    }

                    instruction.Operand = field;
                }

                if (operand is TypeReference) {
                    #if GENERIC_TYPE_REFERENCE
                    if (((TypeReference) operand).IsGenericParameter) {
                        Console.WriteLine("WARNING: GENERIC_TYPE_REFERENCE currently being tested extensively in the devbuilds - use with care!");
                        Console.WriteLine(Environment.StackTrace);

                        Console.WriteLine("Generic param wanted: " + ((TypeReference) operand).FullName);
                        Console.WriteLine("Method: " + method.FullName);
                        for (int gi = 0; gi < method.GenericParameters.Count; gi++) {
                            GenericParameter genericParam = method.GenericParameters[gi];
                            Console.WriteLine("Checking against: " + genericParam.FullName);
                            if (genericParam.FullName == ((TypeReference) operand).FullName) {
                                Console.WriteLine("Success!");
                                instruction.Operand = Module.Import(genericParam);
                                break;
                            }
                        }
                    } else
                    #endif
                    instruction.Operand = FindType((TypeReference) operand);
                }

                #if GENERIC_PARAM
                if (instruction.Operand is GenericParameter) {
                    Console.WriteLine("WARNING: GENERIC_PARAM currently being tested extensively in the devbuilds - use with care!");
                    Console.WriteLine(Environment.StackTrace);

                    instruction.Operand = new GenericParameter(((GenericParameter) instruction.Operand).Name, method);
                }
                #endif
            }

            for (int i = 0; i < body.Variables.Count; i++) {
                #if GENERIC_TYPE_VARIABLE
                if (body.Variables[i].VariableType.IsGenericParameter) {
                    Console.WriteLine("WARNING: GENERIC_TYPE_VARIABLE currently being tested extensively in the devbuilds - use with care!");
                    Console.WriteLine(Environment.StackTrace);

                    TypeReference variableType = body.Variables[i].VariableType;

                    Console.WriteLine("Generic param wanted: " + variableType.FullName);
                    Console.WriteLine("Method: " + method.FullName);
                    for (int gi = 0; gi < method.GenericParameters.Count; gi++) {
                        GenericParameter genericParam = method.GenericParameters[gi];
                        Console.WriteLine("Checking against: " + genericParam.FullName);
                        if (genericParam.FullName == variableType.FullName) {
                            Console.WriteLine("Success!");
                            body.Variables[i].VariableType = Module.Import(genericParam);
                            break;
                        }
                    }
                } else
                #endif
                body.Variables[i].VariableType = FindType(body.Variables[i].VariableType);
            }

            #if GENERIC_TYPE_RETURN
            if (method.ReturnType.IsGenericParameter) {
                Console.WriteLine("WARNING: GENERIC_TYPE_RETURN currently being tested extensively in the devbuilds - use with care!");
                Console.WriteLine(Environment.StackTrace);

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
            #endif
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
            #if GENERIC_TYPE_IMPORT
            if (type.IsGenericParameter) {
                Console.WriteLine("WARNING: GENERIC_TYPE_IMPORT currently being tested extensively in the devbuilds - use with care!");
                Console.WriteLine(Environment.StackTrace);
                return foundType ?? (fallbackToImport ? type : null);
            }
            #endif
            return foundType ?? (fallbackToImport ? Module.Import(type) : null);
        }

        public MethodReference FindMethod(MethodReference method, bool fallbackToImport) {
            TypeReference findTypeRef = FindType(method.DeclaringType, false);
            TypeDefinition findType = findTypeRef == null ? null : findTypeRef.Resolve();

            if (method == null && findType != null) {
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
            return str;
        }

        public static string RemovePrefix(string str, string prefix, string strPrefixed = null) {
            strPrefixed = strPrefixed ?? str;
            if (strPrefixed.StartsWith(prefix)) {
                return str.Replace(strPrefixed, strPrefixed.Substring(prefix.Length));
            }
            return str;
        }

        public static bool HasIgnoreAttribute(MethodDefinition method) {
            if (!method.HasCustomAttributes) {
                return false;
            }
            foreach (CustomAttribute attrib in method.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod.MonoModIgnore") {
                    return true;
                }
            }
            return false;
        }

        public static bool HasIgnoreAttribute(TypeDefinition type) {
            if (!type.HasCustomAttributes) {
                return false;
            }
            foreach (CustomAttribute attrib in type.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod.MonoModIgnore") {
                    return true;
                }
            }
            return false;
        }

    }
}
