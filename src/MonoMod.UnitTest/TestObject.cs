#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

using Xunit;
using System;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public class TestObject {
        public static readonly object Lock = new object();

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
            TestObject instance = new TestObject();
            int instanceResult = instance.TestMethod(2, 3);
            Console.WriteLine($"instance.TestMethod(2, 3): {instanceResult}");
            Assert.Equal(instanceExpected, instanceResult);

            int staticResult = TestStaticMethod(2, 3);
            Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
            Assert.Equal(staticExpected, staticResult);

            Console.WriteLine("TestVoidMethod(2, 3):");
            VoidResult = 0;
            TestVoidMethod(2, 3);
            Assert.Equal(voidExpected, VoidResult);
        }
    }

    public class TestObjectGeneric<T> {
    }

    public abstract class TestObjectGeneric<T, TSelf> where TSelf : TestObjectGeneric<T, TSelf> {
        public T Data;
        public static implicit operator T(TestObjectGeneric<T, TSelf> obj) {
            if (obj == default)
                return default;
            return obj.Data;
        }
    }
    public class TestObjectInheritsGeneric : TestObjectGeneric<int, TestObjectInheritsGeneric> {
    }
}
