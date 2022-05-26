#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;

using Xunit;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Reflection.Emit;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class DetourRedoTest {
        [Fact(Skip = "Not reimplemented for new RuntimeDetour")]
        public void TestDetoursRedo() {
            lock (TestObject.Lock) {
                // The following use cases are not meant to be usage examples.
                // Please take a look at DetourTest and HookTest instead.

                // TODO: rewrite this test completely for new RuntimeDetour
#if false
                // Uncomment the following line when you want to run this test isolated and make sure that pins aren't being leaked.
                DetourRuntimeILPlatform runtimeIL = null; // DetourHelper.Runtime as DetourRuntimeILPlatform;

                DetourRuntimeILPlatform.MethodPinInfo[] pinnedPrev = null;
                if (runtimeIL != null)
                    pinnedPrev = runtimeIL.GetPins();

                Step(new NativeDetour(
                    typeof(TestObject).GetMethod("TestStaticMethod"),
                    typeof(DetourRedoTest).GetMethod("TestStaticMethod_A")
                ));

                Step(new Detour(
                    typeof(TestObject).GetMethod("TestStaticMethod"),
                    typeof(DetourRedoTest).GetMethod("TestStaticMethod_A")
                ));

                Step(new Hook(
                    typeof(TestObject).GetMethod("TestStaticMethod"),
                    typeof(DetourRedoTest).GetMethod("TestStaticMethod_A")
                ));

                if (runtimeIL != null) {
                    DetourRuntimeILPlatform.MethodPinInfo[] pinned = runtimeIL.GetPins();
                    Assert.Equal(pinnedPrev.Length, pinned.Length);
                    for (int i = 0; i < pinned.Length; i++) {
                        DetourRuntimeILPlatform.MethodPinInfo pinPrev = pinnedPrev[i];
                        DetourRuntimeILPlatform.MethodPinInfo pin = pinned[i];
                        Assert.Equal(pinPrev.Handle.Value, pin.Handle.Value);
                        Assert.Equal(pinPrev.Count, pin.Count);
                    }
                }

                void Step(IDetour d) {
                    using (d) {
                        Assert.True(d.IsValid);
                        Assert.True(d.IsApplied);

                        int staticResult = TestObject.TestStaticMethod(2, 3);
                        Assert.Equal(12, staticResult);

                        d.Undo();
                        Assert.True(d.IsValid);
                        Assert.False(d.IsApplied);

                        staticResult = TestObject.TestStaticMethod(2, 3);
                        Assert.Equal(6, staticResult);

                        d.Apply();
                        Assert.True(d.IsValid);
                        Assert.True(d.IsApplied);

                        staticResult = TestObject.TestStaticMethod(2, 3);
                        Assert.Equal(12, staticResult);
                    }

                    Assert.False(d.IsValid);
                    Assert.False(d.IsApplied);
                }
#endif
            }
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

    }
}
