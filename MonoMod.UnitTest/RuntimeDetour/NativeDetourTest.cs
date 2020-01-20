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
            if (PlatformHelper.Is(Platform.Windows))
                TestNativeDetoursWindows();
            else if (PlatformHelper.Is(Platform.Unix))
                TestNativeDetoursUnix();
            else
                throw new PlatformNotSupportedException();
        }

        private void TestNativeDetoursWindows() {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            DidNothing = true;
            msvcrt_rand();
            Assert.True(DidNothing);

            using (NativeDetour d = new NativeDetour(
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
        }

        private void TestNativeDetoursUnix() {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            DidNothing = true;
            libc_rand();
            Assert.True(DidNothing);

            using (NativeDetour d = new NativeDetour(
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
        }


        [DllImport("msvcrt", EntryPoint = "rand", CallingConvention = CallingConvention.Cdecl)]
        public static extern int msvcrt_rand();

        [DllImport("libc", EntryPoint = "rand", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libc_rand();

        public static int not_rand() {
            DidNothing = false;
            return -1;
        }

    }
}
