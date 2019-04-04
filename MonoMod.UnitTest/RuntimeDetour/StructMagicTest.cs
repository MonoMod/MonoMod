#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public unsafe class StructMagicTest {

        public static bool IsHook;

        public static StructMagicTest GetStructInstance;
        public int GetStructCounter;

        [Fact]
        public void TestPtrRefMagic() {
            Color c = new Color();

            IsHook = false;
            ManipColor(ref c, 0x0A, 0xDE, 0xEE, 0x80);
            CheckColor(c, 0x0A, 0xDE, 0xEE, 0x80);

            using (new Detour(
                typeof(StructMagicTest).GetMethod("ManipColor"),
                typeof(StructMagicTest).GetMethod("ManipColorHook")
            )) {
                IsHook = true;
                ManipColor(ref c, 0x12, 0x34, 0x56, 0x78);
                CheckColor(c, 0x12, 0x34, 0x56, 0x78);
            }
        }

        [Fact]
        public void TestInstanceMethodReturnStruct() {
            GetStructInstance = this;

            IsHook = false;
            GetStructCounter = 0;
            GetStruct((IntPtr) 100);
            Assert.Equal(100, GetStructCounter);

            using (new Hook(
                typeof(StructMagicTest).GetMethod("GetStruct"),
                typeof(StructMagicTest).GetMethod("GetStructHook")
            )) {
                IsHook = true;
                GetStructCounter = 600;
                GetStruct((IntPtr) 100);
                Assert.Equal(1100, GetStructCounter);
            }
        }

        [Fact]
        public void TestStructMethod() {
            ColorRGBA c = new ColorRGBA();
            c.A = 5;

            IsHook = false;
            Assert.False(c.IsTransparent);
            Assert.Equal(c.A, c.R);

            using (new Hook(
                typeof(ColorRGBA).GetMethod("get_IsTransparent"),
                typeof(StructMagicTest).GetMethod("GetIsTransparentHook")
            )) {
                IsHook = true;
                c.A = 10;
                Assert.True(c.IsTransparent);
                Assert.Equal(c.A, c.R);
                Assert.Equal(c.A, c.G);
            }
        }

        public static void CheckColor(Color c, byte r, byte g, byte b, byte a) {
            Assert.Equal(r, c.R);
            Assert.Equal(g, c.G);
            Assert.Equal(b, c.B);
            Assert.Equal(a, c.A);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public SomeOtherStruct GetStruct(IntPtr x) {
            GetStructCounter += (int) x;
            return new SomeOtherStruct();
        }

        public static SomeOtherStruct GetStructHook(Func<StructMagicTest, IntPtr, SomeOtherStruct> orig, StructMagicTest self, IntPtr x) {
            Assert.True(IsHook);
            IsHook = false;
            SomeOtherStruct s = orig(self, x);
            IsHook = true;

            self.GetStructCounter += 400;
            return s;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ManipColor(ref Color c, byte r, byte g, byte b, byte a) {
            Assert.False(IsHook);
            c.R = r;
            c.G = g;
            c.B = b;
            c.A = a;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ManipColorHook(ColorRGBA* cRGBA, byte r, byte g, byte b, byte a) {
            Assert.True(IsHook);
            cRGBA->R = r;
            cRGBA->G = g;
            cRGBA->B = b;
            cRGBA->A = a;
        }

        public delegate bool d_GetIsTransparent(ref ColorRGBA self);
        public static bool GetIsTransparentHook(d_GetIsTransparent orig, ref ColorRGBA self) {
            Assert.True(IsHook);
            IsHook = false;
            bool rv = orig(ref self);
            IsHook = true;

            self.G = self.A;
            return !rv;
        }

        public struct Color {
            private uint packedValue;

            public byte B {
                get => (byte) (packedValue >> 16);
                set => packedValue = (packedValue & 0xff00ffff) | ((uint) value << 16);
            }

            public byte G {
                get => (byte) (packedValue >> 8);
                set => packedValue = (packedValue & 0xffff00ff) | ((uint) value << 8);
            }

            public byte R {
                get => (byte) (packedValue);
                set => packedValue = (packedValue & 0xffffff00) | value;
            }

            public byte A {
                get => (byte) (packedValue >> 24);
                set => packedValue = (packedValue & 0x00ffffff) | ((uint) value << 24);
            }

            public uint PackedValue {
                get => packedValue;
                set => packedValue = value;
            }
        }

        public struct ColorRGBA {
            public byte R, G, B, A;

            public bool IsTransparent {
                [MethodImpl(MethodImplOptions.NoInlining)]
                get {
                    Assert.False(IsHook);
                    R = A;
                    return A == 0;
                }
            }
        }

        public struct SomeOtherStruct {
            public byte A;
            public byte B;
            public byte C;
        }

    }
}
