#if NET11_0
#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    // .NET 11 runtime-async keeps two MethodDescs for a Task/ValueTask method:
    // the reflected facade and an async-convention variant (coreclr asyncthunks.cpp).
    // A compiled async call site uses the variant and never enters the facade.
    // Hooks on the reflected MethodInfo therefore only apply to the Task-returning
    // stub, and that stub's orig trampoline currently returns a boxed logical T
    // (or null) instead of a Task — so calling orig() as Task<T> NREs.
    [Collection("RuntimeDetour")]
    public class RuntimeAsyncStubTest : TestBase
    {
        // ECMA-335 MethodImplAttributes.Async = 0x2000.
        private const MethodImplAttributes AsyncImpl = (MethodImplAttributes)0x2000;

        public RuntimeAsyncStubTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
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
        public void CompiledAsyncCallSiteBypassesHookedTaskFacade()
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
                Assert.Equal(3, Run(CallCompletedSumFromAsync(1, 2)));
            }
        }

        [Fact]
        public void OrigTrampolineOnRuntimeAsyncFacadeThrows()
        {
            var method = GetMethod(nameof(CompletedSum));
            var call = method.CreateDelegate<Func<int, int, Task<int>>>();

            using (new Hook(
                method,
                new Func<Func<int, int, Task<int>>, int, int, Task<int>>((orig, a, b) =>
                {
                    // orig is typed as Task<int> but the trampoline invokes the async
                    // variant, which does not produce a Task. Using the result as a Task NREs.
                    Assert.Throws<NullReferenceException>(() => Run(orig(a, b)));
                    return Task.FromResult(a + b + 10);
                })
            ))
            {
                Assert.Equal(13, Run(call(1, 2)));
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
            => typeof(RuntimeAsyncStubTest).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
    }
}
#endif
