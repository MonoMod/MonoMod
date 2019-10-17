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
    public class MultiHookTest {
        private int Counter = 0;

        [Fact]
        public void TestMultiHooks() {
            Counter = 1;
            DoNothing();
            Assert.Equal(1, Counter);

            using (ILHook hIL = new ILHook(
                typeof(MultiHookTest).GetMethod("DoNothing"),
                il => {
                    ILCursor c = new ILCursor(il);
                    FieldInfo f_Counter = typeof(MultiHookTest).GetField("Counter", BindingFlags.NonPublic | BindingFlags.Instance);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(OpCodes.Dup);
                    c.Emit(OpCodes.Ldfld, f_Counter);
                    c.Emit(OpCodes.Ldc_I4_3);
                    c.Emit(OpCodes.Mul);
                    c.Emit(OpCodes.Stfld, f_Counter);
                },
                new ILHookConfig {
                    ManualApply = true
                }
            ))
            using (Hook h1 = new Hook(
                typeof(MultiHookTest).GetMethod("DoNothing"),
                new Action<Action<MultiHookTest>, MultiHookTest>((orig, self) => {
                    orig(self);
                    Counter += 2;
                }),
                new HookConfig {
                    ManualApply = true
                }
            ))
            using (Hook h2 = new Hook(
                typeof(MultiHookTest).GetMethod("DoNothing"),
                new Action<Action<MultiHookTest>, MultiHookTest>((orig, self) => {
                    orig(self);
                    Counter *= 2;
                }),
                new HookConfig {
                    ManualApply = true
                }
            )) {
                Counter = 1;
                DoNothing();
                Assert.Equal(1, Counter);

                hIL.Apply();
                h1.Apply();
                Counter = 1;
                DoNothing();
                Assert.Equal((1 * 3) + 2, Counter);
                h1.Undo();
                hIL.Undo();

                h2.Apply();
                hIL.Apply();
                Counter = 1;
                DoNothing();
                Assert.Equal((1 * 3) * 2, Counter);
                hIL.Undo();
                h2.Undo();

                h1.Apply();
                hIL.Apply();
                h2.Apply();
                Counter = 1;
                DoNothing();
                Assert.Equal(((1 * 3) + 2) * 2, Counter);
                h2.Undo();
                hIL.Undo();
                h1.Undo();

                Counter = 1;
                DoNothing();
                Assert.Equal(1, Counter);
            }

            Counter = 1;
            DoNothing();
            Assert.Equal(1, Counter);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DoNothing() {
        }
        
    }
}
