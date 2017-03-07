using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoMod.DebugIL {
    public static class DebugILGeneratorExt {

        public readonly static Type t_MetadataType = typeof(MetadataType);

        public static ScopeDebugInformation GetOrAddScope(this MethodDebugInformation mdi) {
            if (mdi.Scope != null)
                return mdi.Scope;
            return mdi.Scope = new ScopeDebugInformation(
                mdi.Method.Body.Instructions[0],
                mdi.Method.Body.Instructions[mdi.Method.Body.Instructions.Count - 1]
            );
        }

        public static string GenerateVariableName(this VariableDefinition @var, MethodDefinition method = null, int i = -1) {
            TypeReference type = @var.VariableType;
            while (type is TypeSpecification)
                type = ((TypeSpecification) type).ElementType;

            string name = type.Name;

            if (type.MetadataType == MetadataType.Boolean)
                name = "flag";
            else if (type.IsPrimitive)
                name = Enum.GetName(t_MetadataType, type.MetadataType);

            name = name.Substring(0, 1).ToLowerInvariant() + name.Substring(1);

            if (method == null)
                return i < 0 ? name : (name + i);

            // Check for usage as loop counter or similar?

            return i < 0 ? name : (name + i);
        }

    }
}
