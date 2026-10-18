using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core;

public class RuntimeAsyncInfo(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void Test()
    {
        if (!PlatformTriple.Current.SupportedFeatures.Has(RuntimeFeature.HasRuntimeAsync))
            return;

        var method = typeof(RuntimeAsyncInfo).GetMethod(nameof(AsyncMeth), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Helpers.Assert(method is not null);

        var result = PlatformTriple.Current.Runtime.TryGetRuntimeAsyncInfo(method, out var info);

    }

    private static async Task AsyncMeth()
    {
        await Task.Delay(5).ConfigureAwait(false);
    }
}
