using Mono.Cecil;
using StringInject;
using System;
using MonoMod.NET40Shim;
using Mono.Cecil.Cil;

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

        public static bool ParseMMILAccessCtorCall(this MonoModder self, MethodBody body, MethodReference callCtor, ref int instri) {
            bool staticAccess = callCtor.DeclaringType.Name == "StaticAccess" || callCtor.DeclaringType.Name == "StaticAccess`1";
            TypeReference type = null;
            IMetadataTokenProvider member = null;

            if (callCtor.DeclaringType.IsGenericInstance) {
                type = self.Relink(((GenericInstanceType) callCtor.DeclaringType).GenericArguments[0], body.Method);
            }

            if (( staticAccess && callCtor.Parameters.Count == 2 && callCtor.Parameters[0].Name == "type") ||
                (!staticAccess && callCtor.Parameters.Count == 3 && callCtor.Parameters[1].Name == "type")) {
                type = self.FindTypeDeep((string) body.Instructions[instri - 2].Operand);
                body.Instructions.RemoveAt(instri - 2);
                instri--;
            }

            TypeDefinition typeDef = type.Resolve();

            string memberName = (string) body.Instructions[instri - 1].Operand;
            body.Instructions.RemoveAt(instri - 1);
            instri--;
            if (memberName.StartsWith("field:"))
                member = typeDef.FindField(memberName.Substring(6).Trim());
            else if (memberName.StartsWith("method:"))
                member = typeDef.FindMethod(memberName.Substring(7).Trim());
            else
                member = typeDef.FindField(memberName) ?? (IMetadataTokenProvider) typeDef.FindMethod(memberName);

            // Currently in front of us:
            /*
            ld arrsize
            newarr
            arr element #0
            arr element #1
            arr element #n
            call New / Call / Get / Set
            */

            instri++;
            int count = body.Instructions[instri].GetInt();
            body.Instructions.RemoveAt(instri);

            // Remove the newarr
            instri++;
            body.Instructions.RemoveAt(instri);
            instri--;

            // Parse from now on if it's something parseable
            instri++;
            self.DefaultParser(body, body.Instructions[instri], ref instri);
            
            int depth = 0;
            Instruction instr = null;
            // Skip anything including nested arrays
            for (int i = 0; i < count; i++) {
                // At Stelem_Ref right now
                instri++;
                while (depth > 0 || (instr = body.Instructions[instri]).OpCode != OpCodes.Stelem_Ref) {
                    if (instr.OpCode == OpCodes.Newarr)
                        depth++;
                    else if (depth > 0 && instr.OpCode == OpCodes.Stelem_Ref)
                        depth--;
                    instri++;
                }
            }

            // FINALLY replace the call as required
            instri++;
            Instruction callInstr = body.Instructions[instri];
            MethodReference call = (MethodReference) callInstr.Operand;

            ILProcessor il = body.GetILProcessor();
            if (staticAccess)
                switch (call.Name) {
                    case "New":
                        il.InsertBefore(callInstr, il.Create(OpCodes.Newobj, (MethodReference) member));
                        break;

                    case "Call":
                        il.InsertBefore(callInstr, il.Create(OpCodes.Call, (MethodReference) member));
                        break;

                    case "Get":
                        if (member is FieldReference)
                            il.InsertBefore(callInstr, il.Create(OpCodes.Ldsfld, (FieldReference) member));
                        else
                            il.InsertBefore(callInstr, il.Create(OpCodes.Call, (MethodReference) member));
                        break;

                    case "Set":
                        if (member is FieldReference)
                            il.InsertBefore(callInstr, il.Create(OpCodes.Stsfld, (FieldReference) member));
                        else
                            il.InsertBefore(callInstr, il.Create(OpCodes.Call, (MethodReference) member));
                        break;
                }
            else
                switch (call.Name) {
                    case "Call":
                        il.InsertBefore(callInstr, il.Create(OpCodes.Callvirt, (MethodReference) member));
                        break;

                    case "Get":
                        if (member is FieldReference)
                            il.InsertBefore(callInstr, il.Create(OpCodes.Ldfld, (FieldReference) member));
                        else
                            il.InsertBefore(callInstr, il.Create(OpCodes.Callvirt, (MethodReference) member));
                        break;

                    case "Set":
                        if (member is FieldReference)
                            il.InsertBefore(callInstr, il.Create(OpCodes.Stfld, (FieldReference) member));
                        else
                            il.InsertBefore(callInstr, il.Create(OpCodes.Callvirt, (MethodReference) member));
                        break;
                }

            body.Instructions.RemoveAt(instri + 1);

            return false; // Don't let the PatchRefs pass handle this!
        }

    }
}
