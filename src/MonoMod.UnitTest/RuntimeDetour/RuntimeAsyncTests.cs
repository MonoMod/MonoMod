#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;

using Mono.Cecil.Cil;
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
    }
}
