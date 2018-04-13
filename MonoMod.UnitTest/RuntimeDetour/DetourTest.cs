#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

using MonoMod.RuntimeDetour;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public static class DetourTest {
        [Test]
        public static void TestDetours() {
            Console.WriteLine("Detours: none");
            DetourExample.TestStep(5, 6);
            Console.WriteLine();

            // Three examples of using Detour.
            // Note that if you need non-layered, low-level hooks, you can use NativeDetour instead.
            // This is also why the variable type is IDetour.
            // System.Linq.Expressions, thanks to leo (HookedMethod) for telling me about how to (ab)use MethodCallExpression!
            IDetour detourTestMethodA = new RuntimeDetour.Detour(
                () => default(DetourExample).TestMethod(default(int), default(int)),
                () => TestMethod_A(default(DetourExample), default(int), default(int))
            );
            // Method references as delegates.
            IDetour detourTestStaticMethodA = new Detour<Func<int, int, int>>(
                DetourExample.TestStaticMethod,
                TestStaticMethod_A
            );
            // MethodBase, old syntax.
            IDetour detourTestVoidMethodA = new RuntimeDetour.Detour(
                typeof(DetourExample).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                typeof(DetourTest).GetMethod("TestVoidMethod_A", BindingFlags.Static | BindingFlags.Public)
            );
            Console.WriteLine("Detours: A");
            DetourExample.TestStep(42, 12);
            Console.WriteLine("Testing trampoline, should invoke orig, TestVoidMethod(2, 3)");
            detourTestVoidMethodA.GenerateTrampoline<Action<int, int>>()(2, 3);
            Console.WriteLine();

            IDetour detourTestMethodB = new RuntimeDetour.Detour(
                () => default(DetourExample).TestMethod(default(int), default(int)),
                () => TestMethod_B(default(DetourExample), default(int), default(int))
            );
            IDetour detourTestStaticMethodB = new RuntimeDetour.Detour(
                 () => DetourExample.TestStaticMethod(default(int), default(int)),
                 () => TestStaticMethod_B(default(int), default(int))
            );
            IDetour detourTestVoidMethodB = new RuntimeDetour.Detour(
                 () => DetourExample.TestVoidMethod(default(int), default(int)),
                 () => TestVoidMethod_B(default(int), default(int))
            );
            Console.WriteLine("Detours: A + B");
            DetourExample.TestStep(120, 8);
            Console.WriteLine("Testing trampoline, should invoke hook A, TestVoidMethod(2, 3)");
            Action<int, int> trampolineTestVoidMethodB = detourTestVoidMethodB.GenerateTrampoline<Action<int, int>>();
            trampolineTestVoidMethodB(2, 3);
            Console.WriteLine();

            detourTestMethodA.Undo();
            detourTestStaticMethodA.Undo();
            detourTestVoidMethodA.Undo();
            Console.WriteLine("Detours: B");
            DetourExample.TestStep(120, 8);
            Console.WriteLine("Testing trampoline, should invoke orig, TestVoidMethod(2, 3)");
            trampolineTestVoidMethodB(2, 3);
            Console.WriteLine();

            detourTestMethodB.Undo();
            detourTestStaticMethodB.Undo();
            detourTestVoidMethodB.Undo();
            Console.WriteLine("Detours: none");
            DetourExample.TestStep(5, 6);
            Console.WriteLine();
        }

        public static int TestMethod_A(DetourExample self, int a, int b) {
            return 42;
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

        public static void TestVoidMethod_A(int a, int b) {
            Console.WriteLine("Detour A");
        }

        public static int TestMethod_B(DetourExample self, int a, int b) {
            return 120;
        }

        public static int TestStaticMethod_B(int a, int b) {
            return a * b + 2;
        }

        public static void TestVoidMethod_B(int a, int b) {
            Console.WriteLine("Detour B");
        }
    }
}
