using Xunit;
using MonoMod.ModInterop;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public class DynamicDataTest {
        [Fact]
        public void TestDynamicData() {
            Dummy dummy = new Dummy();
            dynamic data = new DynamicData(dummy);

            Assert.Equal(dummy, (Dummy) data);

            Assert.Equal(69, data.A);
            Assert.Equal(420L, data.B);
            Assert.Equal("XYZ", data.C);
            Assert.Null(data.New);

            data.A = 123;
            data.B = 456L;
            data.C = "789";
            data.New = "ABC";
            Assert.Equal(123, dummy.A);
            Assert.Equal(456L, dummy._B);
            Assert.Equal("789", dummy._C);
            Assert.Equal("ABC", data.New);

            data.RegisterMethod("NewMethod", new Func<object, object[], object>((target, args) => (int) args[0] * (int) args[1]));
            Assert.Equal(6, data.PublicMethod(4, 2));
            Assert.Equal(2, data.PrivateMethod(4, 2));
            Assert.Equal(8, data.NewMethod(4, 2));
        }

        public class Dummy {

            public int A = 69;
            private long B = 420L;
            protected string C { get; set; } = "XYZ";

            public long _B => B;
            public string _C => C;

            public int PublicMethod(int a, int b) => a + b;
            private int PrivateMethod(int a, int b) => a - b;

        }

    }
}
