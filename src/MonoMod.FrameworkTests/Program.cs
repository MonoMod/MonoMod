#define MM
//#define HARMONY

#if MM
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
#endif
#if HARMONY
extern alias harmony;
using harmony::HarmonyLib;
#endif

using MonoMod.Backports;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#if false && NETFRAMEWORK
internal static class Program { 
[LoaderOptimization(LoaderOptimization.MultiDomainHost)]
public static void Main() {
#endif

Console.WriteLine("Attach debugger now, then press enter to break");
Console.ReadLine();
if (Debugger.IsAttached) {
    Debugger.Break();
}

#if true

// do this nice and early so we can manage to track it in windbg
_ = TestClass.TargetHook(_ => default, null);
_ = TestClass.TargetHook(_ => default, new());

var platform = PlatformTriple.Current;

var memAlloc = platform.System.MemoryAllocator;

if (memAlloc.TryAllocateInRange(new((nint) 0x1000000000uL, (nint) 0x10000, (nint) 0xffffffffffuL, 16), out var allocated)) {
    Console.WriteLine($"Allocated at {allocated.BaseAddress:x16}");
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
    using (var detour = new Hook(method, method2)) {
        var test = new TestClass();
        _ = test.TestDetourMethod();
        //detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    }

    Console.WriteLine();

    {
        var test = new TestClass();
        _ = test.TestDetourMethod();
    }

    Console.WriteLine();

    using (var detour = new Hook(() => new TestClass().TestDetourMethod(), () => TestClass.Target(null!),
        config: new("fwTest2")))
    using (var detour2 = new Hook(() => new TestClass().TestDetourMethod(), () => TestClass.Target2(null!),
        config: new DetourConfig("fwTest3", priority: 4).WithBefore("fwTest2"))) {
        var test = new TestClass();
        _ = test.TestDetourMethod();
        //detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
        //detour2.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    }
}

Console.WriteLine();

using (var detour = new Hook(() => new TestClass().TestDetourMethod(), () => TestClass.Target(null!),
    config: new("fwTest2")))
using (var detour2 = new Hook(() => new TestClass().TestDetourMethod(), () => TestClass.Target2(null!),
    config: new DetourConfig("fwTest3", priority: 4).WithBefore("fwTest2")))
using (var detour3 = new Hook(() => new TestClass().TestDetourMethod(), () => TestClass.Target3(null!)))
using (var detour4 = new Hook(() => new TestClass().TestDetourMethod(), () => TestClass.Target4(null!))) {
    var test = new TestClass();
    _ = test.TestDetourMethod();
    //detour.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    //detour2.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    //detour3.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
    //detour4.GenerateTrampoline<Func<TestClass, FunkyStruct>>()(test);
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
#else
// FX AppDomain shenanigans
#if NETFRAMEWORK

#if MM
_ = PlatformTriple.Current;
#endif
#if HARMONY
var hm = new Harmony("firstDomain");
#endif

var st = new StackTrace(0, true);
Console.WriteLine(st);

var domain = AppDomain.CreateDomain("dom2", AppDomain.CurrentDomain.Evidence, AppDomain.CurrentDomain.SetupInformation);
var domainCodeType = typeof(DomainHookCode);
var code = (DomainHookCode) domain.CreateInstanceAndUnwrap(domainCodeType.Assembly.FullName, domainCodeType.FullName);

code.DoPatch();
DoInvokeToUpperInvariant();

GC.KeepAlive(code);
GC.KeepAlive(domain);

[MethodImpl(MethodImplOptionsEx.NoInlining)]
static void DoInvokeToUpperInvariant() {
    var res = "hello, world!".ToUpperInvariant();
    Console.WriteLine(res);
}

#if NETFRAMEWORK
}
}
#endif

public class DomainHookCode : MarshalByRefObject {
    private static void DoStackTraceInit() {
        var st = new StackTrace(0, true);
        Console.WriteLine(st);
    }

#if MM
    public DomainHookCode() {
        // We have to resolve a stack trace which asks for file info scan through a DMD to prevent hard-crashes when doing domain shenanigans
        using (var dmd = new DynamicMethodDefinition("test dmd", typeof(void), ArrayEx.Empty<Type>())) {
            var il = dmd.GetILProcessor();
            using var typedRef = il.EmitNewTypedReference(DoStackTraceInit, out _);
            il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod(nameof(Action.Invoke))!);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(GC).GetMethod(nameof(GC.KeepAlive))!);
            il.Emit(OpCodes.Ret);
            _ = dmd.Generate().Invoke(null, null);
        }
        _ = ToUpperHook(null!, "");
    }

    private Hook? hook;
    public void DoPatch() {
        Console.WriteLine("Doing patch");
        hook = new(() => "".ToUpperInvariant(), () => ToUpperHook(null!, null!));
    }

    private static string ToUpperHook(Func<string, string> orig, string inst) {
        if (orig is null)
            return "";
        Debugger.Break();
        var trace = new StackTrace(0, true);
        return orig(inst) + " hooked " + trace;
    }
#endif
#if HARMONY
    public DomainHookCode() => DoStackTraceInit();
    private Harmony? harmony;
    public void DoPatch() {
        harmony = new("domain hook");
        var method = AccessTools.DeclaredMethod(typeof(string), nameof(string.ToUpperInvariant));
        var prefix = AccessTools.DeclaredMethod(typeof(DomainHookCode), nameof(ToUpperPrefix));
        var postfix = AccessTools.DeclaredMethod(typeof(DomainHookCode), nameof(ToUpperPostfix));
        harmony.Patch(method, prefix: new(prefix), postfix: new(postfix));
    }

    private static void ToUpperPrefix(out string __state) {
        Debugger.Break();
        var trace = new StackTrace(0, true);
        __state = " hooked " + trace;
    }

    private static void ToUpperPostfix(ref string __result, string __state) {
        __result += __state;
    }
#endif
}

#endif

#endif