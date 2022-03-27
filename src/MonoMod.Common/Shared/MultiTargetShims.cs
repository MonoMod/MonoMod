#nullable enable
using System;
#if NETSTANDARD
using System.IO;
using System.Linq;
using System.Reflection;
#endif
using Mono.Cecil;

namespace MonoMod;

/* This class is included in every MonoMod assembly.
 * As far as I know, methods aren't guaranteed to be inlined
 * across assembly boundaries.
 * -ade
 */
internal static class MultiTargetShims {

    // Can't use the globalization-aware overloads on old .NET targets...
    // Weirdly enough this is very spotty, and the compiler will only *fall back* to extension methods,
    // thus keeping this un-#if'd is zero perf cost and zero maintenance cost.
    // However, the compiler likes to yell at us, so we #if it anyway.

#if !NET5_0_OR_GREATER
    public static string Replace(this string self, string oldValue, string newValue, StringComparison comparison)
        => self.Replace(oldValue, newValue);

    public static bool Contains(this string self, string value, StringComparison comparison)
        => self.Contains(value);

    public static int GetHashCode(this string self, StringComparison comparison)
        => self.GetHashCode();

    public static int IndexOf(this string self, char value, StringComparison comparison)
        => self.IndexOf(value);

    public static int IndexOf(this string self, string value, StringComparison comparison)
        => self.IndexOf(value, 0, value.Length, comparison); // funny enough, this overload exists in .NET 3.5, but not most of the other StringComparison overloads
#endif

#if NETSTANDARD

    public static Module[] GetModules(this Assembly asm)
        => asm.Modules.ToArray();
    public static Module? GetModule(this Assembly asm, string name)
        => asm.Modules.FirstOrDefault(module => module.Name == name);

    public static byte[] GetBuffer(this MemoryStream ms) {
        long posPrev = ms.Position;
        byte[] data = new byte[ms.Length];
        int read = 0;
        do {
            int amt = ms.Read(data, read, data.Length - read);
            read += amt;
            if (amt == 0) {
                MMDbgLog.Log("A read of a non-empty memory stream to a non-empty buffer failed; what?");
                break;
            }
        } while (read < data.Length);
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
