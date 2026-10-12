using MonoMod.Utils;
using System;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace MonoMod.UnitTest
{
    public class MonoCorlibInternalTest
    {
        [MonoFact]
        public void TestSetMonoCorlibInternal()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("MonoMod.UnitTest.CorlibInternal"), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("Main").DefineType("AccessTest", TypeAttributes.Public);
            var method = type.DefineMethod("ReadPrivateValue", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Call, typeof(MonoCorlibInternalTest).GetMethod(nameof(PrivateValue), BindingFlags.NonPublic | BindingFlags.Static));
            il.Emit(OpCodes.Ret);
            var readPrivateValue = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), type.CreateType().GetMethod("ReadPrivateValue"));
            Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName == assembly.FullName);
            try
            {
                assembly.SetMonoCorlibInternal(true);
                Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName == assembly.FullName);
                Assert.Equal(42, readPrivateValue());
            }
            finally
            {
                assembly.SetMonoCorlibInternal(false);
            }
            Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName == assembly.FullName);
        }

        private static int PrivateValue() => 42;

        public sealed class MonoFactAttribute : FactAttribute
        {
            public MonoFactAttribute()
            {
                if (PlatformDetection.Runtime is not RuntimeKind.Mono)
                    Skip = "Only supported on Mono.";
            }
        }
    }
}
