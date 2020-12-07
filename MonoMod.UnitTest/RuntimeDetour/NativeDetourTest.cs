#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Reflection.Emit;
using System.Text;
using System.Runtime.InteropServices;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class NativeDetourTest {
        private static bool DidNothing = true;

        [PlatformFact("Windows", "Unix")]
        public void TestNativeDetours() {
            // https://github.com/dotnet/coreclr/pull/8263
            dt_rand d_not_rand = not_rand;
            GCHandle gch_not_rand = GCHandle.Alloc(d_not_rand);
            IntPtr ptr_not_rand = Marshal.GetFunctionPointerForDelegate(d_not_rand);

            try {
                if (PlatformHelper.Is(Platform.Windows))
                    TestNativeDetoursWindows(d_not_rand, ptr_not_rand);
                else if (PlatformHelper.Is(Platform.Unix))
                    TestNativeDetoursUnix(d_not_rand, ptr_not_rand);
                else
                    throw new PlatformNotSupportedException();

            } finally {
                gch_not_rand.Free();
            }
        }

        private void TestNativeDetoursWindows(dt_rand d_not_rand, IntPtr ptr_not_rand) {
            if (!DynDll.TryOpenLibrary($"msvcrt.{PlatformHelper.LibrarySuffix}", out IntPtr msvcrt)) {
                Console.WriteLine("NativeDetourTest skipped - msvcrt not found!");
                return;
            }

            DidNothing = true;
            msvcrt_rand();
            Assert.True(DidNothing);

            DidNothing = true;
            Assert.Equal(-1, d_not_rand());
            Assert.False(DidNothing);

            using (new NativeDetour(
                typeof(NativeDetourTest).GetMethod("msvcrt_rand"),
                typeof(NativeDetourTest).GetMethod("not_rand")
            )) {
                DidNothing = true;
                Assert.Equal(-1, msvcrt_rand());
                Assert.False(DidNothing);
            }

            DidNothing = true;
            msvcrt_rand();
            Assert.True(DidNothing);

            NativeDetour d = new NativeDetour(
                msvcrt.GetFunction("rand"),
                ptr_not_rand
            );

            DidNothing = true;
            Assert.Equal(-1, msvcrt_rand());
            Assert.False(DidNothing);

            d.Dispose();

            DidNothing = true;
            msvcrt_rand();
            Assert.True(DidNothing);
        }

        private void TestNativeDetoursUnix(dt_rand d_not_rand, IntPtr ptr_not_rand) {
            DidNothing = true;
            libc_rand();
            Assert.True(DidNothing);

            DidNothing = true;
            Assert.Equal(-1, d_not_rand());
            Assert.False(DidNothing);

            using (new NativeDetour(
                typeof(NativeDetourTest).GetMethod("libc_rand"),
                typeof(NativeDetourTest).GetMethod("not_rand")
            )) {
                DidNothing = true;
                Assert.Equal(-1, libc_rand());
                Assert.False(DidNothing);
            }

            DidNothing = true;
            libc_rand();
            Assert.True(DidNothing);

            /* dl's dlopen doesn't follow ld, meaning that we need to...
             * - use libc.so.6 on Linux and hope that everyone is using glibc.
             * - use /usr/lib/libc.dylib on macOS because macOS is macOS.
             * If libc cannot be dlopened, skip the native -> managed detour test.
             * - ade
             */
            if (!(PlatformHelper.Is(Platform.Linux) && DynDll.TryOpenLibrary("libc.so.6", out IntPtr libc)) &&
                !(PlatformHelper.Is(Platform.MacOS) && DynDll.TryOpenLibrary("/usr/lib/libc.dylib", out libc)) &&
                !DynDll.TryOpenLibrary($"libc.{PlatformHelper.LibrarySuffix}", out libc))
                return;

            NativeDetour d = new NativeDetour(
                libc.GetFunction("rand"),
                ptr_not_rand
            );

            DidNothing = true;
            Assert.Equal(-1, libc_rand());
            Assert.False(DidNothing);

            d.Dispose();

            DidNothing = true;
            libc_rand();
            Assert.True(DidNothing);
        }

        [DllImport("msvcrt", EntryPoint = "rand", CallingConvention = CallingConvention.Cdecl)]
        public static extern int msvcrt_rand();

        [DllImport("libc", EntryPoint = "rand", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libc_rand();

        public static int not_rand() {
            DidNothing = false;
            return -1;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int dt_rand();

    }
}
