// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// This file is ported and adapted from ComputeSharp (Sergio0694/ComputeSharp),
// more info in ThirdPartyNotices.txt in the root of the project.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MonoMod.SourceGen.Internal.Helpers;

/// <summary>
/// A helper type to build sequences of values with pooled buffers.
/// </summary>
/// <typeparam name="T">The type of items to create sequences for.</typeparam>
internal struct ImmutableArrayBuilder<T> : IDisposable
{
    /// <summary>
    /// The shared <see cref="ObjectPool{T}"/> instance to share <see cref="Writer"/> objects.
    /// </summary>
    private static readonly ObjectPool<Writer> SharedObjectPool = new(static () => new Writer());

    /// <summary>
    /// The rented <see cref="Writer"/> instance to use.
    /// </summary>
    private Writer? writer;

    /// <summary>
    /// Creates a <see cref="ImmutableArrayBuilder{T}"/> value with a pooled underlying data writer.
    /// </summary>
    /// <returns>A <see cref="ImmutableArrayBuilder{T}"/> instance to write data to.</returns>
    public static ImmutableArrayBuilder<T> Rent()
    {
        return new(SharedObjectPool.Allocate());
    }

    /// <summary>
    /// Creates a new <see cref="ImmutableArrayBuilder{T}"/> object with the specified parameters.
    /// </summary>
    /// <param name="writer">The target data writer to use.</param>
    private ImmutableArrayBuilder(Writer writer)
    {
        this.writer = writer;
    }

    /// <inheritdoc cref="ImmutableArray{T}.Builder.Count"/>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => writer!.Count;
    }

    /// <summary>
    /// Gets the data written to the underlying buffer so far, as a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public readonly ReadOnlySpan<T> WrittenSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => writer!.WrittenSpan;
    }

    /// <inheritdoc cref="ImmutableArray{T}.Builder.Add(T)"/>
    public readonly void Add(T item)
    {
        writer!.Add(item);
    }

    /// <summary>
    /// Adds the specified items to the end of the array.
    /// </summary>
    /// <param name="items">The items to add at the end of the array.</param>
    public readonly void AddRange(ReadOnlySpan<T> items)
    {
        writer!.AddRange(items);
    }

    public readonly void Clear() => writer!.Clear();

    /// <inheritdoc cref="ImmutableArray{T}.Builder.ToImmutable"/>
    public readonly ImmutableArray<T> ToImmutable()
    {
        var array = writer!.WrittenSpan.ToArray();

        return Unsafe.As<T[], ImmutableArray<T>>(ref array);
    }

    /// <inheritdoc cref="ImmutableArray{T}.Builder.ToArray"/>
    public readonly T[] ToArray()
    {
        return writer!.WrittenSpan.ToArray();
    }

    /// <inheritdoc/>
    public override readonly string ToString()
    {
        return writer!.WrittenSpan.ToString();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var writer = this.writer;

        this.writer = null;

        if (writer is not null)
        {
            writer.Clear();

            SharedObjectPool.Free(writer);
        }
    }

    /// <summary>
    /// A class handling the actual buffer writing.
    /// </summary>
    private sealed class Writer
    {
        /// <summary>
        /// The underlying <typeparamref name="T"/> array.
        /// </summary>
        private T[] array;

        /// <summary>
        /// The starting offset within <see cref="array"/>.
        /// </summary>
        private int index;

        /// <summary>
        /// Creates a new <see cref="Writer"/> instance with the specified parameters.
        /// </summary>
        public Writer()
        {
            if (typeof(T) == typeof(char))
            {
                array = new T[1024];
            }
            else
            {
                array = new T[8];
            }

            index = 0;
        }

        /// <inheritdoc cref="ImmutableArrayBuilder{T}.Count"/>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => index;
        }

        /// <inheritdoc cref="ImmutableArrayBuilder{T}.WrittenSpan"/>
        public ReadOnlySpan<T> WrittenSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(array, 0, index);
        }

        /// <inheritdoc cref="ImmutableArrayBuilder{T}.Add"/>
        public void Add(T value)
        {
            EnsureCapacity(1);

            array[index++] = value;
        }

        /// <inheritdoc cref="ImmutableArrayBuilder{T}.AddRange"/>
        public void AddRange(ReadOnlySpan<T> items)
        {
            EnsureCapacity(items.Length);

            items.CopyTo(array.AsSpan(index));

            index += items.Length;
        }

        /// <summary>
        /// Clears the items in the current writer.
        /// </summary>
        public void Clear()
        {
            if (typeof(T) != typeof(char))
            {
                array.AsSpan(0, index).Clear();
            }

            index = 0;
        }

        /// <summary>
        /// Ensures that <see cref="array"/> has enough free space to contain a given number of new items.
        /// </summary>
        /// <param name="requestedSize">The minimum number of items to ensure space for in <see cref="array"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int requestedSize)
        {
            if (requestedSize > array.Length - index)
            {
                ResizeBuffer(requestedSize);
            }
        }

        /// <summary>
        /// Resizes <see cref="array"/> to ensure it can fit the specified number of new items.
        /// </summary>
        /// <param name="sizeHint">The minimum number of items to ensure space for in <see cref="array"/>.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ResizeBuffer(int sizeHint)
        {
            var minimumSize = index + sizeHint;
            var requestedSize = Math.Max(array.Length * 2, minimumSize);

            var newArray = new T[requestedSize];

            Array.Copy(array, newArray, index);

            array = newArray;
        }
    }
}

/// <summary>
/// Private helpers for the <see cref="ImmutableArrayBuilder{T}"/> type.
/// </summary>
file static class ImmutableArrayBuilder
{
    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> for <c>"index"</c>.
    /// </summary>
    public static void ThrowArgumentOutOfRangeExceptionForIndex()
    {
        throw new ArgumentOutOfRangeException("index");
    }
}