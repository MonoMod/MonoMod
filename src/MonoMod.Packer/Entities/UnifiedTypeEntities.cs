using AsmResolver;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class UnifiedTypeEntities : EntityBase {
        private string DebuggerDisplay() => "Unified " + types[0].Definition.FullName;

        private readonly IReadOnlyList<TypeEntity> types;

        public UnifiedTypeEntities(TypeEntityMap map, IReadOnlyList<TypeEntity> types) : base(map) {
            Helpers.Assert(types.Count > 0);
            this.types = types;
        }

        public Utf8String? Namespace => types[0].Definition.Namespace;
        public Utf8String? Name => types[0].Definition.Name;


        private ImmutableArray<UnifiedTypeEntities> lazyNestedTypes;
        public ImmutableArray<UnifiedTypeEntities> NestedTypes {
            get {
                if (lazyNestedTypes.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyNestedTypes,
                        MakeNestedTypes()
                    );
                }

                return lazyNestedTypes;
            }
        }

        private ImmutableArray<UnifiedTypeEntities> MakeNestedTypes() {
            var dict = new Dictionary<NullableUtf8String, List<TypeEntity>>();
            foreach (var type in types) {
                foreach (var nested in type.NestedTypes) {
                    Helpers.DAssert(nested.Definition.Namespace is null);
                    if (!dict.TryGetValue(nested.Definition.Name, out var list)) {
                        dict.Add(nested.Definition.Name, list = new());
                    }
                    list.Add(nested);
                }
            }

            return dict.Values.Select(l => new UnifiedTypeEntities(Map, l)).ToImmutableArray();
        }

        [Flags]
        private enum Completion {
            None = 0,

            BeginMergeTypes = 1 << 0,
            EndMergeTypes = 1 << 1,

            All = BeginMergeTypes | EndMergeTypes,
        }

        private static Completion NextIncomplete(Completion completion, Completion filter = Completion.All) {
            var val = ~completion & filter;
            return val & ~(val - 1);
        }

        // TODO: make all of this thread-safe?

        private Completion completion;
        private void MarkComplete(Completion value) {
            Helpers.DAssert(BitOperations.PopCount((uint)(int)value) == 1);
            completion |= value;
        }

        public void Complete() {
            while (true) {
                var incomplete = NextIncomplete(completion);
                switch (incomplete) {
                    case Completion.None:
                        return;

                    case Completion.BeginMergeTypes:
                    case Completion.EndMergeTypes:
                        EnsureTypesMerged();
                        Helpers.DAssert(completion.Has(Completion.BeginMergeTypes));
                        Helpers.DAssert(completion.Has(Completion.EndMergeTypes));
                        break;

                    default:
                        throw new InvalidOperationException(incomplete.ToString());
                }
            }
        }

        private void EnsureTypesMerged() {
            if (completion.Has(Completion.BeginMergeTypes)) {
                return;
            }

            MarkComplete(Completion.BeginMergeTypes);

            MergeTypesCore();

            MarkComplete(Completion.EndMergeTypes);
        }

        private ImmutableArray<MergedTypeEntity> lazyMergedTypes;
        private ImmutableDictionary<TypeEntity, MergedTypeEntity>? lazyTypesToMerged;

        private void MergeTypesCore() {
            throw new NotImplementedException();
        }

        public ThreeState IsUnifiedWith(TypeEntity a, TypeEntity b) {
            Helpers.DAssert(types.Contains(a));
            Helpers.DAssert(types.Contains(b));

            EnsureTypesMerged();

            Helpers.DAssert(completion.Has(Completion.BeginMergeTypes));
            if (!completion.Has(Completion.EndMergeTypes)) {
                // this is being called while trying to unify this type; return a tentative "yes"
                // if, later on, this proves to be incorrect, we can bail then, and redo everything knowing that it can't
                return ThreeState.Maybe;
            }

            throw new NotImplementedException();
        }
    }
}
