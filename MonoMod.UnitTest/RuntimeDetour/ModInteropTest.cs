using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public static class ModInteropTest {
        [Test]
        public static void TestUtils() {
            typeof(UtilsC).RegisterModInterop();

            typeof(UtilsA).RegisterModInterop();
            Assert.AreEqual(UtilsA.Something(2, 3),     UtilsC.Something(2, 3));
            Assert.AreEqual(UtilsA.AnotherThing(2, 3),  UtilsC.AnotherThing(2, 3));

            typeof(UtilsB).RegisterModInterop();
            Assert.AreEqual(UtilsA.Something(2, 3),     UtilsC.Something(2, 3));
            Assert.AreEqual(UtilsB.AnotherThing(2, 3),  UtilsC.AnotherThing(2, 3));

        }

        [ModExportName("ModA")] // Abused for this test, please ignore.
        public static class UtilsA {

            public static int Something(int a, int b) {
                return a + b;
            }

            public static int AnotherThing(int a, int b) {
                return a;
            }

        }

        [ModExportName("ModB")] // Abused for this test, please ignore.
        public static class UtilsB {

            public static int Something(int a, int b) {
                return a * b;
            }

            public static int AnotherThing(int a, int b) {
                return b;
            }

        }

        [ModExportName("ModC")] // Abused for this test, please ignore.
        public static class UtilsC {

            // Simple use case: Get the first registered "Something".
            public readonly static Func<int, int, int> Something;

            // More complicated use case: Get AnotherThing, apply it on a field of another name.
            [ModImport("AnotherThing")]
            public readonly static Func<int, int, int> AnotherThingFromAnywhere;

            // More complicated use case: Only get AnotherThing from ModB specifically.
            [ModImport("ModB.AnotherThing")]
            // This is only called AnotherThing because we're wrapping this.
            public readonly static Func<int, int, int> AnotherThingFromModB;

            // Example of a wrapper.
            // If ModB.AnotherThing exists, use it.
            // If not, use any other AnotherThing.
            // Internal so that only our mod sees this.
            internal static int AnotherThing(int a, int b) {
                if (AnotherThingFromModB != null)
                    return AnotherThingFromModB(a, b);
                return AnotherThingFromAnywhere(a, b);
            }

        }

    }
}
