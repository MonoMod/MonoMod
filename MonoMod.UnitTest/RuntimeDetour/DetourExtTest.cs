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
    public class DetourExtTest {
        [Fact]
        public void TestDetours() {
            lock (TestObject.Lock) {
                // The following use cases are not meant to be usage examples.
                // Please take a look at DetourTest and HookTest instead.

                using (NativeDetour d = new NativeDetour(
                // .GetNativeStart() to enforce a native detour.
                typeof(TestObject).GetMethod("TestStaticMethod").GetNativeStart(),
                typeof(DetourExtTest).GetMethod("TestStaticMethod_A")
            )) {
                    int staticResult = d.GenerateTrampoline<Func<int, int, int>>()(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(6, staticResult);

                    staticResult = TestObject.TestStaticMethod(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(12, staticResult);
                }

                // We can't create a backup for this.
                MethodBase dm;
                using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(typeof(TestObject).GetMethod("TestStaticMethod"))) {
                    dm = dmd.Generate();
                }
                using (NativeDetour d = new NativeDetour(
                    dm,
                    typeof(DetourExtTest).GetMethod("TestStaticMethod_A")
                )) {
                    int staticResult = d.GenerateTrampoline<Func<int, int, int>>()(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(6, staticResult);

                    staticResult = (int) dm.Invoke(null, new object[] { 2, 3 });
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(12, staticResult);
                }

                // This was provided by Chicken Bones (tModLoader).
                using (Hook h = new Hook(
                    typeof(Encoding).GetMethod("GetEncoding", new Type[] { typeof(string) }),
                    new Func<Func<string, Encoding>, string, Encoding>((orig, name) => {
                        if (name == "IBM437")
                            return null;
                        return orig(name);
                    })
                )) {
                    Assert.Null(Encoding.GetEncoding("IBM437"));
                }
            }
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

    }
}
