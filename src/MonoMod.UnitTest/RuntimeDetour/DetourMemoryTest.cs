#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using MonoMod.Utils;
using New::MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class DetourMemoryTest : TestBase
    {
        public DetourMemoryTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDetourMemory()
        {
            if (PlatformDetection.Runtime is RuntimeKind.Mono)
            {
                // GC.GetTotalMemory likes to die on Mono:
                // * Assertion: should not be reached at sgen-scan-object.h:91
                // at System.GC:GetTotalMemory <0x00061>
                return;
            }

            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.
            var hooks = new HashSet<Hook>();

            var memPre = GC.GetTotalMemory(true);
            long memPost;

            try
            {

                Console.WriteLine($"GC.GetTotalMemory before detour memory test: {memPre}");
                for (var i = 0; i < 256; i++)
                {
                    var h = new Hook(
                        typeof(DetourMemoryTest).GetMethod("TestStaticMethod", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic),
                        typeof(DetourMemoryTest).GetMethod("TestStaticMethodHook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    );
                    hooks.Add(h);
                    var staticResult = TestStaticMethod(2, 3).Count;
                    Assert.Equal(6 + 1 + i, staticResult);
                }

                memPost = GC.GetTotalMemory(true);
                Console.WriteLine($"GC.GetTotalMemory after detour memory test: {memPost}");
                Console.WriteLine($"After - Before: {memPost - memPre}");

            }
            finally
            {
                foreach (var h in hooks)
                    h.Dispose();
                hooks.Clear();
            }

            GC.Collect();
            var memClear = GC.GetTotalMemory(true);

            Console.WriteLine($"GC.GetTotalMemory after detour memory test clear: {memClear}");
            Console.WriteLine($"Clear - Before: {memClear - memPre}");
        }

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable CA1031 // Do not catch general exception types
        internal static Counter<int> TestStaticMethod(int a, int b)
        {
            _ = new TestObjectGeneric<string>();
            try
            {
                a *= new int?(b).Value;

                b += new List<TestObjectGeneric<TestObject>>() { new TestObjectGeneric<TestObject>() }.GetEnumerator().Current?.GetHashCode() ?? 0;

                var list = new List<string>();
                list.AddRange(["A", "B", "C"]);

                var array2d1 = new string[][] { new string[] { "A" } };
                var array2d2 = new string[,] { { "B" } };
                foreach (var str in list)
                {
                    TargetTest(array2d1[0][0], array2d2[0, 0], str);
                }

            }
            catch (Exception e) when (e is null)
            {
                return new Counter<int> { Count = -2 };
            }
            catch (Exception)
            {
                return new Counter<int> { Count = -1 };
            }
            return new Counter<int> { Count = a };
        }
#pragma warning restore CA1031 // Do not catch general exception types
#pragma warning restore CA1508 // Avoid dead conditional code
#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional

        internal static Counter<int> TestStaticMethodHook(Func<int, int, Counter<int>> orig, int a, int b)
        {
            var c = orig(a, b);
            c.Count++;
            return c;
        }

        internal static int TargetTest<T>(string a, string b, string c)
        {
            return (a + b + c).GetHashCode(StringComparison.Ordinal);
        }

        internal static int TargetTest(string a, string b, string c)
        {
            return (a + b + c).GetHashCode(StringComparison.Ordinal);
        }

        internal struct Counter<T> where T : struct
        {
            public T Count;
        }

    }
}
