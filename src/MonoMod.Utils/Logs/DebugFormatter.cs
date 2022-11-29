using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Backports;
using MonoMod.Utils;

namespace MonoMod.Logs {
    public static class DebugFormatter {
        // We have explicit checks for types which may prove problematic using default formatting

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool CanDebugFormat<T>(in T value, out object? extraData) {
            extraData = null;

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
            if (typeof(T) == typeof(Exception))
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
            if (value is Exception ex) {
                // we want to stringify the exception once, and reuse that where possible
                extraData = ex.ToString();
                return true;
            }

            // then for IDebugFormattable
            if (value is IDebugFormattable)
                return true;

            return false;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool TryFormatInto<T>(in T value, object? extraData, Span<char> into, out int wrote) {
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
            if (typeof(T) == typeof(Exception))
                return TryFormatException(Transmute<Exception>(in value), Unsafe.As<string?>(extraData), into, out wrote);

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
            if (value is Exception ex)
                return TryFormatException(ex, Unsafe.As<string?>(extraData), into, out wrote);
            
            if (value is IDebugFormattable)
                return ((IDebugFormattable) value).TryFormatInto(into, out wrote);

            Helpers.Assert(false, $"Called TryFormatInto with value of unknown type {value.GetType()}");
            wrote = 0;
            return false;
        }

        private static bool TryFormatException(Exception e, string? eStr, Span<char> into, out int wrote) {
            wrote = 0;

            // if eStr is null, then we have to alloc here, oh well
            eStr ??= e.ToString();

            var nl = Environment.NewLine;

            if (into.Slice(wrote).Length < eStr.Length)
                return false;
            eStr.AsSpan().CopyTo(into.Slice(wrote));
            wrote += eStr.Length;

            int w;

            // extra information for specific exception types

            if (e is ReflectionTypeLoadException rtle) {
                for (var i = 0; i < 4 && i < rtle.Types.Length; i++) {
                    if (!Into(into.Slice(wrote), out w, $"{nl}System.Reflection.ReflectionTypeLoadException.Types[{i}] = {rtle.Types[i]}"))
                        return false;
                    wrote += w;
                }
                if (rtle.Types.Length >= 4) {
                    if (!Into(into.Slice(wrote), out w, $"{nl}System.Reflection.ReflectionTypeLoadException.Types[...] = ..."))
                        return false;
                    wrote += w;
                }

                if (rtle.LoaderExceptions.Length > 0) {
                    const string Sep = "System.Reflection.ReflectionTypeLoadException.LoaderExceptions = [";
                    if (into.Slice(wrote).Length < nl.Length + Sep.Length)
                        return false;
                    nl.AsSpan().CopyTo(into.Slice(wrote));
                    wrote += nl.Length;
                    Sep.AsSpan().CopyTo(into.Slice(wrote));
                    wrote += Sep.Length;

                    for (var i = 0; i < rtle.LoaderExceptions.Length; i++) {
                        var ex = rtle.LoaderExceptions[i];
                        if (ex is null)
                            continue;
                        if (into.Slice(wrote).Length < nl.Length)
                            return false;
                        nl.AsSpan().CopyTo(into.Slice(wrote));
                        wrote += nl.Length;
                        // this'll necessarily call ToString on ex each time we call in here, but oh well, there's not a good place to stash the stringified value
                        if (!TryFormatException(ex, null, into.Slice(wrote), out w))
                            return false;
                        wrote += w;
                    }

                    if (into.Slice(wrote).Length < nl.Length + 1)
                        return false;
                    nl.AsSpan().CopyTo(into.Slice(wrote));
                    wrote += nl.Length;

                    into[wrote++] = ']';
                }
            }

            if (e is TypeLoadException tle) {
                if (!Into(into.Slice(wrote), out w, $"{nl}System.TypeLoadException.TypeName = {tle.TypeName}"))
                    return false;
                wrote += w;
            }

            if (e is BadImageFormatException bife) {
                if (!Into(into.Slice(wrote), out w, $"{nl}System.BadImageFormatException.FileName = {bife.FileName}"))
                    return false;
                wrote += w;
            }

            return true;
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
            if (!TryFormatMethodBase(method, into.Slice(wrote), out w))
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
            if (!TryFormatMemberInfoName(method, into.Slice(wrote), out var w))
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

        public static string Format(ref FormatInterpolatedStringHandler handler) {
            return handler.ToStringAndClear();
        }

        public static bool Into(Span<char> into, out int wrote,
            [InterpolatedStringHandlerArgument("into")] ref FormatIntoInterpolatedStringHandler handler) {
            _ = into;
            wrote = handler.pos;
            return !handler.incomplete;
        }
    }

    [InterpolatedStringHandler]
    public ref struct FormatInterpolatedStringHandler {
        private DebugLogInterpolatedStringHandler handler;

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public FormatInterpolatedStringHandler(int literalLen, int formattedCount) {
            handler = new(literalLen, formattedCount, enabled: true, recordHoles: false, out _);
        }

        public override string ToString() => handler.ToString();
        public string ToStringAndClear() => handler.ToStringAndClear();

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendLiteral(string s) {
            handler.AppendLiteral(s);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted(string? s) {
            handler.AppendFormatted(s);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted(string? s, int alignment = 0, string? format = default) {
            handler.AppendFormatted(s, alignment, format);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted(ReadOnlySpan<char> s) {
            handler.AppendFormatted(s);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted(ReadOnlySpan<char> s, int alignment = 0, string? format = default) {
            handler.AppendFormatted(s, alignment, format);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted<T>(T value) {
            handler.AppendFormatted(value);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted<T>(T value, int alignment) {
            handler.AppendFormatted(value, alignment);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted<T>(T value, string? format) {
            handler.AppendFormatted(value, format);
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted<T>(T value, int alignment, string? format) {
            handler.AppendFormatted(value, alignment, format);
        }
    }
}