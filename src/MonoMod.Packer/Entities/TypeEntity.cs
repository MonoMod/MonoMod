using AsmResolver.DotNet;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class TypeEntity : EntityBase {
        private string DebuggerDisplay() => Definition.FullName;

        public readonly TypeDefinition Definition;

        public TypeEntity(TypeEntityMap map, TypeDefinition def) : base(map) {
            Definition = def;
        }

        private MethodEntity CreateMethod(MethodDefinition m) => new(Map, m);

        private ImmutableArray<MethodEntity> lazyStaticMethods;
        public ImmutableArray<MethodEntity> StaticMethods {
            get {
                if (lazyStaticMethods.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyStaticMethods,
                        Definition.Methods
                            .Where(m => m.IsStatic)
                            .Select(CreateMethod)
                            .ToImmutableArray()
                    );
                }
                return lazyStaticMethods;
            }
        }

        private ImmutableArray<MethodEntity> lazyInstanceMethods;
        public ImmutableArray<MethodEntity> InstanceMethods {
            get {
                if (lazyInstanceMethods.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyInstanceMethods,
                        Definition.Methods
                            .Where(m => !m.IsStatic)
                            .Select(CreateMethod)
                            .ToImmutableArray()
                    );
                }
                return lazyInstanceMethods;
            }
        }

        private FieldEntity CreateField(FieldDefinition f) => new(Map, f);

        private ImmutableArray<FieldEntity> lazyStaticFields;
        public ImmutableArray<FieldEntity> StaticFields {
            get {
                if (lazyStaticFields.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyStaticFields,
                        Definition.Fields
                            .Where(f => f.IsStatic)
                            .Select(CreateField)
                            .ToImmutableArray()
                    );
                }
                return lazyStaticFields;
            }
        }

        private ImmutableArray<FieldEntity> lazyInstanceFields;
        public ImmutableArray<FieldEntity> InstanceFields {
            get {
                if (lazyInstanceFields.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyInstanceFields,
                        Definition.Fields
                            .Where(f => !f.IsStatic)
                            .Select(CreateField)
                            .ToImmutableArray()
                    );
                }
                return lazyInstanceFields;
            }
        }

        private ImmutableArray<TypeEntity> lazyNestedTypes;
        public ImmutableArray<TypeEntity> NestedTypes {
            get {
                if (lazyNestedTypes.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyNestedTypes,
                        Definition.NestedTypes
                            .Select(Map.Lookup)
                            .ToImmutableArray()
                    );
                }
                return lazyNestedTypes;
            }
        }
    }
}
