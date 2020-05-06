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
using System.IO;
using System.Diagnostics;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class DetourExtTest {
        [Fact]
        public void TestDetoursExt() {
            lock (TestObject.Lock) {
                // The following use cases are not meant to be usage examples.
                // Please take a look at DetourTest and HookTest instead.

#if true
                using (NativeDetour d = new NativeDetour(
                    // .GetNativeStart() to enforce a native detour.
                    typeof(TestObject).GetMethod("TestStaticMethod").Pin().GetNativeStart(),
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

                    // FIXME: dm.Invoke can fail with a release build in mono 5.X!
                    // staticResult = (int) dm.Invoke(null, new object[] { 2, 3 });
                    staticResult = ((Func<int, int, int>) dm.CreateDelegate<Func<int, int, int>>())(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(12, staticResult);
                }
#endif

                // This was provided by Chicken Bones (tModLoader).
                // GetEncoding behaves differently on .NET Core and even between .NET Framework versions,
                // which is why this test only applies to Mono, preferably on Linux to verify if flagging
                // regions of code as read-writable and then read-executable works for AOT'd code.
#if false
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
#endif

                // This was provided by a Harmony user.
                // TextWriter's methods (including all overrides) were unable to be hooked on some runtimes.
#if true
                // FIXME: .NET 5 introduces similar behavior for macOS and Linux, but RD isn't ready for that. See DetourRuntimeNETPlatform for more info.
                if (PlatformHelper.Is(Platform.Windows)) {

                    using (MemoryStream ms = new MemoryStream()) {

                        using (StreamWriter writer = new StreamWriter(ms, Encoding.UTF8, 1024, true)) {
                            // In case anyone needs to debug this mess anytime in the future ever again:
                            /*
                            MethodBase m = typeof(StreamWriter).GetMethod("Write", new Type[] { typeof(string) });
                            Console.WriteLine($"meth: 0x{(long) m?.MethodHandle.Value:X16}");
                            Console.WriteLine($"getf: 0x{(long) m?.MethodHandle.GetFunctionPointer():X16}");
                            Console.WriteLine($"fptr: 0x{(long) m?.GetLdftnPointer():X16}");
                            Console.WriteLine($"nats: 0x{(long) m?.GetNativeStart():X16}");
                            */

                            // Debugger.Break();
                            writer.Write("A");

                            using (Hook h = new Hook(
                                typeof(StreamWriter).GetMethod("Write", new Type[] { typeof(string) }),
                                new Action<Action<StreamWriter, string>, StreamWriter, string>((orig, self, value) => {
                                    orig(self, "-");
                                })
                            )) {
                                // Debugger.Break();
                                writer.Write("B");
                            }

                            writer.Write("C");
                        }

                        ms.Seek(0, SeekOrigin.Begin);

                        using (StreamReader reader = new StreamReader(ms, Encoding.UTF8, false, 1024, true)) {
                            Assert.Equal("A-C", reader.ReadToEnd());
                        }

                    }
                }
#endif
            }
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

    }
}
