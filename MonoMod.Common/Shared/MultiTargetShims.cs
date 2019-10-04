using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using MonoMod.Utils;
using Mono.Cecil;

/* This class is included in every MonoMod assembly.
 * As far as I know, methods aren't guaranteed to be inlined
 * across assembly boundaries.
 * -ade
 */
static class MultiTargetShims {

    private static readonly object[] _NoArgs = new object[0];

#if NETSTANDARD

    public static Module[] GetModules(this Assembly asm)
        => asm.Modules.ToArray();
    public static Module GetModule(this Assembly asm, string name)
        => asm.Modules.FirstOrDefault(module => module.Name == name);

    public static byte[] GetBuffer(this MemoryStream ms) {
        long posPrev = ms.Position;
        byte[] data = new byte[ms.Length];
        ms.Read(data, 0, data.Length);
        ms.Position = posPrev;
        return data;
    }

#endif

#if CECIL0_10
    public static TypeReference GetConstraintType(this TypeReference type)
        => type;
#else
    public static TypeReference GetConstraintType(this GenericParameterConstraint constraint)
        => constraint.ConstraintType;
#endif

}
