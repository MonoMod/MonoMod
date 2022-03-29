// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace System {

#pragma warning disable CA1036 // Override methods on comparable types
#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable CA2231 // Overload operator equals on overriding value type Equals
    // For some reason, the BCL implementation doesn't do any of the above either.

#pragma warning disable IDE0008 // Use explicit type
#pragma warning disable IDE0046
    // Most of this code is copied directly from the BCL.

#pragma warning disable CA1051 // Do not declare visible instance fields
    // One of the major points of ValueTuple is the visible instance fields

    /// <summary>
    /// Helper so we can call some tuple methods recursively without knowing the underlying types.
    /// </summary>
    internal interface ITupleInternal {
        int Size { get; }

        int GetHashCode(IEqualityComparer comparer);

        string ToStringEnd();
    }

    /// <summary>
    /// The ValueTuple types (from arity 0 to 8) comprise the runtime implementation that underlies tuples in C# and struct tuples in F#.
    /// Aside from created via language syntax, they are most easily created via the ValueTuple.Create factory methods.
    /// The System.ValueTuple types differ from the System.Tuple types in that:
    /// - they are structs rather than classes,
    /// - they are mutable rather than readonly, and
    /// - their members (such as Item1, Item2, etc) are fields rather than properties.
    /// </summary>
    public readonly struct ValueTuple
        : IEquatable<ValueTuple>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple>, ITupleInternal {
        int ITupleInternal.Size => 0;

        /// <summary>Creates a new struct 0-tuple.</summary>
        /// <returns>A 0-tuple.</returns>
        public static ValueTuple Create() {
            return default;
        }

        /// <summary>Creates a new struct 1-tuple, or singleton.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <returns>A 1-tuple (singleton) whose value is (item1).</returns>
        public static ValueTuple<T1> Create<T1>(T1 item1) {
            return new ValueTuple<T1>(item1);
        }

        /// <summary>Creates a new struct 2-tuple, or pair.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <returns>A 2-tuple (pair) whose value is (item1, item2).</returns>
        public static ValueTuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) {
            return new ValueTuple<T1, T2>(item1, item2);
        }

        /// <summary>Creates a new struct 3-tuple, or triple.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <typeparam name="T3">The type of the third component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <param name="item3">The value of the third component of the tuple.</param>
        /// <returns>A 3-tuple (triple) whose value is (item1, item2, item3).</returns>
        public static ValueTuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) {
            return new ValueTuple<T1, T2, T3>(item1, item2, item3);
        }

        /// <summary>Creates a new struct 4-tuple, or quadruple.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <typeparam name="T3">The type of the third component of the tuple.</typeparam>
        /// <typeparam name="T4">The type of the fourth component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <param name="item3">The value of the third component of the tuple.</param>
        /// <param name="item4">The value of the fourth component of the tuple.</param>
        /// <returns>A 4-tuple (quadruple) whose value is (item1, item2, item3, item4).</returns>
        public static ValueTuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) {
            return new ValueTuple<T1, T2, T3, T4>(item1, item2, item3, item4);
        }

        /// <summary>Creates a new struct 5-tuple, or quintuple.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <typeparam name="T3">The type of the third component of the tuple.</typeparam>
        /// <typeparam name="T4">The type of the fourth component of the tuple.</typeparam>
        /// <typeparam name="T5">The type of the fifth component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <param name="item3">The value of the third component of the tuple.</param>
        /// <param name="item4">The value of the fourth component of the tuple.</param>
        /// <param name="item5">The value of the fifth component of the tuple.</param>
        /// <returns>A 5-tuple (quintuple) whose value is (item1, item2, item3, item4, item5).</returns>
        public static ValueTuple<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) {
            return new ValueTuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
        }

        /// <summary>Creates a new struct 6-tuple, or sextuple.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <typeparam name="T3">The type of the third component of the tuple.</typeparam>
        /// <typeparam name="T4">The type of the fourth component of the tuple.</typeparam>
        /// <typeparam name="T5">The type of the fifth component of the tuple.</typeparam>
        /// <typeparam name="T6">The type of the sixth component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <param name="item3">The value of the third component of the tuple.</param>
        /// <param name="item4">The value of the fourth component of the tuple.</param>
        /// <param name="item5">The value of the fifth component of the tuple.</param>
        /// <param name="item6">The value of the sixth component of the tuple.</param>
        /// <returns>A 6-tuple (sextuple) whose value is (item1, item2, item3, item4, item5, item6).</returns>
        public static ValueTuple<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) {
            return new ValueTuple<T1, T2, T3, T4, T5, T6>(item1, item2, item3, item4, item5, item6);
        }

        /// <summary>Creates a new struct 7-tuple, or septuple.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <typeparam name="T3">The type of the third component of the tuple.</typeparam>
        /// <typeparam name="T4">The type of the fourth component of the tuple.</typeparam>
        /// <typeparam name="T5">The type of the fifth component of the tuple.</typeparam>
        /// <typeparam name="T6">The type of the sixth component of the tuple.</typeparam>
        /// <typeparam name="T7">The type of the seventh component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <param name="item3">The value of the third component of the tuple.</param>
        /// <param name="item4">The value of the fourth component of the tuple.</param>
        /// <param name="item5">The value of the fifth component of the tuple.</param>
        /// <param name="item6">The value of the sixth component of the tuple.</param>
        /// <param name="item7">The value of the seventh component of the tuple.</param>
        /// <returns>A 7-tuple (septuple) whose value is (item1, item2, item3, item4, item5, item6, item7).</returns>
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) {
            return new ValueTuple<T1, T2, T3, T4, T5, T6, T7>(item1, item2, item3, item4, item5, item6, item7);
        }

        /// <summary>Creates a new struct 8-tuple, or octuple.</summary>
        /// <typeparam name="T1">The type of the first component of the tuple.</typeparam>
        /// <typeparam name="T2">The type of the second component of the tuple.</typeparam>
        /// <typeparam name="T3">The type of the third component of the tuple.</typeparam>
        /// <typeparam name="T4">The type of the fourth component of the tuple.</typeparam>
        /// <typeparam name="T5">The type of the fifth component of the tuple.</typeparam>
        /// <typeparam name="T6">The type of the sixth component of the tuple.</typeparam>
        /// <typeparam name="T7">The type of the seventh component of the tuple.</typeparam>
        /// <typeparam name="T8">The type of the eighth component of the tuple.</typeparam>
        /// <param name="item1">The value of the first component of the tuple.</param>
        /// <param name="item2">The value of the second component of the tuple.</param>
        /// <param name="item3">The value of the third component of the tuple.</param>
        /// <param name="item4">The value of the fourth component of the tuple.</param>
        /// <param name="item5">The value of the fifth component of the tuple.</param>
        /// <param name="item6">The value of the sixth component of the tuple.</param>
        /// <param name="item7">The value of the seventh component of the tuple.</param>
        /// <param name="item8">The value of the eighth component of the tuple.</param>
        /// <returns>An 8-tuple (octuple) whose value is (item1, item2, item3, item4, item5, item6, item7, item8).</returns>
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8>> Create<T1, T2, T3, T4, T5, T6, T7, T8>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8) {
            return new ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8>>(item1, item2, item3, item4, item5, item6, item7, Create(item8));
        }

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public int CompareTo(ValueTuple other) {
            return 0;
        }

        int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return 0;
        }

        int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            return 0;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="ValueTuple"/>.</returns>
        public override bool Equals(object? obj) {
            return obj is ValueTuple;
        }

        /// <summary>Returns a value indicating whether this instance is equal to a specified value.</summary>
        /// <param name="other">An instance to compare to this instance.</param>
        /// <returns>true if <paramref name="other" /> has the same value as this instance; otherwise, false.</returns>
        public bool Equals(ValueTuple other) {
            return true;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            return other is ValueTuple;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode() {
            return 0;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return 0;
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return 0;
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>()</c>.
        /// </remarks>
        public override string ToString() {
            return "()";
        }

        string ITupleInternal.ToStringEnd() {
            return ")";
        }

        internal static int CombineHashCodes(int h1, int h2) {
            // TODO: implement using polyfilled hashcode helpers
            throw new NotImplementedException();
        }

        internal static int CombineHashCodes(int h1, int h2, int h3) {
            return CombineHashCodes(CombineHashCodes(h1, h2), h3);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4) {
            return CombineHashCodes(CombineHashCodes(h1, h2), CombineHashCodes(h3, h4));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5) {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), h5);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6) {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7) {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6, h7));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8) {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6, h7, h8));
        }

        internal static int HashCodeOf<T>(IEqualityComparer? comparer, T value) {
            return value is { } notnull ? (comparer ?? EqualityComparer<T>.Default).GetHashCode(notnull) : 0;
        }
    }

    /// <summary>Represents a 1-tuple, or singleton, as a value type.</summary>
    /// <typeparam name="T1">The type of the tuple's only component.</typeparam>
    public struct ValueTuple<T1>
        : IEquatable<ValueTuple<T1>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        public ValueTuple(T1 item1) {
            Item1 = item1;
        }

        readonly int ITupleInternal.Size => 1;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1> other) {
            return Comparer<T1>.Default.Compare(Item1, other.Item1);
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1> other) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return Comparer<T1>.Default.Compare(Item1, other.Item1);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            return comparer.Compare(Item1, valueTuple.Item1);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its field
        /// is equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1);
        }

        readonly bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1);
            }

            return false;
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.HashCodeOf(null, Item1);
        }

        readonly int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return ValueTuple.HashCodeOf(comparer, Item1);
        }

        readonly int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return ValueTuple.HashCodeOf(comparer, Item1);
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1)</c>,
        /// where <c>Item1</c> represents the value of <see cref="Item1"/>. If the field is <see langword="null"/>,
        /// it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty})";
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty})";
        }
    }

    /// <summary>
    /// Represents a 2-tuple, or pair, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2>
        : IEquatable<ValueTuple<T1, T2>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2}"/> instance's first component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        public ValueTuple(T1 item1, T2 item2) {
            Item1 = item1;
            Item2 = item2;
        }

        readonly int ITupleInternal.Size => 2;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        ///
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2}" /> instance is equal to a specified <see cref="ValueTuple{T1, T2}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer: null, Item1),
                ValueTuple.HashCodeOf(null, Item2)
            );
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2)</c>,
        /// where <c>Item1</c> and <c>Item2</c> represent the values of the <see cref="Item1"/>
        /// and <see cref="Item2"/> fields. If either field value is <see langword="null"/>,
        /// it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty})";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1, T2> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return CompareTo(valueTuple);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2}"/> instance is equal to a specified object based on a specified comparison method.
        /// </summary>
        /// <param name="other">The object to compare with this instance.</param>
        /// <param name="comparer">An object that defines the method to use to evaluate whether the two objects are equal.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        ///
        /// <remarks>
        /// <para>
        /// This member is an explicit interface member implementation. It can be used only when the
        ///  <see cref="ValueTuple{T1, T2}"/> instance is cast to an <see cref="IStructuralEquatable"/> interface.
        /// </para>
        /// <para>
        /// The <see cref="IEqualityComparer.Equals(object,object)"/> implementation is called only if <c>other</c> is not <see langword="null"/>,
        ///  and if it can be successfully cast (in C#) or converted (in Visual Basic) to a <see cref="ValueTuple{T1, T2}"/>
        ///  whose components are of the same types as those of the current instance. The IStructuralEquatable.Equals(Object, IEqualityComparer) method
        ///  first passes the <see cref="Item1"/> values of the <see cref="ValueTuple{T1, T2}"/> objects to be compared to the
        ///  <see cref="IEqualityComparer.Equals(object,object)"/> implementation. If this method call returns <see langword="true"/>, the method is
        ///  called again and passed the <see cref="Item2"/> values of the two <see cref="ValueTuple{T1, T2}"/> instances.
        /// </para>
        /// </remarks>
        readonly bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2);
            }

            return false;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer, Item1),
                ValueTuple.HashCodeOf(comparer, Item2)
            );
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty})";
        }
    }

    /// <summary>
    /// Represents a 3-tuple, or triple, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    /// <typeparam name="T3">The type of the tuple's third component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2, T3>
        : IEquatable<ValueTuple<T1, T2, T3>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2, T3>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3}"/> instance's second component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3}"/> instance's third component.
        /// </summary>
        public T3 Item3;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2, T3}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        /// <param name="item3">The value of the tuple's third component.</param>
        public ValueTuple(T1 item1, T2 item2, T3 item3) {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        int ITupleInternal.Size => 3;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2, T3> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            if (result == 0) {
                result = Comparer<T3>.Default.Compare(Item3, other.Item3);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2, T3}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2, T3> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1, T2, T3}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2, T3> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                   && EqualityComparer<T3>.Default.Equals(Item3, other.Item3);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2, T3}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(null, Item1),
                ValueTuple.HashCodeOf(null, Item2),
                ValueTuple.HashCodeOf(null, Item3)
            );
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2, T3}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2, T3}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2, Item3)</c>.
        /// If any field value is <see langword="null"/>, it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty})";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1, T2, T3> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return CompareTo(valueTuple);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2, T3> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            if (result == 0) {
                result = comparer.Compare(Item3, valueTuple.Item3);
            }

            return result;
        }

        readonly bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2, T3> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2)
                       && comparer.Equals(Item3, valueTuple.Item3);
            }

            return false;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer, Item1),
                ValueTuple.HashCodeOf(comparer, Item2),
                ValueTuple.HashCodeOf(comparer, Item3)
            );
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty})";
        }
    }

    /// <summary>
    /// Represents a 4-tuple, or quadruple, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    /// <typeparam name="T3">The type of the tuple's third component.</typeparam>
    /// <typeparam name="T4">The type of the tuple's fourth component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2, T3, T4>
        : IEquatable<ValueTuple<T1, T2, T3, T4>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4}"/> instance's second component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4}"/> instance's third component.
        /// </summary>
        public T3 Item3;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4}"/> instance's fourth component.
        /// </summary>
        public T4 Item4;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2, T3, T4}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        /// <param name="item3">The value of the tuple's third component.</param>
        /// <param name="item4">The value of the tuple's fourth component.</param>
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4) {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }

        int ITupleInternal.Size => 4;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2, T3, T4> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            if (result == 0) {
                result = Comparer<T3>.Default.Compare(Item3, other.Item3);
            }

            if (result == 0) {
                result = Comparer<T4>.Default.Compare(Item4, other.Item4);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2, T3, T4}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2, T3, T4> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1, T2, T3, T4}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2, T3, T4> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                   && EqualityComparer<T3>.Default.Equals(Item3, other.Item3)
                   && EqualityComparer<T4>.Default.Equals(Item4, other.Item4);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2, T3, T4}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(null, Item1),
                ValueTuple.HashCodeOf(null, Item2),
                ValueTuple.HashCodeOf(null, Item3),
                ValueTuple.HashCodeOf(null, Item4)
            );
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2, T3, T4}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2, T3, T4}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2, Item3, Item4)</c>.
        /// If any field value is <see langword="null"/>, it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty})";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1, T2, T3, T4> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return CompareTo(valueTuple);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2, T3, T4> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            if (result == 0) {
                result = comparer.Compare(Item3, valueTuple.Item3);
            }

            if (result == 0) {
                result = comparer.Compare(Item4, valueTuple.Item4);
            }

            return result;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2, T3, T4> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2)
                       && comparer.Equals(Item3, valueTuple.Item3)
                       && comparer.Equals(Item4, valueTuple.Item4);
            }

            return false;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer, Item1),
                ValueTuple.HashCodeOf(comparer, Item2),
                ValueTuple.HashCodeOf(comparer, Item3),
                ValueTuple.HashCodeOf(comparer, Item4)
            );
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty})";
        }
    }

    /// <summary>
    /// Represents a 5-tuple, or quintuple, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    /// <typeparam name="T3">The type of the tuple's third component.</typeparam>
    /// <typeparam name="T4">The type of the tuple's fourth component.</typeparam>
    /// <typeparam name="T5">The type of the tuple's fifth component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2, T3, T4, T5>
        : IEquatable<ValueTuple<T1, T2, T3, T4, T5>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance's second component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance's third component.
        /// </summary>
        public T3 Item3;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance's fourth component.
        /// </summary>
        public T4 Item4;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance's fifth component.
        /// </summary>
        public T5 Item5;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        /// <param name="item3">The value of the tuple's third component.</param>
        /// <param name="item4">The value of the tuple's fourth component.</param>
        /// <param name="item5">The value of the tuple's fifth component.</param>
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
        }

        int ITupleInternal.Size => 5;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2, T3, T4, T5> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            if (result == 0) {
                result = Comparer<T3>.Default.Compare(Item3, other.Item3);
            }

            if (result == 0) {
                result = Comparer<T4>.Default.Compare(Item4, other.Item4);
            }

            if (result == 0) {
                result = Comparer<T5>.Default.Compare(Item5, other.Item5);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2, T3, T4, T5> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1, T2, T3, T4, T5}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2, T3, T4, T5> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                   && EqualityComparer<T3>.Default.Equals(Item3, other.Item3)
                   && EqualityComparer<T4>.Default.Equals(Item4, other.Item4)
                   && EqualityComparer<T5>.Default.Equals(Item5, other.Item5);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(null, Item1),
                ValueTuple.HashCodeOf(null, Item2),
                ValueTuple.HashCodeOf(null, Item3),
                ValueTuple.HashCodeOf(null, Item4),
                ValueTuple.HashCodeOf(null, Item5)
            );
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2, Item3, Item4, Item5)</c>.
        /// If any field value is <see langword="null"/>, it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty})";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1, T2, T3, T4, T5> valueTask) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return CompareTo(valueTask);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2, T3, T4, T5> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            if (result == 0) {
                result = comparer.Compare(Item3, valueTuple.Item3);
            }

            if (result == 0) {
                result = comparer.Compare(Item4, valueTuple.Item4);
            }

            if (result == 0) {
                result = comparer.Compare(Item5, valueTuple.Item5);
            }

            return result;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2, T3, T4, T5> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2)
                       && comparer.Equals(Item3, valueTuple.Item3)
                       && comparer.Equals(Item4, valueTuple.Item4)
                       && comparer.Equals(Item5, valueTuple.Item5);
            }

            return false;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer, Item1),
                ValueTuple.HashCodeOf(comparer, Item2),
                ValueTuple.HashCodeOf(comparer, Item3),
                ValueTuple.HashCodeOf(comparer, Item4),
                ValueTuple.HashCodeOf(comparer, Item5)
            );
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty})";
        }
    }

    /// <summary>
    /// Represents a 6-tuple, or sextuple, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    /// <typeparam name="T3">The type of the tuple's third component.</typeparam>
    /// <typeparam name="T4">The type of the tuple's fourth component.</typeparam>
    /// <typeparam name="T5">The type of the tuple's fifth component.</typeparam>
    /// <typeparam name="T6">The type of the tuple's sixth component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2, T3, T4, T5, T6>
        : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5, T6>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance's second component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance's third component.
        /// </summary>
        public T3 Item3;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance's fourth component.
        /// </summary>
        public T4 Item4;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance's fifth component.
        /// </summary>
        public T5 Item5;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance's sixth component.
        /// </summary>
        public T6 Item6;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        /// <param name="item3">The value of the tuple's third component.</param>
        /// <param name="item4">The value of the tuple's fourth component.</param>
        /// <param name="item5">The value of the tuple's fifth component.</param>
        /// <param name="item6">The value of the tuple's sixth component.</param>
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
        }

        int ITupleInternal.Size => 6;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2, T3, T4, T5, T6> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            if (result == 0) {
                result = Comparer<T3>.Default.Compare(Item3, other.Item3);
            }

            if (result == 0) {
                result = Comparer<T4>.Default.Compare(Item4, other.Item4);
            }

            if (result == 0) {
                result = Comparer<T5>.Default.Compare(Item5, other.Item5);
            }

            if (result == 0) {
                result = Comparer<T6>.Default.Compare(Item6, other.Item6);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2, T3, T4, T5, T6> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                   && EqualityComparer<T3>.Default.Equals(Item3, other.Item3)
                   && EqualityComparer<T4>.Default.Equals(Item4, other.Item4)
                   && EqualityComparer<T5>.Default.Equals(Item5, other.Item5)
                   && EqualityComparer<T6>.Default.Equals(Item6, other.Item6);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(null, Item1),
                ValueTuple.HashCodeOf(null, Item2),
                ValueTuple.HashCodeOf(null, Item3),
                ValueTuple.HashCodeOf(null, Item4),
                ValueTuple.HashCodeOf(null, Item5),
                ValueTuple.HashCodeOf(null, Item6)
            );
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2, Item3, Item4, Item5, Item6)</c>.
        /// If any field value is <see langword="null"/>, it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty}, {Item6?.ToString() ?? string.Empty})";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1, T2, T3, T4, T5, T6> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return CompareTo(valueTuple);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2, T3, T4, T5, T6> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            if (result == 0) {
                result = comparer.Compare(Item3, valueTuple.Item3);
            }

            if (result == 0) {
                result = comparer.Compare(Item4, valueTuple.Item4);
            }

            if (result == 0) {
                result = comparer.Compare(Item5, valueTuple.Item5);
            }

            if (result == 0) {
                result = comparer.Compare(Item6, valueTuple.Item6);
            }

            return result;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2, T3, T4, T5, T6> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2)
                       && comparer.Equals(Item3, valueTuple.Item3)
                       && comparer.Equals(Item4, valueTuple.Item4)
                       && comparer.Equals(Item5, valueTuple.Item5)
                       && comparer.Equals(Item6, valueTuple.Item6);
            }

            return false;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer, Item1),
                ValueTuple.HashCodeOf(comparer, Item2),
                ValueTuple.HashCodeOf(comparer, Item3),
                ValueTuple.HashCodeOf(comparer, Item4),
                ValueTuple.HashCodeOf(comparer, Item5),
                ValueTuple.HashCodeOf(comparer, Item6)
            );
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty}, {Item6?.ToString() ?? string.Empty})";
        }
    }

    /// <summary>
    /// Represents a 7-tuple, or septuple, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    /// <typeparam name="T3">The type of the tuple's third component.</typeparam>
    /// <typeparam name="T4">The type of the tuple's fourth component.</typeparam>
    /// <typeparam name="T5">The type of the tuple's fifth component.</typeparam>
    /// <typeparam name="T6">The type of the tuple's sixth component.</typeparam>
    /// <typeparam name="T7">The type of the tuple's seventh component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7>
        : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>, ITupleInternal {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's second component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's third component.
        /// </summary>
        public T3 Item3;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's fourth component.
        /// </summary>
        public T4 Item4;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's fifth component.
        /// </summary>
        public T5 Item5;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's sixth component.
        /// </summary>
        public T6 Item6;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance's seventh component.
        /// </summary>
        public T7 Item7;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        /// <param name="item3">The value of the tuple's third component.</param>
        /// <param name="item4">The value of the tuple's fourth component.</param>
        /// <param name="item5">The value of the tuple's fifth component.</param>
        /// <param name="item6">The value of the tuple's sixth component.</param>
        /// <param name="item7">The value of the tuple's seventh component.</param>
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
        }

        int ITupleInternal.Size => 7;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2, T3, T4, T5, T6, T7> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            if (result == 0) {
                result = Comparer<T3>.Default.Compare(Item3, other.Item3);
            }

            if (result == 0) {
                result = Comparer<T4>.Default.Compare(Item4, other.Item4);
            }

            if (result == 0) {
                result = Comparer<T5>.Default.Compare(Item5, other.Item5);
            }

            if (result == 0) {
                result = Comparer<T6>.Default.Compare(Item6, other.Item6);
            }

            if (result == 0) {
                result = Comparer<T7>.Default.Compare(Item7, other.Item7);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2, T3, T4, T5, T6, T7> valueTuple && Equals(valueTuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                   && EqualityComparer<T3>.Default.Equals(Item3, other.Item3)
                   && EqualityComparer<T4>.Default.Equals(Item4, other.Item4)
                   && EqualityComparer<T5>.Default.Equals(Item5, other.Item5)
                   && EqualityComparer<T6>.Default.Equals(Item6, other.Item6)
                   && EqualityComparer<T7>.Default.Equals(Item7, other.Item7);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(null, Item1),
                ValueTuple.HashCodeOf(null, Item2),
                ValueTuple.HashCodeOf(null, Item3),
                ValueTuple.HashCodeOf(null, Item4),
                ValueTuple.HashCodeOf(null, Item5),
                ValueTuple.HashCodeOf(null, Item6),
                ValueTuple.HashCodeOf(null, Item7)
            );
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2, Item3, Item4, Item5, Item6, Item7)</c>.
        /// If any field value is <see langword="null"/>, it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty}, {Item6?.ToString() ?? string.Empty}, {Item7?.ToString() ?? string.Empty})";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is not ValueTuple<T1, T2, T3, T4, T5, T6, T7> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
            }

            return CompareTo(valueTuple);
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2, T3, T4, T5, T6, T7> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            if (result == 0) {
                result = comparer.Compare(Item3, valueTuple.Item3);
            }

            if (result == 0) {
                result = comparer.Compare(Item4, valueTuple.Item4);
            }

            if (result == 0) {
                result = comparer.Compare(Item5, valueTuple.Item5);
            }

            if (result == 0) {
                result = comparer.Compare(Item6, valueTuple.Item6);
            }

            if (result == 0) {
                result = comparer.Compare(Item7, valueTuple.Item7);
            }

            return result;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2, T3, T4, T5, T6, T7> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2)
                       && comparer.Equals(Item3, valueTuple.Item3)
                       && comparer.Equals(Item4, valueTuple.Item4)
                       && comparer.Equals(Item5, valueTuple.Item5)
                       && comparer.Equals(Item6, valueTuple.Item6)
                       && comparer.Equals(Item7, valueTuple.Item7);
            }

            return false;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            return ValueTuple.CombineHashCodes
            (
                ValueTuple.HashCodeOf(comparer, Item1),
                ValueTuple.HashCodeOf(comparer, Item2),
                ValueTuple.HashCodeOf(comparer, Item3),
                ValueTuple.HashCodeOf(comparer, Item4),
                ValueTuple.HashCodeOf(comparer, Item5),
                ValueTuple.HashCodeOf(comparer, Item6),
                ValueTuple.HashCodeOf(comparer, Item7)
            );
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty}, {Item6?.ToString() ?? string.Empty}, {Item7?.ToString() ?? string.Empty})";
        }
    }


