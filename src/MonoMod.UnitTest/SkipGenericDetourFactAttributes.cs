using MonoMod.Utils;
using System;
using Xunit;

namespace MonoMod.UnitTest
{
    /// <summary>
    /// Skips the test on runtimes where detours on closed generic types are not reliably applied
    /// (System Mono, and .NET Core 2.x where the hook silently does not take effect).
    /// Same limitation as documented in DetouringGenericMethods.StaticMethodOfSharedGenericType.
    /// </summary>
    public sealed class SkipMonoAndOldCoreClrFactAttribute : FactAttribute
    {
        public SkipMonoAndOldCoreClrFactAttribute()
        {
            if (PlatformDetection.Runtime is RuntimeKind.Mono || (PlatformDetection.Runtime is RuntimeKind.CoreCLR && PlatformDetection.RuntimeVersion < new Version(3, 0)))
            {
                Skip = "Detours on closed generic types are not reliably applied on Mono and .NET Core 2.x.";
            }
        }
    }

    /// <summary>
    /// Skips the test on Mono, which throws InvalidOperationException instead of ArgumentException
    /// when creating a detour on an unbound generic type.
    /// </summary>
    public sealed class SkipMonoFactAttribute : FactAttribute
    {
        public SkipMonoFactAttribute()
        {
            if (PlatformDetection.Runtime is RuntimeKind.Mono)
            {
                Skip = "Mono throws InvalidOperationException instead of ArgumentException for unbound generic types.";
            }
        }
    }
}
