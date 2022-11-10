#define MM

using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;

using MonoMod.Backports;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Runtime.InteropServices;

#if NETCOREAPP1_0_OR_GREATER
using Xunit;
using Xunit.Abstractions;
#endif

Console.WriteLine("Attach debugger now, then press enter to break");
Console.ReadLine();
if (Debugger.IsAttached) {
    Debugger.Break();
}

var triple = PlatformTriple.Current;

unsafe {

    var msvcrt = DynDll.OpenLibrary("c");
    var msvcrand = (delegate* unmanaged[Cdecl]<int>) DynDll.GetFunction(msvcrt, "rand");

    var get1del = (Get1Delegate) Get1;
    var get1ptr = (delegate* unmanaged[Cdecl]<int>) Marshal.GetFunctionPointerForDelegate(get1del);

    var rand1 = msvcrand();
    Console.WriteLine(rand1);
    var get1a = get1ptr();
    Console.WriteLine(get1a);

    using var nativeDetour = triple.CreateNativeDetour((IntPtr) msvcrand, (IntPtr) get1ptr);
    Helpers.Assert(nativeDetour.HasOrigEntry);
    var altrand1 = (delegate* unmanaged[Cdecl]<int>) nativeDetour.OrigEntry;

    for (var i = 0; i < 10; i++) {
        var rand2 = msvcrand();
        Console.WriteLine(rand2);
        var galtrand1 = altrand1();
        Console.WriteLine(galtrand1);
    }

GC.KeepAlive(get1del);
}

static int Get1() {
    return 1;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Get1Delegate();