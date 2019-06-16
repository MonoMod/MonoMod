#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Reflection.Emit;
using System.Text;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class DetourRedoTest {
        [Fact]
        public void TestDetoursRedo() {
            lock (TestObject.Lock) {
                // The following use cases are not meant to be usage examples.
                // Please take a look at DetourTest and HookTest instead.

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
            }
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

    }
}
