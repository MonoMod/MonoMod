#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using MonoMod.Core;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class DetourTest : TestBase
    {
        public DetourTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDetours()
        {
            lock (TestObject.Lock)
            {
                Console.WriteLine("Detours: none");
                TestObject.TestStep(5, 6, 8);
                Console.WriteLine();

                // Three examples of using Detour.
                // This is also why the variable type is IDetour.
                // System.Linq.Expressions, thanks to leo (HookedMethod) for telling me about how to (ab)use MethodCallExpression!
                using var detourTestMethodA = new Hook(
                    () => default(TestObject).TestMethod(default, default),
                    () => TestMethod_A(default, default, default)
                );

                using var detourTestStaticMethodA = new Hook(
                    () => TestObject.TestStaticMethod(default, default),
                    () => TestStaticMethod_A(default, default)
                );
                //Console.ReadLine();

                // MethodBase, old syntax.
                // Note: You only need GetTypeInfo() if you target .NET Standard 1.6
                using var detourTestVoidMethodA = new Hook(
                    typeof(TestObject).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                    typeof(DetourTest).GetMethod("TestVoidMethod_A", BindingFlags.Static | BindingFlags.NonPublic)
                );
                Console.WriteLine("Detours: A");
                TestObject.TestStep(42, 12, 1);
                //Console.WriteLine("Testing trampoline, should invoke orig, TestVoidMethod(2, 3)");
                //detourTestVoidMethodA.GenerateTrampoline<Action<int, int>>()(2, 3);
                Console.WriteLine();

                using var detourTestMethodB = new Hook(
                    () => default(TestObject).TestMethod(default, default),
                    () => TestMethod_B(default, default, default)
                );
                using var detourTestStaticMethodB = new Hook(
                    () => TestObject.TestStaticMethod(default, default),
                    () => TestStaticMethod_B(default, default)
                );
                using var detourTestVoidMethodB = new Hook(
                    () => TestObject.TestVoidMethod(default, default),
                    () => TestVoidMethod_B(default, default)
                );
                Console.WriteLine("Detours: A + B");
                TestObject.TestStep(120, 8, 2);
                //Console.WriteLine("Testing trampoline, should invoke hook A, TestVoidMethod(2, 3)");
                //Action<int, int> trampolineTestVoidMethodB = detourTestVoidMethodB.GenerateTrampoline<Action<int, int>>();
                //trampolineTestVoidMethodB(2, 3);
                Console.WriteLine();

                detourTestMethodA.Undo();
                detourTestStaticMethodA.Undo();
                detourTestVoidMethodA.Undo();
                Console.WriteLine("Detours: B");
                TestObject.TestStep(120, 8, 2);
                //Console.WriteLine("Testing trampoline, should invoke orig, TestVoidMethod(2, 3)");
                //trampolineTestVoidMethodB(2, 3);
                Console.WriteLine();

                detourTestMethodB.Undo();
                detourTestStaticMethodB.Undo();
                detourTestVoidMethodB.Undo();
                Console.WriteLine("Detours: none");
                TestObject.TestStep(5, 6, 8);
                Console.WriteLine();
            }
        }

        [Fact]
        public void Test_GenericClassDetour()
        {
            var originalMethod1 = typeof(AbstractGenericTestObject<double, DoubleTestItem>).GetMethod("GetDouble");
            Assert.NotNull(originalMethod1);

            var replacement1 = typeof(TestObjectPatch).GetMethod("TestDetour1");
            Assert.NotNull(replacement1);

            var obj1 = new TestObjectDouble();
            var obj2 = new TestObjectString();
            Assert.Equal(1, obj1.GetDouble());
            Assert.Equal(1, obj2.GetDouble());

            var detour1 = DetourFactory.Current.CreateDetour(originalMethod1, replacement1);

            Assert.Equal(999, obj1.GetDouble());
            Assert.Equal(1, obj2.GetDouble());

            var originalMethod2 = typeof(AbstractGenericTestObject<string, StringTestItem>).GetMethod("GetDouble");
            Assert.NotNull(originalMethod2);

            var replacement2 = typeof(TestObjectPatch).GetMethod("TestDetour2");
            Assert.NotNull(replacement2);

            var detour2 = DetourFactory.Current.CreateDetour(originalMethod2, replacement2);
            Assert.Equal(888, obj2.GetDouble());
            Assert.Equal(999, obj1.GetDouble());

            detour1.Undo();
            detour1.Dispose();
            Assert.Equal(1, obj1.GetDouble());
            Assert.Equal(888, obj2.GetDouble());

            detour2.Undo();
            detour2.Dispose();
            Assert.Equal(1, obj1.GetDouble());
            Assert.Equal(1, obj2.GetDouble());
        }

        [Fact]
        public void Test_GenericClassDetourWithoutGenericParameter()
        {
            var originalMethod = typeof(TestSingleGenericObject<>).GetMethod("GetDouble");
            Assert.NotNull(originalMethod);

            var replacement = typeof(TestObjectPatch).GetMethod("TestDetour3");
            Assert.NotNull(replacement);

            var instance = new TestSingleGenericObject<string>();
            Assert.Equal(1, instance.GetDouble());

            // Creating a detour on an open generic type definition (TestSingleGenericObject<>)
            // throws because methods on unbound generic types cannot be JIT compiled.
            // To detour a generic method, use a closed generic type like TestSingleGenericObject<string>.
            Assert.Throws<ArgumentException>(() =>
                DetourFactory.Current.CreateDetour(originalMethod, replacement));

            // Verify the instance still works normally
            Assert.Equal(1, instance.GetDouble());
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
            Console.WriteLine("Detour A");
            TestObject.VoidResult += 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int TestMethod_B(TestObject self, int a, int b)
        {
            return 120;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int TestStaticMethod_B(int a, int b)
        {
            return a * b + 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void TestVoidMethod_B(int a, int b)
        {
            Console.WriteLine("Detour B");
            TestObject.VoidResult += 2;
        }
    }
}