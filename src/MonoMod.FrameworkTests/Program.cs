using MonoMod.Core;
using MonoMod.Core.Platforms;
using System;

var factory = (HostTripleDetourFactory) DetourFactory.Current;

var method = typeof(TestClass).GetMethod(nameof(TestClass.TestDetourMethod))!;

var ptr = factory.GetNativeMethodBody(method);
Console.WriteLine(ptr);

static class TestClass {
    public static void TestDetourMethod() {
        throw new InvalidOperationException();
    }
}