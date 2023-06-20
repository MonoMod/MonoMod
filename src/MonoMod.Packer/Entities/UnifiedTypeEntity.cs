using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Diagnostics;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class UnifiedTypeEntity : TypeEntityBase {
        private string DebuggerDisplay() => "Unified " + types[0].Definition.FullName;

        private readonly IReadOnlyList<TypeEntity> types;

        public UnifiedTypeEntity(TypeEntityMap map, IReadOnlyList<TypeEntity> types) : base(map) {
            Helpers.Assert(types.Count > 0);
            this.types = types;

            if (types.Count > 1) {
                if (types[0].Definition.IsTypeOf("System", "Object")) {
                    map.Diagnostics.ReportDiagnostic(ErrorCode.WRN_MergingSystemObject, this);
                }
            }
        }

        public override Utf8String? Namespace => types[0].Definition.Namespace;
        public override Utf8String? Name => types[0].Definition.Name;

        public new ImmutableArray<UnifiedTypeEntity> NestedTypes => base.NestedTypes.CastArray<UnifiedTypeEntity>();
        protected override ImmutableArray<TypeEntityBase> MakeNestedTypes() {
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

            return dict.Values
                .Select(l => new UnifiedTypeEntity(Map, l))
                .ToImmutableArray()
                .CastArray<TypeEntityBase>();
        }

        public new ImmutableArray<UnifiedMethodEntity> StaticMethods => base.StaticMethods.CastArray<UnifiedMethodEntity>();
        protected override ImmutableArray<MethodEntityBase> MakeStaticMethods() {
            return MakeMethodsWithFilter(static t => t.StaticMethods);
        }

        public new ImmutableArray<UnifiedMethodEntity> InstanceMethods => base.InstanceMethods.CastArray<UnifiedMethodEntity>();
        protected override ImmutableArray<MethodEntityBase> MakeInstanceMethods() {
            return MakeMethodsWithFilter(static t => t.InstanceMethods);
        }

        private ImmutableArray<MethodEntityBase> MakeMethodsWithFilter(Func<TypeEntity, ImmutableArray<MethodEntity>> filter) {
            var dict = new Dictionary<string, List<MethodEntity>>();
            // for methods, we unify by full sig
            foreach (var type in types) {
                foreach (var method in filter(type)) {
                    var fullName = method.Definition.FullName;
                    if (!dict.TryGetValue(fullName, out var list)) {
                        dict.Add(fullName, list = new());
                    }
                    list.Add(method);
                }
            }

            return dict.Values
                .Select(l => new UnifiedMethodEntity(Map, l))
                .ToImmutableArray()
                .CastArray<MethodEntityBase>();
        }

        public new ImmutableArray<UnifiedFieldEntity> StaticFields => base.StaticFields.CastArray<UnifiedFieldEntity>();
        protected override ImmutableArray<FieldEntityBase> MakeStaticFields() {
            return MakeFieldsWithFilter(static t => t.StaticFields);
        }

        public new ImmutableArray<UnifiedFieldEntity> InstanceFields => base.InstanceFields.CastArray<UnifiedFieldEntity>();
        protected override ImmutableArray<FieldEntityBase> MakeInstanceFields() {
            return MakeFieldsWithFilter(static t => t.InstanceFields);
        }

        private ImmutableArray<FieldEntityBase> MakeFieldsWithFilter(Func<TypeEntity, ImmutableArray<FieldEntity>> filter) {
            var dict = new Dictionary<NullableUtf8String, List<FieldEntity>>();
            // for fields, we unify by-name only
            foreach (var type in types) {
                foreach (var field in filter(type)) {
                    if (!dict.TryGetValue(field.Definition.Name, out var list)) {
                        dict.Add(field.Definition.Name, list = new());
                    }
                    list.Add(field);
                }
            }

            return dict.Values
                .Select(l => new UnifiedFieldEntity(Map, l))
                .ToImmutableArray()
                .CastArray<FieldEntityBase>();
        }

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules() {
            var builder = ImmutableArray.CreateBuilder<ModuleDefinition>();
            foreach (var type in types) {
                builder.AddRange(type.ContributingModules);
            }
            return builder.ToImmutable();
        }

        // note: this returns the overall (most strict) type merge mode; there may be a less strict one used when merging some component types
        protected override TypeMergeMode? GetTypeMergeMode() {
            var result = TypeMergeModeExtra.MaxValue;
            foreach (var type in types) {
                result = int.Min(result, (int) type.TypeMergeMode);
            }
            return (TypeMergeMode) result;
        }

        private TypeEntityBase? lazyBaseType;

        protected override bool GetHasUnifiableBase() {
            // this *should* basically always be true; its worth checking anyway
            if (!types.All(static t => t.HasUnifiableBase))
                return false;

            TypeEntityBase? baseType = null;
            foreach (var type in types) {
                var thisBaseType = type.BaseType;
                if (thisBaseType is TypeEntity te) {
                    // resolve TypeEntitys to their UnifiedType
                    thisBaseType = te.UnifiedType;
                }
                if (baseType is null) {
                    baseType = thisBaseType;
                } else if (baseType != thisBaseType) {
                    if (thisBaseType is null) {
                        // we're merging object with some other type; we've already reported an error, bail out
                        return false;
                    }
                    // we need to check the inheritance graph, but only if we're allowed to merge non-layout identical types
                    if (TypeMergeMode >= TypeMergeMode.MergeLayoutIdentical) {
                        if (IsDerived(baseType, thisBaseType)) {
                            // move baseType to more derived thisBaseType
                            baseType = thisBaseType;
                        } else if (IsDerived(thisBaseType, baseType)) {
                            // allow, keep baseType the same as it is the more derived
                        } else {
                            // no relation, bail out
                            return false;
                        }
                    } else {
                        // we're not allowe3d to merge these, bail out
                        return false;
                    }
                }
                // baseType == type.BaseType, move on
            }
            // write the computed base type so that it can be accessed
            Volatile.Write(ref lazyBaseType, baseType);
            return true;
        }

        protected override TypeEntityBase? GetBaseType() {
            if (!HasUnifiableBase)
                return null;
            // by this point, lazyBaseType is guaranteed to have been set
            return lazyBaseType;
        }

        private static bool IsDerived(TypeEntityBase @base, TypeEntityBase derived) {
            var cur = derived.BaseType;
            while (cur is not null) {
                if (cur == @base)
                    return true;
            }
            return false;
        }

        private bool isCheckingFullyUnified;
        public ThreeState CanBeFullyUnifiedUncached() {
            if (isCheckingFullyUnified) {
                // assume yes, if we reach this point
                return ThreeState.Maybe;
            }
            isCheckingFullyUnified = true;

            try {
                if (types.Count == 1) {
                    // if we have only one original, we unify completely
                    return true;
                }

                if (IsModuleType) {
                    // if this type is the module type, it must *always* be fully unified, no matter what
                    return true;
                }

                var overallMergeMode = TypeMergeMode;
                if (overallMergeMode is TypeMergeMode.DoNotMerge) {
                    // at least one component type is DoNotMerge; we also know there are multiple, so we definitely fully unify
                    return false;
                }

                if (!HasUnifiableBase) {
                    // the base class isn't unifiable, we can't merge
                    return false;
                }

                if (overallMergeMode is TypeMergeMode.MergeAlways) {

                }

                throw new NotImplementedException();

            } finally {
                isCheckingFullyUnified = false;
            }
        }
    }
}
