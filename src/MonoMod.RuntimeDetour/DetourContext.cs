using MonoMod.Core;
using MonoMod.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public abstract class DetourContext {

        private static DetourContext? globalCurrent;

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static DetourContext? SetGlobalContext(DetourContext? context) {
            return Interlocked.Exchange(ref globalCurrent, context);
        }

        [ThreadStatic]
        private static Scope? current;

        public static DetourContext? Current => current?.Context ?? globalCurrent;

        private class Scope {
            public readonly DetourContext Context;
            public readonly Scope? Prev;

            public bool Active = true;

            public Scope(DetourContext context, Scope? prev) {
                Context = context;
                Prev = prev;
            }
        }

        private sealed class ContextScopeHandler : ScopeHandlerBase {
            public static readonly ContextScopeHandler Instance = new();
            public override void EndScope(object? data) {
                var scope = (Scope) data!;
                scope.Active = false;

                while (current is { Active: false })
                    current = current.Prev;
            }
        }

        private static DataScope PushContext(DetourContext ctx) {
            current = new(ctx, current);
            return new(ContextScopeHandler.Instance, current);
        }

        public DataScope Use() => PushContext(this);

        public static DetourConfig? GetDefaultConfig() {
            return TryGetCurrentConfig(out var cfg) ? cfg : null;
        }

        public static IDetourFactory GetDefaultFactory() {
            return TryGetCurrentFactory(out var fac) ? fac : DetourFactory.Current;
        }

        public DetourConfig? Config => TryGetConfig(out var cfg) ? cfg : null;
        protected abstract bool TryGetConfig(out DetourConfig? config);

        public static DetourConfig? CurrentConfig => TryGetCurrentConfig(out var cfg) ? cfg : null;
        public static bool TryGetCurrentConfig(out DetourConfig? config) {
            var scope = current;
            while (scope is not null) {
                if (scope.Active && scope.Context.TryGetConfig(out config)) {
                    return true;
                }
                scope = scope.Prev;
            }

            if (globalCurrent is { } gc && gc.TryGetConfig(out config)) {
                return true;
            }

            config = null;
            return false;
        }



        public IDetourFactory? Factory => TryGetFactory(out var fac) ? fac : null;
        protected abstract bool TryGetFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory);

        public static IDetourFactory? CurrentFactory => TryGetCurrentFactory(out var fac) ? fac : null;
        public static bool TryGetCurrentFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory) {
            var scope = current;
            while (scope is not null) {
                if (scope.Context.TryGetFactory(out detourFactory)) {
                    return true;
                }
                scope = scope.Prev;
            }

            if (globalCurrent is { } gc && gc.TryGetFactory(out detourFactory)) {
                return true;
            }

            detourFactory = null;
            return false;
        }
    }

    public class EmptyDetourContext : DetourContext {
        protected override bool TryGetConfig(out DetourConfig? config) {
            config = null;
            return false;
        }

        protected override bool TryGetFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory) {
            detourFactory = null;
            return false;
        }
    }

    public class DetourConfigContext : EmptyDetourContext {
        private readonly DetourConfig? cfg;
        public DetourConfigContext(DetourConfig? cfg) {
            this.cfg = cfg;
        }
        protected override bool TryGetConfig(out DetourConfig? config) {
            config = cfg;
            return true;
        }
    }

    public class DetourFactoryContext : EmptyDetourContext {
        private readonly IDetourFactory fac;
        public DetourFactoryContext(IDetourFactory fac) {
            this.fac = fac;
        }
        protected override bool TryGetFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory) {
            detourFactory = fac;
            return true;
        }
    }
}
