using Xunit;
using MonoMod.ModInterop;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

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

            Assert.Equal("ABC", new DynamicData(dummy).Get<string>("New"));
            Assert.Equal(8, new DynamicData(dummy).Invoke<int>("NewMethod", 4, 2));

            new DynamicData(dummy) {
                { "Hello", "World!" }
            };
            Assert.Equal("World!", new DynamicData(dummy).Get<string>("Hello"));

            Assert.Equal(dummy, DynamicData.Set(dummy, new {
                A = 10,
                Other = "New"
            }));
            Assert.Equal(10, dummy.A);
            Assert.Equal("New", data.Other);

            data.CopyFrom(new {
                A = 20,
                Other = "Newer"
            });
            Assert.Equal(20, dummy.A);
            Assert.Equal("Newer", data.Other);

            dummy = DynamicData.New<Dummy>()(new {
                A = 30,
                B = 60L,
                C = "90",
                Other = "Newest"
            });
            Assert.Equal(30, dummy.A);
            Assert.Equal(60L, dummy._B);
            Assert.Equal("90", dummy._C);
            Assert.Equal("Newest", new DynamicData(dummy).Get<string>("Other"));

            Dummy dummyTo = new Dummy();
            Assert.Equal(69, dummyTo.A);
            Assert.Equal(420L, dummyTo._B);
            Assert.Equal("XYZ", dummyTo._C);

            DynamicData dataTo = new DynamicData(dummyTo);
            foreach (KeyValuePair<string, object> kvp in new DynamicData(dummy))
                dataTo.Set(kvp.Key, kvp.Value);
            Assert.Equal(30, dummyTo.A);
            Assert.Equal(60L, dummyTo._B);
            Assert.Equal("90", dummyTo._C);
            Assert.Equal("Newest", new DynamicData(dummyTo).Get<string>("Other"));
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
