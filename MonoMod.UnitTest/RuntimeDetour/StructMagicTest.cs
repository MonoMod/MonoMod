#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    [TestClass]
    public unsafe class StructMagicTest {

        public static bool IsFast = false;

        [TestMethod]
        public void TestPtrRefMagic() {
            Color c = new Color();

            IsFast = false;
            ManipColor(ref c, 0x0A, 0xDE, 0xEE, 0x80);
            CheckColor(c, 0x0A, 0xDE, 0xEE, 0x80);

            using (Detour detour = new Detour(
                typeof(StructMagicTest).GetMethod("ManipColor"),
                typeof(StructMagicTest).GetMethod("ManipColorFast")
            )) {
                IsFast = true;
                ManipColor(ref c, 0x12, 0x34, 0x56, 0x78);
                CheckColor(c, 0x12, 0x34, 0x56, 0x78);
            }
        }

        // Prevent JIT from inlining the method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ManipColor(ref Color c, byte r, byte g, byte b, byte a) {
            Assert.IsFalse(IsFast);
            c.R = r;
            c.G = g;
            c.B = b;
            c.A = a;
        }

        // Prevent JIT from inlining the method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ManipColorFast(ColorRGBA* cRGBA, byte r, byte g, byte b, byte a) {
            Assert.IsTrue(IsFast);
            cRGBA->R = r;
            cRGBA->G = g;
            cRGBA->B = b;
            cRGBA->A = a;
        }

        public static void CheckColor(Color c, byte r, byte g, byte b, byte a) {
            Assert.AreEqual(r, c.R);
            Assert.AreEqual(g, c.G);
            Assert.AreEqual(b, c.B);
            Assert.AreEqual(a, c.A);
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

    }
}
