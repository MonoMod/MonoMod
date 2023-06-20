using AsmResolver.DotNet;

namespace MonoMod.Packer.Utilities {
    internal static class WellKnownAttributeChecks {
        private const string AttributeNamespace = "MonoMod.Packer.Attributes";

        private const string TypeMergeModeAttrName = "TypeMergeModeAttribute";
        private const string DoNotMergeAttrName = "DoNotMergeAttribute";
        private const string MergeIdenticalOnlyAttrName = "MergeIdenticalOnlyAttribute";
        private const string MergeLayoutIdenticalAttrName = "MergeLayoutIdenticalAttribute";
        private const string MergeAlwaysAttrName = "MergeAlwaysAttribute";

        public static TypeMergeMode? GetDeclaredMergeMode(this IHasCustomAttribute type) {
            TypeMergeMode? result = null;
            // when resolving this, we return the most restricted of the found attributes
            foreach (var attr in type.CustomAttributes) {
                var attrDeclType = attr.Constructor?.DeclaringType;
                if (attrDeclType is null) {
                    continue;
                }

                if (attrDeclType.Namespace != AttributeNamespace) {
                    continue;
                }

                TypeMergeMode? thisValueResult = null;
                switch (attrDeclType.Name?.Value) {
                    case TypeMergeModeAttrName:
                        var sig = attr.Signature;
                        // TODO: error logging?
                        if (sig is null) {
                            continue;
                        }
                        if (sig.FixedArguments.Count < 1) {
                            continue;
                        }
                        var arg = sig.FixedArguments[0];
                        if (arg.Element is not int ival) {
                            continue;
                        }
                        if (ival is not (>= TypeMergeModeExtra.MinValue and <= TypeMergeModeExtra.MaxValue)) {
                            continue;
                        }
                        thisValueResult = (TypeMergeMode) ival;
                        break;

                    case DoNotMergeAttrName:
                        thisValueResult = TypeMergeMode.DoNotMerge;
                        break;
                    case MergeIdenticalOnlyAttrName:
                        thisValueResult = TypeMergeMode.UnifyIdentical;
                        break;
                    case MergeLayoutIdenticalAttrName:
                        thisValueResult = TypeMergeMode.MergeLayoutIdentical;
                        break;
                    case MergeAlwaysAttrName:
                        thisValueResult = TypeMergeMode.MergeAlways;
                        break;

                    default:
                        continue;
                }

                if (result is null) {
                    result = thisValueResult;
                } else if (thisValueResult is { } val) {
                    result = (TypeMergeMode) int.Min((int) result.Value, (int) val);
                }
            }

            return result;
        }

        private const string BaseTypeMergeModeAttrName = "BaseTypeMergeModeAttribute";
        private const string MergeExactBaseAttrName = "MergeExactBaseAttribute";
        private const string MergeMoreDerivedBaseAttrName = "MergeMoreDerivedBaseAttribute";

        public static BaseTypeMergeMode? GetDeclaredBaseMergeMode(this IHasCustomAttribute type) {
            BaseTypeMergeMode? result = null;
            // when resolving this, we return the most restricted of the found attributes
            foreach (var attr in type.CustomAttributes) {
                var attrDeclType = attr.Constructor?.DeclaringType;
                if (attrDeclType is null) {
                    continue;
                }

                if (attrDeclType.Namespace != AttributeNamespace) {
                    continue;
                }

                BaseTypeMergeMode? thisValueResult = null;
                switch (attrDeclType.Name?.Value) {
                    case BaseTypeMergeModeAttrName:
                        var sig = attr.Signature;
                        // TODO: error logging?
                        if (sig is null) {
                            continue;
                        }
                        if (sig.FixedArguments.Count < 1) {
                            continue;
                        }
                        var arg = sig.FixedArguments[0];
                        if (arg.Element is not int ival) {
                            continue;
                        }
                        if (ival is not (>= BaseTypeMergeModeExtra.MinValue and <= BaseTypeMergeModeExtra.MaxValue)) {
                            continue;
                        }
                        thisValueResult = (BaseTypeMergeMode) ival;
                        break;

                    case MergeExactBaseAttrName:
                        thisValueResult = BaseTypeMergeMode.Exact;
                        break;
                    case MergeMoreDerivedBaseAttrName:
                        thisValueResult = BaseTypeMergeMode.AllowMoreDerived;
                        break;

                    default:
                        continue;
                }

                if (result is null) {
                    result = thisValueResult;
                } else if (thisValueResult is { } val) {
                    result = (BaseTypeMergeMode) int.Min((int) result.Value, (int) val);
                }
            }

            return result;
        }
    }
}
