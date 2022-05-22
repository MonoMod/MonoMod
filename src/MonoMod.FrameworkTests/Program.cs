using MonoMod.Backports;
using MonoMod.Core;
using MonoMod.RuntimeDetour;
using System;
using System.Runtime.CompilerServices;

var method = typeof(TestClass).GetMethod(nameof(TestClass.TestDetourMethod))!;
var method2 = typeof(TestClass).GetMethod(nameof(TestClass.Target))!;
var method3 = typeof(TestClass).GetMethod(nameof(TestClass.Target2))!;

/*using (DetourFactory.Current.CreateDetour(method, method2, true)) {
    var test = new TestClass();
    _ = test.TestDetourMethod();
}

var cwt = new ConditionalWeakTable<object, object>();

foreach (var entry in cwt) {
    Console.WriteLine(entry);
}

GC.GetTotalMemory(true);*/

using (var detour = new Detour(method, method2)) {
    var test = new TestClass();
    _ = test.TestDetourMethod();
    detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
}

Console.WriteLine();

{
    var test = new TestClass();
    _ = test.TestDetourMethod();
}

Console.WriteLine();

using (var detour = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target(null!)))
using (var detour2 = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target2(null!))) {
    var test = new TestClass();
    _ = test.TestDetourMethod();
    detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    detour2.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
}

class TestClass {
    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public FunkyStruct TestDetourMethod() {
        var factory = DetourFactory.Current;

        Console.WriteLine(factory.SupportedFeatures);

        return default;
    }

    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public static FunkyStruct Target(TestClass self) {
        Console.WriteLine($"Method successfully detoured {self} te");
        return default;
    }

    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public static FunkyStruct Target2(TestClass self) {
        Console.WriteLine($"Method successfully detoured {self} 2");
        return default;
    }
}

struct FunkyStruct {
    public long A;
    public byte B;
}