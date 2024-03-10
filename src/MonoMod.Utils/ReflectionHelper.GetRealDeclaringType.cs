using System;
using System.Reflection;

namespace MonoMod.Utils
{
    public static partial class ReflectionHelper
    {

        private static Type? t_RuntimeModule =
            typeof(Module).Assembly
            .GetType("System.Reflection.RuntimeModule");

        private static PropertyInfo? p_RuntimeModule_RuntimeType =
            typeof(Module).Assembly
            .GetType("System.Reflection.RuntimeModule")
            ?.GetProperty("RuntimeType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static FieldInfo? f_RuntimeModule__impl =
            typeof(Module).Assembly
            .GetType("System.Reflection.RuntimeModule")
            ?.GetField("_impl", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo? m_RuntimeModule_GetGlobalType =
            typeof(Module).Assembly
            .GetType("System.Reflection.RuntimeModule")
            ?.GetMethod("GetGlobalType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        public static Type? GetModuleType(this Module? module)
        {
            // Sadly we can't blindly resolve type 0x02000001 as the runtime throws ArgumentException.

            if (module == null || t_RuntimeModule == null || !t_RuntimeModule.IsInstanceOfType(module))
                return null;

            // .NET
            if (p_RuntimeModule_RuntimeType != null)
                return (Type)p_RuntimeModule_RuntimeType.GetValue(module, ArrayEx.Empty<object?>())!;

            // Mono
            if (f_RuntimeModule__impl != null &&
                m_RuntimeModule_GetGlobalType != null)
                return (Type)m_RuntimeModule_GetGlobalType.Invoke(null, new object?[] { f_RuntimeModule__impl.GetValue(module) })!;

            return null;
        }

        public static Type? GetRealDeclaringType(this MemberInfo member)
            => Helpers.ThrowIfNull(member).DeclaringType ?? member.Module?.GetModuleType();

    }
}
