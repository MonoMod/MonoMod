using MonoMod.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    public class DetouringMethodsReturningStructs : TestBase
    {
        public DetouringMethodsReturningStructs(ITestOutputHelper helper) : base(helper)
        {
        }

        public enum CallType
        {
            Static,
            Instance
        }

        [SuppressMessage("", "CA1812", Justification = "Implicitly instantiated by xUnit")]
        private class AddTestData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                foreach (var type in Enum.GetValues(typeof(CallType)))
                {
                    for (var n = 1; n <= 20; n++)
                    {
                        yield return new object[] { type, n };
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Theory]
        [ClassData(typeof(AddTestData))]
        public void ReturningStructMethodsDoesNotThrow(CallType type, int byteCount)
        {
            var all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var suffix = byteCount < 10 ? $"0{byteCount}" : $"{byteCount}";
            switch (type)
            {
                case CallType.Static:
                {
                    var method = typeof(StaticHolder).GetMethod($"OriginalStatic{suffix}", all);
                    var replacement = typeof(StaticHolder).GetMethod($"ReplacementStatic{suffix}", all);
                    using var result = DetourFactory.Current.CreateDetour(method, replacement);
                    method.Invoke(null, ["test"]);
                    break;
                }
                case CallType.Instance:
                {
                    var method = typeof(InstanceHolder).GetMethod($"OriginalInstance{suffix}", all);
                    var replacement = typeof(InstanceHolder).GetMethod($"ReplacementInstance{suffix}", all);
                    using var result = DetourFactory.Current.CreateDetour(method, replacement);
                    var instance = new InstanceHolder();
                    method.Invoke(instance, ["test"]);
                    break;
                }
            }
        }

        private class StaticHolder
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St01 OriginalStatic01(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St01();
            }

            public static St01 ReplacementStatic01(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St01) called with garbage string parameter"); }
                return new St01();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St02 OriginalStatic02(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St02();
            }

            public static St02 ReplacementStatic02(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St02) called with garbage string parameter"); }
                return new St02();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St03 OriginalStatic03(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St03();
            }

            public static St03 ReplacementStatic03(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St03) called with garbage string parameter"); }
                return new St03();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St04 OriginalStatic04(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St04();
            }

            public static St04 ReplacementStatic04(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St04) called with garbage string parameter"); }
                return new St04();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St05 OriginalStatic05(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St05();
            }

            public static St05 ReplacementStatic05(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St05) called with garbage string parameter"); }
                return new St05();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St06 OriginalStatic06(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St06();
            }

            public static St06 ReplacementStatic06(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St06) called with garbage string parameter"); }
                return new St06();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St07 OriginalStatic07(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St07();
            }

            public static St07 ReplacementStatic07(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St07) called with garbage string parameter"); }
                return new St07();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St08 OriginalStatic08(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St08();
            }

            public static St08 ReplacementStatic08(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St08) called with garbage string parameter"); }
                return new St08();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St09 OriginalStatic09(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St09();
            }

            public static St09 ReplacementStatic09(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St09) called with garbage string parameter"); }
                return new St09();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St10 OriginalStatic10(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St10();
            }

            public static St10 ReplacementStatic10(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St10) called with garbage string parameter"); }
                return new St10();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St11 OriginalStatic11(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St11();
            }

            public static St11 ReplacementStatic11(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St11) called with garbage string parameter"); }
                return new St11();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St12 OriginalStatic12(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St12();
            }

            public static St12 ReplacementStatic12(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St12) called with garbage string parameter"); }
                return new St12();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St13 OriginalStatic13(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St13();
            }

            public static St13 ReplacementStatic13(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St13) called with garbage string parameter"); }
                return new St13();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St14 OriginalStatic14(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St14();
            }

            public static St14 ReplacementStatic14(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St14) called with garbage string parameter"); }
                return new St14();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St15 OriginalStatic15(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St15();
            }

            public static St15 ReplacementStatic15(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St15) called with garbage string parameter"); }
                return new St15();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St16 OriginalStatic16(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St16();
            }

            public static St16 ReplacementStatic16(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St16) called with garbage string parameter"); }
                return new St16();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St17 OriginalStatic17(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St17();
            }

            public static St17 ReplacementStatic17(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St17) called with garbage string parameter"); }
                return new St17();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St18 OriginalStatic18(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St18();
            }

            public static St18 ReplacementStatic18(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St18) called with garbage string parameter"); }
                return new St18();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St19 OriginalStatic19(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St19();
            }

            public static St19 ReplacementStatic19(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St19) called with garbage string parameter"); }
                return new St19();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static St20 OriginalStatic20(string s)
            {
                Assert.Fail("Original static should never be called");
                return new St20();
            }

            public static St20 ReplacementStatic20(string s)
            {
                if (s != "test") { Assert.Fail("Replacement static (St20) called with garbage string parameter"); }
                return new St20();
            }
        }

        private class InstanceHolder
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public St01 OriginalInstance01(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St01();
            }

            public static St01 ReplacementInstance01(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St01) called with garbage string parameter"); }
                return new St01();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St02 OriginalInstance02(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St02();
            }

            public static St02 ReplacementInstance02(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St02) called with garbage string parameter"); }
                return new St02();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St03 OriginalInstance03(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St03();
            }

            public static St03 ReplacementInstance03(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St03) called with garbage string parameter"); }
                return new St03();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St04 OriginalInstance04(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St04();
            }

            public static St04 ReplacementInstance04(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St04) called with garbage string parameter"); }
                return new St04();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St05 OriginalInstance05(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St05();
            }

            public static St05 ReplacementInstance05(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St05) called with garbage string parameter"); }
                return new St05();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St06 OriginalInstance06(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St06();
            }

            public static St06 ReplacementInstance06(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St06) called with garbage string parameter"); }
                return new St06();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St07 OriginalInstance07(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St07();
            }

            public static St07 ReplacementInstance07(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St07) called with garbage string parameter"); }
                return new St07();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St08 OriginalInstance08(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St08();
            }

            public static St08 ReplacementInstance08(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St08) called with garbage string parameter"); }
                return new St08();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St09 OriginalInstance09(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St09();
            }

            public static St09 ReplacementInstance09(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St09) called with garbage string parameter"); }
                return new St09();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St10 OriginalInstance10(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St10();
            }

            public static St10 ReplacementInstance10(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St10) called with garbage string parameter"); }
                return new St10();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St11 OriginalInstance11(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St11();
            }

            public static St11 ReplacementInstance11(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St11) called with garbage string parameter"); }
                return new St11();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St12 OriginalInstance12(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St12();
            }

            public static St12 ReplacementInstance12(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St12) called with garbage string parameter"); }
                return new St12();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St13 OriginalInstance13(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St13();
            }

            public static St13 ReplacementInstance13(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St13) called with garbage string parameter"); }
                return new St13();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St14 OriginalInstance14(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St14();
            }

            public static St14 ReplacementInstance14(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St14) called with garbage string parameter"); }
                return new St14();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St15 OriginalInstance15(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St15();
            }

            public static St15 ReplacementInstance15(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St15) called with garbage string parameter"); }
                return new St15();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St16 OriginalInstance16(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St16();
            }

            public static St16 ReplacementInstance16(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St16) called with garbage string parameter"); }
                return new St16();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St17 OriginalInstance17(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St17();
            }

            public static St17 ReplacementInstance17(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St17) called with garbage string parameter"); }
                return new St17();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St18 OriginalInstance18(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St18();
            }

            public static St18 ReplacementInstance18(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St18) called with garbage string parameter"); }
                return new St18();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St19 OriginalInstance19(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St19();
            }

            public static St19 ReplacementInstance19(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St19) called with garbage string parameter"); }
                return new St19();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public St20 OriginalInstance20(string s)
            {
                Assert.Fail("Original instance should never be called");
                return new St20();
            }

            public static St20 ReplacementInstance20(InstanceHolder _, string s)
            {
                if (s != "test") { Assert.Fail("Replacement instance (St20) called with garbage string parameter"); }
                return new St20();
            }
        }

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value 0
        private struct St01
        {
            public byte b1;
        }

        private struct St02
        {
            public byte b1;
            public byte b2;
        }

        private struct St03
        {
            public byte b1;
            public byte b2;
            public byte b3;
        }

        private struct St04
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
        }

        private struct St05
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
        }

        private struct St06
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
        }

        private struct St07
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
        }

        private struct St08
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
        }

        private struct St09
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
        }

        private struct St10
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
        }

        private struct St11
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
        }

        private struct St12
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
        }

        private struct St13
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
        }

        private struct St14
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
        }

        private struct St15
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
            public byte b15;
        }

        private struct St16
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
            public byte b15;
            public byte b16;
        }

        private struct St17
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
            public byte b15;
            public byte b16;
            public byte b17;
        }

        private struct St18
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
            public byte b15;
            public byte b16;
            public byte b17;
            public byte b18;
        }

        private struct St19
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
            public byte b15;
            public byte b16;
            public byte b17;
            public byte b18;
            public byte b19;
        }

        private struct St20
        {
            public byte b1;
            public byte b2;
            public byte b3;
            public byte b4;
            public byte b5;
            public byte b6;
            public byte b7;
            public byte b8;
            public byte b9;
            public byte b10;
            public byte b11;
            public byte b12;
            public byte b13;
            public byte b14;
            public byte b15;
            public byte b16;
            public byte b17;
            public byte b18;
            public byte b19;
            public byte b20;
        }
    }
}