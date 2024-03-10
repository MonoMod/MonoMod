#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class HookTest : TestBase
    {
        public HookTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestHooks()
        {
            lock (TestObject.Lock)
            {
                Console.WriteLine("Hooks: none");
                TestObject.TestStep(5, 6, 8);
                Console.WriteLine();

                // Note: You only need GetTypeInfo() if you target .NET Standard 1.6

                DetourManager.DetourApplied += DetourManager_DetourApplied;
                DetourManager.DetourUndone += DetourManager_DetourUndone;

                try
                {
                    using var hookTestMethodA = new Hook(
                        typeof(TestObject).GetMethod("TestMethod", BindingFlags.Instance | BindingFlags.Public),
                        typeof(HookTest).GetMethod("TestMethod_A", BindingFlags.Static | BindingFlags.NonPublic)
                    );
                    using var hookTestStaticMethodA = new Hook(
                        typeof(TestObject).GetMethod("TestStaticMethod", BindingFlags.Static | BindingFlags.Public),
                        typeof(HookTest).GetMethod("TestStaticMethod_A", BindingFlags.Static | BindingFlags.NonPublic)
                    );
                    using var hookTestVoidMethodA = new Hook(
                        typeof(TestObject).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                        typeof(HookTest).GetMethod("TestVoidMethod_A", BindingFlags.Static | BindingFlags.NonPublic)
                    );
                    Console.WriteLine("Hooks: A");
                    TestObject.TestStep(42, 12, 1);
                    Console.WriteLine();

                    using var hookTestMethodB = new Hook(
                        typeof(TestObject).GetMethod("TestMethod", BindingFlags.Instance | BindingFlags.Public),
                        typeof(HookTest).GetMethod("TestMethod_B", BindingFlags.Static | BindingFlags.NonPublic)
                    );
                    using var hookTestStaticMethodB = new Hook(
                        typeof(TestObject).GetMethod("TestStaticMethod", BindingFlags.Static | BindingFlags.Public),
                        new Func<Func<int, int, int>, int, int, int>((orig, a, b) =>
                        {
                            return orig(a, b) + 2;
                        })
                    );
                    using var hookTestVoidMethodB = new Hook(
                        typeof(TestObject).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                        new Action<Action<int, int>, int, int>((orig, a, b) =>
                        {
                            Console.WriteLine("Hook B");
                            TestObject.VoidResult += 2;
                            orig(a, b);
                        })
                    );
                    Console.WriteLine("Hooks: A + B");
                    TestObject.TestStep(42 + 42, 12 + 2, 3);
                    Console.WriteLine();

                    hookTestMethodA.Undo();
                    hookTestStaticMethodA.Undo();
                    hookTestVoidMethodA.Undo();
                    Console.WriteLine("Hooks: B");
                    TestObject.TestStep(5 + 42, 6 + 2, 12);
                    Console.WriteLine();

                    hookTestMethodB.Undo();
                    hookTestStaticMethodB.Undo();
                    hookTestVoidMethodB.Undo();
                    Console.WriteLine("Detours: none");
                    TestObject.TestStep(5, 6, 8);
                    Console.WriteLine();
                }
                finally
                {
                    DetourManager.DetourApplied -= DetourManager_DetourApplied;
                    DetourManager.DetourUndone -= DetourManager_DetourUndone;
                }
            }
        }

        private void DetourManager_DetourUndone(DetourInfo obj)
        {

        }

        private void DetourManager_DetourApplied(DetourInfo obj)
        {

        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int TestMethod_A(TestObject self, int a, int b)
        {
            return 42;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int TestStaticMethod_A(int a, int b)
        {
            return a * b * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void TestVoidMethod_A(int a, int b)
        {
            Console.WriteLine("Hook A");
            TestObject.VoidResult += 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int TestMethod_B(Func<TestObject, int, int, int> orig, TestObject self, int a, int b)
        {
            return orig(self, a, b) + 42;
        }

    }
}
