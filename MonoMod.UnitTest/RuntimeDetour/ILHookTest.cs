#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Text;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class ILHookTest {
        private bool DidNothing = true;

        [Fact]
        public void TestILHooks() {
            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);

            using (ILHook h = new ILHook(
                typeof(ILHookTest).GetMethod("DoNothing"),
                il => {
                    ILCursor c = new ILCursor(il);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(OpCodes.Ldc_I4_0);
                    c.Emit(OpCodes.Stfld, typeof(ILHookTest).GetField("DidNothing", BindingFlags.NonPublic | BindingFlags.Instance));
                }
            )) {
                DidNothing = true;
                DoNothing();
                Assert.False(DidNothing);
            }

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DoNothing() {
        }
        
    }
}
