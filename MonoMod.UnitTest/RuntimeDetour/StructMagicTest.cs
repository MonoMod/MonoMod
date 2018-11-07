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
                typeof(StructMagicTest).GetTypeInfo().GetMethod("ManipColor"),
                typeof(StructMagicTest).GetTypeInfo().GetMethod("ManipColorHook")
            )) {
                IsHook = true;
                ManipColor(ref c, 0x12, 0x34, 0x56, 0x78);
                CheckColor(c, 0x12, 0x34, 0x56, 0x78);
            }
        }

        [Fact]
        public void TestReturnStruct() {
            GetStructInstance = this;

            IsHook = false;
            GetStructCounter = 0;
            GetStruct((IntPtr) 100);
            Assert.Equal(100, GetStructCounter);

            using (new Hook(
                typeof(StructMagicTest).GetTypeInfo().GetMethod("GetStruct"),
                typeof(StructMagicTest).GetTypeInfo().GetMethod("GetStructHook")
            )) {
                IsHook = true;
                GetStructCounter = 600;
                GetStruct((IntPtr) 100);
                Assert.Equal(1100, GetStructCounter);
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

        public struct SomeOtherStruct {
            public byte A;
            public byte B;
            public byte C;
        }

    }
}
