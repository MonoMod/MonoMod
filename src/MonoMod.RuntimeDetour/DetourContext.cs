using MonoMod.Core;
using MonoMod.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A persistent context which may be used to configure all detours created while active.
    /// </summary>
    public abstract class DetourContext
    {

        private static DetourContext? globalCurrent;

        /// <summary>
        /// Sets the global <see cref="DetourContext"/>, returning the old global context, if any.
        /// </summary>
        /// <param name="context">The <see cref="DetourContext"/> to make global.</param>
        /// <returns>The <see cref="DetourContext"/> which was previously global, if any.</returns>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static DetourContext? SetGlobalContext(DetourContext? context)
        {
            return Interlocked.Exchange(ref globalCurrent, context);
        }

        [ThreadStatic]
        private static Scope? current;

        /// <summary>
        /// Gets the <see cref="DetourContext"/> which is most local at the point which this is accessed.
        /// </summary>
        /// <remarks>
        /// <para>This will return the <see cref="DetourContext"/> which is on top of the context stack for the current thread,
        /// and if there are no contexts for the current thread, it will return the current global context, if any.</para>
        /// <para>This property has very limited use. Consider using <see cref="CurrentConfig"/>, <see cref="GetDefaultConfig"/>, 
        /// <see cref="CurrentFactory"/>, or <see cref="GetDefaultFactory"/> instead of this property.</para>
        /// </remarks>
        public static DetourContext? Current
        {
            get
            {
                var cur = current;
                while (cur is not null && !cur.Active)
                    cur = cur.Prev;
                return cur?.Context ?? globalCurrent;
            }
        }

        private sealed class Scope
        {
            public readonly DetourContext Context;
            public readonly Scope? Prev;

            public bool Active = true;

            public Scope(DetourContext context, Scope? prev)
            {
                Context = context;
                Prev = prev;
            }
        }

        private sealed class ContextScopeHandler : ScopeHandlerBase
        {
            public static readonly ContextScopeHandler Instance = new();
            public override void EndScope(object? data)
            {
                var scope = (Scope)data!;
                scope.Active = false;

                while (current is { Active: false })
                    current = current.Prev;
            }
        }

        private static DataScope PushContext(DetourContext ctx)
        {
            current = new(ctx, current);
            return new(ContextScopeHandler.Instance, current);
        }

        /// <summary>
        /// Pushes this detour context to the top of this thread's context stack, and returns a <see cref="DataScope"/>
        /// which can be used in a <see langword="using"/> block to automatically pop it from the context stack.
        /// </summary>
        /// <returns>A <see cref="DataScope"/> which manages the lifetime of this context on the context stack.</returns>
        [CLSCompliant(false)]
        public DataScope Use() => PushContext(this);

        /// <summary>
        /// Gets the default <see cref="DetourConfig"/> at this location, if any.
        /// </summary>
        /// <remarks>
        /// This method behaves similarly to <see cref="CurrentConfig"/>, but may fall back to some default value
        /// when no context provides a config.
        /// </remarks>
        /// <returns>The default <see cref="DetourConfig"/>, if any.</returns>
        public static DetourConfig? GetDefaultConfig()
        {
            return TryGetCurrentConfig(out var cfg) ? cfg : null;
        }

        /// <summary>
        /// Gets the default <see cref="IDetourFactory"/> at this location.
        /// </summary>
        /// <remarks>
        /// This method behaves similarly to <see cref="CurrentFactory"/>, except that it returns <see cref="DetourFactory.Current"/>
        /// when no context provides a factory.
        /// </remarks>
        /// <returns>The default <see cref="IDetourFactory"/>.</returns>
        public static IDetourFactory GetDefaultFactory()
        {
            return TryGetCurrentFactory(out var fac) ? fac : DetourFactory.Current;
        }

        /// <summary>
        /// Gets the <see cref="DetourConfig"/> provided by this <see cref="DetourContext"/>, if any.
        /// </summary>
        public DetourConfig? Config => TryGetConfig(out var cfg) ? cfg : null;
        /// <summary>
        /// Tries to get the <see cref="DetourConfig"/> provided by this context.
        /// </summary>
        /// <remarks>
        /// A context may return <see langword="true"/>, but return a <see langword="null"/> config. This is valid. This will cause config lookup to
        /// stop at this context, and return <see langword="null"/>, even if a later context would return one.
        /// </remarks>
        /// <param name="config">The <see cref="DetourConfig"/> provided by this context.</param>
        /// <returns><see langword="true"/> if this context was able to provide a <see cref="DetourConfig"/>; <see langword="false"/> otherwise.</returns>
        protected abstract bool TryGetConfig(out DetourConfig? config);

        /// <summary>
        /// Gets the <see cref="DetourConfig"/> active at the current location, if any.
        /// </summary>
        /// <remarks>
        /// This property is equivalent to the result of <see cref="TryGetCurrentConfig(out DetourConfig?)"/>, except
        /// that it returns <see langword="null"/> when no context provides a config.
        /// </remarks>
        public static DetourConfig? CurrentConfig => TryGetCurrentConfig(out var cfg) ? cfg : null;
        /// <summary>
        /// Tries to get the <see cref="DetourConfig"/> active at the current location.
        /// </summary>
        /// <remarks>
        /// This method walks up the context stack, calling <see cref="TryGetConfig(out DetourConfig?)"/> on each context it encounters.
        /// It then returns the result that the first context which returned <see langword="true"/> returned. If none did, it checks
        /// the global context.
        /// </remarks>
        /// <param name="config">The active <see cref="DetourConfig"/>.</param>
        /// <returns><see langword="true"/> if a <see cref="DetourConfig"/> was found; <see langword="false"/> otherwise.</returns>
        public static bool TryGetCurrentConfig(out DetourConfig? config)
        {
            var scope = current;
            while (scope is not null)
            {
                if (scope.Active && scope.Context.TryGetConfig(out config))
                {
                    return true;
                }
                scope = scope.Prev;
            }

            if (globalCurrent is { } gc && gc.TryGetConfig(out config))
            {
                return true;
            }

            config = null;
            return false;
        }

        /// <summary>
        /// Gets the <see cref="IDetourFactory"/> provided by this <see cref="DetourContext"/>, if any.
        /// </summary>
        public IDetourFactory? Factory => TryGetFactory(out var fac) ? fac : null;
        /// <summary>
        /// Tries to get the <see cref="IDetourFactory"/> provided by this context.
        /// </summary>
        /// <param name="detourFactory">The provided factory, if any.</param>
        /// <returns><see langword="true"/> if this context was able to provide a <see cref="IDetourFactory"/>; <see langword="false"/> otherwise.</returns>
        protected abstract bool TryGetFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory);

        /// <summary>
        /// Gets the <see cref="IDetourFactory"/> active at the current location, if any.
        /// </summary>
        /// <remarks>
        /// This property is equivalent to the result of <see cref="TryGetCurrentFactory(out IDetourFactory)"/>, except
        /// that it returns <see langword="null"/> when no context provides a factory.
        /// </remarks>
        public static IDetourFactory? CurrentFactory => TryGetCurrentFactory(out var fac) ? fac : null;
        /// <summary>
        /// Tries to get the <see cref="IDetourFactory"/> active at the current location.
        /// </summary>
        /// <remarks>
        /// This method walks up the context stack, calling <see cref="TryGetFactory(out IDetourFactory)"/> on each context it encounters.
        /// It then returns the result that the first context which returned <see langword="true"/> returned. If none did, it checks
        /// the global context.
        /// </remarks>
        /// <param name="detourFactory">The active <see cref="IDetourFactory"/>.</param>
        /// <returns><see langword="true"/> if an <see cref="IDetourFactory"/> was found; <see langword="false"/> otherwise.</returns>
        public static bool TryGetCurrentFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory)
        {
            var scope = current;
            while (scope is not null)
            {
                if (scope.Context.TryGetFactory(out detourFactory))
                {
                    return true;
                }
                scope = scope.Prev;
            }

            if (globalCurrent is { } gc && gc.TryGetFactory(out detourFactory))
            {
                return true;
            }

            detourFactory = null;
            return false;
        }
    }

    /// <summary>
    /// A <see cref="DetourContext"/> base class which does not resolve any values for the context.
    /// </summary>
    public abstract class EmptyDetourContext : DetourContext
    {
        /// <inheritdoc/>
        protected override bool TryGetConfig(out DetourConfig? config)
        {
            config = null;
            return false;
        }
        /// <inheritdoc/>
        protected override bool TryGetFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory)
        {
            detourFactory = null;
            return false;
        }
    }

    /// <summary>
    /// A <see cref="DetourContext"/> which unconditionally resolves a <see cref="DetourConfig"/>.
    /// </summary>
    public class DetourConfigContext : EmptyDetourContext
    {
        private readonly DetourConfig? cfg;
        /// <summary>
        /// Constructs a <see cref="DetourConfigContext"/> which resolves the provided <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="cfg">The <see cref="DetourConfig"/> to resolve. If this is <see langword="null"/>, the resolved config is <see langword="null"/>.</param>
        public DetourConfigContext(DetourConfig? cfg)
        {
            this.cfg = cfg;
        }
        /// <inheritdoc/>
        protected override bool TryGetConfig(out DetourConfig? config)
        {
            config = cfg;
            return true;
        }
    }

    /// <summary>
    /// A <see cref="DetourContext"/> which unconditionally resolves a <see cref="IDetourFactory"/>.
    /// </summary>
    public class DetourFactoryContext : EmptyDetourContext
    {
        private readonly IDetourFactory fac;
        /// <summary>
        /// Constructs a <see cref="DetourFactoryContext"/> which resolves the provided <see cref="IDetourFactory"/>.
        /// </summary>
        /// <param name="fac">The <see cref="IDetourFactory"/> to resolve.</param>
        public DetourFactoryContext(IDetourFactory fac)
        {
            this.fac = fac;
        }
        /// <inheritdoc/>
        protected override bool TryGetFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory)
        {
            detourFactory = fac;
            return true;
        }
    }
}
