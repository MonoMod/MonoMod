﻿using MonoMod.Backports;
using MonoMod.Logs;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.Utils {
    public static class Helpers {

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void Swap<T>(ref T a, ref T b) => (b, a) = (a, b);

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool Has<T>(this T @enum, T value) where T : struct, Enum {
            var flgsVal = NumericValue(value);
            return (NumericValue(@enum) & flgsVal) == flgsVal;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ulong NumericValue<T>(T value) where T : struct, Enum {
            ulong result = 0;
            Unsafe.CopyBlock(ref Unsafe.As<ulong, byte>(ref result), ref Unsafe.As<T, byte>(ref value), (uint) Unsafe.SizeOf<T>());
            return result;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void ThrowIfArgumentNull<T>([NotNull] T? arg, [CallerArgumentExpression("arg")] string name = "") {
            if (arg is null)
                ThrowArgumentNull(name);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static T ThrowIfNull<T>([NotNull] T? arg, [CallerArgumentExpression("arg")] string name = "") {
            if (arg is null)
                ThrowArgumentNull(name);
            return arg;
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        [DoesNotReturn]
        private static void ThrowArgumentNull(string argName) {
            throw new ArgumentNullException(argName);
        }

        public static T EventAdd<T>(ref T? evt, T del) where T : Delegate {
            T? orig;
            T newDel;
            do {
                orig = evt;
                newDel = (T) Delegate.Combine(orig, del);
            } while (Interlocked.CompareExchange(ref evt, newDel, orig) != orig);
            return newDel;
        }

        public static T? EventRemove<T>(ref T? evt, T del) where T : Delegate {
            T? orig;
            T? newDel;
            do {
                orig = evt;
                newDel = (T?) Delegate.Remove(orig, del);
            } while (Interlocked.CompareExchange(ref evt, newDel, orig) != orig);
            return newDel;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void Assert([DoesNotReturnIf(false)] bool value,
            string? message = null,
            [CallerArgumentExpression("value")] string expr = ""
        ) {
            if (!value)
                ThrowAssertionFailed(message, expr);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [Conditional("DEBUG")]
        public static void DAssert([DoesNotReturnIf(false)] bool value, 
            string? message = null, 
            [CallerArgumentExpression("value")] string expr = ""
        ) {
            if (!value)
                ThrowAssertionFailed(message, expr);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void Assert([DoesNotReturnIf(false)] bool value,
            [InterpolatedStringHandlerArgument("value")] ref AssertionInterpolatedStringHandler message,
            [CallerArgumentExpression("value")] string expr = ""
        ) {
            if (!value)
                ThrowAssertionFailed(message.ToStringAndClear(), expr);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [Conditional("DEBUG")]
        public static void DAssert([DoesNotReturnIf(false)] bool value,
            [InterpolatedStringHandlerArgument("value")] ref AssertionInterpolatedStringHandler message,
            [CallerArgumentExpression("value")] string expr = ""
        ) {
            if (!value)
                ThrowAssertionFailed(message.ToStringAndClear(), expr);
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        [DoesNotReturn]
        private static void ThrowAssertionFailed(string? msg, string expr) {
            DebugLog.Log(AssemblyInfo.AssemblyName + ".Assert", LogLevel.Assert, $"Assertion failed! {expr} {msg}");
            throw new AssertionFailedException(msg, expr);
        }

#region GetOrInit*
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public unsafe static T GetOrInit<T>(ref T? location, Func<T> init) where T : class {
            if (location is not null)
                return location;
            return InitializeValue(ref location, &ILHelpers.TailCallFunc<T>, init);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public unsafe static T GetOrInitWithLock<T>(ref T? location, object @lock, Func<T> init) where T : class {
            if (location is not null)
                return location;
            return InitializeValueWithLock(ref location, @lock, &ILHelpers.TailCallFunc<T>, init);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public unsafe static T GetOrInit<T>(ref T? location, delegate*<T> init) where T : class {
            if (location is not null)
                return location;
            return InitializeValue(ref location, &ILHelpers.TailCallDelegatePtr<T>, (IntPtr) init);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public unsafe static T GetOrInitWithLock<T>(ref T? location, object @lock, delegate*<T> init) where T : class {
            if (location is not null)
                return location;
            return InitializeValueWithLock(ref location, @lock, &ILHelpers.TailCallDelegatePtr<T>, (IntPtr)init);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public unsafe static T GetOrInit<T, TParam>(ref T? location, delegate*<TParam, T> init, TParam obj) where T : class {
            if (location is not null)
                return location;
            return InitializeValue(ref location, init, obj);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public unsafe static T GetOrInitWithLock<T, TParam>(ref T? location, object @lock, delegate*<TParam, T> init, TParam obj) where T : class {
            if (location is not null)
                return location;
            return InitializeValueWithLock(ref location, @lock, init, obj);
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        private unsafe static T InitializeValue<T, TParam>(ref T? location, delegate*<TParam, T> init, TParam obj) where T : class {
            _ = Interlocked.CompareExchange(ref location, init(obj), null);
            return location!;
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        private unsafe static T InitializeValueWithLock<T, TParam>(ref T? location, object @lock, delegate*<TParam, T> init, TParam obj) where T : class {
            lock (@lock) {
                if (location is not null)
                    return location;
                return location = init(obj);
            }
        }
        #endregion

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool MaskedSequenceEqual(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> mask) {
            if (mask.Length < first.Length || mask.Length < second.Length)
                ThrowMaskTooShort();

            return first.Length == second.Length &&
                MaskedSequenceEqualCore(
                    ref MemoryMarshal.GetReference(first),
                    ref MemoryMarshal.GetReference(second),
                    ref MemoryMarshal.GetReference(mask),
                    (nuint) first.Length);
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        [DoesNotReturn]
        private static void ThrowMaskTooShort() {
            throw new ArgumentException("Mask too short", "mask");
        }

        // The below is a slightly modified version of SequenceEqual from Backports

        // Optimized byte-based SequenceEquals. The "length" parameter for this one is declared a nuint rather than int as we also use it for types other than byte
        // where the length can exceed 2Gb once scaled by sizeof(T).
        private static unsafe bool MaskedSequenceEqualCore(ref byte first, ref byte second, ref byte maskBytes, nuint length) {
            if (Unsafe.AreSame(ref first, ref second))
                goto Equal;

            nint i = 0; // Use IntPtr for arithmetic to avoid unnecessary 64->32->64 truncations
            var n = (nint) (void*) length;

            if ((byte*) n >= (byte*) sizeof(nuint)) {
                nuint mask;
                n -= sizeof(nuint);
                while ((byte*) n > (byte*) i) {
                    mask = Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref maskBytes, i));
                    if ((Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref first, i)) & mask) !=
                        (Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref second, i)) & mask)) {
                        goto NotEqual;
                    }
                    i += sizeof(nuint);
                }
                mask = Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref maskBytes, i));
                return (Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref first, n)) & mask) ==
                       (Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref second, n)) & mask);
            }

            while ((byte*) n > (byte*) i) {
                var mask = Unsafe.AddByteOffset(ref maskBytes, i);
                if ((Unsafe.AddByteOffset(ref first, i) & mask) != (Unsafe.AddByteOffset(ref second, i) & mask))
                    goto NotEqual;
                i += 1;
            }

            Equal:
            return true;

            NotEqual: // Workaround for https://github.com/dotnet/coreclr/issues/13549
            return false;
        }
    }

    [InterpolatedStringHandler]
    public ref struct AssertionInterpolatedStringHandler {
        private DebugLogInterpolatedStringHandler handler;

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public AssertionInterpolatedStringHandler(int literalLen, int formattedCount, bool assertValue, out bool isEnabled) {
            handler = new(literalLen, formattedCount, enabled: !assertValue, recordHoles: false, out isEnabled);
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