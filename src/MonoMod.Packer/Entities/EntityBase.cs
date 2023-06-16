using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Immutable;

namespace MonoMod.Packer.Entities {
    internal abstract class EntityBase {
        public readonly TypeEntityMap Map;

        protected EntityBase(TypeEntityMap map) {
            Map = map;
        }

        public abstract Utf8String? Name { get; }

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
