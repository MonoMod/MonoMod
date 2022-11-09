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

    var msvcrt = DynDll.OpenLibrary("msvcrt");
    var msvcrand = (delegate* unmanaged[Cdecl]<int>) DynDll.GetFunction(msvcrt, "rand");

    var get1del = (Get1Delegate) Get1;
    var get1ptr = (delegate* unmanaged[Cdecl]<int>) Marshal.GetFunctionPointerForDelegate(get1del);

    var rand1 = msvcrand();
    var get1a = get1ptr();

    var altrand1 = (delegate* unmanaged[Cdecl]<int>) triple.Architecture.AltEntryFactory!.CreateAlternateEntrypoint((IntPtr) msvcrand, 5, out var altrandh);
    var rand2 = altrand1();


    GC.KeepAlive(get1del);
    GC.KeepAlive(altrandh);
    altrandh?.Dispose();
}

static int Get1() {
    return 1;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Get1Delegate();