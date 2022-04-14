using MonoMod.Backports;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using System;
using System.Runtime.CompilerServices;

var platTriple = PlatformTriple.Current;

var method = typeof(TestClass).GetMethod(nameof(TestClass.TestDetourMethod))!;

var ptr = platTriple.GetNativeMethodBody(method);
Console.WriteLine($"{ptr:X16}");

TestClass.TestDetourMethod();

static class TestClass {
    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public static void TestDetourMethod() {
        var factory = DetourFactory.Current;

        Console.WriteLine(factory.SupportedFeatures);
    }
}