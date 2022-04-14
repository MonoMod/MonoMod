using MonoMod.Core;
using MonoMod.Core.Platforms;
using System;

var platTriple = PlatformTriple.Current;

var method = typeof(TestClass).GetMethod(nameof(TestClass.TestDetourMethod))!;

var ptr = platTriple.GetNativeMethodBody(method);
Console.WriteLine($"{ptr:X16}");

static class TestClass {
    public static void TestDetourMethod() {
        throw new InvalidOperationException();
    }
}