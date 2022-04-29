using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Reflection;

namespace MonoMod.Core.Platforms {
    static partial class AbiSelftest {

        private static StructKindFlags? GetStructKind(Type type) {
            var typeSize = type.GetManagedSize();

            GetFields:
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

            if (fields.Length == 0) {
                if (typeSize == 1) {
                    return StructKindFlags.Empty;
                } else {
                    goto ReturnXKind;
                }
            }

            if (type.IsAutoLayout && !type.IsPrimitive && fields.Length == 1) {
                // we want to unwrap the type if its auto with only one member
                type = fields[0].FieldType;
                goto GetFields;
            }

            // if the size is one of the odd numbers, we'll use that
            StructKindFlags? oddKind = typeSize switch {
                3 => StructKindFlags.OddSize3,
                5 => StructKindFlags.OddSize5,
                6 => StructKindFlags.OddSize6,
                7 => StructKindFlags.OddSize7,
                9 => StructKindFlags.OddSize9,
                _ => null
            };
            if (oddKind is { } odd)
                return odd;

            if (fields.Length > 4) // we only have specific types for up to 4 fields
                goto ReturnXKind;

            Type? fieldType = null;
            foreach (var field in fields) {
                var ftype = field.FieldType;
                if (fieldType is null) {
                    fieldType = ftype;
                } else if (fieldType != ftype) {
                    // there are mismatched field types, just use the X kinds
                    goto ReturnXKind;
                }
            }

            Helpers.DAssert(fieldType is not null);

            if (fieldType.IsPointer) {
                fieldType = typeof(UIntPtr);
            }

            if (fieldType == typeof(IntPtr)) {
                fieldType = IntPtr.Size == 8 ? typeof(long) : typeof(int);
            } else if (fieldType == typeof(UIntPtr)) {
                fieldType = IntPtr.Size == 8 ? typeof(ulong) : typeof(uint);
            }

            var typeCode = Type.GetTypeCode(fieldType);

            // we have fields.Length fields of type fieldType
            StructKindFlags? fieldSizeKind = fields.Length switch {
                0 => StructKindFlags.Empty,
                1 => typeCode switch {
                    TypeCode.Single => StructKindFlags.HfaFloat1,
                    TypeCode.Double => StructKindFlags.HfaDouble1,
                    TypeCode.Byte or TypeCode.SByte => StructKindFlags.Byte1,
                    TypeCode.Int16 or TypeCode.UInt16 => StructKindFlags.Short1,
                    TypeCode.Int32 or TypeCode.UInt32 => StructKindFlags.Int1,
                    TypeCode.Int64 or TypeCode.UInt64 => StructKindFlags.Long1,
                    _ => null
                },
                2 => typeCode switch {
                    TypeCode.Single => StructKindFlags.HfaFloat2,
                    TypeCode.Double => StructKindFlags.HfaDouble2,
                    TypeCode.Byte or TypeCode.SByte => StructKindFlags.Byte2,
                    TypeCode.Int16 or TypeCode.UInt16 => StructKindFlags.Short2,
                    TypeCode.Int32 or TypeCode.UInt32 => StructKindFlags.Int2,
                    TypeCode.Int64 or TypeCode.UInt64 => StructKindFlags.Long2,
                    _ => null
                },
                3 => typeCode switch {
                    TypeCode.Single => StructKindFlags.HfaFloat3,
                    TypeCode.Double => StructKindFlags.HfaDouble3,
                    TypeCode.Byte or TypeCode.SByte => StructKindFlags.Byte3,
                    TypeCode.Int16 or TypeCode.UInt16 => StructKindFlags.Short3,
                    TypeCode.Int32 or TypeCode.UInt32 => StructKindFlags.Int3,
                    TypeCode.Int64 or TypeCode.UInt64 => StructKindFlags.Long3,
                    _ => null
                },
                4 => typeCode switch {
                    TypeCode.Single => StructKindFlags.HfaFloat4,
                    TypeCode.Double => StructKindFlags.HfaDouble4,
                    TypeCode.Byte or TypeCode.SByte => StructKindFlags.Byte4,
                    TypeCode.Int16 or TypeCode.UInt16 => StructKindFlags.Short4,
                    TypeCode.Int32 or TypeCode.UInt32 => StructKindFlags.Int4,
                    TypeCode.Int64 or TypeCode.UInt64 => StructKindFlags.Long4,
                    _ => null
                },
                _ => null
            };

            if (fieldSizeKind is { } kind)
                return kind;

            ReturnXKind:
            // return an X kind, as appropriate
            return typeSize switch {
                1 => StructKindFlags.X_1,
                2 => StructKindFlags.X_2,
                3 => StructKindFlags.X_3,
                4 => StructKindFlags.X_4,
                5 => StructKindFlags.X_5,
                6 => StructKindFlags.X_6,
                7 => StructKindFlags.X_7,
                8 => StructKindFlags.X_8,
                9 => StructKindFlags.X_9,
                10 => StructKindFlags.X10,
                11 => StructKindFlags.X11,
                12 => StructKindFlags.X12,
                13 => StructKindFlags.X13,
                14 => StructKindFlags.X14,
                15 => StructKindFlags.X15,
                16 => StructKindFlags.X16,
                _ => null
            };
        }

        private sealed class SelftestClassifier {
            private readonly StructKindFlags ReturnedByValue;
            private readonly StructKindFlags PassedByValue;

            public Classifier Classifier { get; }

            public SelftestClassifier(StructKindFlags returnedByValue, StructKindFlags passedByValue) {
                ReturnedByValue = returnedByValue;
                PassedByValue = passedByValue;

                Classifier = Classify;
            }

            private TypeClassification Classify(Type type, bool isReturn) {
                var maybeKind = GetStructKind(type);

                if (maybeKind is not { } kind) {
                    // if we couldn't identify it, it's too large to go in register and must be passed by pointer

                    // TODO: if the platform uses a stack-based calling convention, like x86, then this is incorrect
                    // too large structs become ByVal if not a return value, not ByRef
                    return TypeClassification.ByRef;
                }

                var compareTo = isReturn ? ReturnedByValue : PassedByValue;

                return (kind & compareTo) == kind 
                     ? TypeClassification.ByVal // the type is passed by value
                     : TypeClassification.ByRef; // the type is passed with a return buffer or byref
            }
        }
    }
}
