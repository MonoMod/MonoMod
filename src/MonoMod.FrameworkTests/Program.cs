using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

Console.ReadLine();
if (Debugger.IsAttached) {
    Debugger.Break();
}

var platform = PlatformTriple.Current;

var memAlloc = platform.System.MemoryAllocator;

if (memAlloc.TryAllocateInRange(new((nint) 0x1000000000uL, (nint) 0x10000, (nint) 0xffffffffffuL, 16), out var allocated)) {


    allocated.Dispose();
}

var method = typeof(TestClass).GetMethod(nameof(TestClass.TestDetourMethod))!;
var method2 = typeof(TestClass).GetMethod(nameof(TestClass.Target))!;
var method3 = typeof(TestClass).GetMethod(nameof(TestClass.Target2))!;
var targetHook = typeof(TestClass).GetMethod(nameof(TestClass.TargetHook))!;

/*using (DetourFactory.Current.CreateDetour(method, method2, true)) {
    var test = new TestClass();
    _ = test.TestDetourMethod();
}

var cwt = new ConditionalWeakTable<object, object>();

foreach (var entry in cwt) {
    Console.WriteLine(entry);
}

GC.GetTotalMemory(true);*/

using (var hook = new Hook(method, targetHook)) {

    using (var h = new ILHook(
        method,
        il => {
            var c = new ILCursor(il);
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Call, method2);
            c.Emit(OpCodes.Pop);
        }
    )) {
        var test = new TestClass();
        _ = test.TestDetourMethod();
    }
}

Console.WriteLine();

using (new DetourFactoryContext(DetourFactory.Current).Use()) {

    using (new DetourConfigContext(new("fwTest")).Use())
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

    using (var detour = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target(null!),
        config: new("fwTest2")))
    using (var detour2 = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target2(null!),
        config: new DetourConfig("fwTest3", priority: 4).WithBefore("fwTest2"))) {
        var test = new TestClass();
        _ = test.TestDetourMethod();
        detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
        detour2.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    }
}

Console.WriteLine();

using (var detour = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target(null!),
    config: new("fwTest2")))
using (var detour2 = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target2(null!),
    config: new DetourConfig("fwTest3", priority: 4).WithBefore("fwTest2")))
using (var detour3 = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target3(null!)))
using (var detour4 = new Detour(() => new TestClass().TestDetourMethod(), () => TestClass.Target4(null!))) {
    var test = new TestClass();
    _ = test.TestDetourMethod();
    detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    detour2.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    detour3.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    detour4.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
}

/*
#if NET45_OR_GREATER
//using System.Threading.Tasks;

var sb = new StringBuilder();
var sw = new StringWriter();

using (new Hook(() => default(TextWriter)!.WriteLineAsync(ArrayEx.Empty<char>()), (Delegate)WriteLineAsyncPatch)) {
    var chrs = new char[] { 'a', 'b', 'c' };
    await sw.WriteLineAsync(chrs);
}

static Task WriteLineAsyncPatch(Func<TextWriter, char[], Task> orig, TextWriter writer, char[] buffer) {
    Console.WriteLine("WriteLineAsync called");
    return orig(writer, buffer);
}
#endif
*/

Console.WriteLine("Done!");


class TestClass {
    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public virtual FunkyStruct TestDetourMethod() {
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
    public static FunkyStruct TargetHook(Func<TestClass?, FunkyStruct> orig, TestClass? self) {
        if (self is null) {
            Console.WriteLine("self is null");
        } else {
            Console.WriteLine("self is not null");
        }
        // TODO: Mono dies on the generated call to AppendFormatted for some reason
        Console.WriteLine($"Method successfully detoured {self} hook");
        return orig(self);
    }

    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public static FunkyStruct Target2(TestClass self) {
        Console.WriteLine($"Method successfully detoured {self} 2");
        return default;
    }

    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public static FunkyStruct Target3(TestClass self) {
        Console.WriteLine($"Method successfully detoured {self} 3");
        return default;
    }

    [MethodImpl(MethodImplOptionsEx.NoInlining)]
    public static FunkyStruct Target4(TestClass self) {
        Console.WriteLine($"Method successfully detoured {self} 4");
        return default;
    }
}

struct FunkyStruct {
    public long A;
    public byte B;
}