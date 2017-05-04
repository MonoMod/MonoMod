using Mono.Cecil;
using StringInject;
using System;
using MonoMod.NET40Shim;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using Mono.Collections.Generic;

namespace MonoMod.InlineRT {
    public static class MMILAccessParser {

        // Format of an array:
        /*
        newarr [mscorlib]System.Object

        // start
        dup // arr
        ldc.i4.0 // index
        // value
        call string Example::StaticA()
        // store
        stelem.ref

        // start
        dup // arr
        ldc.i4.1 // index
        // value
        ldc.i4.2
        ldc.i4.4
        call int32 Example::StaticB(int32, int32)
        // optional: box
        box [mscorlib]System.Int32
        // store
        stelem.ref

        // that's it
        */

        // Format of an Access call:
        /*
        ld self (if any)
        ld type name (if any)
        ld member name
        newobj Access <!!!!
        ld arrsize
        newarr
        arr element #0
        arr element #1
        arr element #n
        call New / Call / Get / Set
        */

        public static bool ParseMMILAccessCall(this MonoModder self, MethodBody body, MethodReference call, MethodReference callOrig, ref int instri) {
            string callName = $"{call.DeclaringType.FullName.Substring(11)}::{call.Name}";

            if (callName == "Access::.ctor" || callName == "StaticAccess::.ctor" ||
                callName == "Access`1::.ctor" || callName == "StaticAccess`1::.ctor")
                return ParseMMILAccessCtorCall(self, body, call, callOrig, ref instri);

            if (callName == "BatchAccess::.ctor" || callName == "BatchAccess`1::.ctor")
                return ParseMMILBatchAccessCtorCall(self, body, call, callOrig, ref instri);

            return true;
        }

        public static void ParseMMILAccessCtorHead(
            MonoModder self, MethodBody body, MethodReference callCtor, MethodReference callCtorOrig, ref int instri,
            out TypeReference type_, out IMetadataTokenProvider member_
        ) {
            TypeReference type = null;
            IMetadataTokenProvider member = null;

            if (callCtorOrig.DeclaringType.IsGenericInstance) {
                type = self.Relink(((GenericInstanceType) callCtorOrig.DeclaringType).GenericArguments[0], body.Method);
            }

            if (callCtor.Parameters.Count >= 2 && callCtor.Parameters[callCtor.Parameters.Count - 2].Name == "type") {
                type = self.FindTypeDeep((string) body.Instructions[instri - 2].Operand);
                body.Instructions.RemoveAt(instri - 2);
                instri--;
            } else if (callCtor.Parameters.Count == 1 && callCtor.Parameters[0].Name == "type") {
                type = self.FindTypeDeep((string) body.Instructions[instri - 1].Operand);
                body.Instructions.RemoveAt(instri - 1);
                instri--;
            }

            TypeDefinition typeDef = type.Resolve();

            if (callCtor.Parameters.Count >= 1 && callCtor.Parameters[callCtor.Parameters.Count - 1].Name == "name") {
                string memberName = (string) body.Instructions[instri - 1].Operand;
                body.Instructions.RemoveAt(instri - 1);
                instri--;
                if (memberName.StartsWith("field:"))
                    member = typeDef.FindField(memberName.Substring(6).Trim());
                else if (memberName.StartsWith("method:"))
                    member = typeDef.FindMethod(memberName.Substring(7).Trim());
                else
                    member = typeDef.FindField(memberName) ?? (IMetadataTokenProvider) typeDef.FindMethod(memberName);

                member.SetPublic(true);
            }

            // Remove the newobj constructor call
            body.Instructions.RemoveAt(instri);

            type_ = type;
            member_ = member;
        }

