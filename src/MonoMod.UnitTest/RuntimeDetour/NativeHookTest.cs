extern alias New;
using MonoMod.Utils;
using New::MonoMod.RuntimeDetour;
using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class NativeHookTest : TestBase
    {
        public NativeHookTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public unsafe void TestNativeHooks()
        {
            var libc = DynDll.OpenLibrary(PlatformDetection.OS switch
            {
                OSKind.Windows => "msvcrt",
                _ => "c",
            });

            var rand = (delegate* unmanaged[Cdecl]<int>)DynDll.GetExport(libc, "rand");

            Assert.NotEqual(-1, rand());

            using (new NativeHook((IntPtr)rand, (RandDelegate)RandHook))
            {
                Assert.Equal(-1, rand());
            }

            Assert.NotEqual(-1, rand());

            if (NativeHook.CanCallOriginal)
            {
                using (new NativeHook((IntPtr)rand, (RandMixHookDelegate)RandMixHook))
                {
                    Assert.Equal(-1, rand());
                }

                Assert.NotEqual(-1, rand());
            }
            else
            {
                MMDbgLog.Warning("Not trying RandMixHook; CreateAltEntryPoint is not supported on this arch");
            }
        }

        private static int RandHook()
        {
            return -1;
        }

        private static int RandMixHook(RandDelegate orig)
        {
            Assert.NotEqual(-1, orig());
            return RandHook();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RandDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RandMixHookDelegate(RandDelegate orig);
    }
}
