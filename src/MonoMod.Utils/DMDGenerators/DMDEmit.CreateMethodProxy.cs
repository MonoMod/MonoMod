#if !NETSTANDARD
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace MonoMod.Utils
{
    internal static partial class _DMDEmit
    {

        private readonly static MethodInfo m_MethodBase_InvokeSimple = typeof(MethodBase).GetMethod(
            "Invoke", BindingFlags.Public | BindingFlags.Instance, null,
            new Type[] { typeof(object), typeof(object[]) },
            null
        )!;

        private static MethodBuilder _CreateMethodProxy(MethodBuilder context, MethodInfo target)
        {
            var tb = (TypeBuilder)context.DeclaringType!;
            var name = $".dmdproxy<{target.Name.Replace('.', '_')}>?{target.GetHashCode()}";
            MethodBuilder mb;

            // System.NotSupportedException: The invoked member is not supported before the type is created.
            /*
            mb = tb.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static) as MethodBuilder;
            if (mb != null)
                return mb;
            */

            var args = target.GetParameters().Select(param => param.ParameterType).ToArray();
            mb = tb.DefineMethod(
                name,
                MethodAttributes.HideBySig | MethodAttributes.Private | MethodAttributes.Static,
                CallingConventions.Standard,
                target.ReturnType,
                args
            );
            var il = mb.GetILGenerator();

            // Load the DynamicMethod reference first.
            _ = il.EmitNewTypedReference(target, out _);
            // Note: we throw away the scope holder because this method will live for the entire lifetime of the program.

            // Load any other arguments on top of that.
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4, args.Length);
            il.Emit(OpCodes.Newarr, typeof(object));

            for (var i = 0; i < args.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);

                il.Emit(OpCodes.Ldarg, i);

                var argType = args[i];
                var argIsByRef = argType.IsByRef;
                if (argIsByRef)
                    argType = argType.GetElementType() ?? argType;
                var argIsValueType = argType.IsValueType;
                if (argIsValueType)
                {
                    il.Emit(OpCodes.Box, argType);
                }

                il.Emit(OpCodes.Stelem_Ref);
            }

            // Invoke the delegate and return its result.
            il.Emit(OpCodes.Callvirt, m_MethodBase_InvokeSimple);

            if (target.ReturnType == typeof(void))
                il.Emit(OpCodes.Pop);
            else if (target.ReturnType.IsValueType)
                il.Emit(OpCodes.Unbox_Any, target.ReturnType);
            il.Emit(OpCodes.Ret);

            return mb;
        }

    }
}
#endif