        public static bool ParseMMILAccessCtorCall(MonoModder self, MethodBody body, MethodReference callCtor, MethodReference callCtorOrig, ref int instri) {
            ILProcessor il = body.GetILProcessor();
            Collection<Instruction> instrs = body.Instructions;

            bool staticAccess = callCtor.DeclaringType.Name == "StaticAccess" || callCtor.DeclaringType.Name == "StaticAccess`1";
            TypeReference type = null;
            IMetadataTokenProvider member = null;
            ParseMMILAccessCtorHead(self, body, callCtor, callCtorOrig, ref instri, out type, out member);

            // Currently in front of us:
            /*
            ld arrsize
            newarr
            arr element #0
            arr element #1
            arr element #n
            call New / Call / Get / Set
            */

            int count = instrs[instri].GetInt();
            instrs.RemoveAt(instri);

            // Remove the newarr
            instrs.RemoveAt(instri);

            // Parse array content
            int depth = 0;
            Instruction instr = null;
            // Skip anything including nested arrays
            for (int i = 0; i < count; i++) {
                instrs.RemoveAt(instri); // arr
                instrs.RemoveAt(instri); // index
                while ((instr = instrs[instri]).OpCode != OpCodes.Stelem_Ref || depth > 0) {
                    // Nested parsing
                    self.DefaultParser(self, body, instrs[instri], ref instri);

                    if (instr.OpCode == OpCodes.Newarr)
                        depth++;
                    else if (depth > 0 && instr.OpCode == OpCodes.Stelem_Ref)
                        depth--;

                    instri++;
                }
                // At Stelem_Ref right now
                if (instrs[instri - 1].OpCode == OpCodes.Box) {
                    instrs.RemoveAt(instri - 1);
                    instri--;
                }
                instrs.RemoveAt(instri); // stelem.ref
            }

            // FINALLY replace the call as required
            Instruction callInstr = instrs[instri];
            MethodDefinition call = ((MethodReference) callInstr.Operand).Resolve();

            // Remove the MMILAccess call
            instrs.RemoveAt(instri);

            if (staticAccess)
                switch (call.Name) {
                    case "New":
                        instrs.Insert(instri, il.Create(OpCodes.Newobj, (MethodReference) member));
                        instri++;
                        break;

                    case "Call":
                        instrs.Insert(instri, il.Create(OpCodes.Call, (MethodReference) member));
                        instri++;
                        il.InsertLdnullIfRequired(ref instri, (MethodReference) member);
                        break;

                    case "Get":
                        if (member is FieldReference)
                            instrs.Insert(instri, il.Create(OpCodes.Ldsfld, (FieldReference) member));
                        else
                            instrs.Insert(instri, il.Create(OpCodes.Call, (MethodReference) member));
                        instri++;
                        break;

                    case "Set":
                        if (member is FieldReference)
                            instrs.Insert(instri, il.Create(OpCodes.Stsfld, (FieldReference) member));
                        else
                            instrs.Insert(instri, il.Create(OpCodes.Call, (MethodReference) member));
                        instri++;
                        break;
                }
            else
                switch (call.Name) {
                    case "Call":
                        instrs.Insert(instri, il.Create(OpCodes.Callvirt, (MethodReference) member));
                        instri++;
                        il.InsertLdnullIfRequired(ref instri, (MethodReference) member);
                        break;

                    case "Get":
                        if (member is FieldReference)
                            instrs.Insert(instri, il.Create(OpCodes.Ldfld, (FieldReference) member));
                        else
                            instrs.Insert(instri, il.Create(OpCodes.Callvirt, (MethodReference) member));
                        instri++;
                        break;

                    case "Set":
                        if (member is FieldReference)
                            instrs.Insert(instri, il.Create(OpCodes.Stfld, (FieldReference) member));
                        else
                            instrs.Insert(instri, il.Create(OpCodes.Callvirt, (MethodReference) member));
                        instri++;
                        break;
                }

            instri--;
            return false; // Don't let the PatchRefs pass handle the newly emitted call!
        }


