using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class PrimitiveReturnDetourTest {
        [Fact]
        public void TestPrimitiveReturnDetour() {
            // Note: this test is mainly relevant for x86, but should pass in x64 as well

            // Test that detouring function with a primitive sizeof > IntPtr.Size does not generate a thisptr return buffer handler
            MethodInfo from = typeof(Foo).GetMethod(nameof(Foo.TestMethod));
            MethodInfo to = typeof(PrimitiveReturnDetourTest).GetMethod(nameof(TestMethodDetour));
            long res = new Foo().TestMethod(10, 10);
            Assert.Equal(100010, res);

            NativeDetour det = new NativeDetour(from, to);
            det.Apply();
            res = new Foo().TestMethod(10, 10);
            Assert.Equal(-1, res);

            // Test that detouring function with sizeof > IntPtr.Size generates a thisptr return buffer handler
            MethodInfo from2 = typeof(Foo).GetMethod(nameof(Foo.TestMethod2));
            MethodInfo to2 = typeof(PrimitiveReturnDetourTest).GetMethod(nameof(TestMethodDetour2));
            Baz res2 = new Foo().TestMethod2(10, 10);
            Assert.Equal(100010, res2.baz);

            NativeDetour det2 = new NativeDetour(from2, to2);
            det2.Apply();
            res2 = new Foo().TestMethod2(10, 10);
            Assert.Equal(-1, res2.baz);
        }

        public static long TestMethodDetour(Foo @this, int a, int b) {
            if (a != b)
                throw new Exception($"return long: a != b: a = {a}; b = {b}");
            return -1;
        }

        public static Baz TestMethodDetour2(Foo @this, int a, int b) {
            if (a != b)
                throw new Exception($"return struct (sizeof == 8): a != b: a = {a}; b = {b}");
            return new Baz {baz = -1};
        }

        public class Foo {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public long TestMethod(int a, int b) {
                return a + b * 10000L;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public Baz TestMethod2(int a, int b) {
                return new Baz {baz = a + b * 10000L};
            }
        }

        public struct Baz {
            public long baz;
        }
    }
}