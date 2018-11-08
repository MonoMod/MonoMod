using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using MonoMod.Utils;

/* This class is included in every MonoMod assembly.
 * As far as I know, methods aren't guaranteed to be inlined
 * across assembly boundaries.
 * -ade
 */
static class NETStandardShims {

#if !NETSTANDARD

    public static Type GetTypeInfo(this Type t)
        => t;
    public static Type AsType(this Type t)
        => t;

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

#if !NETSTANDARD1_X
    
    public static string GetLocation(this Assembly asm)
        => asm.Location;

    public static int GetMetadataToken(this MemberInfo m)
        => m.MetadataToken;

#else

    public static MethodImplAttributes GetMethodImplementationFlags(this MethodBase m)
        => m.MethodImplementationFlags;

    private static readonly FastReflectionDelegate _Assembly_get_Location =
        typeof(Assembly).GetMethod("get_Location", BindingFlags.Public | BindingFlags.Instance)
        ?.CreateFastDelegate();
    public static string GetLocation(this Assembly asm)
        => (string) _Assembly_get_Location(asm);

    private static readonly FastReflectionDelegate _MemberInfo_get_MetadataToken =
        typeof(MemberInfo).GetMethod("get_MetadataToken", BindingFlags.Public | BindingFlags.Instance)
        ?.CreateFastDelegate();
    public static int GetMetadataToken(this MemberInfo m)
        => (int) _MemberInfo_get_MetadataToken(m);

#endif

    public static Delegate CreateDelegate(Type type, object target, MethodInfo method)
        =>
#if NETSTANDARD
            method.CreateDelegate(type, target);
#else
            Delegate.CreateDelegate(type, target, method);
#endif

}
