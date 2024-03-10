#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public unsafe class StructMagicTest : TestBase
    {

        internal static bool IsHook;

        internal static StructMagicTest GetStructInstance;
        internal int GetStructCounter;

        public StructMagicTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestPtrRefMagic()
        {
            var c = new Color();

            IsHook = false;
            ManipColor(ref c, 0x0A, 0xDE, 0xEE, 0x80);
            CheckColor(c, 0x0A, 0xDE, 0xEE, 0x80);

            using (new Hook(
                typeof(StructMagicTest).GetMethod("ManipColor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic),
                typeof(StructMagicTest).GetMethod("ManipColorHook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ))
            {
                IsHook = true;
                ManipColor(ref c, 0x12, 0x34, 0x56, 0x78);
                CheckColor(c, 0x12, 0x34, 0x56, 0x78);
            }
        }

        [Fact]
        public void TestInstanceMethodReturnStruct()
        {
            GetStructInstance = this;

            IsHook = false;
            GetStructCounter = 0;
            GetStruct((IntPtr)100);
            Assert.Equal(100, GetStructCounter);

            using (new Hook(
                typeof(StructMagicTest).GetMethod("GetStruct", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                typeof(StructMagicTest).GetMethod("GetStructHook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ))
            {
                IsHook = true;
                GetStructCounter = 600;
                GetStruct((IntPtr)100);
                Assert.Equal(1100, GetStructCounter);
            }
        }

        [Fact]
        public void TestStructMethod()
        {
            var c = new ColorRGBA();
            c.A = 5;

            IsHook = false;
            Assert.False(c.IsTransparent);
            Assert.Equal(c.A, c.R);

            using (new Hook(
                typeof(ColorRGBA).GetMethod("get_IsTransparent"),
                typeof(StructMagicTest).GetMethod("GetIsTransparentHook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ))
            {
                IsHook = true;
                c.A = 10;
                Assert.True(c.IsTransparent);
                Assert.Equal(c.A, c.R);
                Assert.Equal(c.A, c.G);
            }
        }

        [Fact]
        public void TestStructToString()
        {
            var c = new ColorRGBA()
            {
                R = 1,
                G = 2,
                B = 3,
                A = 4
            };

            IsHook = false;
            Assert.Equal("1 2 3 4", c.ToString());

            using (new Hook(
                typeof(ColorRGBA).GetMethod("ToString"),
                typeof(StructMagicTest).GetMethod("ToStringHook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ))
            {
                IsHook = true;
                Assert.Equal("1 2 3 4 hooked", c.ToString());
            }
        }

        internal static void CheckColor(Color c, byte r, byte g, byte b, byte a)
        {
            Assert.Equal(r, c.R);
            Assert.Equal(g, c.G);
            Assert.Equal(b, c.B);
            Assert.Equal(a, c.A);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal SomeOtherStruct GetStruct(IntPtr x)
        {
            GetStructCounter += (int)x;
            return new SomeOtherStruct();
        }

        internal static SomeOtherStruct GetStructHook(Func<StructMagicTest, IntPtr, SomeOtherStruct> orig, StructMagicTest self, IntPtr x)
        {
            Assert.True(IsHook);
            IsHook = false;
            var s = orig(self, x);
            IsHook = true;

            self.GetStructCounter += 400;
            return s;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ManipColor(ref Color c, byte r, byte g, byte b, byte a)
        {
            Assert.False(IsHook);
            c.R = r;
            c.G = g;
            c.B = b;
            c.A = a;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ManipColorHook(ColorRGBA* cRGBA, byte r, byte g, byte b, byte a)
        {
            Assert.True(IsHook);
            cRGBA->R = r;
            cRGBA->G = g;
            cRGBA->B = b;
            cRGBA->A = a;
        }

        internal delegate bool d_GetIsTransparent(ref ColorRGBA self);
        internal static bool GetIsTransparentHook(d_GetIsTransparent orig, ref ColorRGBA self)
        {
            Assert.True(IsHook);
            IsHook = false;
            var rv = orig(ref self);
            IsHook = true;

            self.G = self.A;
            return !rv;
        }

        internal delegate string d_ToString(ref ColorRGBA self);
        internal static string ToStringHook(d_ToString orig, ref ColorRGBA self)
        {
            Assert.True(IsHook);
            IsHook = false;
            var rv = orig(ref self);
            IsHook = true;

            return rv + " hooked";
        }

        internal struct Color
        {
            private uint packedValue;

            public byte B
            {
                get => (byte)(packedValue >> 16);
                set => packedValue = (packedValue & 0xff00ffff) | ((uint)value << 16);
            }

            public byte G
            {
                get => (byte)(packedValue >> 8);
                set => packedValue = (packedValue & 0xffff00ff) | ((uint)value << 8);
            }

            public byte R
            {
                get => (byte)(packedValue);
                set => packedValue = (packedValue & 0xffffff00) | value;
            }

            public byte A
            {
                get => (byte)(packedValue >> 24);
                set => packedValue = (packedValue & 0x00ffffff) | ((uint)value << 24);
            }

            public uint PackedValue
            {
                get => packedValue;
                set => packedValue = value;
            }
        }

        internal struct ColorRGBA
        {
            public byte R, G, B, A;

            public bool IsTransparent
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                get
                {
                    Assert.False(IsHook);
                    R = A;
                    return A == 0;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString()
            {
                Assert.False(IsHook);
                return $"{R} {G} {B} {A}";
            }
        }

        internal struct SomeOtherStruct
        {
#pragma warning disable CS0649 // Not initialized
            public byte A;
            public byte B;
            public byte C;
#pragma warning restore CS0649
        }
    }
}
