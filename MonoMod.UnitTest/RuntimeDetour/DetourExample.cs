#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

using MonoMod.RuntimeDetour;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public class DetourExample {
        public static int VoidResult;

        // Prevent JIT from inlining the method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TestMethod(int a, int b = 3) {
            return a + b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TestStaticMethod(int a, int b = 3) {
            return a * b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TestVoidMethod(int a, int b = 3) {
            Console.WriteLine($"{a} {b} {TestStaticMethod(a, b)}");
            VoidResult += a * b;
            if (a > 1 && b > 1) {
                TestVoidMethod(a - 1, b - 1);
            }
        }

        public static void TestStep(int instanceExpected, int staticExpected, int voidExpected) {
            DetourExample instance = new DetourExample();
            int instanceResult = instance.TestMethod(2, 3);
            Console.WriteLine($"instance.TestMethod(2, 3): {instanceResult}");
            Assert.AreEqual(instanceExpected, instanceResult);

            int staticResult = TestStaticMethod(2, 3);
            Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
            Assert.AreEqual(staticExpected, staticResult);

            Console.WriteLine("TestVoidMethod(2, 3):");
            VoidResult = 0;
            TestVoidMethod(2, 3);
            Assert.AreEqual(voidExpected, VoidResult);
        }
    }
}