        public static bool ParseMMILBatchAccessCtorCall(this MonoModder self, MethodBody body, MethodReference callCtor, MethodReference callCtorOrig, ref int instri) {
            ILProcessor il = body.GetILProcessor();
            Collection<Instruction> instrs = body.Instructions;

            TypeReference type = null;
            IMetadataTokenProvider member = null;
            ParseMMILAccessCtorHead(self, body, callCtor, callCtorOrig, ref instri, out type, out member);
            TypeDefinition typeDef = type.Resolve();

            List<string> with = new List<string>();
            List<string> without = new List<string>();
            List<string> current = new List<string>();

            // Currently in front of us:
            /*
            ld arrsize (opt)
            newarr (opt)
            arr element #0 (opt)
            arr element #1 (opt)
            arr element #n (opt)
            call With / Without / ... (opt)
            target (opt)
            call CopyTo / get_AllMethods / ...
            */

            int? count = instrs[instri].GetIntOrNull();
            if (count != null) {
                ParseFilter:
                instrs.RemoveAt(instri);

                // Remove the newarr
                instrs.RemoveAt(instri);

                // Parse array content
                for (int i = 0; i < count; i++) {
                    instrs.RemoveAt(instri); // arr
                    instrs.RemoveAt(instri); // index

                    // ldstr
                    current.Add((string) instrs[instri].Operand);
                    instrs.RemoveAt(instri);
                    
                    instrs.RemoveAt(instri); // stelem.ref
                }

                // Should be at With / Without now
                MethodReference callFilter = (MethodReference) instrs[instri].Operand;
                if (callFilter.Name == "With")
                    with.AddRange(current);
                else if (callFilter.Name == "Without")
                    without.AddRange(current);
                current.Clear();
                instrs.RemoveAt(instri); // call

                // Follow-up.
                if ((count = instrs[instri].GetIntOrNull()) != null)
                    goto ParseFilter;
            }

            // Skip any target-loading instructions
            while (!((instrs[instri].Operand as MethodReference)?.DeclaringType?.Name?.StartsWith("BatchAccess") ?? false)) {
                // Nested parsing
                self.DefaultParser(self, body, instrs[instri], ref instri);
                instri++;
            }

            // FINALLY replace the call as required
            Instruction callInstr = instrs[instri];
            MethodDefinition call = ((MethodReference) callInstr.Operand).Resolve();

            VariableDefinition varTarget = null;
            if (call.Parameters.Count != 0 && call.Parameters[call.Parameters.Count - 1].Name == "target") {
                // Replace MMILBatchAccess call with local store for the target
                varTarget = new VariableDefinition(type);
                body.Variables.Add(varTarget);
                instrs[instri] = il.Create(OpCodes.Stloc, varTarget.Index);
                instri++;

            } else {
                // Remove MMILBatchAccess call
                instrs.RemoveAt(instri);
            }


            switch (call.Name) {
                case "get_AllMethods":
                    il.InsertMetadataTokenArray(ref instri, typeDef.Methods.Filtered(with, without));
                    break;

                case "get_AllFields":
                    il.InsertMetadataTokenArray(ref instri, typeDef.Fields.Filtered(with, without));
                    break;

                case "get_AllProperties":
                    il.InsertMetadataTokenArray(ref instri, typeDef.Properties.Filtered(with, without));
                    break;

                case "CopyTo":
                    Collection<IMetadataTokenProvider> tokens = new Collection<IMetadataTokenProvider>();
                    for (int i = 0; i < typeDef.Fields.Count; i++) {
                        FieldDefinition mtp = typeDef.Fields[i];
                        if (mtp.IsStatic)
                            continue;
                        string name = mtp.Name;
                        string nameExplicit = "field:" + name;
                        if (without.Count != 0 && (without.Contains(name) || without.Contains(nameExplicit)))
                            continue;
                        if (with.Count == 0 && !mtp.IsPublic)
                            continue;
                        if (with.Count == 0 || with.Contains(name) || with.Contains(nameExplicit)) {
                            tokens.Add(mtp);
                        }
                    }
                    for (int i = 0; i < typeDef.Properties.Count; i++) {
                        PropertyDefinition mtp = typeDef.Properties[i];
                        if (!mtp.HasThis)
                            continue;
                        if (mtp.GetMethod == null || mtp.SetMethod == null)
                            continue;
                        string name = mtp.Name;
                        string nameExplicit = "property:" + name;
                        if (without.Count != 0 && (without.Contains(name) || without.Contains(nameExplicit)))
                            continue;
                        if (with.Count == 0 && (!mtp.GetMethod.IsPublic || !mtp.SetMethod.IsPublic))
                            continue;
                        if (with.Count == 0 || with.Contains(name) || with.Contains(nameExplicit)) {
                            tokens.Add(mtp);
                        }
                    }
                    // Store source in local
                    VariableDefinition varSource = new VariableDefinition(type);
                    body.Variables.Add(varSource);
                    instrs[instri] = il.Create(OpCodes.Stloc, varSource.Index);
                    instri++;
                    il.InsertCopyTo(ref instri, tokens, varSource, varTarget);
                    break;
            }

            instri--;
            return false; // Don't let the PatchRefs pass handle the newly emitted call!
        }


