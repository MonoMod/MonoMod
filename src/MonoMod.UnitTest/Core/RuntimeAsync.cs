using Mono.Cecil.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core;

public class RuntimeAsync(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void UtilsRuntimeAsyncMatchesCore()
    {
        if (PlatformDetection.Runtime is RuntimeKind.CoreCLR
            && PlatformDetection.RuntimeVersion.Major == 10)
        {
            // .NET 10 technically supports runtime async as opt-in, but we don't support that configuration.
            return;
        }

        Assert.True(Utils.RuntimeAsync.IsSupported == PlatformTriple.Current.SupportedFeatures.Has(RuntimeFeature.HasRuntimeAsync));
    }

    [SkipIfNoRuntimeAsyncFact]
    public void Test()
    {
        if (!PlatformTriple.Current.SupportedFeatures.Has(RuntimeFeature.HasRuntimeAsync))
            return;

        var method = typeof(RuntimeAsync).GetMethod(nameof(AsyncMeth), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Helpers.Assert(method is not null);

        Assert.True(PlatformTriple.Current.Runtime.TryGetRuntimeAsyncInfo(method, out var info));

        var target = PlatformTriple.Current.Runtime.GetAsyncVariantRealDetourTarget(
            new(method, info.AsyncVariant,
                method, info.AsyncVariant,
                (nint)0),
            AbiArgumentMap.Identity,
            out var hnd);

    }

    private static async Task AsyncMeth()
    {
        await Task.Delay(5).ConfigureAwait(false);
    }

    [SkipIfNoRuntimeAsyncFact]
    public async Task TestAsyncDmd()
    {
        var method = typeof(RuntimeAsync).GetMethod(nameof(AsyncMeth), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Helpers.Assert(method is not null);

        MethodInfo compiled;
        using (var dmd = new DynamicMethodDefinition("test async", typeof(Task), []))
        {
            dmd.IsAsync = true;
            var ilp = dmd.GetILProcessor();

            var asyncCall = dmd.Module.ImportReference(method);
            ilp.Emit(OpCodes.Call, asyncCall);
            Utils.RuntimeAsync.EmitAwait(ilp, asyncCall.ReturnType);
            ilp.Emit(OpCodes.Ret);

            compiled = dmd.Generate();
        }

        var result = compiled.Invoke(null, null);
        await Assert.IsAssignableFrom<Task>(result);
    }
}

public sealed class SkipIfNoRuntimeAsyncFactAttribute : FactAttribute
{
    public SkipIfNoRuntimeAsyncFactAttribute()
    {
        if (!Utils.RuntimeAsync.IsSupported)
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
