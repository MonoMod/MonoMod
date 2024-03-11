using MonoMod.Utils;
using System;
using MonoMod.Cil;
using Xunit;
using Xunit.Abstractions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;

namespace MonoMod.UnitTest.Github
{
    public class Issue171 : TestBase
    {
        public Issue171(ITestOutputHelper helper) : base(helper)
        {
        }

        private static double Fn1() => default;
        private static IntPtr Fn2() => default;
        private static UIntPtr Fn3() => default;
        private static object Fn4() => default;

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void CalliToManagedMethodDoesNotFail(int idx)
        {
            ReadOnlySpan<MethodInfo> methods = [
                ((Delegate)Fn1).Method,
                ((Delegate)Fn2).Method,
                ((Delegate)Fn3).Method,
                ((Delegate)Fn4).Method
            ];

            DoTestForMethod(methods[idx]);
        }

        private static void DoTestForMethod(MethodInfo original)
        {
            var originalParameters = original.GetParameters();
            var offset = original.IsStatic ? 0 : 1;
            var parameterTypes = new Type[originalParameters.Length + offset];
            if (!original.IsStatic)
                parameterTypes[0] = original.GetThisParamType();
            for (var i = 0; i < originalParameters.Length; i++)
                parameterTypes[i + offset] = originalParameters[i].ParameterType;

            using var dmd = new DynamicMethodDefinition(
                "Proxy",
                original.ReturnType,
                parameterTypes
            );

            using var il = new ILContext(dmd.Definition);
            var c = new ILCursor(il);

            var originalRef = il.Module.ImportReference(original);

            var callsite = new CallSite(originalRef.ReturnType)
            {
                HasThis = originalRef.HasThis,
                ExplicitThis = originalRef.ExplicitThis,
                CallingConvention = originalRef.CallingConvention
            };
            foreach (var param in originalRef.Parameters)
                callsite.Parameters.Add(param);

            for (var i = 0; i < parameterTypes.Length; i++)
                c.EmitLdarg(i);
            c.EmitLdftn(original);
            c.Emit(OpCodes.Calli, callsite);
            c.EmitRet();

            var method = dmd.Generate();
            method.Invoke(null, []);
        }
    }
}
