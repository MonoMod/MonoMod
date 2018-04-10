using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.RuntimeDetour {
    internal class DetourExample {
        // Prevent JIT from inlining the method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TestMethod(int a, int b) {
            return a + b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TestStaticMethod(int a, int b) {
            return a * b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TestVoidMethod(int a, int b) {
            Console.WriteLine($"{a} {b} {TestStaticMethod(a, b)}");
            if (a > 0) {
                TestVoidMethod(a - 1, b - 1);
            }
        }

        public static void Test() {
            DetourExample instance = new DetourExample();
            Console.WriteLine($"instance.TestMethod(2, 3): {instance.TestMethod(2, 3)}");
            Console.WriteLine($"TestStaticMethod(2, 3): {TestStaticMethod(2, 3)}");
            Console.WriteLine("TestVoidMethod(2, 3):");
            TestVoidMethod(2, 3);
        }

        public static void Run() {
            Console.WriteLine("DetourExample");
#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

            Console.WriteLine("Detours: none");
            Test();
            Console.WriteLine();


            // Three examples of using Detour.
            // Note that if you need non-layered, low-level hooks, you can use NativeDetour instead.
            // This is also why the variable type is IDetour.
            // System.Linq.Expressions, thanks to leo (HookedMethod) for telling me about how to (ab)use MethodCallExpression!
            IDetour detourTestMethodA = new Detour(
                () => default(DetourExample).TestMethod(default(int), default(int)),
                () => TestMethod_A(default(DetourExample), default(int), default(int))
            );
            // Method references as delegates.
            IDetour detourTestStaticMethodA = new Detour<Func<int, int, int>>(
                DetourExample.TestStaticMethod,
                DetourExample.TestStaticMethod_A
            );
            // MethodBase, old syntax.
            IDetour detourTestVoidMethodA = new Detour(
                typeof(DetourExample).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                typeof(DetourExample).GetMethod("TestVoidMethod_A", BindingFlags.Static | BindingFlags.Public)
            );
            Console.WriteLine("Detours: A");
            Test();
            Console.WriteLine("Testing trampoline, should invoke orig, TestVoidMethod(2, 3)");
            detourTestVoidMethodA.GenerateTrampoline<Action<int, int>>()(2, 3);
            Console.WriteLine();

            IDetour detourTestMethodB = new Detour(
                () => default(DetourExample).TestMethod(default(int), default(int)),
                () => TestMethod_B(default(DetourExample), default(int), default(int))
            );
            IDetour detourTestStaticMethodB = new Detour(
                 () => TestStaticMethod(default(int), default(int)),
                 () => TestStaticMethod_B(default(int), default(int))
            );
            IDetour detourTestVoidMethodB = new Detour(
                 () => TestVoidMethod(default(int), default(int)),
                 () => TestVoidMethod_B(default(int), default(int))
            );
            Console.WriteLine("Detours: A + B");
            Test();
            Console.WriteLine("Testing trampoline, should invoke hook A, TestVoidMethod(2, 3)");
            Action<int, int> trampolineTestVoidMethodB = detourTestVoidMethodB.GenerateTrampoline<Action<int, int>>();
            trampolineTestVoidMethodB(2, 3);
            Console.WriteLine();

            detourTestMethodA.Undo();
            detourTestStaticMethodA.Undo();
            detourTestVoidMethodA.Undo();
            Console.WriteLine("Detours: B");
            Test();
            Console.WriteLine("Testing trampoline, should invoke orig, TestVoidMethod(2, 3)");
            trampolineTestVoidMethodB(2, 3);
            Console.WriteLine();

            detourTestMethodB.Undo();
            detourTestStaticMethodB.Undo();
            detourTestVoidMethodB.Undo();
            Console.WriteLine("Detours: none");
            Test();
            Console.WriteLine();
        }

        public static int TestMethod_A(DetourExample self, int a, int b) {
            return 42;
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

        public static void TestVoidMethod_A(int a, int b) {
            Console.WriteLine("Hook A");
        }

        public static int TestMethod_B(DetourExample self, int a, int b) {
            return 120;
        }

        public static int TestStaticMethod_B(int a, int b) {
            return a * b + 2;
        }

        public static void TestVoidMethod_B(int a, int b) {
            Console.WriteLine("Hook B");
        }
    }
}
