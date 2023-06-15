using AsmResolver;
using System.Collections.Immutable;

namespace MonoMod.Packer.Entities {
    internal abstract class TypeEntityBase : EntityBase {
        protected TypeEntityBase(TypeEntityMap map) : base(map) {

        }

        public abstract Utf8String? Namespace { get; }
        public abstract Utf8String? Name { get; }


        private ImmutableArray<MethodEntityBase> lazyStaticMethods;
        public ImmutableArray<MethodEntityBase> StaticMethods {
            get {
                if (lazyStaticMethods.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyStaticMethods,
                        MakeStaticMethods()
                    );
                }
                return lazyStaticMethods;
            }
        }

        protected abstract ImmutableArray<MethodEntityBase> MakeStaticMethods();

        private ImmutableArray<MethodEntityBase> lazyInstanceMethods;
        public ImmutableArray<MethodEntityBase> InstanceMethods {
            get {
                if (lazyInstanceMethods.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyInstanceMethods,
                        MakeInstanceMethods()
                    );
                }
                return lazyInstanceMethods;
            }
        }

        protected abstract ImmutableArray<MethodEntityBase> MakeInstanceMethods();

        private ImmutableArray<FieldEntityBase> lazyStaticFields;
        public ImmutableArray<FieldEntityBase> StaticFields {
            get {
                if (lazyStaticFields.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyStaticFields,
                        MakeStaticFields()
                    );
                }
                return lazyStaticFields;
            }
        }

        protected abstract ImmutableArray<FieldEntityBase> MakeStaticFields();

        private ImmutableArray<FieldEntityBase> lazyInstanceFields;
        public ImmutableArray<FieldEntityBase> InstanceFields {
            get {
                if (lazyInstanceFields.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyInstanceFields,
                        MakeInstanceFields()
                    );
                }
                return lazyInstanceFields;
            }
        }

        protected abstract ImmutableArray<FieldEntityBase> MakeInstanceFields();

        private ImmutableArray<TypeEntityBase> lazyNestedTypes;
        public ImmutableArray<TypeEntityBase> NestedTypes {
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

        protected abstract ImmutableArray<TypeEntityBase> MakeNestedTypes();
    }
}
