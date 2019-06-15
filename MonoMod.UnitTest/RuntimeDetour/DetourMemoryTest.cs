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
using System.Collections.Generic;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class DetourMemoryTest {
        [Fact]
        public void TestDetourMemory() {
            lock (TestObject.Lock) {
                // The following use cases are not meant to be usage examples.
                // Please take a look at DetourTest and HookTest instead.
                HashSet<Hook> hooks = new HashSet<Hook>();

                long memPre = GC.GetTotalMemory(true);
                long memPost;

                try {

                    Console.WriteLine($"GC.GetTotalMemory before detour memory test: {memPre}");
                    for (int i = 0; i < 256; i++) {
                        Hook h = new Hook(
                            typeof(TestObject).GetMethod("TestStaticMethod"),
                            typeof(DetourMemoryTest).GetMethod("TestStaticMethod_A")
                        );
                        hooks.Add(h);
                        int staticResult = TestObject.TestStaticMethod(2, 3);
                        Assert.Equal(6 + 1 + i, staticResult);
                    }

                    memPost = GC.GetTotalMemory(true);
                    Console.WriteLine($"GC.GetTotalMemory after detour memory test: {memPost}");
                    Console.WriteLine($"After - Before: {memPost - memPre}");

                } finally {
                    foreach (Hook h in hooks)
                        h.Dispose();
                    hooks.Clear();
                }

                GC.Collect();
                long memClear = GC.GetTotalMemory(true);

                Console.WriteLine($"GC.GetTotalMemory after detour memory test clear: {memClear}");
                Console.WriteLine($"Clear - Before: {memClear - memPre}");
            }
        }

        public static int TestStaticMethod_A(Func<int, int, int> orig, int a, int b) {
            return orig(a, b) + 1;
        }

    }
}
