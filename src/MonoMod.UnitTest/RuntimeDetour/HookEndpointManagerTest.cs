#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

#if false // TODO: again, figure out where HookEndpointManager should end up

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Text;
using MonoMod.RuntimeDetour.HookGen;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class HookEndpointManagerTest {
        private bool DidNothing = true;

        private static readonly MethodInfo m_DoNothing = typeof(HookEndpointManagerTest).GetMethod("DoNothing");
        private static event Action<Action<HookEndpointManagerTest>, HookEndpointManagerTest> OnDoNothing {
            add {
                HookEndpointManager.Add(m_DoNothing, value);
            }
            remove {
                HookEndpointManager.Remove(m_DoNothing, value);
            }
        }

        private static event ILContext.Manipulator ILDoNothing {
            add {
                HookEndpointManager.Modify(m_DoNothing, value);
            }
            remove {
                HookEndpointManager.Unmodify(m_DoNothing, value);
            }
        }

        // TODO: re-enable when new RuntimeDetour supports HookEndpointManager
        [Fact(Skip = "New RuntimeDetour doesn't yet support HookEndpointManager")]
        public void TestHookEndpointManager() {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);

            OnDoNothing += DoNothingHook;
            DidNothing = true;
            DoNothing();
            Assert.False(DidNothing);
            OnDoNothing -= DoNothingHook;

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);

            ILDoNothing += DoNothingILHook;
            DidNothing = true;
            DoNothing();
            Assert.False(DidNothing);
            ILDoNothing -= DoNothingILHook;

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DoNothing() {
        }

        private static void DoNothingHook(Action<HookEndpointManagerTest> orig, HookEndpointManagerTest test) {
            test.DidNothing = false;
        }

        private static void DoNothingILHook(ILContext il) {
            ILCursor c = new ILCursor(il);
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldc_I4_0);
            c.Emit(OpCodes.Stfld, typeof(ILHookTest).GetField("DidNothing", BindingFlags.NonPublic | BindingFlags.Instance));
        }

    }
}
#endif