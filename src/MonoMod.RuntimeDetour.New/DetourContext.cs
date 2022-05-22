using MonoMod.Core;
using MonoMod.RuntimeDetour.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace MonoMod.RuntimeDetour {
    public abstract class DetourContext {

        [ThreadStatic]
        private static Scope? current;

        public static DetourContext? Current => current?.Context;

        private class Scope {
            public readonly DetourContext Context;
            public readonly Scope? Prev;

            public Scope(DetourContext context, Scope? prev) {
                Context = context;
                Prev = prev;
            }
        }

        private sealed class ContextScopeHandler : ScopeHandlerBase {
            public static readonly ContextScopeHandler Instance = new();
            public override void EndScope(object? data) {
                current = current?.Prev;
            }
        }

        private static DataScope PushContext(DetourContext ctx) {
            current = new(ctx, current);
            return new(ContextScopeHandler.Instance, null);
        }

        public DataScope Use() => PushContext(this);



        public DetourConfig? DetourConfig => TryGetDetourConfig(out var cfg) ? cfg : null;
        protected abstract bool TryGetDetourConfig(out DetourConfig? config);

        public static DetourConfig? CurrentDetourConfig => TryGetCurrentDetourConfig(out var cfg) ? cfg : null;
        public static bool TryGetCurrentDetourConfig(out DetourConfig? config) {
            var scope = current;
            while (scope is not null) {
                if (scope.Context.TryGetDetourConfig(out var cfg)) {
                    config = cfg;
                    return true;
                }
                scope = scope.Prev;
            }
            config = null;
            return false;
        }



        public IDetourFactory? DetourFactory => TryGetDetourFactory(out var fac) ? fac : null;
        protected abstract bool TryGetDetourFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory);

        public static IDetourFactory? CurrentDetourFactory => TryGetCurrentDetourFactory(out var fac) ? fac : null;
        public static bool TryGetCurrentDetourFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory) {
            var scope = current;
            while (scope is not null) {
                if (scope.Context.TryGetDetourFactory(out var fac)) {
                    detourFactory = fac;
                    return true;
                }
                scope = scope.Prev;
            }
            detourFactory = null;
            return false;
        }
    }

    public class EmptyDetourContext : DetourContext {
        protected override bool TryGetDetourConfig(out DetourConfig? config) {
            config = null;
            return false;
        }

        protected override bool TryGetDetourFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory) {
            detourFactory = null;
            return false;
        }
    }

    public class DetourConfigContext : EmptyDetourContext {
        private readonly DetourConfig? cfg;
        public DetourConfigContext(DetourConfig? cfg) {
            this.cfg = cfg;
        }
        protected override bool TryGetDetourConfig(out DetourConfig? config) {
            config = cfg;
            return true;
        }
    }

    public class DetourFactoryContext : EmptyDetourContext {
        private readonly IDetourFactory fac;
        public DetourFactoryContext(IDetourFactory fac) {
            this.fac = fac;
        }
        protected override bool TryGetDetourFactory([MaybeNullWhen(false)] out IDetourFactory detourFactory) {
            detourFactory = fac;
            return true;
        }
    }
}