        public static void InsertLdnullIfRequired(this ILProcessor il, ref int instri, MethodReference method) {
            if (method.ReturnType != null && method.ReturnType.MetadataType != MetadataType.Void)
                return;
            if (instri < il.Body.Instructions.Count && il.Body.Instructions[instri].OpCode == OpCodes.Pop) {
                il.Body.Instructions.RemoveAt(instri);
                return;
            }
            il.Body.Instructions.Insert(instri, il.Create(OpCodes.Ldnull));
            instri++;
        }


        private readonly static System.Reflection.MethodInfo _TypeHandleConverter = typeof(Type).GetMethod("GetTypeFromHandle", new Type[] { typeof(RuntimeTypeHandle) });
        private readonly static System.Reflection.MethodInfo _MethodHandleConverter = typeof(System.Reflection.MethodBase).GetMethod("GetMethodFromHandle", new Type[] { typeof(RuntimeMethodHandle) });
        private readonly static System.Reflection.MethodInfo _FieldHandleConverter = typeof(System.Reflection.FieldInfo).GetMethod("GetFieldFromHandle", new Type[] { typeof(RuntimeFieldHandle) });

        public static void InsertMetadataTokenArray<T>(this ILProcessor il, ref int instri, Collection<T> tokens) where T : IMetadataTokenProvider {
            MethodBody body = il.Body;
            Collection<Instruction> instrs = body.Instructions;
            MethodDefinition method = body.Method;
            Type t = typeof(T);

            System.Reflection.MethodInfo converter = null;
            if (typeof(TypeReference).IsAssignableFrom(t))
                converter = _TypeHandleConverter;
            else if (typeof(MethodReference).IsAssignableFrom(t))
                converter = _MethodHandleConverter;
            else if (typeof(FieldReference).IsAssignableFrom(t))
                converter = _FieldHandleConverter;
            MethodReference converterRef = method.Module.ImportReference(converter);

            instrs.Insert(instri, il.Create(OpCodes.Ldc_I4, tokens.Count));
            instri++;

            instrs.Insert(instri, il.Create(OpCodes.Newarr, method.Module.ImportReference(converter.ReturnType)));
            instri++;

            for (int i = 0; i < tokens.Count; i++) {
                IMetadataTokenProvider mtp = tokens[i];

                instrs.Insert(instri, il.Create(OpCodes.Dup));
                instri++;
                instrs.Insert(instri, il.Create(OpCodes.Ldc_I4, i));
                instri++;

                if (mtp is TypeReference)
                    instrs.Insert(instri, il.Create(OpCodes.Ldtoken, (TypeReference) mtp));
                else if (mtp is MethodReference)
                    instrs.Insert(instri, il.Create(OpCodes.Ldtoken, (MethodReference) mtp));
                else if (mtp is FieldReference)
                    instrs.Insert(instri, il.Create(OpCodes.Ldtoken, (FieldReference) mtp));
                instri++;
                instrs.Insert(instri, il.Create(OpCodes.Call, converterRef));
                instri++;

                instrs.Insert(instri, il.Create(OpCodes.Stelem_Ref));
                instri++;
            }
        }


        public static void InsertCopyTo(this ILProcessor il, ref int instri, Collection<IMetadataTokenProvider> tokens, VariableReference from, VariableReference to) {
            MethodBody body = il.Body;
            Collection<Instruction> instrs = body.Instructions;
            MethodDefinition method = body.Method;

            for (int i = 0; i < tokens.Count; i++) {
                IMetadataTokenProvider mtp = tokens[i];

                instrs.Insert(instri, il.Create(OpCodes.Ldloc, to.Index));
                instri++;

                instrs.Insert(instri, il.Create(OpCodes.Ldloc, from.Index));
                instri++;

                if (mtp is PropertyDefinition) {
                    instrs.Insert(instri, il.Create(OpCodes.Call, ((PropertyDefinition) mtp).GetMethod));
                    instri++;
                    instrs.Insert(instri, il.Create(OpCodes.Call, ((PropertyDefinition) mtp).SetMethod));
                    instri++;
                } else if (mtp is FieldDefinition) {
                    instrs.Insert(instri, il.Create(OpCodes.Ldfld, (FieldDefinition) mtp));
                    instri++;
                    instrs.Insert(instri, il.Create(OpCodes.Stfld, (FieldDefinition) mtp));
                    instri++;
                }
            }
        }


    }
}
