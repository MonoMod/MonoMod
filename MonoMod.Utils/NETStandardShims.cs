using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;

/* This class is included in every MonoMod assembly.
 * As far as I know, methods aren't guaranteed to be inlined
 * across assembly boundaries.
 * -ade
 */
static class NETStandardShims {

#if !NETSTANDARD

    public static Type GetTypeInfo(this Type type)
        => type;
    public static Type AsType(this Type type)
        => type;

    public static MethodInfo GetMethodInfo(this Delegate d)
        => d.Method;

#else

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

    public static Delegate CreateDelegate(Type type, object target, MethodInfo method)
        =>
#if NETSTANDARD
            method.CreateDelegate(type, target);
#else
            Delegate.CreateDelegate(type, target, method);
#endif

}
