// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace System {
    //
    // This pattern of easily inlinable "void Throw" routines that stack on top of NoInlining factory methods
    // is a compromise between older JITs and newer JITs (RyuJIT in Core CLR 1.1.0+ and desktop CLR in 4.6.3+).
    // This package is explicitly targeted at older JITs as newer runtimes expect to implement Span intrinsically for
    // best performance.
    //
    // The aim of this pattern is three-fold
    // 1. Extracting the throw makes the method preforming the throw in a conditional branch smaller and more inlinable
    // 2. Extracting the throw from generic method to non-generic method reduces the repeated codegen size for value types
    // 3a. Newer JITs will not inline the methods that only throw and also recognise them, move the call to cold section
    //     and not add stack prep and unwind before calling https://github.com/dotnet/coreclr/pull/6103
    // 3b. Older JITs will inline the throw itself and move to cold section; but not inline the non-inlinable exception
    //     factory methods - still maintaining advantages 1 & 2
    //
    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "We don't call any virtual methods on Exception")]
    internal static partial class ThrowHelper {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowIfArgumentNull([NotNull] object? obj, ExceptionArgument argument) {
            if (obj is null)
                ThrowArgumentNullException(argument);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowIfArgumentNull([NotNull] object? obj, string argument, string? message = null) {
            if (obj is null)
                ThrowArgumentNullException(argument, message);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentNullException(ExceptionArgument argument) => throw CreateArgumentNullException(argument);
        [DoesNotReturn]
        internal static void ThrowArgumentNullException(string argument, string? message = null) => throw CreateArgumentNullException(argument, message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentNullException(ExceptionArgument argument) => CreateArgumentNullException(argument.ToString());
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentNullException(string argument, string? message = null) => new ArgumentNullException(argument, message);

        [DoesNotReturn]
        internal static void ThrowArrayTypeMismatchException() => throw CreateArrayTypeMismatchException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArrayTypeMismatchException() => new ArrayTypeMismatchException();

        [DoesNotReturn]
        internal static void ThrowArgumentException_InvalidTypeWithPointersNotSupported(Type type) => throw CreateArgumentException_InvalidTypeWithPointersNotSupported(type);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentException_InvalidTypeWithPointersNotSupported(Type type) => new ArgumentException($"Type {type} with managed pointers cannot be used in a Span");

        [DoesNotReturn]
        internal static void ThrowArgumentException_DestinationTooShort() => throw CreateArgumentException_DestinationTooShort();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentException_DestinationTooShort() => new ArgumentException("Destination too short");


        [DoesNotReturn]
        internal static void ThrowArgumentException(string message, string? argument = null) => throw CreateArgumentException(message, argument);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentException(string message, string? argument) => new ArgumentException(message, argument ?? "");

        [DoesNotReturn]
        internal static void ThrowIndexOutOfRangeException() => throw CreateIndexOutOfRangeException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateIndexOutOfRangeException() => new IndexOutOfRangeException();

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException() => throw CreateArgumentOutOfRangeException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentOutOfRangeException() => new ArgumentOutOfRangeException();

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument) => throw CreateArgumentOutOfRangeException(argument);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentOutOfRangeException(ExceptionArgument argument) => new ArgumentOutOfRangeException(argument.ToString());

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_PrecisionTooLarge() => throw CreateArgumentOutOfRangeException_PrecisionTooLarge();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentOutOfRangeException_PrecisionTooLarge() => new ArgumentOutOfRangeException("precision", $"Precision too large (max: {StandardFormat.MaxPrecision})");

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_SymbolDoesNotFit() => throw CreateArgumentOutOfRangeException_SymbolDoesNotFit();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentOutOfRangeException_SymbolDoesNotFit() => new ArgumentOutOfRangeException("symbol", "Bad format specifier");

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException() => throw CreateInvalidOperationException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateInvalidOperationException() => new InvalidOperationException();

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_OutstandingReferences() => throw CreateInvalidOperationException_OutstandingReferences();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateInvalidOperationException_OutstandingReferences() => new InvalidOperationException("Outstanding references");

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_UnexpectedSegmentType() => throw CreateInvalidOperationException_UnexpectedSegmentType();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateInvalidOperationException_UnexpectedSegmentType() => new InvalidOperationException("Unexpected segment type");

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_EndPositionNotReached() => throw CreateInvalidOperationException_EndPositionNotReached();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateInvalidOperationException_EndPositionNotReached() => new InvalidOperationException("End position not reached");

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_PositionOutOfRange() => throw CreateArgumentOutOfRangeException_PositionOutOfRange();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentOutOfRangeException_PositionOutOfRange() => new ArgumentOutOfRangeException("position");

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_OffsetOutOfRange() => throw CreateArgumentOutOfRangeException_OffsetOutOfRange();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentOutOfRangeException_OffsetOutOfRange() => new ArgumentOutOfRangeException(nameof(ExceptionArgument.offset));

        [DoesNotReturn]
        internal static void ThrowObjectDisposedException_ArrayMemoryPoolBuffer() => throw CreateObjectDisposedException_ArrayMemoryPoolBuffer();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateObjectDisposedException_ArrayMemoryPoolBuffer() => new ObjectDisposedException("ArrayMemoryPoolBuffer");

        [DoesNotReturn]
        internal static void ThrowFormatException_BadFormatSpecifier() => throw CreateFormatException_BadFormatSpecifier();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateFormatException_BadFormatSpecifier() => new FormatException("Bad format specifier");

        [DoesNotReturn]
        internal static void ThrowArgumentException_OverlapAlignmentMismatch() => throw CreateArgumentException_OverlapAlignmentMismatch();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateArgumentException_OverlapAlignmentMismatch() => new ArgumentException("Overlap alignment mismatch");

        [DoesNotReturn]
        internal static void ThrowNotSupportedException(string? msg = null) => throw CreateThrowNotSupportedException(msg);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateThrowNotSupportedException(string? msg) => new NotSupportedException();

        [DoesNotReturn]
        internal static void ThrowKeyNullException() => ThrowArgumentNullException(ExceptionArgument.key);

        [DoesNotReturn]
        internal static void ThrowValueNullException() => throw CreateThrowValueNullException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateThrowValueNullException() => new ArgumentException("Value is null");

        [DoesNotReturn]
        internal static void ThrowOutOfMemoryException() => throw CreateOutOfMemoryException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception CreateOutOfMemoryException() => new OutOfMemoryException();

        //
        // Enable use of ThrowHelper from TryFormat() routines without introducing dozens of non-code-coveraged "bytesWritten = 0; return false" boilerplate.
        //
        public static bool TryFormatThrowFormatException(out int bytesWritten) {
            bytesWritten = 0;
            ThrowHelper.ThrowFormatException_BadFormatSpecifier();
            return false;
        }

        //
        // Enable use of ThrowHelper from TryParse() routines without introducing dozens of non-code-coveraged "value= default; bytesConsumed = 0; return false" boilerplate.
        //
        public static bool TryParseThrowFormatException<T>(out T value, out int bytesConsumed) {
            value = default!;
            bytesConsumed = 0;
            ThrowHelper.ThrowFormatException_BadFormatSpecifier();
            return false;
        }

        //
        // ReadOnlySequence .ctor validation Throws coalesced to enable inlining of the .ctor
        //
        [DoesNotReturn]
        public static void ThrowArgumentValidationException<T>(ReadOnlySequenceSegment<T>? startSegment, int startIndex, ReadOnlySequenceSegment<T>? endSegment)
            => throw CreateArgumentValidationException(startSegment, startIndex, endSegment);

        private static Exception CreateArgumentValidationException<T>(ReadOnlySequenceSegment<T>? startSegment, int startIndex, ReadOnlySequenceSegment<T>? endSegment) {
            if (startSegment == null)
                return CreateArgumentNullException(ExceptionArgument.startSegment);
            else if (endSegment == null)
                return CreateArgumentNullException(ExceptionArgument.endSegment);
            else if (startSegment != endSegment && startSegment.RunningIndex > endSegment.RunningIndex)
                return CreateArgumentOutOfRangeException(ExceptionArgument.endSegment);
            else if ((uint) startSegment.Memory.Length < (uint) startIndex)
                return CreateArgumentOutOfRangeException(ExceptionArgument.startIndex);
            else
                return CreateArgumentOutOfRangeException(ExceptionArgument.endIndex);
        }

        [DoesNotReturn]
        public static void ThrowArgumentValidationException(Array? array, int start)
            => throw CreateArgumentValidationException(array, start);

        private static Exception CreateArgumentValidationException(Array? array, int start) {
            if (array == null)
                return CreateArgumentNullException(ExceptionArgument.array);
            else if ((uint) start > (uint) array.Length)
                return CreateArgumentOutOfRangeException(ExceptionArgument.start);
            else
                return CreateArgumentOutOfRangeException(ExceptionArgument.length);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentException_TupleIncorrectType(object other) => throw new ArgumentException($"Value tuple of incorrect type (found {other.GetType()})", nameof(other));

        //
        // ReadOnlySequence Slice validation Throws coalesced to enable inlining of the Slice
        //
        [DoesNotReturn]
        public static void ThrowStartOrEndArgumentValidationException(long start)
            => throw CreateStartOrEndArgumentValidationException(start);

        private static Exception CreateStartOrEndArgumentValidationException(long start) {
            if (start < 0)
                return CreateArgumentOutOfRangeException(ExceptionArgument.start);
            return CreateArgumentOutOfRangeException(ExceptionArgument.length);
        }

    }

    //
    // The convention for this enum is using the argument name as the enum name
    //
    internal enum ExceptionArgument {
        length,
        start,
        bufferSize,
        minimumBufferSize,
        elementIndex,
        comparable,
        comparer,
        destination,
        offset,
        startSegment,
        endSegment,
        startIndex,
        endIndex,
        array,
        culture,
        manager,
        key,
        collection,
        index,
        type,
        self,
        value,
        oldValue,
        newValue,
    }
}