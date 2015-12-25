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
        
        public List<TypeDefinition> TypesPatched = new List<TypeDefinition>();
        public List<string> TypesAdded = new List<string>();
        
        public static Action<string> DefaultLogger;
        public Action<string> Logger;

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
            
            if (args.Length == 2) {
                Console.WriteLine("Parsing command: " + args[0]);
                if (args[0] == "feed") {
                    Feed.Me(args[1]);
                } else {
                    Console.WriteLine("Unknown command: " + args[0]);
                }
                return;
            }

            if (args.Length != 1) {
                Console.WriteLine("No valid arguments (executable path) passed.");
                return;
            }

            MonoMod mm = new MonoMod(args[0]);

            mm.AutoPatch();
        }

        /// <summary>
        /// Reads the main assembly to mod.
        /// </summary>
        /// <param name="loadDependencies">If set to <c>true</c> load dependencies when not already loaded.</param>
        public virtual void Read(bool loadDependencies = true) {
            if (Module == null) {
                Log("Reading assembly as Mono.Cecil ModuleDefinition and AssemblyDefinition...");
                Module = ModuleDefinition.ReadModule(In.FullName, new ReaderParameters(ReadingMode.Immediate));
                LoadBlacklist(Module);
            }

            if (loadDependencies && Dependencies.Count == 0 && Dir != null) {
                //Seemingly obsolete as all the references are assembly references
                /*Log("Reading module dependencies...");
                for (int mi = 0; mi < Module.ModuleReferences.Count; mi++) {
                    LoadDependency(Module.ModuleReferences[mi]);
                }*/

                Log("Reading assembly dependencies...");
                for (int mi = 0; mi < Module.AssemblyReferences.Count; mi++) {
                    LoadDependency(Module.AssemblyReferences[mi]);
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

            Log("Writing to output file...");
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

                    method.NoInlining = false;
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

            Log("Patching "+In.Name+" ...");
            
            if (read) {
                Read(true);
            }

            Log("Replacing main EntryPoint...");
            Entry = PatchEntry(Module.EntryPoint);
            
            if (Dir != null) {
                string fileName = In.Name.Substring(0, In.Name.LastIndexOf("."));
                Log("Scanning for files matching "+fileName+".*.mm.dll ...");
                List<ModuleDefinition> mods = new List<ModuleDefinition>();
                foreach (FileInfo f in Dir.GetFiles()) {
                    if (f.Name.StartsWith(fileName) && f.Name.ToLower().EndsWith(".mm.dll")) {
                        Log("Found "+f.Name+" , reading...");
                        ModuleDefinition mod = ModuleDefinition.ReadModule(f.FullName, new ReaderParameters(ReadingMode.Immediate));
                        PrePatchModule(mod);
                        mods.Add(mod);
                    }
                }
                foreach (ModuleDefinition mod in mods) {
                    PatchModule(mod);
                }
                Log("Patching / fixing references...");
                PatchRefs();
            }

            Optimize();

            Log("Done.");
            
            if (write) {
                Write();
            }
        }
        
        public virtual void PrePatchModule(ModuleDefinition mod) {
            Module.AssemblyReferences.Add(mod.Assembly.Name);
            
            for (int i = 0; i < mod.Types.Count; i++) {
                PrePatchType(mod.Types[i]);
            }
            
            for (int i = 0; i < mod.Resources.Count; i++) {
                if (!(mod.Resources[i] is EmbeddedResource)) {
                    continue;
                }
                EmbeddedResource resOrig = ((EmbeddedResource) mod.Resources[i]);
                string name = resOrig.Name;
                if (name.StartsWith(mod.Assembly.Name.Name)) {
                    name = Module.Assembly.Name.Name + name.Substring(mod.Assembly.Name.Name.Length);
                }
                EmbeddedResource res = new EmbeddedResource(name, resOrig.Attributes, resOrig.GetResourceData());
                Module.Resources.Add(res);
            }
        }
        
        public virtual TypeReference PrePatchType(TypeDefinition type) {
            string typeName = RemovePrefixes(type.FullName, type);
            
            if (TypesPatched.Contains(type)) {
                PrePatchNested(type);
                return null;
            }

            /*if (type.Attributes.HasFlag(TypeAttributes.NotPublic) &&
                type.Attributes.HasFlag(TypeAttributes.Interface)) {
                Log("Type is a private interface; ignore...");
                PrePatchNested(type);
                return null;
            }*/

            if (HasAttribute(type, "MonoModIgnore")) {
                PrePatchNested(type);
                return null;
            }

            //check if type exists at all
            TypeReference origType = Module.GetType(typeName, true);
            if (origType == null) {
                if (type.Name.StartsWith("patch_")) {
                    PrePatchNested(type);
                    return null;
                }
            }

            //check if type exists in module to patch
            origType = Module.GetType(typeName, false);
            bool isTypeAdded = origType == null;
            if (!isTypeAdded) {
                return origType;
            }
            
            //(un?)fortunately we're forced to add types ever since some workarounds stopped working
            Log("T+: " + typeName);
            
            TypeDefinition newType = new TypeDefinition(type.Namespace, type.Name, type.Attributes, null);
            newType.ClassSize = type.ClassSize;
            //TODO yell about custom attribute support in Mono.Cecil
            //newType.CustomAttributes = type.CustomAttributes;
            if (type.DeclaringType != null) {
                newType.DeclaringType = (FindType(type.DeclaringType, newType, false) ?? PrePatchType(type.DeclaringType)).Resolve();
                newType.DeclaringType.NestedTypes.Add(newType);
            } else {
                Module.Types.Add(newType);
            }
            TypesAdded.Add(typeName);
            newType.MetadataToken = type.MetadataToken;
            for (int i = 0; i < type.GenericParameters.Count; i++) {
                newType.GenericParameters.Add(new GenericParameter(type.GenericParameters[i].Name, newType) {
                    Attributes = type.GenericParameters[i].Attributes,
                    MetadataToken = type.GenericParameters[i].MetadataToken
                });
            }
            newType.PackingSize = type.PackingSize;
            //Methods and Fields gets filled automatically
            
            PrePatchNested(type);
            return newType;
        }
        
        protected virtual void PrePatchNested(TypeDefinition type) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PrePatchType(type.NestedTypes[i]);
            }
        }

        /// <summary>
        /// Patches the module and adds the patched types to the given list.
        /// </summary>
        /// <param name="mod">Mod to patch into the input module.</param>
        /// <param name="types">Type list containing all patched types.</param>
        public virtual void PatchModule(ModuleDefinition mod) {
            for (int i = 0; i < mod.Types.Count; i++) {
                PatchType(mod.Types[i]);
            }
        }

        /// <summary>
        /// Patches the type and adds it to TypesPatched if it's actually patched.
        /// </summary>
        /// <returns>The original type that has been patched.</returns>
        /// <param name="type">Type to patch into the input module.</param>
        public virtual TypeReference PatchType(TypeDefinition type) {
            string typeName = type.FullName;
            Log("T: " + typeName);
            
            if (TypesPatched.Contains(type)) {
                PatchNested(type);
                return null;
            }

            typeName = RemovePrefixes(typeName, type);

            /*if (type.Attributes.HasFlag(TypeAttributes.NotPublic) &&
                type.Attributes.HasFlag(TypeAttributes.Interface)) {
                Log("Type is a private interface; ignore...");
                PatchNested(type);
                return null;
            }*/

            if (HasAttribute(type, "MonoModIgnore")) {
                PatchNested(type);
                return null;
            }

            //check if type exists at all
            TypeReference origType = Module.GetType(typeName, true);
            if (origType == null) {
                if (type.Name.StartsWith("patch_")) {
                    PatchNested(type);
                    return null;
                }
            }

            //check if type exists in module to patch
            origType = Module.GetType(typeName, false);
            bool isTypeAdded = TypesAdded.Contains(typeName);
            
            TypeDefinition origTypeResolved = origType.Resolve();

            IsBlacklisted(origTypeResolved.Module.Name, origTypeResolved.FullName, HasAttribute(origTypeResolved, "MonoModBlacklisted"));

            if (type.Name.StartsWith("remove_") || HasAttribute(type, "MonoModRemove")) {
                Module.Types.Remove(origTypeResolved);
                return null;
            }

            //type = Module.Import(type).Resolve();
            
            for (int ii = 0; ii < type.Properties.Count; ii++) {
                PropertyDefinition property = type.Properties[ii];
                Log("P: "+property.FullName);
                
                if (isTypeAdded) {
                    PropertyDefinition newProperty = new PropertyDefinition(property.Name, property.Attributes, FindType(property.PropertyType, origType, false));
                    
                    for (int cai = 0; cai < property.CustomAttributes.Count; cai++) {
                        CustomAttribute oca = property.CustomAttributes[cai];
                        CustomAttribute ca = new CustomAttribute(FindMethod(oca.Constructor, newProperty, false), oca.GetBlob());
                        for (int caii = 0; caii < oca.ConstructorArguments.Count; caii++) {
                            //TODO do more with the attributes
                            CustomAttributeArgument ocaa = oca.ConstructorArguments[caii];
                            ca.ConstructorArguments.Add(new CustomAttributeArgument(FindType(ocaa.Type, newProperty, false),
                                ocaa.Value is TypeReference ? FindType((TypeReference) ocaa.Type, newProperty, false) :
                                ocaa.Value
                            ));
                        }
                        newProperty.CustomAttributes.Add(ca);
                    }
                    
                    newProperty.DeclaringType = origTypeResolved;
                    origTypeResolved.Properties.Add(newProperty);
                }
                
                MethodDefinition getter = property.GetMethod;
                if (getter != null && !HasAttribute(getter, "MonoModIgnore")) {
                    //getter = Module.Import(getter).Resolve();
                    PatchMethod(getter);
                }
                
                MethodDefinition setter = property.SetMethod;
                if (setter != null && !HasAttribute(setter, "MonoModIgnore")) {
                    //setter = Module.Import(setter).Resolve();
                    PatchMethod(setter);
                }
            }
            
            for (int ii = 0; ii < type.Methods.Count; ii++) {
                MethodDefinition method = type.Methods[ii];
                Log("M: "+method.FullName);
                
                if (!AllowedSpecialName(method) || HasAttribute(method, "MonoModIgnore")) {
                    continue;
                }

                //method = Module.Import(method).Resolve();
                PatchMethod(method);
            }
            
            if (HasAttribute(type, "MonoModEnumReplace")) {
                for (int ii = 0; ii < origTypeResolved.Fields.Count;) {
                    if (origTypeResolved.Fields[ii].Name == "value__") {
                        ii++;
                        continue;
                    }
                    
                    origTypeResolved.Fields.RemoveAt(ii);
                }
            }

            for (int ii = 0; ii < type.Fields.Count; ii++) {
                FieldDefinition field = type.Fields[ii];
                /*if (field.Attributes.HasFlag(FieldAttributes.SpecialName)) {
                    continue;
                }*/
                
                if (HasAttribute(field, "MonoModIgnore")) {
                    continue;
                }

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
                Log("F: "+field.FullName);

                FieldDefinition newField = new FieldDefinition(field.Name, field.Attributes, FindType(field.FieldType, type));
                newField.InitialValue = field.InitialValue;
                newField.Constant = field.Constant;
                for (int cai = 0; cai < field.CustomAttributes.Count; cai++) {
                    CustomAttribute oca = field.CustomAttributes[cai];
                    CustomAttribute ca = new CustomAttribute(FindMethod(oca.Constructor, newField, false), oca.GetBlob());
                    for (int caii = 0; caii < oca.ConstructorArguments.Count; caii++) {
                        //TODO do more with the attributes
                        CustomAttributeArgument ocaa = oca.ConstructorArguments[caii];
                        ca.ConstructorArguments.Add(new CustomAttributeArgument(FindType(ocaa.Type, newField, false),
                            ocaa.Value is TypeReference ? FindType((TypeReference) ocaa.Type, newField, false) :
                            ocaa.Value
                        ));
                    }
                    newField.CustomAttributes.Add(ca);
                }
                origTypeResolved.Fields.Add(newField);
            }

            TypesPatched.Add(type);
            PatchNested(type);
            return origType;
        }
        
        protected virtual void PatchNested(TypeDefinition type) {
            for (int i = 0; i < type.NestedTypes.Count; i++) {
                PatchType(type.NestedTypes[i]);
            }
        }

        /// <summary>
        /// Patches the given method into the input module.
        /// </summary>
        /// <param name="method">Method to patch in.</param>
        public virtual MethodDefinition PatchMethod(MethodDefinition method) {
            if (method.Name.StartsWith("orig_")) {
                Log(method.Name + " is an orig_ method; ignoring...");
                return null;
            }
            
            Log("Patching "+method.Name+" ...");

            Log("Checking for already existing methods...");

            TypeDefinition origType = Module.GetType(RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType));
            bool isTypeAdded = TypesAdded.Contains(origType.FullName);

            MethodDefinition origMethod = null; //original method that is going to be changed if existing (f.e. X)
            MethodDefinition origMethodOrig = null; //orig_ method (f.e. orig_X)

            //TODO the orig methods of replace_ methods can't be found
            for (int i = 0; i < origType.Methods.Count; i++) {
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName, method.DeclaringType)) {
                    origMethod = origType.Methods[i];
                }
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName.Replace(method.Name, "orig_"+method.Name), method.DeclaringType)) {
                    origMethodOrig = origType.Methods[i];
                }
            }

            if (origMethod != null && origMethodOrig == null && !isTypeAdded) {
                IsBlacklisted(origMethod.Module.Name, origMethod.DeclaringType.FullName+"."+origMethod.Name, HasAttribute(origMethod, "MonoModBlacklisted"));
                if (method.Name.StartsWith("replace_") || HasAttribute(method, "MonoModReplace")) {
                    Log("Method existing; replacing...");
                } else {
                    Log("Method existing; creating copy...");

                    MethodDefinition copy = new MethodDefinition("orig_"+origMethod.Name, origMethod.Attributes & ~MethodAttributes.SpecialName & ~MethodAttributes.RTSpecialName, origMethod.ReturnType);
                    copy.DeclaringType = origMethod.DeclaringType;
                    copy.MetadataToken = origMethod.MetadataToken;
                    copy.Body = origMethod.Body;
                    
                    for (int i = 0; i < origMethod.GenericParameters.Count; i++) {
                        copy.GenericParameters.Add(new GenericParameter(origMethod.GenericParameters[i].Name, copy) {
                            Attributes = origMethod.GenericParameters[i].Attributes,
                            MetadataToken = origMethod.GenericParameters[i].MetadataToken
                        });
                    }

                    for (int i = 0; i < origMethod.Parameters.Count; i++) {
                        copy.Parameters.Add(origMethod.Parameters[i]);
                    }
                    
                    for (int cai = 0; cai < origMethod.CustomAttributes.Count; cai++) {
                        CustomAttribute oca = origMethod.CustomAttributes[cai];
                        CustomAttribute ca = new CustomAttribute(FindMethod(oca.Constructor, copy, false), oca.GetBlob());
                        for (int caii = 0; caii < oca.ConstructorArguments.Count; caii++) {
                            //TODO do more with the attributes
                            CustomAttributeArgument ocaa = oca.ConstructorArguments[caii];
                            ca.ConstructorArguments.Add(new CustomAttributeArgument(FindType(ocaa.Type, copy, false),
                                ocaa.Value is TypeReference ? FindType((TypeReference) ocaa.Type, copy, false) :
                                ocaa.Value
                            ));
                        }
                        copy.CustomAttributes.Add(ca);
                    }

                    origType.Methods.Add(copy);
                    origMethodOrig = copy;
                    Log("Added copy of original method to "+copy.FullName);
                }
            } else if (origMethod != null) {
                Log("Prefixed method existing; ignoring...");
            }

            //fix for .cctor not linking to orig_.cctor
            if (origMethodOrig != null && origMethod.IsConstructor && origMethod.IsStatic && !isTypeAdded) {
                Collection<Instruction> instructions = method.Body.Instructions;
                ILProcessor ilProcessor = method.Body.GetILProcessor();
                ilProcessor.InsertBefore(instructions[instructions.Count - 1], ilProcessor.Create(OpCodes.Call, origMethodOrig));
            }

            for (int i = 0; method.HasBody && i < method.Body.Variables.Count; i++) {
                //TODO debug! (Import crashes)
                method.Body.Variables[i].VariableType = FindType(method.Body.Variables[i].VariableType, method);
            }

            Log("Storing method to main module...");

            if (origMethod != null) {
                origMethod.Body = method.Body;
                method = origMethod;
            } else {
                MethodDefinition clone = new MethodDefinition(method.Name, (origMethodOrig ?? method).Attributes, Module.Import(typeof(void)));
                origType.Methods.Add(clone);
                clone.MetadataToken = (origMethodOrig ?? method).MetadataToken;
                clone.CallingConvention = (origMethodOrig ?? method).CallingConvention;
                clone.ExplicitThis = (origMethodOrig ?? method).ExplicitThis;
                clone.MethodReturnType = (origMethodOrig ?? method).MethodReturnType;
                clone.NoInlining = (origMethodOrig ?? method).NoInlining;
                clone.NoOptimization = (origMethodOrig ?? method).NoOptimization;
                clone.Attributes = (origMethodOrig ?? method).Attributes;
                clone.ImplAttributes = (origMethodOrig ?? method).ImplAttributes;
                clone.SemanticsAttributes = (origMethodOrig ?? method).SemanticsAttributes;
                clone.DeclaringType = origType;
                for (int i = 0; i < (origMethodOrig ?? method).GenericParameters.Count; i++) {
                    clone.GenericParameters.Add(new GenericParameter((origMethodOrig ?? method).GenericParameters[i].Name, clone));
                }
                for (int i = 0; i < (origMethodOrig ?? method).Parameters.Count; i++) {
                    clone.Parameters.Add(new ParameterDefinition(FindType((origMethodOrig ?? method).Parameters[i].ParameterType, clone)));
                }
                for (int i = 0; i < (origMethodOrig ?? method).Overrides.Count; i++) {
                    clone.Overrides.Add((origMethodOrig ?? method).Overrides[i]);
                }
                for (int cai = 0; cai < method.CustomAttributes.Count; cai++) {
                    CustomAttribute oca = method.CustomAttributes[cai];
                    CustomAttribute ca = new CustomAttribute(FindMethod(oca.Constructor, clone, false), oca.GetBlob());
                    for (int caii = 0; caii < oca.ConstructorArguments.Count; caii++) {
                        //TODO do more with the attributes
                        CustomAttributeArgument ocaa = oca.ConstructorArguments[caii];
                        ca.ConstructorArguments.Add(new CustomAttributeArgument(FindType(ocaa.Type, clone, false),
                            ocaa.Value is TypeReference ? FindType((TypeReference) ocaa.Type, clone, false) :
                            ocaa.Value
                        ));
                    }
                    clone.CustomAttributes.Add(ca);
                }
                clone.ReturnType = FindType((origMethodOrig ?? method).ReturnType, clone);
                clone.Body = method.Body;
                method = clone;
            }

            if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) {
                Log("Finding property...");

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
                    Log("Creating new property...");
                    property = new PropertyDefinition(method.Name.Substring(4), PropertyAttributes.None, method.ReturnType);
                    
                    PropertyDefinition origProperty = null;
                    for (int i = 0; i < origType.Properties.Count; i++) {
                        if (origType.Properties[i].Name == property.Name) {
                            origProperty = origType.Properties[i];
                            break;
                        }
                    }
                    
                    for (int cai = 0; origProperty != null && cai < origProperty.CustomAttributes.Count; cai++) {
                        CustomAttribute oca = origProperty.CustomAttributes[cai];
                        CustomAttribute ca = new CustomAttribute(FindMethod(oca.Constructor, property, false), oca.GetBlob());
                        for (int caii = 0; caii < oca.ConstructorArguments.Count; caii++) {
                            //TODO do more with the attributes
                            CustomAttributeArgument ocaa = oca.ConstructorArguments[caii];
                            ca.ConstructorArguments.Add(new CustomAttributeArgument(FindType(ocaa.Type, property, false),
                                ocaa.Value is TypeReference ? FindType((TypeReference) ocaa.Type, property, false) :
                                ocaa.Value
                            ));
                        }
                        property.CustomAttributes.Add(ca);
                    }
                    
                    origType.Properties.Add(property);
                }

                if (method.Name.StartsWith("get_")) {
                    Log("Replacing getter...");
                    property.GetMethod = method;
                } else {
                    Log("Replacing setter...");
                    property.SetMethod = method;
                }
            }
            
            return method;
        }

        /// <summary>
        /// Patches the references in all of the types in TypesPatched.
        /// </summary>
        public virtual void PatchRefs() {
            foreach (TypeDefinition type in TypesPatched) {
                if (type == null) {
                    continue;
                }
                string typeName = type.FullName;
                Log("TR: "+typeName);

                typeName = RemovePrefixes(typeName, type);
                bool isTypeAdded = TypesAdded.Contains(typeName);

                TypeDefinition origType = Module.GetType(typeName);
                IsBlacklisted(origType.Module.Name, origType.FullName, HasAttribute(origType, "MonoModBlacklisted"));
                if (isTypeAdded) {
                    for (int i = 0; i < type.Interfaces.Count; i++) {
                        origType.Interfaces.Add(FindType(type.Interfaces[i], origType, true));
                    }
                    for (int cai = 0; cai < type.CustomAttributes.Count; cai++) {
                        CustomAttribute oca = type.CustomAttributes[cai];
                        CustomAttribute ca = new CustomAttribute(FindMethod(oca.Constructor, origType, true), oca.GetBlob());
                        for (int caii = 0; caii < oca.ConstructorArguments.Count; caii++) {
                            //TODO do more with the attributes
                            CustomAttributeArgument ocaa = oca.ConstructorArguments[caii];
                            ca.ConstructorArguments.Add(new CustomAttributeArgument(FindType(ocaa.Type, origType, true),
                                ocaa.Value is TypeReference ? FindType((TypeReference) ocaa.Type, origType, true) :
                                ocaa.Value
                            ));
                        }
                        origType.CustomAttributes.Add(ca);
                    }
                    origType.BaseType = type.BaseType == null ? null : FindType(type.BaseType, origType, true);
                }
                for (int ii = 0; ii < type.Methods.Count; ii++) {
                    MethodDefinition method = type.Methods[ii];

                    if (!AllowedSpecialName(method) || HasAttribute(method, "MonoModIgnore")) {
                        continue;
                    }

                    for (int iii = 0; iii < origType.Methods.Count; iii++) {
                        MethodDefinition origMethod = origType.Methods[iii];
                        if (origMethod.FullName == RemovePrefixes(method.FullName, method.DeclaringType)) {
                            method = origMethod;
                            Log("MR: "+method.FullName);
                            PatchRefsInMethod(method);
                            break;
                        }
                    }
                }
                for (int ii = 0; ii < type.Properties.Count; ii++) {
                    PropertyDefinition property = type.Properties[ii];
                    
                    for (int iii = 0; iii < origType.Properties.Count; iii++) {
                        PropertyDefinition origProperty = origType.Properties[iii];
                        if (origProperty.FullName == RemovePrefixes(property.FullName, property.DeclaringType)) {
                            property = origProperty;
                            Log("PR: "+property.FullName);
                            MethodDefinition getter = property.GetMethod;
                            if (getter != null && !HasAttribute(getter, "MonoModIgnore")) {
                                //getter = Module.Import(getter).Resolve();
                                PatchRefsInMethod(getter);
                            }
                            
                            MethodDefinition setter = property.SetMethod;
                            if (setter != null && !HasAttribute(setter, "MonoModIgnore")) {
                                //setter = Module.Import(setter).Resolve();
                                PatchRefsInMethod(setter);
                            }
                            break;
                        }
                    }
                }
                if (isTypeAdded) {
                    for (int ii = 0; ii < type.Fields.Count; ii++) {
                        FieldDefinition field = type.Fields[ii];
                        
                        if (HasAttribute(field, "MonoModIgnore")) {
                            continue;
                        }
        
                        FieldDefinition origField = null;
                        for (int iii = 0; iii < origType.Fields.Count; iii++) {
                            if (origType.Fields[iii].Name == field.Name) {
                                origField = origType.Fields[iii];
                                break;
                            }
                        }
                        if (origField == null) {
                            //The field should've been added...
                            continue;
                        }
                        Log("FR: "+origField.FullName);
                        
                        origField.FieldType = FindType(origField.FieldType, type);
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
                Log(method.Name + " is an orig_ method; ignoring...");
                return;
            }

            Log("Patching references in "+method.Name+" ...");

            Log("Checking for original methods...");

            TypeDefinition origType = Module.GetType(RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType));
            bool isTypeAdded = TypesAdded.Contains(origType.FullName);

            MethodDefinition origMethodOrig = null; //orig_ method (f.e. orig_X)

            for (int i = 0; i < origType.Methods.Count; i++) {
                if (origType.Methods[i].FullName == RemovePrefixes(method.FullName.Replace(method.Name, "orig_"+method.Name), method.DeclaringType)) {
                    origMethodOrig = origType.Methods[i];
                }
            }

            if (origMethodOrig != null) {
                IsBlacklisted(origMethodOrig.Module.Name, method.DeclaringType+"."+RemovePrefixes(method.Name), HasAttribute(origMethodOrig, "MonoModBlacklisted"));
                Log("Prefixed method existing; ignoring...");
            }

            Log("Modifying method body...");
            for (int i = 0; method.HasBody && i < method.Body.Instructions.Count; i++) {
                Instruction instruction = method.Body.Instructions[i];
                object operand = instruction.Operand;
                
                if (operand is MethodReference) {
                    MethodReference methodCalled = FindLinked((MethodReference) operand);
                    if (methodCalled.FullName == RemovePrefixes(method.FullName, method.DeclaringType)) {
                        operand = method;
                    } else {
                        MethodReference findMethod = FindMethod(methodCalled, method, false);

                        if (origMethodOrig != null && methodCalled.FullName == origMethodOrig.FullName) {
                            Log("Found call to the original method; linking...");
                            findMethod = origMethodOrig;
                        }
                        
                        if (findMethod == null && methodCalled.IsGenericInstance) {
                            GenericInstanceMethod genericMethodCalled = ((GenericInstanceMethod) methodCalled);
                            Log("Calling method: " + genericMethodCalled.FullName);
                            Log("Element method: " + genericMethodCalled.ElementMethod.FullName);
                            GenericInstanceMethod genericMethod = new GenericInstanceMethod(FindMethod(genericMethodCalled.ElementMethod, method, true));

                            for (int gi = 0; gi < genericMethodCalled.GenericArguments.Count; gi++) {
                                Log("Generic argument: " + genericMethodCalled.GenericArguments[gi]);
                                //genericMethod.GenericArguments.Add(genericMethodCalled.GenericArguments[gi]);
                                bool found = false;
                                for (int gii = 0; gii < method.GenericParameters.Count && !found; gii++) {
                                    GenericParameter genericParam = method.GenericParameters[gii];
                                    Log("Checking against: " + genericParam.FullName);
                                    if (genericParam.FullName == genericMethodCalled.GenericArguments[gi].FullName) {
                                        Log("Success!");
                                        genericMethod.GenericArguments.Add(genericParam);
                                        found = true;
                                    }
                                }
                                for (int gii = 0; gii < method.DeclaringType.GenericParameters.Count && !found; gii++) {
                                    GenericParameter genericParam = method.DeclaringType.GenericParameters[gii];
                                    Log("Checking against: " + genericParam.FullName);
                                    if (genericParam.FullName == genericMethodCalled.GenericArguments[gi].FullName) {
                                        Log("Success!");
                                        genericMethod.GenericArguments.Add(genericParam);
                                        found = true;
                                    }
                                }
                                if (!found) {
                                    genericMethod.GenericArguments.Add(FindType(genericMethodCalled.GenericArguments[gi], method, true));
                                }
                            }

                            findMethod = genericMethod;
                        }

                        if (findMethod == null) {
                            try {
                                findMethod = Module.Import(methodCalled);
                            } catch {
                                //uh. generic type failed importing?
                            }
                        }

                        MethodDefinition findMethodDef = findMethod == null ? null : findMethod.Resolve();
                        if (findMethodDef != null) {
                            IsBlacklisted(findMethod.Module.Name, findMethod.DeclaringType.FullName+"."+findMethod.Name, HasAttribute(findMethodDef, "MonoModBlacklisted"));
                            //Everything the mod touches is our kingdom
                            findMethodDef.IsPrivate = false;
                            findMethodDef.IsPublic = true;
                            findMethodDef.DeclaringType.IsNotPublic = false;
                            findMethodDef.DeclaringType.IsPublic = true;
                            if (!isTypeAdded) {
                                //Quite untested - fixes invalid IL when calling virtual methods when not virtual in patch
                                if (findMethodDef.Attributes.HasFlag(MethodAttributes.Virtual)) {
                                    instruction.OpCode = OpCodes.Callvirt;
                                }
                                //Fixes linkto base methods being called as callvirt
                                //FIXME find out other cases where this should be set due to linkto
                                //FIXME test something better than name...
                                if (method.DeclaringType.BaseType != null && findMethodDef.DeclaringType.Name == method.DeclaringType.BaseType.Name) {
                                    instruction.OpCode = OpCodes.Call;
                                }
                            }
                        }
                        
                        operand = findMethod ?? Module.Import(methodCalled);
                    }
                }

                if (operand is FieldReference) {
                    FieldReference field = FindLinked((FieldReference) operand);

                    TypeReference findTypeRef = FindType(field.DeclaringType, method, false);
                    TypeDefinition findType = findTypeRef == null ? null : findTypeRef.Resolve();
                    
                    if (findType != null) {
                        for (int ii = 0; ii < findType.Fields.Count; ii++) {
                            if (findType.Fields[ii].Name == field.Name) {
                                field = findType.Fields[ii];
                                
                                if (field.DeclaringType.IsGenericInstance) {
                                    field = Module.Import(new FieldReference(field.Name, FindType(field.FieldType, findTypeRef), findTypeRef));
                                }
                                
                                if (field.Module != Module) {
                                    field = Module.Import(field);
                                }
                                
                                break;
                            }
                        }
                    }
                    
                    if (field == operand) {
                        field = new FieldReference(field.Name, FindType(field.FieldType, method), FindType(field.DeclaringType, method));
                    }

                    if (field != null) {
                        IsBlacklisted(field.Module.Name, field.DeclaringType.FullName+"."+field.Name);
                        //Everything the mod touches is our kingdom
                        if (field.IsDefinition) {
                            ((FieldDefinition) field).IsPrivate = false;
                            ((FieldDefinition) field).IsPublic = true;
                            ((FieldDefinition) field).DeclaringType.IsNotPublic = false;
                            ((FieldDefinition) field).DeclaringType.IsPublic = true;
                        }
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

                Log("Generic param wanted: " + returnType.FullName);
                Log("Method: " + method.FullName);
                for (int gi = 0; gi < method.GenericParameters.Count; gi++) {
                    GenericParameter genericParam = method.GenericParameters[gi];
                    Log("Checking against: " + genericParam.FullName);
                    if (genericParam.FullName == returnType.FullName) {
                        Log("Success!");
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
            
            for (int i = 0; i < method.Overrides.Count; i++) {
                method.Overrides[i] = FindMethod(method.Overrides[i], method, true);
            }
        }

        /// <summary>
        /// Finds a type in the input module based on a type in any other module.
        /// </summary>
        /// <returns>The found type or either null or the imported type.</returns>
        /// <param name="type">Type to find.</param>
        /// <param name="context">Context containing some info.</param>
        /// <param name="fallbackToImport">If set to <c>true</c> this method returns the type to find as imported in the input module.</param>
        public virtual TypeReference FindType(TypeReference type, MemberReference context = null, bool fallbackToImport = true, bool loadedDependency = false) {
            if (type == null) {
                Log("ERROR: Can't find null type!");
                Log(Environment.StackTrace);
                return null;
            }
            string typeName = RemovePrefixes(type.FullName, type);
            TypeReference foundType = Module.GetType(typeName);
            if (foundType == null && type.IsByReference) {
                foundType = new ByReferenceType(FindType(((ByReferenceType) type).ElementType, context, fallbackToImport));
            }
            if (foundType == null && type.IsArray) {
                foundType = new ArrayType(FindType(((ArrayType) type).ElementType, context, fallbackToImport), ((ArrayType) type).Dimensions.Count);
            }
            if (foundType == null && context != null && type.IsGenericParameter) {
                foundType = FindTypeGeneric(type, context, fallbackToImport);
            }
            if (foundType == null && context != null && type.IsGenericInstance) {
                foundType = new GenericInstanceType(FindType(((GenericInstanceType) type).ElementType, context, fallbackToImport));
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
            if (foundType == null && type.IsDefinition && type.Scope.Name.EndsWith(".mm")) {
                foundType = PatchType((TypeDefinition) type);
            }
            if (foundType == null) {
                if (!loadedDependency) {
                    LoadDependency(type.Scope.Name);
                    return FindType(type, context, fallbackToImport, true);
                } else {
                    Log("Type not found : " + type.FullName);
                    Log("Type scope     : " + type.Scope.Name);
                }
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
                MethodReference r = ((MethodReference) context).GetElementMethod();
                for (int gi = 0; gi < r.GenericParameters.Count; gi++) {
                    GenericParameter genericParam = r.GenericParameters[gi];
                    if (genericParam.FullName == type.FullName) {
                        //TODO variables hate me, import otherwise
                        return genericParam;
                    }
                }
                if (type.Name.StartsWith("!!")) {
                    return r.GenericParameters[int.Parse(type.Name.Substring(2))];
                }
            }
            if (context is TypeReference) {
                TypeReference r = ((TypeReference) context).GetElementType();
                for (int gi = 0; gi < r.GenericParameters.Count; gi++) {
                    GenericParameter genericParam = r.GenericParameters[gi];
                    if (genericParam.FullName == type.FullName) {
                        //TODO variables hate me, import otherwise
                        return genericParam;
                    }
                }
                if (type.Name.StartsWith("!")) {
                    return r.GenericParameters[int.Parse(type.Name.Substring(1))];
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
                bool typeMismatch = findType.FullName != RemovePrefixes(method.DeclaringType.FullName, method.DeclaringType);
                
                string methodName = RemovePrefixes(method.FullName, method.DeclaringType);
                methodName = methodName.Substring(methodName.IndexOf(" ") + 1);
                methodName = MakeMethodNameFindFriendly(methodName, method, findType);
                
                for (int ii = 0; ii < findType.Methods.Count; ii++) {
                    MethodReference foundMethod = findType.Methods[ii];
                    string foundMethodName = foundMethod.FullName;
                    foundMethodName = foundMethodName.Replace(findType.FullName, findTypeRef.FullName);
                    foundMethodName = foundMethodName.Substring(foundMethodName.IndexOf(" ") + 1);
                    //TODO find a better way to compare methods / fix comparing return types
                    foundMethodName = MakeMethodNameFindFriendly(foundMethodName, foundMethod, findType);
                    
                    if (methodName == foundMethodName) {
                        IsBlacklisted(foundMethod.Module.Name, foundMethod.DeclaringType.FullName+"."+foundMethod.Name, HasAttribute(foundMethod.Resolve(), "MonoModBlacklisted"));
                        
                        if (typeMismatch && method.DeclaringType.IsGenericInstance) {
                            //TODO test return type context
                            MethodReference genMethod = new MethodReference(method.Name, FindType(method.ReturnType, findTypeRef), findTypeRef);
                            genMethod.CallingConvention = method.CallingConvention;
                            genMethod.HasThis = method.HasThis;
                            genMethod.ExplicitThis = method.ExplicitThis;
                            for (int i = 0; i < method.GenericParameters.Count; i++) {
                                genMethod.GenericParameters.Add((GenericParameter) (FindType(method.GenericParameters[i], genMethod, false) ?? FindType(method.GenericParameters[i], findTypeRef)));
                            }
                            for (int i = 0; i < method.Parameters.Count; i++) {
                                genMethod.Parameters.Add(new ParameterDefinition(FindType(method.Parameters[i].ParameterType, genMethod)));
                            }
                            
                            foundMethod = Module.Import(genMethod);
                        }
                        
                        if (foundMethod.Module != Module) {
                            foundMethod = Module.Import(foundMethod);
                        }
                        
                        if (method.IsGenericInstance) {
                            GenericInstanceMethod genMethod = new GenericInstanceMethod(foundMethod);
                            GenericInstanceMethod methodg = ((GenericInstanceMethod) method);
                            
                            for (int i = 0; i < methodg.GenericArguments.Count; i++) {
                                genMethod.GenericArguments.Add(FindType(methodg.GenericArguments[i], genMethod));
                            }
                            
                            foundMethod = genMethod;
                        }
                        
                        return foundMethod;
                    }
                }
            }
            
            if (fallbackToImport) {
                return Module.Import(method);
            }
            
            if (!method.DeclaringType.IsArray) {
                Log("Method not found     : " + method.FullName);
                Log("Method type scope    : " + method.DeclaringType.Scope.Name);
                Log("Found type reference : " + findTypeRef);
                Log("Found type definition: " + findType);
                if (findTypeRef != null) {
                    Log("Found type scope     : " + findTypeRef.Scope.Name);
                }
                
                if (findType != null) {
                    string methodName = method.FullName;
                    methodName = methodName.Substring(methodName.IndexOf(" ") + 1);
                    methodName = MakeMethodNameFindFriendly(methodName, method, findType);
                    Log("debug m -1 / " + (findType.Methods.Count - 1) + ": " + methodName);
                    for (int ii = 0; ii < findType.Methods.Count; ii++) {
                        MethodReference foundMethod = findType.Methods[ii];
                        string foundMethodName = foundMethod.FullName;
                        foundMethodName = foundMethodName.Replace(findType.FullName, findTypeRef.FullName);
                        foundMethodName = foundMethodName.Substring(foundMethodName.IndexOf(" ") + 1);
                        //TODO find a better way to compare methods / fix comparing return types
                        foundMethodName = MakeMethodNameFindFriendly(foundMethodName, foundMethod, findType);
                        Log("debug m "+ii+" / " + (findType.Methods.Count - 1) + ": " + foundMethodName);
                    }
                }
            }
            
            if (findTypeRef == null) {
                return method;
            }
            
            MethodReference fbgenMethod = new MethodReference(method.Name, FindType(method.ReturnType, findTypeRef), findTypeRef);
            fbgenMethod.CallingConvention = method.CallingConvention;
            fbgenMethod.HasThis = method.HasThis;
            fbgenMethod.ExplicitThis = method.ExplicitThis;
            for (int i = 0; i < method.GenericParameters.Count; i++) {
                fbgenMethod.GenericParameters.Add((GenericParameter) FindType(method.GenericParameters[i], fbgenMethod));
            }
            for (int i = 0; i < method.Parameters.Count; i++) {
                fbgenMethod.Parameters.Add(new ParameterDefinition(FindType(method.Parameters[i].ParameterType, fbgenMethod)));
            }
            
            if (method.IsGenericInstance) {
                GenericInstanceMethod genMethod = new GenericInstanceMethod(fbgenMethod);
                GenericInstanceMethod methodg = ((GenericInstanceMethod) method);
                
                for (int i = 0; i < methodg.GenericArguments.Count; i++) {
                    genMethod.GenericArguments.Add(FindType(methodg.GenericArguments[i], genMethod));
                }
                
                fbgenMethod = genMethod;
            }
            
            return fbgenMethod;
        }
        
        /// <summary>
        /// Loads a dependency and adds it to Dependencies. Requires the field Dir to be set.
        /// </summary>
        /// <param name="dependency">Dependency to load.</param>
        public virtual void LoadDependency(AssemblyNameReference dependency) {
            LoadDependency(dependency.Name, dependency.ToString());
        }
        
        /// <summary>
        /// Loads a dependency and adds it to Dependencies. Requires the field Dir to be set.
        /// </summary>
        /// <param name="name">Dependency name (f.e. "mscorlib").</param>
        /// <param name="fullName">Full dependency name (f.e. "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089").</param>
        public virtual void LoadDependency(string name, string fullName = null) {
            DirectoryInfo dir = Dir;
            if (dir == null) {
                dir = In.Directory;
            }
            string path = Path.Combine(Dir.FullName, name + ".dll");
            if (!File.Exists(path)) {
                path = Path.Combine(Dir.FullName, name + ".exe");
            }
            if (!File.Exists(path)) {
                path = Path.Combine(Dir.FullName, name);
            }
            if (!File.Exists(path) && fullName != null) {
                //check if available in GAC
                System.Reflection.Assembly asm = System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(fullName));
                if (asm != null) {
                    path = asm.Location;
                }
            }
            if (!File.Exists(path)) {
                Log("WARNING: Dependency \"" + fullName + "\" not found; ignoring...");
                return;
            }
            ModuleDefinition module = ModuleDefinition.ReadModule(path, new ReaderParameters(ReadingMode.Immediate));
            Dependencies.Add(module);
            LoadBlacklist(module);
            Log("Dependency \""+fullName+"\" loaded.");
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
                Log("Entry point not found; skipping...");
                return null;
            }

            Log("M:"+entryOld.Name);

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
            Log("Checking if MonoMod already was there...");
            for (int ti = 0; ti < Module.Types.Count; ti++) {
                if (Module.Types[ti].Namespace == "MonoMod" && Module.Types[ti].Name == "WasHere") {
                    Log("MonoMod was there.");
                    return Module.Types[ti].Resolve();
                }
            }
            Log("Adding MonoMod.WasHere");
            TypeDefinition wasHere = new TypeDefinition("MonoMod", "WasHere", TypeAttributes.Public | TypeAttributes.Class) {
                BaseType = Module.Import(typeof(object))
            };
            Module.Types.Add(wasHere);
            return wasHere;
        }
        
        /// <summary>
        /// Checks if the method has a special name that is "allowed" to be patched.
        /// </summary>
        /// <returns><c>true</c> if the special name used in the method is allowed, <c>false</c> otherwise.</returns>
        /// <param name="method">Method to check.</param>
        public virtual bool AllowedSpecialName(MethodDefinition method) {
            if (TypesAdded.Contains(method.DeclaringType.FullName)) {
                return true;
            }
            
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
            
            if (method.IsGetter || method.IsSetter) {
                return true;
            }

            return !method.Attributes.HasFlag(MethodAttributes.SpecialName);
        }
        
        public virtual MethodReference FindLinked(MethodReference method) {
            MethodDefinition def = method.Resolve();
            if (def == null || !def.HasCustomAttributes) {
                return method;
            }
            CustomAttribute attrib = null;
            foreach (CustomAttribute attrib_ in def.CustomAttributes) {
                if (attrib_.AttributeType.FullName == "MonoMod.MonoModLinkTo") {
                    attrib = attrib_;
                    break;
                }
            }
            if (attrib == null) {
                return method;
            }
            
            if (attrib.ConstructorArguments.Count == 1) {
                //TODO get from delegate
                return method;
            }
            
            TypeDefinition type = null;
            
            if (attrib.ConstructorArguments[0].Type.FullName == "System.String") {
                //TODO get type from name
            } else {
                //TODO get type from type
                type = FindType((TypeReference) attrib.ConstructorArguments[0].Value).Resolve();
            }
            
            //TODO get method from name
            for (int i = 0; i < type.Methods.Count; i++) {
                if (type.Methods[i].Name == ((string) attrib.ConstructorArguments[1].Value) && type.Methods[i].Parameters.Count == method.Parameters.Count) {
                    //Probably check for more than that
                    method = type.Methods[i];
                    break;
                }
            }
            
            //TODO cache somewhere
            
            //orig
            //IL_003e: call instance void [FNA]Microsoft.Xna.Framework.Game::Update(class [FNA]Microsoft.Xna.Framework.GameTime)
            //linkmed
            //IL_0045: callvirt instance void [FNA]Microsoft.Xna.Framework.Game::Update(class [FNA]Microsoft.Xna.Framework.GameTime)
            
            return method;
        }
        
        public virtual FieldReference FindLinked(FieldReference field) {
            FieldDefinition def = field.Resolve();
            if (def == null || !def.HasCustomAttributes) {
                return field;
            }
            CustomAttribute attrib = null;
            foreach (CustomAttribute attrib_ in def.CustomAttributes) {
                if (attrib_.AttributeType.FullName == "MonoMod.MonoModLinkTo") {
                    attrib = attrib_;
                    break;
                }
            }
            if (attrib == null) {
                return field;
            }
            
            TypeDefinition type = null;
            
            if (attrib.ConstructorArguments[0].Type.FullName == "System.String") {
                //TODO get type from name
            } else {
                type = FindType((TypeReference) attrib.ConstructorArguments[0].Value).Resolve();
            }
            
            //TODO get method from name
            for (int i = 0; i < type.Fields.Count; i++) {
                if (type.Methods[i].Name == ((string) attrib.ConstructorArguments[1].Value)) {
                    //Probably check for more than that
                    field = type.Fields[i];
                    break;
                }
            }
            
            //TODO cache somewhere
            
            return field;
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
        
        [Obsolete("Use MakeMethodNameFindFriendly instead.")]
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
        
        public static string MakeMethodNameFindFriendly(string str, MethodReference method, TypeReference type, bool inner = false, string[] genParams = null) {
            while (method.IsGenericInstance) {
                method = ((GenericInstanceMethod) method).ElementMethod;
            }
            
            if (!inner) {
                int indexOfMethodDoubleColons = str.IndexOf("::");
                int openArgs = str.IndexOf("(", indexOfMethodDoubleColons);
                int numGenParams = 0;
                
                //screw generic parameters - replace them!
                int open = indexOfMethodDoubleColons;
                if (-1 < (open = str.IndexOf("<", open + 1)) && open < openArgs) {
                    int close_ = open;
                    int close = close_;
                    while (-1 < (close_ = str.IndexOf(">", close_ + 1)) && close_ < openArgs) {
                        close = close_;
                    }
                    
                    numGenParams = method.GenericParameters.Count;
                    if (numGenParams == 0) {
                        numGenParams = 1;
                        //GenericParams.Count is 0 (WHY?) so we must count ,s
                        int level = 0;
                        for (int i = open; i < close; i++) {
                            if (str[i] == '<') {
                                level++;
                            } else if (str[i] == '>') {
                                level--;
                            } else if (str[i] == ',' && level == 0) {
                                numGenParams++;
                            }
                        }
                        genParams = new string[numGenParams];
                        int j = 0;
                        //Simply approximate that the generic parameters MUST exist in the parameters in correct order...
                        for (int i = 0; i < method.Parameters.Count && j < genParams.Length; i++) {
                            TypeReference paramType = method.Parameters[i].ParameterType;
                            while (paramType.IsArray || paramType.IsByReference) {
                                paramType = paramType.GetElementType();
                            }
                            if (paramType.IsGenericParameter) {
                                genParams[j] = paramType.Name;
                                j++;
                            }
                        }
                    }
                    
                    str = str.Substring(0, open + 1) + numGenParams + str.Substring(close);
                    openArgs = str.IndexOf("(", indexOfMethodDoubleColons);
                }
                
                //add them if missing
                open = str.IndexOf("<", indexOfMethodDoubleColons);
                if ((open <= -1 || openArgs < open) && method.HasGenericParameters) {
                    int pos = indexOfMethodDoubleColons + 2 + method.Name.Length;
                    str = str.Substring(0, pos) + "<" + method.GenericParameters.Count + ">" + str.Substring(pos);
                    openArgs = str.IndexOf("(", indexOfMethodDoubleColons);
                }
                
                //screw multidimensional arrays - replace them!
                open = str.IndexOf("[");
                if (-1 < open && open < indexOfMethodDoubleColons) {
                    int close = str.IndexOf("]", open);
                    int n = 1;
                    int i = open;
                    while (-1 < (i = str.IndexOf(",", i + 1)) && i < close) {
                        n++;
                    }
                    str = str.Substring(0, open + 1) + n + str.Substring(close);
                    openArgs = str.IndexOf("(", indexOfMethodDoubleColons);
                }
                
                if (method.GenericParameters.Count != 0) {
                    numGenParams = method.GenericParameters.Count;
                    genParams = new string[numGenParams];
                    for (int i = 0; i < method.GenericParameters.Count; i++) {
                        genParams[i] = method.GenericParameters[i].Name;
                    }
                }
                
                //screw arg~ oh, wait, that's what we're trying to fix. Continue on.
                open = openArgs;
                if (-1 < open) {
                    //Methods without () would be weird...
                    //Well, make the params find-friendly
                    int close = str.IndexOf(")", open);
                    str = str.Substring(0, open) + MakeMethodNameFindFriendly(str.Substring(open, close - open + 1), method, type, true, genParams) + str.Substring(close + 1);
                    openArgs = str.IndexOf("(", indexOfMethodDoubleColons);
                }
                
                return str;
            }
            
            for (int i = 0; i < type.GenericParameters.Count; i++) {
                str = str.Replace("("+type.GenericParameters[i].Name+",", "(!"+i+",");
                str = str.Replace(","+type.GenericParameters[i].Name+",", ",!"+i+",");
                str = str.Replace(","+type.GenericParameters[i].Name+")", ",!"+i+")");
                str = str.Replace("("+type.GenericParameters[i].Name+")", "(!"+i+")");
                
                str = str.Replace("("+type.GenericParameters[i].Name+"&", "(!"+i+"&");
                str = str.Replace(","+type.GenericParameters[i].Name+"&", ",!"+i+"&");
                str = str.Replace(","+type.GenericParameters[i].Name+"&", ",!"+i+"&");
                str = str.Replace("("+type.GenericParameters[i].Name+"&", "(!"+i+"&");
                
                str = str.Replace("("+type.GenericParameters[i].Name+"[", "(!"+i+"[");
                str = str.Replace(","+type.GenericParameters[i].Name+"[", ",!"+i+"[");
                str = str.Replace(","+type.GenericParameters[i].Name+"[", ",!"+i+"[");
                str = str.Replace("("+type.GenericParameters[i].Name+"[", "(!"+i+"[");
                
                str = str.Replace("<"+type.GenericParameters[i].Name+",", "<!"+i+",");
                str = str.Replace(","+type.GenericParameters[i].Name+">", ",!"+i+">");
                str = str.Replace("<"+type.GenericParameters[i].Name+">", "<!"+i+">");
            }
            if (genParams == null) {
                return str;
            }
            
            for (int i = 0; i < genParams.Length; i++) {
                str = str.Replace("("+genParams[i]+",", "(!!"+i+",");
                str = str.Replace(","+genParams[i]+",", ",!!"+i+",");
                str = str.Replace(","+genParams[i]+")", ",!!"+i+")");
                str = str.Replace("("+genParams[i]+")", "(!!"+i+")");
                
                str = str.Replace("("+genParams[i]+"&", "(!!"+i+"&");
                str = str.Replace(","+genParams[i]+"&", ",!!"+i+"&");
                str = str.Replace(","+genParams[i]+"&", ",!!"+i+"&");
                str = str.Replace("("+genParams[i]+"&", "(!!"+i+"&");
                
                str = str.Replace("("+genParams[i]+"[", "(!!"+i+"[");
                str = str.Replace(","+genParams[i]+"[", ",!!"+i+"[");
                str = str.Replace(","+genParams[i]+"[", ",!!"+i+"[");
                str = str.Replace("("+genParams[i]+"[", "(!!"+i+"[");
                
                str = str.Replace("<"+genParams[i]+",", "<!!"+i+",");
                str = str.Replace(","+genParams[i]+">", ",!!"+i+">");
                str = str.Replace("<"+genParams[i]+">", "<!!"+i+">");
            }
            return str;
        }

        /// <summary>
        /// Determines if the field has got a specific MonoMod attribute.
        /// </summary>
        /// <returns><c>true</c> if the field contains the given MonoMod attribute, <c>false</c> otherwise.</returns>
        /// <param name="field">Field.</param>
        /// <param name="attribute">Attribute.</param>
        public static bool HasAttribute(FieldDefinition field, string attribute) {
            if (field == null) {
                return false;
            }
            if (!field.HasCustomAttributes) {
                return false;
            }
            foreach (CustomAttribute attrib in field.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod." + attribute) {
                    return true;
                }
            }
            return false;
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
        
        protected virtual void Log(object obj) {
            Log(obj.ToString());
        }
        
        protected virtual void Log(string txt) {
            if (Logger != null) {
                Logger(txt);
                return;
            }
            if (DefaultLogger != null) {
                DefaultLogger(txt);
                return;
            }
            Console.WriteLine(txt);
        }

    }
}
