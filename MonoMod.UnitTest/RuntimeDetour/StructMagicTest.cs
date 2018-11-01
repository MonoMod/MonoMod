#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public unsafe class StructMagicTest {

        public static bool IsHook = false;

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

        [Fact(Skip = "Fix for struct-returning instance methods not yet implemented. Unskip once it's supported.")]
        public void TestReturnStruct() {
            IsHook = false;
            CheckSize(GetSize(), 1D, 2D);

            using (new Hook(
                typeof(StructMagicTest).GetMethod("GetSize"),
                typeof(StructMagicTest).GetMethod("GetSizeHook")
            )) {
                IsHook = true;
                Size s = GetSize();
                CheckSize(s, 10D, 20D);
            }
        }

        public static void CheckColor(Color c, byte r, byte g, byte b, byte a) {
            Assert.Equal(r, c.R);
            Assert.Equal(g, c.G);
            Assert.Equal(b, c.B);
            Assert.Equal(a, c.A);
        }

        public static void CheckSize(Size s, double width, double height) {
            Assert.Equal(width, s.Width);
            Assert.Equal(height, s.Height);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Size GetSize() {
            Assert.False(IsHook);
            return new Size(1D, 2D);
        }

        public static Size GetSizeHook(Func<StructMagicTest, Size> orig, StructMagicTest self) {
            Assert.True(IsHook);
            IsHook = false;
            Size s = orig(self);
            IsHook = true;

            s.Width *= 10D;
            s.Height *= 10D;
            return s;
        }

        // Prevent JIT from inlining the method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ManipColor(ref Color c, byte r, byte g, byte b, byte a) {
            Assert.False(IsHook);
            c.R = r;
            c.G = g;
            c.B = b;
            c.A = a;
        }

        // Prevent JIT from inlining the method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ManipColorHook(ColorRGBA* cRGBA, byte r, byte g, byte b, byte a) {
            Assert.True(IsHook);
            cRGBA->R = r;
            cRGBA->G = g;
            cRGBA->B = b;
            cRGBA->A = a;
        }

        public struct Color {
            private uint packedValue;

            public byte B {
                get {
                    unchecked {
                        return (byte) (packedValue >> 16);
                    }
                }
                set {
                    packedValue = (packedValue & 0xff00ffff) | ((uint) value << 16);
                }
            }

            public byte G {
                get {
                    unchecked {
                        return (byte) (packedValue >> 8);
                    }
                }
                set {
                    packedValue = (packedValue & 0xffff00ff) | ((uint) value << 8);
                }
            }

            public byte R {
                get {
                    unchecked {
                        return (byte) (packedValue);
                    }
                }
                set {
                    packedValue = (packedValue & 0xffffff00) | value;
                }
            }

            public byte A {
                get {
                    unchecked {
                        return (byte) (packedValue >> 24);
                    }
                }
                set {
                    packedValue = (packedValue & 0x00ffffff) | ((uint) value << 24);
                }
            }

            public uint PackedValue {
                get {
                    return packedValue;
                }
                set {
                    packedValue = value;
                }
            }
        }

        public struct ColorRGBA {
            public byte R, G, B, A;
        }

        public struct Size {
            public double Width { get; set; }
            public double Height { get; set; }

            public Size(double width, double height) {
                Width = width;
                Height = height;
            }
        }

    }
}
