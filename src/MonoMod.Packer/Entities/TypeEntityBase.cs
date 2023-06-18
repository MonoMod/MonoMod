using AsmResolver;
using System.Collections.Immutable;
using System.Threading;

namespace MonoMod.Packer.Entities {
    internal abstract class TypeEntityBase : EntityBase {
        protected TypeEntityBase(TypeEntityMap map) : base(map) {

        }

        public abstract Utf8String? Namespace { get; }

        private int updatingState;
        private const int TypeMergeModeMask = 0b1 << 0;
        private const int HasUnifiableBaseMask = 0b1 << 1;
        private const int UnifiableBaseMask = 0b1 << 2;
        private void MarkState(int state) {
            int prev, next;
            do {
                prev = Volatile.Read(ref updatingState);
                next = prev | state;
            } while (Interlocked.CompareExchange(ref updatingState, next, prev) != prev);
        }

        private TypeMergeMode lazyTypeMergeMode;
        public TypeMergeMode TypeMergeMode {
            get {
                if ((updatingState & TypeMergeModeMask) == 0) {
                    lazyTypeMergeMode = GetTypeMergeMode() ?? Map.Options.TypeMergeMode;
                    MarkState(TypeMergeModeMask);
                }
                return lazyTypeMergeMode;
            }
        }

        protected abstract TypeMergeMode? GetTypeMergeMode();

        private bool lazyHasUnifiableBase;
        public bool HasUnifiableBase {
            get {
                if ((updatingState & HasUnifiableBaseMask) == 0) {
                    lazyHasUnifiableBase = GetHasUnifiableBase();
                    MarkState(HasUnifiableBaseMask);
                }
                return lazyHasUnifiableBase;
            }
        }

        protected abstract bool GetHasUnifiableBase();

        private TypeEntityBase? lazyUnifiableBase;
        public TypeEntityBase? BaseType {
            get {
                if ((updatingState & UnifiableBaseMask) == 0) {
                    lazyUnifiableBase = GetBaseType();
                    MarkState(UnifiableBaseMask);
                }
                return lazyUnifiableBase;
            }
        }
        protected abstract TypeEntityBase? GetBaseType();

        public virtual bool IsModuleType => Namespace is null && Name == "<Module>"; // TODO: is there a constant somewhere for this?

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
