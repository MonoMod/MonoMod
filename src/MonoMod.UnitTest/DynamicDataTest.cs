using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public class DynamicDataTest : TestBase
    {
        public DynamicDataTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDynamicDataByrefsVt()
        {
            var dict = new Dictionary<string, int>();
            using dynamic data = new DynamicData(dict);

            Assert.Equal(dict, (Dictionary<string, int>)data);

            dict.Add("s", 5);
            Assert.True(dict.TryGetValue("s", out var addedVal1));
            Assert.Equal(5, addedVal1);

            data.TryGetValue("s", out int s2);

            object result = default(int);
            Assert.True(data.TryGetValue("s", result));
            Assert.Equal(5, result);
        }

        [Fact]
        public void TestDynamicDataByrefsNullVt()
        {
            var dict = new Dictionary<string, int?>();
            using dynamic data = new DynamicData(dict);

            Assert.Equal(dict, (Dictionary<string, int?>)data);

            dict.Add("s", 5);
            Assert.True(dict.TryGetValue("s", out var addedVal1));
            Assert.Equal(5, addedVal1);

            //data.TryGetValue("s", out int? s2);

            var result = new StrongBox<int?>(null);
            Assert.True(data.TryGetValue("s", result));
            Assert.Equal(5, result.Value);
        }


        [Fact]
        public void TestDynamicDataByrefsRef()
        {
            var dict = new Dictionary<string, string>();
            using dynamic data = new DynamicData(dict);

            Assert.Equal(dict, (Dictionary<string, string>)data);

            dict.Add("s", "5");
            Assert.True(dict.TryGetValue("s", out var addedVal1));
            Assert.Equal("5", addedVal1);

            //data.TryGetValue("s", out string? s2);

            var result = new WeakBox();
            Assert.True(data.TryGetValue("s", result));
            Assert.Equal("5", result.Value);
        }

        [Fact]
        public void TestDynamicData()
        {
            var dummy = new Dummy();
            using dynamic data = new DynamicData(dummy);

            Assert.Equal(dummy, (Dummy)data);

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

            data.RegisterMethod("NewMethod", new Func<object, object[], object>((target, args) => (int)args[0] * (int)args[1]));
            Assert.Equal(6, data.PublicMethod(4, 2));
            Assert.Equal(2, data.PrivateMethod(4, 2));
            Assert.Equal(16, data.PrivateBaseMethod(4, 2));
            Assert.Equal(8, data.NewMethod(4, 2));

            using (var dyndata = new DynamicData(dummy))
                Assert.Equal("ABC", dyndata.Get<string>("New"));
            using (var dyndata = new DynamicData(dummy))
                Assert.Equal(8, dyndata.Invoke<int>("NewMethod", 4, 2));

            using (new DynamicData(dummy) {
                { "Hello", "World!" }
            }) { }
            using (var dyndata = new DynamicData(dummy))
                Assert.Equal("World!", dyndata.Get<string>("Hello"));

            Assert.Equal(dummy, DynamicData.Set(dummy, new
            {
                A = 10,
                Other = "New"
            }));
            Assert.Equal(10, dummy.A);
            Assert.Equal("New", data.Other);

            data.CopyFrom(new
            {
                A = 20,
                Other = "Newer"
            });
            Assert.Equal(20, dummy.A);
            Assert.Equal("Newer", data.Other);

            dummy = DynamicData.New<Dummy>()(new
            {
                A = 30,
                B = 60L,
                C = "90",
                Other = "Newest"
            });
            Assert.Equal(30, dummy.A);
            Assert.Equal(60L, dummy._B);
            Assert.Equal("90", dummy._C);
            using (var dyndata = new DynamicData(dummy))
                Assert.Equal("Newest", dyndata.Get<string>("Other"));

            var dummyTo = new Dummy();
            Assert.Equal(69, dummyTo.A);
            Assert.Equal(420L, dummyTo._B);
            Assert.Equal("XYZ", dummyTo._C);

            using var dataTo = DynamicData.For(dummyTo);
            Assert.Equal(dataTo, DynamicData.For(dummyTo));
            using (var dyndata = new DynamicData(dummy))
            {
                foreach (var kvp in dyndata)
                    dataTo.Set(kvp.Key, kvp.Value);
            }
            Assert.Equal(30, dummyTo.A);
            Assert.Equal(60L, dummyTo._B);
            Assert.Equal("90", dummyTo._C);
            using (var dyndata = new DynamicData(dummy))
                Assert.Equal("Newest", dyndata.Get<string>("Other"));
        }

#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable CA1822 // Mark members as static
        private class DummyBase
        {

            private int PrivateBaseMethod(int a, int b) => a * b * b;

        }

        private class Dummy : DummyBase
        {

            public int A = 69;
            private long B = 420L;
            protected string C { get; set; } = "XYZ";

            public long _B => B;
            public string _C => C;

            public int PublicMethod(int a, int b) => a + b;
            private int PrivateMethod(int a, int b) => a - b;

        }
#pragma warning restore CA1822 // Mark members as static
#pragma warning restore IDE0051 // Remove unused private members

    }
}
