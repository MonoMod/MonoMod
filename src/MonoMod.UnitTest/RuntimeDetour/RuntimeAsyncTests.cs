#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;

using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public sealed class SkipIfNoRuntimeAsyncFactAttribute : FactAttribute
    {
        public SkipIfNoRuntimeAsyncFactAttribute()
        {
            if (!RuntimeAsync.IsSupported)
            {
                Skip = "RuntimeAsync is not supported on the current runtime.";
            }

            if (PlatformDetection.Runtime is RuntimeKind.CoreCLR
                && PlatformDetection.RuntimeVersion.Major == 10)
            {
                // .NET 10 technically supports runtime async as opt-in, but we don't support that configuration.
                Skip = "RuntimeAsync is not enabled on the current runtime.";
            }
        }
    }

    [Collection("RuntimeDetour")]
    public class RuntimeAsyncTests : TestBase
    {
        private const MethodImplAttributes AsyncImpl = (MethodImplAttributes)RuntimeAsync.AsyncIl;

        public RuntimeAsyncTests(ITestOutputHelper helper) : base(helper)
        {
        }

        public void UtilsRuntimeAsyncMatchesCore()
        {
            if (PlatformDetection.Runtime is RuntimeKind.CoreCLR
                && PlatformDetection.RuntimeVersion.Major == 10)
            {
                // .NET 10 technically supports runtime async as opt-in, but we don't support that configuration.
                return;
            }

            Assert.True(RuntimeAsync.IsSupported == PlatformTriple.Current.SupportedFeatures.Has(MonoMod.Core.Platforms.RuntimeFeature.HasRuntimeAsync));
        }
        private static async Task AsyncMeth()
        {
            await Task.Delay(5).ConfigureAwait(false);
        }

        [SkipIfNoRuntimeAsyncFact]
        public async Task TestAsyncDmd()
        {
            var method = GetMethod(nameof(AsyncMeth));
            Helpers.Assert(method is not null);

            MethodInfo compiled;
            using (var dmd = new DynamicMethodDefinition("test async", typeof(Task), []))
            {
                dmd.IsAsync = true;
                var ilp = dmd.GetILProcessor();

                var asyncCall = dmd.Module.ImportReference(method);
                ilp.Emit(OpCodes.Call, asyncCall);
                RuntimeAsync.EmitAwait(ilp, asyncCall.ReturnType);
                ilp.Emit(OpCodes.Ret);

                compiled = dmd.Generate();
            }

            var result = compiled.Invoke(null, null);
            await Assert.IsAssignableFrom<Task>(result).ConfigureAwait(false);
        }

        [SkipIfNoRuntimeAsyncFact]
        public void RuntimeAsyncMethodHasAsyncImplAndIL()
        {
            var method = GetMethod(nameof(CompletedSum));
            Assert.True(
                method.MethodImplementationFlags.HasFlag(AsyncImpl),
                "CompletedSum was not compiled as MethodImpl.Async. " +
                "The test project must set Features=runtime-async=on for net11.0.");

            var body = method.GetMethodBody();
            Assert.NotNull(body);
            var il = body!.GetILAsByteArray();
            Assert.NotNull(il);
            Assert.NotEmpty(il);
        }

        [Fact]
        public void HookOnRuntimeAsyncFacadeAppliesToReflectedCall()
        {
            var method = GetMethod(nameof(CompletedSum));
            var call = method.CreateDelegate<Func<int, int, Task<int>>>();
            Assert.Equal(3, Run(call(1, 2)));

            var hookEntered = false;
            using (new Hook(
                method,
                new Func<Func<int, int, Task<int>>, int, int, Task<int>>((_, a, b) =>
                {
                    hookEntered = true;
                    return Task.FromResult(a + b + 10);
                })
            ))
            {
                Assert.Equal(13, Run(call(1, 2)));
                Assert.True(hookEntered);
            }

            Assert.Equal(3, Run(call(1, 2)));
        }

        [Fact]
        public void CompiledAsyncCallSiteIsHooked()
        {
            var method = GetMethod(nameof(CompletedSum));

            using (new Hook(
                method,
                new Func<Func<int, int, Task<int>>, int, int, Task<int>>((_, a, b) =>
                    Task.FromResult(a + b + 10))
            ))
            {
                var call = method.CreateDelegate<Func<int, int, Task<int>>>();
                Assert.Equal(13, Run(call(1, 2)));
                Assert.Equal(13, Run(CallCompletedSumFromAsync(1, 2)));
            }
        }

        [Fact]
        public void HookSynchronousTaskReturningMethod()
        {
            var method = GetMethod(nameof(SyncReturnsTask));
            var call = method.CreateDelegate<Func<int, int, Task<int>>>();
            Assert.Equal(21, Run(call(7, 3)));

            using (new Hook(
                method,
                new Func<Func<int, int, Task<int>>, int, int, Task<int>>((orig, a, b) => orig(a, b + 1))
            ))
            {
                Assert.Equal(28, Run(call(7, 3)));
            }

            Assert.Equal(21, Run(call(7, 3)));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<int> CompletedSum(int a, int b)
        {
            return a + b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Task<int> SyncReturnsTask(int a, int b)
        {
            return Task.FromResult(a * b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<int> CallCompletedSumFromAsync(int a, int b)
        {
            return await CompletedSum(a, b).ConfigureAwait(false);
        }

        private static int Run(Task<int> task) => task.GetAwaiter().GetResult();

        private static MethodInfo GetMethod(string name)
            => typeof(RuntimeAsyncTests).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;

        private static int asyncNoResultCalls;
        private static int asyncTaskIntCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task AsyncNoResult()
        {
            asyncNoResultCalls++;
            await Task.Yield();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Task<int> TaskIntTarget()
        {
            asyncTaskIntCalls++;
            return Task.FromResult(42);
        }

        [Fact]
        public async Task CovariantTaskReturnsAreSupported()
        {
            using (DetourFactory.Current.CreateDetour(GetMethod(nameof(AsyncNoResult)), GetMethod(nameof(TaskIntTarget))))
            {
                asyncNoResultCalls = 0;
                asyncTaskIntCalls = 0;
                await AsyncNoResult();
                Assert.Equal(0, asyncNoResultCalls);
                Assert.Equal(1, asyncTaskIntCalls);
            }
        }

        private static TaskCompletionSource<int> osrGate = new();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<int> OsrLoop(int n)
        {
            var s = await osrGate.Task.ConfigureAwait(false);
            for (var i = 0; i < n; i++)
                s += i ^ (s >> 3);
            return s;
        }

        [SkipIfNoRuntimeAsyncFact]
        public void SuspendedOriginalSurvivesOsrWhileHooked()
        {
            var expected = 3;
            for (var i = 0; i < 1_000_000; i++)
                expected += i ^ (expected >> 3);

            osrGate = new();
            var pending = OsrLoop(1_000_000);
            using (new Hook(GetMethod(nameof(OsrLoop)), new Func<Func<int, Task<int>>, int, Task<int>>((orig, n) => Task.FromResult(-1))))
            {
                osrGate.SetResult(3);
                Assert.Equal(expected, Run(pending));
            }
        }

        private static MethodInfo AwaitDmd(Type awaitable, string source, bool cecil)
        {
            using var dmd = new DynamicMethodDefinition("Await", typeof(Task<int>), []);
            dmd.IsAsync = true;
            if (cecil)
            {
                var il = dmd.GetILProcessor();
                il.Emit(OpCodes.Call, dmd.Module.ImportReference(GetMethod(source)));
                RuntimeAsync.EmitAwait(il, dmd.Module.ImportReference(awaitable));
                il.Emit(OpCodes.Ret);
            }
            else
            {
                var il = dmd.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Call, GetMethod(source));
                RuntimeAsync.EmitAwait(il, awaitable);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
            }
            return dmd.Generate();
        }

        private sealed class IntAwaiter : INotifyCompletion
        {
            private readonly int value;
            public IntAwaiter(int value) => this.value = value;
            public bool IsCompleted => true;
            public int GetResult() => value;
            public void OnCompleted(Action continuation) => continuation();
        }

        private struct StructAwaitable
        {
            public int Value;
            public IntAwaiter GetAwaiter() => new(Value);
        }

        private static StructAwaitable GetStructAwaitable() => new() { Value = 4 };

        [SkipIfNoRuntimeAsyncFact]
        public void EmitAwaitStructAwaitable()
        {
            var method = AwaitDmd(typeof(StructAwaitable), nameof(GetStructAwaitable), cecil: false);
            Assert.Equal(4, Run((Task<int>)method.Invoke(null, null)!));
        }

        // 4. EmitAwait emits call where C# emits callvirt, so an overridden GetAwaiter is skipped (also: await null doesn't throw,
        // interface awaiters give "Bad IL format"). Same for IsCompleted/GetResult, both overloads.
        // Fix: callvirt when the receiver isn't a value type.
        private class BaseAwaitable
        {
            public virtual IntAwaiter GetAwaiter() => new(1);
        }

        private sealed class DerivedAwaitable : BaseAwaitable
        {
            public override IntAwaiter GetAwaiter() => new(2);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
            Justification = "The point is to return the base, so the await emitter sees only the base")]
        private static BaseAwaitable GetDerivedAwaitable() => new DerivedAwaitable();

        [SkipIfNoRuntimeAsyncFact]
        public void EmitAwaitVirtualGetAwaiter()
        {
            var method = AwaitDmd(typeof(BaseAwaitable), nameof(GetDerivedAwaitable), cecil: false);
            Assert.Equal(2, Run((Task<int>)method.Invoke(null, null)!));
        }


        private sealed class GenericAwaiter<T> : INotifyCompletion
        {
            private readonly T value;
            public GenericAwaiter(T value) => this.value = value;
            public bool IsCompleted => true;
            public T GetResult() => value;
            public void OnCompleted(Action continuation) => continuation();
        }

        private sealed class GenericAwaitable<T>
        {
            private readonly T value;
            public GenericAwaitable(T value) => this.value = value;
            public GenericAwaiter<T> GetAwaiter() => new(value);
        }

        private class GenericAwaitableBase<T>
        {
            private readonly T value;
            public GenericAwaitableBase(T value) => this.value = value;
            public GenericAwaiter<T> GetAwaiter() => new(value);
        }

        private sealed class GenericDerivedAwaitable<T> : GenericAwaitableBase<T>
        {
            public GenericDerivedAwaitable(T value) : base(value) { }
        }

        private static GenericAwaitable<int> GetGenericAwaitable() => new(9);
        private static GenericDerivedAwaitable<int> GetGenericDerivedAwaitable() => new(11);

        [SkipIfNoRuntimeAsyncFact]
        public void CecilEmitAwaitGenericAwaitable()
        {
            var method = AwaitDmd(typeof(GenericAwaitable<int>), nameof(GetGenericAwaitable), cecil: true);
            Assert.Equal(9, Run((Task<int>)method.Invoke(null, null)!));
        }

        [SkipIfNoRuntimeAsyncFact]
        public void CecilEmitAwaitKeepsGenericMemberSignatureOpen()
        {
            using var dmd = new DynamicMethodDefinition("Await", typeof(Task<int>), []);
            dmd.IsAsync = true;
            var il = dmd.GetILProcessor();
            il.Emit(OpCodes.Call, dmd.Module.ImportReference(GetMethod(nameof(GetGenericAwaitable))));
            RuntimeAsync.EmitAwait(il, dmd.Module.ImportReference(typeof(GenericAwaitable<int>)));

            Mono.Cecil.MethodReference? getAwaiter = null;
            foreach (var instruction in il.Body.Instructions)
            {
                if (instruction.Operand is Mono.Cecil.MethodReference method && method.Name == "GetAwaiter")
                {
                    getAwaiter = method;
                    break;
                }
            }

            Assert.NotNull(getAwaiter);
            var declaringType = Assert.IsType<Mono.Cecil.GenericInstanceType>(getAwaiter!.DeclaringType);
            Assert.Equal(typeof(int).FullName, declaringType.GenericArguments[0].FullName);

            var returnType = Assert.IsType<Mono.Cecil.GenericInstanceType>(getAwaiter.ReturnType);
            var returnTypeArgument = Assert.IsType<Mono.Cecil.GenericParameter>(returnType.GenericArguments[0]);
            Assert.Equal(Mono.Cecil.GenericParameterType.Type, returnTypeArgument.Type);
            Assert.Equal(0, returnTypeArgument.Position);
        }

        [SkipIfNoRuntimeAsyncFact]
        public void CecilEmitAwaitInheritedGenericAwaitable()
        {
            var method = AwaitDmd(typeof(GenericDerivedAwaitable<int>), nameof(GetGenericDerivedAwaitable), cecil: true);
            Assert.Equal(11, Run((Task<int>)method.Invoke(null, null)!));
        }
    }
}
