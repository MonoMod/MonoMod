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

#if NETCOREAPP1_0_OR_GREATER
using Xunit;
using Xunit.Abstractions;
#endif

Console.WriteLine("Attach debugger now, then press enter to break");
Console.ReadLine();
if (Debugger.IsAttached) {
    Debugger.Break();
}

#if NETCOREAPP1_0_OR_GREATER

{
    using var tcTest = new MonoMod.UnitTest.TieredCompilationTests(new DummyOutputHelper());
    tcTest.WithTieredCompilation();
}

internal class DummyOutputHelper : ITestOutputHelper {
    public void WriteLine(string message) {
    }

    public void WriteLine(string format, params object[] args) {
    }
}

#endif