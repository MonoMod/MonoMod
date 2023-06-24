using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Immutable;
using System.Threading;

namespace MonoMod.Packer.Entities {
    internal abstract class EntityBase {
        public readonly TypeEntityMap Map;

        protected EntityBase(TypeEntityMap map) {
            Map = map;
        }

        public abstract Utf8String? Name { get; }

        private EntityInitializationState updatingState;

        protected bool HasState(EntityInitializationState state) => (updatingState & state) != 0;
        protected void MarkState(EntityInitializationState state) {
            ref var fieldRef = ref Unsafe.As<EntityInitializationState, int>(ref updatingState);
            int prev, next;
            do {
                prev = Volatile.Read(ref fieldRef);
                next = prev | (int)state;
            } while (Interlocked.CompareExchange(ref fieldRef, next, prev) != prev);
        }

        public EntityBase GetUnified() => GetUnifiedCore();
        protected abstract EntityBase GetUnifiedCore();

        private ImmutableArray<ModuleDefinition> lazyContributingModules;
        public ImmutableArray<ModuleDefinition> ContributingModules {
            get {
                if (lazyContributingModules.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyContributingModules,
                        MakeContributingModules()
                    );
                }
                return lazyContributingModules;
            }
        }

        protected abstract ImmutableArray<ModuleDefinition> MakeContributingModules();
    }
}
