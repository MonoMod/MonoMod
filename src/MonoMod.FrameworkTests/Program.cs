#define MM

using MonoMod.RuntimeDetour;
using System;
using System.Diagnostics;
using MonoMod.Utils;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using MonoMod.Core.Platforms;

#if NETCOREAPP1_0_OR_GREATER
//using Xunit.Abstractions;
#endif

#if NET9_0
AppContext.SetSwitch("MonoMod.LogInMemory", true);
#endif

Console.WriteLine("Attach debugger now, then press enter to break");
//Console.ReadLine();
if (Debugger.IsAttached)
{
    Debugger.Break();
}

_ = PlatformTriple.Current;

var instance = new Subclass();

// This should work fine without any patches
_ = instance.Method("test");

using var hook = new ILHook(((Delegate)instance.Method).Method, c => { });

// Test for correct behavior: this should NOT throw NullReferenceException
// If the bug exists, this will fail because 'this' becomes null (and so the method throws)
// If the bug is fixed, this will pass because 'this' remains valid
_ = instance.Method("test");

Console.WriteLine(HookSrc());

using (new Hook(new Func<int>(HookSrc).Method, (Func<int> orig) =>
{
    return orig() + 1;
}))
{
    Console.WriteLine(HookSrc());
}

[MethodImpl(MethodImplOptions.NoInlining)]
static int HookSrc()
{
    return 1;
}

#if false
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

    if (NativeHook.CanCallOriginal)
    {
        using (new NativeHook((IntPtr)msvcrand, (RandHook)MixRand))
        {
            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine(msvcrand());
            }
        }
    }
    else
    {
        Console.WriteLine("Not trying MixRand; CreateAltEntryPoint is not supported on this arch");
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

#if false
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
#endif

#pragma warning disable CS0649

internal struct SomeStruct
{
    public double n1;
    public double n2;
    public double n3;
    public double n4;
}

internal class Mainclass
{
    public virtual SomeStruct Method(string s)
    {
        Console.WriteLine("Mainclass.Method called");
        return default;
    }
}

internal sealed class Subclass : Mainclass
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code",
        Justification = "The test is specifically verifying that our hook behavior doens't cause `this` to become null")]
    public override SomeStruct Method(string s)
    {
        Console.WriteLine("Subclass.Method called");
        var me = this;
        Console.WriteLine("this = " + me);

        // This is the critical test - if 'this' is null, this would throw NullReferenceException
        // We're testing that this SHOULD NOT throw
        if (this == null)
        {
            throw new InvalidOperationException("this instance became null - this is the bug!");
        }

        return default;
    }
}