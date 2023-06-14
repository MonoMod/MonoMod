using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace MonoMod.Packer.Entities {
    internal sealed class UnifiedTypeEntities : EntityBase {
        private readonly IReadOnlyList<TypeEntity> types;

        public UnifiedTypeEntities(TypeEntityMap map, IReadOnlyList<TypeEntity> types) : base(map) {
            this.types = types;
        }

        private ImmutableArray<MergedTypeEntity> mergedTypes;
        public ImmutableArray<MergedTypeEntity> MergedTypes {
            get {
                if (mergedTypes.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref mergedTypes,
                        CreateMergedTypes()
                    );
                }
                return mergedTypes;
            }
        }

        private ManualResetEventSlim? waitHandle;

        private ImmutableArray<MergedTypeEntity> CreateMergedTypes() {
            throw new NotImplementedException();
        }
    }

    internal sealed class MergedTypeEntity : EntityBase {
        // TODO:
        private MergedTypeEntity(TypeEntityMap map) : base(map) { }
    }
}
