using System;
namespace MonoMod.Core.Platforms;

// We reference a lot of types/fields/etc that don't exist in old frameworks in xmldocs.
#pragma warning disable CA1200

public readonly record struct RuntimeAsyncMethodInfo
{
    /// <summary>
    /// The normal method variant, if present.
    /// </summary>
    /// <remarks>
    /// <para>This variant does NOT use the async calling convention, and corresponds to the <see cref="T:System.Threading.Tasks.Task"/>
    /// or <see cref="T:System.Threading.Tasks.ValueTask"/> returning method.</para>
    /// <para>A method MAY not have this variant, for instance if it is an infrastructure method like
    /// <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await(System.Threading.Tasks.Task)"/>.</para>
    /// </remarks>
    public RuntimeMethodHandle NormalVariant { get; init; }

    /// <summary>
    /// The async method variant, if present.
    /// </summary>
    /// <remarks>
    /// <para>This variant uses the async calling convention, unwrapping the result type.</para>
    /// <para>Even methods which do not have the async implementation flag may have this variant.</para>
    /// </remarks>
    public RuntimeMethodHandle AsyncVariant { get; init; }

    public RuntimeAsyncVariantFlags NormalVariantFlags { get; init; }

    public RuntimeAsyncVariantFlags AsyncVariantFlags { get; init; }
}

[Flags]
public enum RuntimeAsyncVariantFlags
{
    None = 0,
    /// <summary>
    /// The corresponding variant is a thin thunk that does not need to be detoured for full coverage.
    /// </summary>
    /// <remarks>
    /// <para>On CoreCLR, the async variant generated for non-<see cref="F:System.Runtime.CompilerServices.MethodImplOptions.Async"/> methods
    /// is usually NOT a thunk, instead being separately compiled with the same IL with the extra runtime async machinery mappended. Thus,
    /// in that case, both variants MUST be detoured for full coverage.</para>
    /// </remarks>
    IsThunk = 1 << 0,
}
