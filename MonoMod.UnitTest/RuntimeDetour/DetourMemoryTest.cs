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
                            typeof(DetourMemoryTest).GetMethod("TestStaticMethod"),
                            typeof(DetourMemoryTest).GetMethod("TestStaticMethodHook")
                        );
                        hooks.Add(h);
                        int staticResult = TestStaticMethod(2, 3).Count;
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


        public static Counter<int> TestStaticMethod(int a, int b) {
            TestObjectGeneric<string> test = new TestObjectGeneric<string>();
            try {
                Console.WriteLine("TEST");

                a *= new int?(b).Value;

                Console.WriteLine(new List<TestObjectGeneric<TestObject>>() { new TestObjectGeneric<TestObject>() }.GetEnumerator().Current);

                List<string> list = new List<string>();
                list.AddRange(new string[] { "A", "B", "C" });

                string[][] array2d1 = new string[][] { new string[] { "A" } };
                string[,] array2d2 = new string[,] { { "B" } };
                foreach (string str in list) {
                    TargetTest(array2d1[0][0], array2d2[0, 0], str);
                }

            } catch (Exception e) when (e == null) {
                return new Counter<int> { Count = -2 };
            } catch (Exception) {
                return new Counter<int> { Count = -1 };
            }
            return new Counter<int> { Count = a };
        }

        public static Counter<int> TestStaticMethodHook(Func<int, int, Counter<int>> orig, int a, int b) {
            Counter<int> c = orig(a, b);
            c.Count++;
            return c;
        }

        public static int TargetTest<T>(string a, string b, string c) {
            return (a + b + c).GetHashCode();
        }

        public static int TargetTest(string a, string b, string c) {
            return (a + b + c).GetHashCode();
        }

        public struct Counter<T> where T : struct {
            public T Count;
        }

    }
}
