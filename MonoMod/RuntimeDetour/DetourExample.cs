using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;
using System.Linq.Expressions;

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
            Console.WriteLine($"{a} {b}");
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

            // Three examples of using NativeDetour.
            // Note that you can't mix and match those.
            // System.Linq.Expressions, thanks to leo (HookedMethod) for telling me about how to (ab)use MethodCallExpression!
            NativeDetour detourTestMethodA = new NativeDetour(
                () => default(DetourExample).TestMethod(default(int), default(int)),
                () => TestMethod_A(default(DetourExample), default(int), default(int))
            );
            // Method references as delegates.
            NativeDetour detourTestStaticMethodA = new NativeDetour<Func<int, int, int>>(
                DetourExample.TestStaticMethod,
                DetourExample.TestStaticMethod_A
            );
            // MethodBase, old syntax.
            NativeDetour detourTestVoidMethodA = new NativeDetour(
                typeof(DetourExample).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                typeof(DetourExample).GetMethod("TestVoidMethod_A", BindingFlags.Static | BindingFlags.Public)
            );

            Console.WriteLine("Detours: A");
            Test();
            Console.WriteLine();

            // Hacky test for trampolines.
            detourTestVoidMethodA.GenerateTrampoline<Action<int, int>>()(1, 2);

            return;

            Console.WriteLine("Detours: A + B");
            Test();
            Console.WriteLine();

            Console.WriteLine("Detours: B");
            Test();
            Console.WriteLine();

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
    }
}