#pragma warning disable CA1508 // Avoid dead conditional code
    // I don't know why, but the analyzer seems to think that TRest always implements ITupleInternal when it doesn't

    /// <summary>
    /// Represents an 8-tuple, or octuple, as a value type.
    /// </summary>
    /// <typeparam name="T1">The type of the tuple's first component.</typeparam>
    /// <typeparam name="T2">The type of the tuple's second component.</typeparam>
    /// <typeparam name="T3">The type of the tuple's third component.</typeparam>
    /// <typeparam name="T4">The type of the tuple's fourth component.</typeparam>
    /// <typeparam name="T5">The type of the tuple's fifth component.</typeparam>
    /// <typeparam name="T6">The type of the tuple's sixth component.</typeparam>
    /// <typeparam name="T7">The type of the tuple's seventh component.</typeparam>
    /// <typeparam name="TRest">The type of the tuple's eighth component.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>
        : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>>, IStructuralEquatable, IStructuralComparable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>>, ITupleInternal
        where TRest : struct {
        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's first component.
        /// </summary>
        public T1 Item1;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's second component.
        /// </summary>
        public T2 Item2;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's third component.
        /// </summary>
        public T3 Item3;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's fourth component.
        /// </summary>
        public T4 Item4;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's fifth component.
        /// </summary>
        public T5 Item5;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's sixth component.
        /// </summary>
        public T6 Item6;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's seventh component.
        /// </summary>
        public T7 Item7;

        /// <summary>
        /// The current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance's eighth component.
        /// </summary>
        public TRest Rest;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> value type.
        /// </summary>
        /// <param name="item1">The value of the tuple's first component.</param>
        /// <param name="item2">The value of the tuple's second component.</param>
        /// <param name="item3">The value of the tuple's third component.</param>
        /// <param name="item4">The value of the tuple's fourth component.</param>
        /// <param name="item5">The value of the tuple's fifth component.</param>
        /// <param name="item6">The value of the tuple's sixth component.</param>
        /// <param name="item7">The value of the tuple's seventh component.</param>
        /// <param name="rest">The value of the tuple's eight component.</param>
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest) {
            if (rest is not ITupleInternal) {
                throw new ArgumentException("The TRest type argument of ValueTuple`8 must be a ValueTuple.", nameof(rest));
            }

            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            Rest = rest;
        }

        int ITupleInternal.Size => Rest is ITupleInternal rest ? 7 + rest.Size : 8;

        /// <summary>Compares this instance to a specified instance and returns an indication of their relative values.</summary>
        /// <param name="other">An instance to compare.</param>
        /// <returns>
        /// A signed number indicating the relative values of this instance and <paramref name="other" />.
        /// Returns less than zero if this instance is less than <paramref name="other" />, zero if this
        /// instance is equal to <paramref name="other" />, and greater than zero if this instance is greater
        /// than <paramref name="other" />.
        /// </returns>
        public readonly int CompareTo(ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other) {
            var result = Comparer<T1>.Default.Compare(Item1, other.Item1);
            if (result == 0) {
                result = Comparer<T2>.Default.Compare(Item2, other.Item2);
            }

            if (result == 0) {
                result = Comparer<T3>.Default.Compare(Item3, other.Item3);
            }

            if (result == 0) {
                result = Comparer<T4>.Default.Compare(Item4, other.Item4);
            }

            if (result == 0) {
                result = Comparer<T5>.Default.Compare(Item5, other.Item5);
            }

            if (result == 0) {
                result = Comparer<T6>.Default.Compare(Item6, other.Item6);
            }

            if (result == 0) {
                result = Comparer<T7>.Default.Compare(Item7, other.Item7);
            }

            if (result == 0) {
                result = Comparer<TRest>.Default.Compare(Rest, other.Rest);
            }

            return result;
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance is equal to a specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> if the current instance is equal to the specified object; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The <paramref name="obj"/> parameter is considered to be equal to the current instance under the following conditions:
        /// <list type="bullet">
        ///     <item><description>It is a <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> value type.</description></item>
        ///     <item><description>Its components are of the same types as those of the current instance.</description></item>
        ///     <item><description>Its components are equal to those of the current instance. Equality is determined by the default object equality comparer for each component.</description></item>
        /// </list>
        /// </remarks>
        public override readonly bool Equals(object? obj) {
            return obj is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple && Equals(tuple);
        }

        /// <summary>
        /// Returns a value that indicates whether the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}" />
        /// instance is equal to a specified <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}" />.
        /// </summary>
        /// <param name="other">The tuple to compare with this instance.</param>
        /// <returns><see langword="true" /> if the current instance is equal to the specified tuple; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// The <paramref name="other" /> parameter is considered to be equal to the current instance if each of its fields
        /// are equal to that of the current instance, using the default comparer for that field's type.
        /// </remarks>
        public readonly bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other) {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                   && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                   && EqualityComparer<T3>.Default.Equals(Item3, other.Item3)
                   && EqualityComparer<T4>.Default.Equals(Item4, other.Item4)
                   && EqualityComparer<T5>.Default.Equals(Item5, other.Item5)
                   && EqualityComparer<T6>.Default.Equals(Item6, other.Item6)
                   && EqualityComparer<T7>.Default.Equals(Item7, other.Item7)
                   && EqualityComparer<TRest>.Default.Equals(Rest, other.Rest);
        }

        /// <summary>
        /// Returns the hash code for the current <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override readonly int GetHashCode() {
            // We want to have a limited hash in this case.  We'll use the last 8 elements of the tuple
            if (Rest is not ITupleInternal rest) {
                return ValueTuple.CombineHashCodes
                (
                    ValueTuple.HashCodeOf(null, Item1),
                    ValueTuple.HashCodeOf(null, Item2),
                    ValueTuple.HashCodeOf(null, Item3),
                    ValueTuple.HashCodeOf(null, Item4),
                    ValueTuple.HashCodeOf(null, Item5),
                    ValueTuple.HashCodeOf(null, Item6),
                    ValueTuple.HashCodeOf(null, Item7)
                );
            }

            var size = rest.Size;
            if (size >= 8) {
                return rest.GetHashCode();
            }

            // In this case, the rest member has less than 8 elements so we need to combine some our elements with the elements in rest
            switch (8 - size) {
                case 1:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                case 2:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item6),
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                case 3:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item5),
                        ValueTuple.HashCodeOf(null, Item6),
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                case 4:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item4),
                        ValueTuple.HashCodeOf(null, Item5),
                        ValueTuple.HashCodeOf(null, Item6),
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                case 5:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item3),
                        ValueTuple.HashCodeOf(null, Item4),
                        ValueTuple.HashCodeOf(null, Item5),
                        ValueTuple.HashCodeOf(null, Item6),
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                case 6:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item2),
                        ValueTuple.HashCodeOf(null, Item3),
                        ValueTuple.HashCodeOf(null, Item4),
                        ValueTuple.HashCodeOf(null, Item5),
                        ValueTuple.HashCodeOf(null, Item6),
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                case 7:
                case 8:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(null, Item1),
                        ValueTuple.HashCodeOf(null, Item2),
                        ValueTuple.HashCodeOf(null, Item3),
                        ValueTuple.HashCodeOf(null, Item4),
                        ValueTuple.HashCodeOf(null, Item5),
                        ValueTuple.HashCodeOf(null, Item6),
                        ValueTuple.HashCodeOf(null, Item7),
                        rest.GetHashCode()
                    );

                default:
                    Debug.Fail("Missed all cases for computing ValueTuple hash code");
                    return -1;
            }
        }

        /// <summary>
        /// Returns a string that represents the value of this <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance.
        /// </summary>
        /// <returns>The string representation of this <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/> instance.</returns>
        /// <remarks>
        /// The string returned by this method takes the form <c>(Item1, Item2, Item3, Item4, Item5, Item6, Item7, Rest)</c>.
        /// If any field value is <see langword="null"/>, it is represented as <see cref="string.Empty"/>.
        /// </remarks>
        public override readonly string ToString() {
            return $"({Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, {Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty}, {Item6?.ToString() ?? string.Empty}, {Item7?.ToString() ?? string.Empty}, {(Rest is ITupleInternal rest ? rest.ToStringEnd() : (Rest + ")"))}";
        }

        readonly int IComparable.CompareTo(object? obj) {
            if (obj == null) {
                return 1;
            }

            if (obj is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> valueTuple) {
                return CompareTo(valueTuple);
            }

            throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(obj));
        }

        readonly int IStructuralComparable.CompareTo(object? other, IComparer comparer) {
            if (other == null) {
                return 1;
            }

            if (other is not ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> valueTuple) {
                throw new ArgumentException("The parameter should be a ValueTuple type of appropriate arity.", nameof(other));
            }

            var result = comparer.Compare(Item1, valueTuple.Item1);
            if (result == 0) {
                result = comparer.Compare(Item2, valueTuple.Item2);
            }

            if (result == 0) {
                result = comparer.Compare(Item3, valueTuple.Item3);
            }

            if (result == 0) {
                result = comparer.Compare(Item4, valueTuple.Item4);
            }

            if (result == 0) {
                result = comparer.Compare(Item5, valueTuple.Item5);
            }

            if (result == 0) {
                result = comparer.Compare(Item6, valueTuple.Item6);
            }

            if (result == 0) {
                result = comparer.Compare(Item7, valueTuple.Item7);
            }

            if (result == 0) {
                result = comparer.Compare(Rest, valueTuple.Rest);
            }

            return result;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (other is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> valueTuple) {
                return comparer.Equals(Item1, valueTuple.Item1)
                       && comparer.Equals(Item2, valueTuple.Item2)
                       && comparer.Equals(Item3, valueTuple.Item3)
                       && comparer.Equals(Item4, valueTuple.Item4)
                       && comparer.Equals(Item5, valueTuple.Item5)
                       && comparer.Equals(Item6, valueTuple.Item6)
                       && comparer.Equals(Item7, valueTuple.Item7)
                       && comparer.Equals(Rest, valueTuple.Rest);
            }

            return false;
        }

        readonly int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        readonly int ITupleInternal.GetHashCode(IEqualityComparer comparer) {
            return GetHashCodeCore(comparer);
        }

        private readonly int GetHashCodeCore(IEqualityComparer comparer) {
            // We want to have a limited hash in this case.  We'll use the last 8 elements of the tuple
            if (Rest is not ITupleInternal rest) {
                return ValueTuple.CombineHashCodes
                (
                    ValueTuple.HashCodeOf(comparer, Item1),
                    ValueTuple.HashCodeOf(comparer, Item2),
                    ValueTuple.HashCodeOf(comparer, Item3),
                    ValueTuple.HashCodeOf(comparer, Item4),
                    ValueTuple.HashCodeOf(comparer, Item5),
                    ValueTuple.HashCodeOf(comparer, Item6),
                    ValueTuple.HashCodeOf(comparer, Item7)
                );
            }

            var size = rest.Size;
            if (size >= 8) {
                return rest.GetHashCode(comparer);
            }

            // In this case, the rest member has less than 8 elements so we need to combine some our elements with the elements in rest
            switch (8 - size) {
                case 1:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                case 2:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item6),
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                case 3:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item5),
                        ValueTuple.HashCodeOf(comparer, Item6),
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                case 4:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item4),
                        ValueTuple.HashCodeOf(comparer, Item5),
                        ValueTuple.HashCodeOf(comparer, Item6),
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                case 5:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item3),
                        ValueTuple.HashCodeOf(comparer, Item4),
                        ValueTuple.HashCodeOf(comparer, Item5),
                        ValueTuple.HashCodeOf(comparer, Item6),
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                case 6:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item2),
                        ValueTuple.HashCodeOf(comparer, Item3),
                        ValueTuple.HashCodeOf(comparer, Item4),
                        ValueTuple.HashCodeOf(comparer, Item5),
                        ValueTuple.HashCodeOf(comparer, Item6),
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                case 7:
                case 8:
                    return ValueTuple.CombineHashCodes
                    (
                        ValueTuple.HashCodeOf(comparer, Item1),
                        ValueTuple.HashCodeOf(comparer, Item2),
                        ValueTuple.HashCodeOf(comparer, Item3),
                        ValueTuple.HashCodeOf(comparer, Item4),
                        ValueTuple.HashCodeOf(comparer, Item5),
                        ValueTuple.HashCodeOf(comparer, Item6),
                        ValueTuple.HashCodeOf(comparer, Item7),
                        rest.GetHashCode(comparer)
                    );

                default:
                    Debug.Fail("Missed all cases for computing ValueTuple hash code");
                    return -1;
            }
        }

        readonly string ITupleInternal.ToStringEnd() {
            return $"{Item1?.ToString() ?? string.Empty}, {Item2?.ToString() ?? string.Empty}, {Item3?.ToString() ?? string.Empty}, " +
                $"{Item4?.ToString() ?? string.Empty}, {Item5?.ToString() ?? string.Empty}, {Item6?.ToString() ?? string.Empty}, " +
                $"{Item7?.ToString() ?? string.Empty}, {(Rest is ITupleInternal rest ? rest.ToStringEnd() : (Rest + ")"))}";
        }
    }
}