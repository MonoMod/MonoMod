using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Utilities;
using MonoMod.Utils;
using System.Collections.Immutable;

namespace MonoMod.Packer.Entities {
    internal abstract class EntityBase {
        public readonly TypeEntityMap Map;

        protected EntityBase(TypeEntityMap map) {
            Map = map;
        }

        public abstract Utf8String? Name { get; }

        private EntityInitializationState updatingState;
        private EntityFlags flags;

        protected bool HasState(EntityInitializationState state) => updatingState.Has(state);
        protected void MarkState(EntityInitializationState state) {
            _ = InterlockedFlags.Set(ref updatingState, state);
        }

        protected bool GetFlag(EntityFlags flags) => this.flags.Has(flags);
        protected void SetFlag(EntityFlags flag, bool value)
            => _ = value
            ? InterlockedFlags.Set(ref flags, flag)
            : InterlockedFlags.Clear(ref flags, flag);

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
