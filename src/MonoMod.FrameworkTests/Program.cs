#define MM

using MonoMod.RuntimeDetour;
using System;
using System.Diagnostics;
using MonoMod.Utils;
using System.Runtime.InteropServices;

#if NETCOREAPP1_0_OR_GREATER
using Xunit.Abstractions;
#endif

Console.WriteLine("Attach debugger now, then press enter to break");
Console.ReadLine();
if (Debugger.IsAttached)
{
    Debugger.Break();
}

var str = "text".AsMemory();

using (new Hook(typeof(ReadOnlyMemory<char>).GetMethod("ToString")!, (ReadOnlyMemoryToString orig, ref ReadOnlyMemory<char> mem) =>
{
    return orig(ref mem) + " lol";
}))
{
    var str2 = str.ToString();
    Console.WriteLine(str2);
}

#if NETCOREAPP1_0_OR_GREATER

{
    using var tcTest = new MonoMod.UnitTest.DetourOrderTest(new DummyOutputHelper());
    tcTest.TestDetoursOrder();
}
{
    using var tcTest = new MonoMod.UnitTest.JitExceptionTest(new DummyOutputHelper());
    tcTest.TestJitExceptions();
}

#endif

unsafe
{

    var clib = PlatformDetection.OS switch
    {
        OSKind.Windows => "msvcrt",
        _ => "c"
    };

    var msvcrt = DynDll.OpenLibrary(clib);
    var msvcrand = (delegate* unmanaged[Cdecl]<int>)DynDll.GetExport(msvcrt, "rand");

    var get1del = (Get1Delegate)Get1;
    var get1ptr = (delegate* unmanaged[Cdecl]<int>)Marshal.GetFunctionPointerForDelegate(get1del);

    var rand1 = msvcrand();
    Console.WriteLine(rand1);
    var get1a = get1ptr();
    Console.WriteLine(get1a);

    /*
    using var detour = DetourFactory.Current.CreateNativeDetour((IntPtr)msvcrand, (IntPtr)get1ptr);
    var altrand1 = (delegate* unmanaged[Cdecl]<int>) detour.OrigEntrypoint;

    for (var i = 0; i < 10; i++) {
        var rand2 = msvcrand();
        Console.WriteLine(rand2);
        var galtrand1 = altrand1();
        Console.WriteLine(galtrand1);
    }
    */

    using (new NativeHook((IntPtr)msvcrand, get1del))
    {
        Helpers.Assert(msvcrand() == 1);
    }

    for (var i = 0; i < 10; i++)
    {
        Console.WriteLine(msvcrand());
    }

    using (new NativeHook((IntPtr)msvcrand, (RandHook)MixRand))
    {
        for (var i = 0; i < 10; i++)
        {
            Console.WriteLine(msvcrand());
        }
    }

    GC.KeepAlive(get1del);
}

static int Get1()
{
    return 1;
}

static int MixRand(Get1Delegate orig)
{
    return (orig() << 4) ^ (orig() >> 4) ^ orig();
}

delegate string ReadOnlyMemoryToString(ref ReadOnlyMemory<char> mem);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Get1Delegate();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int RandHook(Get1Delegate orig);

#if NETCOREAPP1_0_OR_GREATER

internal sealed class DummyOutputHelper : ITestOutputHelper
{
    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }
}

#endif