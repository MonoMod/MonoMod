using MonoMod.Backports;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core
{
    /// <summary>
    /// A factory for creating <see cref="ICoreDetour"/>s and <see cref="ICoreNativeDetour"/>s.
    /// </summary>
    [CLSCompliant(true)]
    public interface IDetourFactory
    {
        /// <summary>
        /// Creates an <see cref="ICoreDetour"/> according to the arguments specified in <paramref name="request"/>.
        /// </summary>
        /// <param name="request">The <see cref="CreateDetourRequest"/> containing detour creation options.</param>
        /// <returns>The created <see cref="ICoreDetour"/>.</returns>
        ICoreDetour CreateDetour(CreateDetourRequest request);
        /// <summary>
        /// Creates an <see cref="ICoreNativeDetour"/> according to the arguments specified in <paramref name="request"/>.
        /// </summary>
        /// <param name="request">The <see cref="CreateNativeDetourRequest"/> containing detour creation options.</param>
        /// <returns>The created <see cref="ICoreNativeDetour"/>.</returns>
        ICoreNativeDetour CreateNativeDetour(CreateNativeDetourRequest request);
        /// <summary>
        /// Gets whether this <see cref="IDetourFactory"/> can create <see cref="ICoreNativeDetour"/>s with an <see cref="ICoreNativeDetour.OrigEntrypoint"/>.
        /// </summary>
        bool SupportsNativeDetourOrigEntrypoint { get; }
    }

    /// <summary>
    /// A request to create an <see cref="ICoreDetour"/>.
    /// </summary>
    /// <param name="Source">The source method for the detour.</param>
    /// <param name="Target">The target method for the detour.</param>
    /// <seealso cref="IDetourFactory.CreateDetour(CreateDetourRequest)"/>
    [CLSCompliant(true)]
    public readonly record struct CreateDetourRequest(MethodBase Source, MethodBase Target)
    {
        /// <summary>
        /// Gets or sets whether or not the detour should be applied when <see cref="IDetourFactory.CreateDetour(CreateDetourRequest)"/> returns.
        /// Defaults to <see langword="true"/>.
        /// </summary>
        public bool ApplyByDefault { get; init; } = true;

        /// <summary>
        /// Gets or sets whether the detour factory should create a clone of <see cref="Source"/> which, when called, behaves as-if
        /// the source was not detoured. If the <see cref="IDetourFactory"/> supports this, the created <see cref="ICoreDetour"/> will
        /// implement <see cref="ICoreDetourWithClone"/>, and the clone will be available from <see cref="ICoreDetourWithClone.SourceMethodClone"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <see cref="IDetourFactory"/> should only generate a clone if that clone is not strictly an IL clone.
        /// </para>
        /// <para>
        /// The <see cref="IDetourFactory"/> is not obligated to respect this option; it is permitted to ignore it. Clients which
        /// require this behavior should have a fallback path which performs an IL clone using <see cref="DynamicMethodDefinition"/>
        /// or another similar method.
        /// </para>
        /// </remarks>
        /// <seealso cref="ICoreDetourWithClone.SourceMethodClone"/>
        /// <seealso cref="DynamicMethodDefinition(MethodBase)"/>
        public bool CreateSourceCloneIfNotILClone { get; init; }
    }

    /// <summary>
    /// A request to create an <see cref="ICoreNativeDetour"/>.
    /// </summary>
    /// <param name="Source">The source function for the detour.</param>
    /// <param name="Target">The target function for the detour.</param>
    /// <seealso cref="IDetourFactory.CreateNativeDetour(CreateNativeDetourRequest)"/>
    [CLSCompliant(true)]
    public readonly record struct CreateNativeDetourRequest(IntPtr Source, IntPtr Target)
    {
        /// <summary>
        /// Gets or sets whether or not the detour should be applied when <see cref="IDetourFactory.CreateNativeDetour(CreateNativeDetourRequest)"/> returns.
        /// Defaults to <see langword="true"/>.
        /// </summary>
        public bool ApplyByDefault { get; init; } = true;
    }

    /// <summary>
    /// Provides access to a default, <see cref="PlatformTriple"/>-based <see cref="IDetourFactory"/>, as well as extension methods to make
    /// using <see cref="IDetourFactory"/> easier.
    /// </summary>
    [CLSCompliant(true)]
    public static class DetourFactory
    {
        // use the actual type for this so that an inlined getter can see the actual type
        private static PlatformTripleDetourFactory? lazyCurrent;
        /// <summary>
        /// Gets the current (default) <see cref="IDetourFactory"/>. This is always the <see cref="PlatformTriple"/>-based <see cref="IDetourFactory"/>.
        /// </summary>
        /// <remarks>
        /// The default <see cref="IDetourFactory"/> is created the first time this property is accessed, using the value of <see cref="PlatformTriple.Current"/>
        /// at that point in time. After it is constructed, the <see cref="PlatformTriple"/> implementation cannot be replaced.
        /// </remarks>
        /// <seealso cref="PlatformTriple.Current"/>
        public static unsafe IDetourFactory Current
        {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            get => Helpers.GetOrInit(ref lazyCurrent, createDefaultFactoryFunc);
        }

        private static readonly Func<PlatformTripleDetourFactory> createDefaultFactoryFunc = CreateDefaultFactory;
        private static PlatformTripleDetourFactory CreateDefaultFactory()
            => new(PlatformTriple.Current);

        /// <summary>
        /// Creates a managed detour from <paramref name="source"/> to <paramref name="target"/>.
        /// </summary>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use to perform the operation.</param>
        /// <param name="source">The source method for the detour.</param>
        /// <param name="target">The target method for the detour.</param>
        /// <param name="applyByDefault"><see langword="true"/> if the detour should be applied when this method returns;
        /// <see langword="false"/> if the caller must apply it themselves.</param>
        /// <returns>The created <see cref="ICoreDetour"/>.</returns>
        public static ICoreDetour CreateDetour(this IDetourFactory factory, MethodBase source, MethodBase target, bool applyByDefault = true)
        {
            Helpers.ThrowIfArgumentNull(factory);
            return factory.CreateDetour(new(source, target) { ApplyByDefault = applyByDefault });
        }

        /// <summary>
        /// Creates a native detour from <paramref name="source"/> to <paramref name="target"/>.
        /// </summary>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use to perform the operation.</param>
        /// <param name="source">The source function for the detour.</param>
        /// <param name="target">The target function for the detour.</param>
        /// <param name="applyByDefault"><see langword="true"/> if the detour should be applied when this method returns;
        /// <see langword="false"/> if the caller must apply it themselves.</param>
        /// <returns>The created <see cref="ICoreNativeDetour"/>.</returns>
        public static ICoreNativeDetour CreateNativeDetour(this IDetourFactory factory, IntPtr source, IntPtr target, bool applyByDefault = true)
        {
            Helpers.ThrowIfArgumentNull(factory);
            return factory.CreateNativeDetour(new(source, target) { ApplyByDefault = applyByDefault });
        }
    }
}
