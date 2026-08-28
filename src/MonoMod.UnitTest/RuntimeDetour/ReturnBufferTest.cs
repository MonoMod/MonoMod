extern alias New;
using MonoMod.Core;
using New::MonoMod.RuntimeDetour;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;
using T = object;

namespace MonoMod.UnitTest
{
    public struct TestStruct
    {
        public ulong f1, f2, f3; // 24 bytes
    }

    [Collection("RuntimeDetour")]
    public class ReturnBufferSysVTest : TestBase
    {
        public ReturnBufferSysVTest(ITestOutputHelper helper) : base(helper) { }

        [Fact]
        public void TestReturnBufferDetour()
        {
            Assert.True(Source(0, 0, 0, 0) is { f1: 1, f2: 2, f3: 3 });

            using var hook = new Hook(
                typeof(ReturnBufferSysVTest).GetMethod("Source",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!,
                typeof(ReturnBufferSysVTest).GetMethod("Target",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!,
                true
            );

            Assert.True(Source(0, 0, 0, 0) is { f1: 4, f2: 5, f3: 6 });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal TestStruct Source(int a, int b, int c, int d)
        {
            return new TestStruct() { f1 = 1, f2 = 2, f3 = 3 };
        }

        internal static TestStruct Target(Func<ReturnBufferSysVTest, int, int, int, int, TestStruct> orig,
            ReturnBufferSysVTest self, int a, int b, int c, int d)
        {
            var s = orig(self, a, b, c, d);
            s.f1 += 3;
            s.f2 += 3;
            s.f3 += 3;
            return s;
        }
    }

    // TODO: after generic detour is done, make a generic version
    public class CountlessTest(ITestOutputHelper helper) : TestBase(helper)
    {
        // after generic detour is done, make them generic
        public int Method0() => 0;
        public int Method1(T a0) => 1;
        public int Method2(T a0, T a1) => 2;
        public int Method3(T a0, T a1, T a2) => 3;
        public int Method4(T a0, T a1, T a2, T a3) => 4;
        public int Method5(T a0, T a1, T a2, T a3, T a4) => 5;
        public int Method6(T a0, T a1, T a2, T a3, T a4, T a5) => 6;
        public int Method7(T a0, T a1, T a2, T a3, T a4, T a5, T a6) => 7;
        public int Method8(T a0, T a1, T a2, T a3, T a4, T a5, T a6, T a7) => 8;
        public int Method0Real() => 114514;
        public int Method1Real(object a0) => 114514;
        public int Method2Real(object a0, object a1) => 114514;
        public int Method3Real(object a0, object a1, object a2) => 114514;
        public int Method4Real(object a0, object a1, object a2, object a3) => 114514;
        public int Method5Real(object a0, object a1, object a2, object a3, object a4) => 114514;
        public int Method6Real(object a0, object a1, object a2, object a3, object a4, object a5) => 114514;
        public int Method7Real(object a0, object a1, object a2, object a3, object a4, object a5, object a6) => 114514;

        public int Method8Real(object a0, object a1, object a2, object a3, object a4, object a5, object a6,
            object a7) => 114514;

        [Fact]
        public void TestCountless()
        {
            var self = typeof(CountlessTest);
            for (var i = 0; i < 9; i++)
            {
                var sth = Enumerable.Repeat(this, i).ToArray();
                var from = self.GetMethod($"Method{i}"); //.MakeGenericMethod([typeof(object)]);
                var to = self.GetMethod($"Method{i}Real");

                Assert.Equal(i, from.Invoke(this, sth));
                using var _ = DetourFactory.Default.CreateDetour(new(from, to));
                Assert.Equal(114514, (int)from.Invoke(this, sth));
            }
        }
    }

    public class ThisIsAbiTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Method0() => 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Method1(T a0) => 1;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Method2(T a0, T a1) => 2;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Method3(T a0, T a1, T a2) => 3;

        public int Method0Real() => 114514;
        public static int Method1Real(object self, object a0) => 114514;
        public int Method2Real(object a1) => 114514;
        public static int Method3Real(object a0, object a1, object a2) => 114514;

        [Fact]
        public void TestOurAbi()
        {
            var self = typeof(ThisIsAbiTest);
            for (var i = 0; i < 4; i++)
            {
                var from = self.GetMethod($"Method{i}"); //.MakeGenericMethod([typeof(object)]);
                var to = self.GetMethod($"Method{i}Real");

                var sth = Enumerable.Repeat(this, from.GetParameters().Length).ToArray();
                var th = from.IsStatic ? null : this;

                Assert.Equal(i, from.Invoke(th, sth));
                using var _ = DetourFactory.Default.CreateDetour(new(from, to));
                Assert.Equal(114514, (int)from.Invoke(th, sth));
            }
        }

        static int OrderedHash(object self, object a0)
        {
            var b = self.GetHashCode() * 5 + a0.GetHashCode();
            return b;
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Method4(T a0) => Throw<int>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Method5(T a0) => Throw<int>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Method6(object self, T a0) => Throw<int>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Method7(object self, T a0) => Throw<int>();

        public int Method4Real(object a0) => OrderedHash(this, a0);
        public static int Method5Real(object self, object a0) => OrderedHash(self, a0);
        public int Method6Real(object a0) => OrderedHash(this, a0);
        public static int Method7Real(object self, object a0) => OrderedHash(self, a0);

        [Fact]
        public void TestAbiParams()
        {
            var self = typeof(ThisIsAbiTest);
            var hash = OrderedHash(this, self);
            object[] arr1 = [self];
            object[] arr2 = [this, self];
            for (var i = 4; i < 8; i++)
            {
                var from = self.GetMethod($"Method{i}"); //.MakeGenericMethod([typeof(object)]);
                var to = self.GetMethod($"Method{i}Real");

                var sth = from.IsStatic ? arr2 : arr1;
                var th = from.IsStatic ? null : this;

                using var _ = DetourFactory.Default.CreateDetour(new(from, to));
                Assert.Equal(hash, (int)from.Invoke(th, sth));
            }
        }

        static TestStruct OrderedHashLarge(object self, object a0)
        {
            var b = self.GetHashCode() * 5 + a0.GetHashCode();
            return new() { f1 = (ulong)b };
        }

        static T Throw<T>()
        {
            Assert.Fail("should be detoured");
            return default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public TestStruct Method8(T a0) => Throw<TestStruct>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public TestStruct Method9(T a0) => Throw<TestStruct>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TestStruct Method10(object self, T a0) => Throw<TestStruct>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TestStruct Method11(object self, T a0) => Throw<TestStruct>();

        public TestStruct Method8Real(object a0) => OrderedHashLarge(this, a0);
        public static TestStruct Method9Real(object self, object a0) => OrderedHashLarge(self, a0);
        public TestStruct Method10Real(object a0) => OrderedHashLarge(this, a0);
        public static TestStruct Method11Real(object self, object a0) => OrderedHashLarge(self, a0);

        [Fact]
        public void TestAbiParamsOnLargeObject()
        {
            var self = typeof(ThisIsAbiTest);
            var hash = OrderedHashLarge(this, self);
            object[] arr1 = [self];
            object[] arr2 = [this, self];
            for (var i = 8; i < 12; i++)
            {
                var from = self.GetMethod($"Method{i}"); //.MakeGenericMethod([typeof(object)]);
                var to = self.GetMethod($"Method{i}Real");

                var sth = from.IsStatic ? arr2 : arr1;
                var th = from.IsStatic ? null : this;

                using var _ = DetourFactory.Default.CreateDetour(new(from, to));
                Assert.Equal(hash, (TestStruct)from.Invoke(th, sth));
            }
        }
    }
}