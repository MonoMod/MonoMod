using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Backports;
using MonoMod.Utils;

namespace MonoMod.Logs {
    public static class DebugFormatter {
        // We have explicit checks for types which may prove problematic using default formatting

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool CanDebugFormat<T>(in T value) {
            // first check for exact type matches
            if (typeof(T) == typeof(Type))
                return true;
            if (typeof(T) == typeof(MethodBase))
                return true;
            if (typeof(T) == typeof(MethodInfo))
                return true;
            if (typeof(T) == typeof(ConstructorInfo))
                return true;
            if (typeof(T) == typeof(FieldInfo))
                return true;
            if (typeof(T) == typeof(PropertyInfo))
                return true;

            if (typeof(T) == typeof(IDebugFormattable))
                return true;
            
            // then object instance matches
            if (value
                is Type
                or MethodBase
                or FieldInfo
                or PropertyInfo)
                return true;

            // then for IDebugFormattable
            if (value is IDebugFormattable)
                return true;

            return false;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool TryFormatInto<T>(in T value, Span<char> into, out int wrote) {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            static ref TOut Transmute<TOut>(in T val)
                => ref Unsafe.As<T, TOut>(ref Unsafe.AsRef(in val));

            if (default(T) == null && value is null) {
                wrote = 0;
                return true;
            }

            if (typeof(T) == typeof(Type))
                return TryFormatType(Transmute<Type>(in value), into, out wrote);
            if (typeof(T) == typeof(MethodInfo))
                return TryFormatMethodInfo(Transmute<MethodInfo>(in value), into, out wrote);
            if (typeof(T) == typeof(ConstructorInfo))
                return TryFormatMethodBase(Transmute<ConstructorInfo>(in value), into, out wrote);
            if (typeof(T) == typeof(FieldInfo))
                return TryFormatFieldInfo(Transmute<FieldInfo>(in value), into, out wrote);
            if (typeof(T) == typeof(PropertyInfo))
                return TryFormatPropertyInfo(Transmute<PropertyInfo>(in value), into, out wrote);
            // don't dispatch for typeof(T) == typeof(MethodBase), because we want to dispatch to the correct derived formatter

            if (typeof(T) == typeof(IDebugFormattable))
                return Transmute<IDebugFormattable>(in value).TryFormatInto(into, out wrote);

            if (value is Type ty)
                return TryFormatType(ty, into, out wrote);
            if (value is MethodInfo mi)
                return TryFormatMethodInfo(mi, into, out wrote);
            if (value is ConstructorInfo ci)
                return TryFormatMethodBase(ci, into, out wrote);
            if (value is MethodBase mb)
                return TryFormatMethodBase(mb, into, out wrote);
            if (value is FieldInfo fi)
                return TryFormatFieldInfo(fi, into, out wrote);
            if (value is PropertyInfo pi)
                return TryFormatPropertyInfo(pi, into, out wrote);
            
            if (value is IDebugFormattable)
                return ((IDebugFormattable) value).TryFormatInto(into, out wrote);

            Helpers.Assert(false, $"Called TryFormatInto with value of unknown type {value.GetType()}");
            wrote = 0;
            return false;
        }

        private static bool TryFormatType(Type type, Span<char> into, out int wrote) {
            wrote = 0;

            var name = type.FullName;
            if (name is null)
                return true;
            if (into.Length < name.Length)
                return false;
            name.AsSpan().CopyTo(into);
            wrote = name.Length;
            return true;
        }

        private static bool TryFormatMethodInfo(MethodInfo method, Span<char> into, out int wrote) {
            var ret = method.ReturnType;
            wrote = 0;
            if (!TryFormatType(ret, into.Slice(wrote), out var w))
                return false;
            wrote += w;
            if (into.Slice(wrote).Length < 1)
                return false;
            into[wrote++] = ' ';
            if (!TryFormatMethodBase(method, into, out w))
                return false;
            wrote += w;
            return true;
        }

        private static bool TryFormatMemberInfoName(MemberInfo member, Span<char> into, out int wrote) {
            wrote = 0;
            int w;
            var declType = member.DeclaringType;
            if (declType is not null) {
                if (!TryFormatType(declType, into.Slice(wrote), out w))
                    return false;
                wrote += w;
                if (into.Slice(wrote).Length < 1)
                    return false;
                into[wrote++] = ':';
            }
            var name = member.Name;
            if (into.Slice(wrote).Length < name.Length)
                return false;
            name.AsSpan().CopyTo(into.Slice(wrote));
            wrote += name.Length;

            return true;
        }

        private static bool TryFormatMethodBase(MethodBase method, Span<char> into, out int wrote) {
            wrote = 0;
            if (!TryFormatMemberInfoName(method, into, out var w))
                return false;
            wrote += w;

            if (method.IsGenericMethod) {
                if (into.Slice(wrote).Length < 1)
                    return false;
                into[wrote++] = '<';

                var genArgs = method.GetGenericArguments();
                for (var i = 0; i < genArgs.Length; i++) {
                    if (i != 0) {
                        if (into.Slice(wrote).Length < 2)
                            return false;
                        into[wrote++] = ',';
                        into[wrote++] = ' ';
                    }

                    if (!TryFormatType(genArgs[i], into.Slice(wrote), out w))
                        return false;
                    wrote += w;
                }

                if (into.Slice(wrote).Length < 1)
                    return false;
                into[wrote++] = '>';
            }

            var args = method.GetParameters();
            if (into.Slice(wrote).Length < 1)
                return false;
            into[wrote++] = '(';

            for (var i = 0; i < args.Length; i++) {
                if (i != 0) {
                    if (into.Slice(wrote).Length < 2)
                        return false;
                    into[wrote++] = ',';
                    into[wrote++] = ' ';
                }

                if (!TryFormatType(args[i].ParameterType, into.Slice(wrote), out w))
                    return false;
                wrote += w;
            }

            if (into.Slice(wrote).Length < 1)
                return false;
            into[wrote++] = ')';

            return true;
        }

        private static bool TryFormatFieldInfo(FieldInfo field, Span<char> into, out int wrote) {
            wrote = 0;
            if (!TryFormatType(field.FieldType, into.Slice(wrote), out var w))
                return false;
            wrote += w;
            if (!TryFormatMemberInfoName(field, into.Slice(wrote), out w))
                return false;
            wrote += w;
            return true;
        }

        private static bool TryFormatPropertyInfo(PropertyInfo prop, Span<char> into, out int wrote) {
            wrote = 0;
            if (!TryFormatType(prop.PropertyType, into.Slice(wrote), out var w))
                return false;
            wrote += w;
            if (!TryFormatMemberInfoName(prop, into.Slice(wrote), out w))
                return false;
            wrote += w;

            var hasGet = prop.CanRead;
            var hasSet = prop.CanWrite;
            var len = 5 + (hasGet ? 4 : 0) + (hasSet ? 4 : 0) + (hasGet && hasSet ? 1 : 0);
            if (into.Slice(wrote).Length < len)
                return false;
            " { ".AsSpan().CopyTo(into.Slice(wrote));
            wrote += 3;
            if (hasGet) {
                "get;".AsSpan().CopyTo(into.Slice(wrote));
                wrote += 4;
            }
            if (hasGet && hasSet) {
                into[wrote++] = ' ';
            }
            if (hasSet) {
                "set;".AsSpan().CopyTo(into.Slice(wrote));
                wrote += 4;
            }
            " }".AsSpan().CopyTo(into.Slice(wrote));
            wrote += 2;
            return true;
        }

        public static bool Into(Span<char> into, out int wrote,
            [InterpolatedStringHandlerArgument("into")] ref FormatIntoInterpolatedStringHandler handler) {
            wrote = handler.pos;
            return !handler.incomplete;
        }

        [InterpolatedStringHandler]
        public ref struct FormatIntoInterpolatedStringHandler {
            private readonly Span<char> _chars;
            internal int pos;
            internal bool incomplete;

            public FormatIntoInterpolatedStringHandler(int literalLen, int numHoles, Span<char> into, out bool enabled) {
                _chars = into;
                pos = 0;
                if (into.Length < literalLen) {
                    incomplete = true;
                    enabled = false;
                } else {
                    incomplete = false;
                    enabled = true;
                }
            }


            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public bool AppendLiteral(string value) {
                if (value.Length == 1) {
                    Span<char> chars = _chars;
                    var pos = this.pos;
                    if ((uint) pos < (uint) chars.Length) {
                        chars[pos] = value[0];
                        this.pos = pos + 1;
                        return true;
                    } else {
                        incomplete = true;
                        return false;
                    }
                }

                if (value.Length == 2) {
                    Span<char> chars = _chars;
                    var pos = this.pos;
                    if ((uint) pos < chars.Length - 1) {
                        value.AsSpan().CopyTo(chars.Slice(pos));
                        this.pos = pos + 2;
                        return true;
                    } else {
                        incomplete = true;
                        return false;
                    }
                }

                return AppendStringDirect(value);
            }

            private bool AppendStringDirect(string value) {
                if (value.AsSpan().TryCopyTo(_chars.Slice(pos))) {
                    pos += value.Length;
                    return true;
                } else {
                    incomplete = true;
                    return false;
                }
            }

            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public bool AppendFormatted(string? value) {
                if (value is null)
                    return true;

                if (value.AsSpan().TryCopyTo(_chars.Slice(pos))) {
                    pos += value.Length;
                    return true;
                } else {
                    incomplete = true;
                    return false;
                }
            }

            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public bool AppendFormatted(string? value, int alignment = 0, string? format = default)
                => AppendFormatted<string?>(value, alignment, format);

            public bool AppendFormatted(ReadOnlySpan<char> value) {
                if (value.TryCopyTo(_chars.Slice(pos))) {
                    pos += value.Length;
                    return true;
                } else {
                    incomplete = true;
                    return false;
                }
            }

            public bool AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = default) {
                var leftAlign = false;
                if (alignment < 0) {
                    leftAlign = true;
                    alignment = -alignment;
                }

                var paddingRequired = alignment - value.Length;
                if (paddingRequired <= 0) {
                    // The value is as large or larger than the required amount of padding,
                    // so just write the value.
                    return AppendFormatted(value);
                }

                if (_chars.Slice(pos).Length < value.Length + paddingRequired) {
                    incomplete = true;
                    return false;
                }

                // Write the value along with the appropriate padding.
                if (leftAlign) {
                    value.CopyTo(_chars.Slice(pos));
                    pos += value.Length;
                    _chars.Slice(pos, paddingRequired).Fill(' ');
                    pos += paddingRequired;
                } else {
                    _chars.Slice(pos, paddingRequired).Fill(' ');
                    pos += paddingRequired;
                    value.CopyTo(_chars.Slice(pos));
                    pos += value.Length;
                }

                return true;
            }

            public bool AppendFormatted<T>(T value) {
                if (typeof(T) == typeof(IntPtr)) {
                    return AppendFormatted(Unsafe.As<T, IntPtr>(ref value));
                }
                if (typeof(T) == typeof(UIntPtr)) {
                    return AppendFormatted(Unsafe.As<T, UIntPtr>(ref value));
                }

                string? s;
                if (DebugFormatter.CanDebugFormat(value)) {
                    int wrote;
                    if (!DebugFormatter.TryFormatInto(value, _chars.Slice(pos), out wrote)) {
                        incomplete = true;
                        return false;
                    }
                    pos += wrote;
                    return true;
                } else if (value is IFormattable) {
                    s = ((IFormattable) value).ToString(format: null, null); // constrained call avoiding boxing for value types (though it might box later anyway
                } else {
                    s = value?.ToString();
                }

                if (s is not null) {
                    return AppendStringDirect(s);
                }

                return true;
            }


            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            private bool AppendFormatted(IntPtr value) {
                if (IntPtr.Size == 4) {
                    return AppendFormatted((int) value);
                } else {
                    return AppendFormatted((long) value);
                }
            }
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            private bool AppendFormatted(IntPtr value, string? format) {
                if (IntPtr.Size == 4) {
                    return AppendFormatted((int) value, format);
                } else {
                    return AppendFormatted((long) value, format);
                }
            }

            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            private bool AppendFormatted(UIntPtr value) {
                if (UIntPtr.Size == 4) {
                    return AppendFormatted((uint) value);
                } else {
                    return AppendFormatted((ulong) value);
                }
            }
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            private bool AppendFormatted(UIntPtr value, string? format) {
                if (UIntPtr.Size == 4) {
                    return AppendFormatted((uint) value, format);
                } else {
                    return AppendFormatted((ulong) value, format);
                }
            }

            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public bool AppendFormatted<T>(T value, int alignment) {
                var startingPos = pos;
                if (!AppendFormatted(value))
                    return false;
                if (alignment != 0) {
                    return AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
                }
                return true;
            }

            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public bool AppendFormatted<T>(T value, string? format) {
                if (typeof(T) == typeof(IntPtr)) {
                    return AppendFormatted(Unsafe.As<T, IntPtr>(ref value), format);
                }
                if (typeof(T) == typeof(UIntPtr)) {
                    return AppendFormatted(Unsafe.As<T, UIntPtr>(ref value), format);
                }

                string? s;
                if (DebugFormatter.CanDebugFormat(value)) {
                    int wrote;
                    if (!DebugFormatter.TryFormatInto(value, _chars.Slice(pos), out wrote)) {
                        incomplete = true;
                        return false;
                    }
                    pos += wrote;
                    return true;
                } else if (value is IFormattable) {
                    s = ((IFormattable) value).ToString(format, null); // constrained call avoiding boxing for value types
                } else {
                    s = value?.ToString();
                }

                if (s is not null) {
                    return AppendStringDirect(s);
                }
                return true;
            }

            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            public bool AppendFormatted<T>(T value, int alignment, string? format) {
                var startingPos = pos;
                if (!AppendFormatted(value, format))
                    return false;
                if (alignment != 0) {
                    return AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
                }
                return true;
            }

            /// <summary>Handles adding any padding required for aligning a formatted value in an interpolation expression.</summary>
            /// <param name="startingPos">The position at which the written value started.</param>
            /// <param name="alignment">Non-zero minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
            private bool AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment) {
                Helpers.DAssert(startingPos >= 0 && startingPos <= pos);
                Helpers.DAssert(alignment != 0);

                var charsWritten = pos - startingPos;

                var leftAlign = false;
                if (alignment < 0) {
                    leftAlign = true;
                    alignment = -alignment;
                }

                var paddingNeeded = alignment - charsWritten;
                if (paddingNeeded > 0) {
                    if (_chars.Slice(pos).Length < paddingNeeded) {
                        incomplete = true;
                        return false;
                    }

                    if (leftAlign) {
                        _chars.Slice(pos, paddingNeeded).Fill(' ');
                    } else {
                        _chars.Slice(startingPos, charsWritten).CopyTo(_chars.Slice(startingPos + paddingNeeded));
                        _chars.Slice(startingPos, paddingNeeded).Fill(' ');
                    }

                    pos += paddingNeeded;
                }
                return true;
            }

        }
    }
}