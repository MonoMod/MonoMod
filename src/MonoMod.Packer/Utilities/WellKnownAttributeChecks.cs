using AsmResolver.DotNet;

namespace MonoMod.Packer.Utilities {
    internal static class WellKnownAttributeChecks {
        private const string AttributeNamespace = "MonoMod.Packer.Attributes";

        private const string TypeMergeModeAttrName = "TypeMergeModeAttribute";
        private const string DoNotMergeAttrName = "DoNotMergeAttribute";
        private const string MergeIdenticalOnlyAttrName = "MergeIdenticalOnlyAttribute";
        private const string MergeLayoutIdenticalAttrName = "MergeLayoutIdenticalAttribute";
        private const string MergeAlwaysAttrName = "MergeAlwaysAttribute";


        public static TypeMergeMode? GetDeclaredMergeMode(this TypeDefinition type) {
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
                        if (arg.ArgumentType.ElementType is not AsmResolver.PE.DotNet.Metadata.Tables.Rows.ElementType.I4) {
                            continue;
                        }
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
    }
}
