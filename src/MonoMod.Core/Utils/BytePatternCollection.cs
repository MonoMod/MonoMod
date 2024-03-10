using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Utils
{
    /// <summary>
    /// A collection of <see cref="BytePattern"/>s which can quickly try to match any contained pattern.
    /// </summary>
    public sealed class BytePatternCollection : IEnumerable<BytePattern>
    {

        private readonly HomogenousPatternCollection[] patternCollections;
        private readonly BytePattern[]? emptyPatterns;

        /// <summary>
        /// The minimum length that this collection can match.
        /// </summary>
        public int MinLength { get; }
        /// <summary>
        /// The maximum value of <see cref="BytePattern.MinLength"/> within this collection.
        /// </summary>
        public int MaxMinLength { get; }
        /// <summary>
        /// The maximum address length.
        /// </summary>
        public int MaxAddressLength { get; }

        /// <summary>
        /// Constructs a <see cref="BytePatternCollection"/> using the provided <see cref="BytePattern"/>s.
        /// </summary>
        /// <param name="patterns">The <see cref="BytePattern"/>s to construct this collection with.</param>
        public BytePatternCollection(ReadOnlyMemory<BytePattern?> patterns)
        {
            (patternCollections, emptyPatterns) = ComputeLut(patterns, out var minLength, out var maxMinLength, out var maxAddrLength);
            MinLength = minLength;
            MaxMinLength = maxMinLength;
            MaxAddressLength = maxAddrLength;
            Helpers.Assert(MinLength > 0);
        }

        /// <summary>
        /// Constructs a <see cref="BytePatternCollection"/> using the provided <see cref="BytePattern"/>s.
        /// </summary>
        /// <param name="patterns">The <see cref="BytePattern"/>s to construct this collection with.</param>
        public BytePatternCollection(params BytePattern?[] patterns) : this(patterns.AsMemory()) { }

        /// <summary>
        /// Gets an enumerator over all of the patterns in this collection.
        /// </summary>
        /// <returns>An enumerator over all patterns in this collection.</returns>
        public IEnumerator<BytePattern> GetEnumerator()
        {
            for (var i = 0; i < patternCollections.Length; i++)
            {
                var coll = patternCollections[i].Lut;
                for (var j = 0; j < coll.Length; j++)
                {
                    if (coll[j] is null)
                        continue;
                    foreach (var pattern in coll[j]!)
                    {
                        yield return pattern;
                    }
                }
            }
            if (emptyPatterns is not null)
            {
                foreach (var pattern in emptyPatterns)
                {
                    yield return pattern;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static (HomogenousPatternCollection[], BytePattern[]?) ComputeLut(
            ReadOnlyMemory<BytePattern?> patterns,
            out int minLength, out int maxMinLength, out int maxAddrLength)
        {
            if (patterns.Length == 0)
            {
                minLength = 0;
                maxMinLength = 0;
                maxAddrLength = 0;
                return (ArrayEx.Empty<HomogenousPatternCollection>(), null);
            }

            // first pass is counting
            // we count the number of 0-offset patterns with each first byte
            Span<int> arrayCounts = stackalloc int[256];
            // we get the min/max min length
            minLength = int.MaxValue;
            maxMinLength = int.MinValue;
            maxAddrLength = 0;
            // and lazily, as we encounter them, we count the number of n-offset patterns
            // we track that by using the offset amount as an index into an array (minus one of course)
            // just don't have huge offsets before the first literal lmao
            int[]?[]? offsetArrayCounts = null;
            // we also count the number of empty patterns, if there are any (which there shouldn't be)
            var emptyPatternCount = 0;

            var distinctOffsetCount = 0; // starts at 1 because of the zero offset

            for (var i = 0; i < patterns.Length; i++)
            {
                var pattern = patterns.Span[i];
                if (pattern is null)
                    continue;

                // update min/max lengths
                if (pattern.MinLength < minLength)
                {
                    minLength = pattern.MinLength;
                }
                if (pattern.MinLength > maxMinLength)
                {
                    maxMinLength = pattern.MinLength;
                }
                if (pattern.AddressBytes > maxAddrLength)
                {
                    maxAddrLength = pattern.AddressBytes;
                }

                // TODO: index masked segments

                // figure out where to put its first segment
                var (seg, offs) = pattern.FirstLiteralSegment;
                if (seg.Length == 0)
                {
                    // empty pattern
                    // these *should* be incredibly rare
                    emptyPatternCount++;
                    continue;
                }
                // as long as we get here, we have at least one distinct offset count
                distinctOffsetCount = 1;
                if (offs == 0)
                {
                    // a zero-offset pattern, so increment the corresponding count in arrayCounts
                    arrayCounts[seg.Span[0]]++;
                }
                else
                {
                    // otherwise, it has an offset, and we need the corresponding offset in offsetArrayCounts
                    if (offsetArrayCounts is null || offsetArrayCounts.Length < offs)
                        Array.Resize(ref offsetArrayCounts, offs);
                    ref var arr = ref offsetArrayCounts[offs - 1];
                    arr ??= new int[256];
                    arr[seg.Span[0]]++;
                }
            }

            // now we want to count the actual number of different offsets we found
            if (offsetArrayCounts is not null)
            {
                foreach (var arr in offsetArrayCounts)
                {
                    if (arr is not null)
                    {
                        distinctOffsetCount++;
                    }
                }
            }

            // now we can begin to allocate our arrays
            // first our empty pattern array
            var emptyPatterns = emptyPatternCount > 0 ? new BytePattern[emptyPatternCount] : null;
            var savedEmptyPatterns = 0;
            // then our list of homogenous pattern collections
            var homoPatterns = new HomogenousPatternCollection[distinctOffsetCount];
            var savedHomoPatterns = 1; // the first value is always present
            homoPatterns[0] = new(0);

            // now iterate through our input pattern list again, and add them to their relevant collections
            for (var i = 0; i < patterns.Length; i++)
            {
                var pattern = patterns.Span[i];
                if (pattern is null)
                    continue;

                var (seg, offs) = pattern.FirstLiteralSegment;

                if (seg.Length == 0)
                {
                    Helpers.DAssert(emptyPatterns is not null);
                    emptyPatterns[savedEmptyPatterns++] = pattern;
                    continue;
                }

                // find the collection it belongs to
                var collectionIdx = -1;
                for (var j = 0; j < homoPatterns.Length; j++)
                {
                    if (homoPatterns[j].Offset == offs)
                    {
                        collectionIdx = j;
                        break;
                    }
                }
                if (collectionIdx == -1)
                {
                    collectionIdx = savedHomoPatterns++;
                    homoPatterns[collectionIdx] = new(offs);
                }

                ReadOnlySpan<int> counts = offs == 0 ? arrayCounts : offsetArrayCounts![offs - 1].AsSpan();
                AddToPatternCollection(ref homoPatterns[collectionIdx], counts, pattern);

                // now that we've added the new pattern, we'll check if it's actually got a smaller offset than the one below it, and if so, swap
                // this ensures that the array stays sorted by offset
                if (collectionIdx > 0 && homoPatterns[collectionIdx - 1].Offset > homoPatterns[collectionIdx].Offset)
                {
                    Helpers.Swap(ref homoPatterns[collectionIdx - 1], ref homoPatterns[collectionIdx]);
                }
            }

            return (homoPatterns, emptyPatterns);

            static void AddToPatternCollection(ref HomogenousPatternCollection collection, ReadOnlySpan<int> arrayCounts, BytePattern pattern)
            {
                var (seg, offs) = pattern.FirstLiteralSegment;

                if (collection.Lut is null)
                {
                    // we need to initialize the collection

                    // allocate the lut
                    var lut = new BytePattern[]?[256];
                    for (var i = 0; i < arrayCounts.Length; i++)
                    {
                        if (arrayCounts[i] > 0)
                        {
                            lut[i] = new BytePattern[arrayCounts[i]];
                        }
                    }

                    collection.Lut = lut;
                }

                Helpers.DAssert(collection.Offset == offs);

                var targetArray = collection.Lut[seg.Span[0]];
                Helpers.DAssert(targetArray is not null);
                var targetIndex = Array.IndexOf(targetArray, null);
                Helpers.DAssert(targetIndex >= 0);
                targetArray[targetIndex] = pattern;

                // then update MinLength if needed
                if (pattern.MinLength < collection.MinLength)
                {
                    collection.MinLength = pattern.MinLength;
                }
            }
        }

        private struct HomogenousPatternCollection
        {
            public BytePattern[]?[] Lut;
            public readonly int Offset;
            public int MinLength;

            public HomogenousPatternCollection(int offs) => (Offset, Lut, MinLength) = (offs, null!, int.MaxValue);

            public void AddFirstBytes(ref FirstByteCollection bytes)
            {
                for (var i = 0; i < Lut.Length; i++)
                {
                    if (Lut[i] is not null)
                    {
                        bytes.Add((byte)i);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to match this pattern over the provided span.
        /// </summary>
        /// <remarks>
        /// <paramref name="address"/> is constructed starting at the byte with the lowest address. This means that
        /// big-endian machines may need the address to be shifted if the address is smaller than 64 bits.
        /// </remarks>
        /// <param name="data">The data to try to match at the start of.</param>
        /// <param name="address">The address which is parsed out of the data.</param>
        /// <param name="matchingPattern">The <see cref="BytePattern"/> which matched the buffer.</param>
        /// <param name="length">The length of the matched pattern.</param>
        /// <returns><see langword="true"/> if <paramref name="data"/> matched at the start; <see langword="false"/> otherwise.</returns>
        /// <seealso cref="BytePattern.TryMatchAt(ReadOnlySpan{byte}, out ulong, out int)"/>
        public bool TryMatchAt(ReadOnlySpan<byte> data, out ulong address, [MaybeNullWhen(false)] out BytePattern matchingPattern, out int length)
        {
            if (data.Length < MinLength)
            {
                length = 0;
                address = 0;
                matchingPattern = null;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            // set up address buffer
            Span<byte> addr = stackalloc byte[sizeof(ulong)];
            var result = TryMatchAt(data, addr, out matchingPattern, out length);
            address = Unsafe.ReadUnaligned<ulong>(ref addr[0]);
            return result;
        }

        /// <summary>
        /// Tries to match this pattern over the provided span.
        /// </summary>
        /// <param name="data">The data to try to match at the start of.</param>
        /// <param name="addrBuf">A buffer to write address bytes to.</param>
        /// <param name="matchingPattern">The <see cref="BytePattern"/> which matched the buffer.</param>
        /// <param name="length">The length of the matched pattern.</param>
        /// <returns><see langword="true"/> if <paramref name="data"/> matched at the start; <see langword="false"/> otherwise.</returns>
        /// <seealso cref="BytePattern.TryMatchAt(ReadOnlySpan{byte}, Span{byte}, out int)"/>
        public bool TryMatchAt(ReadOnlySpan<byte> data, Span<byte> addrBuf, [MaybeNullWhen(false)] out BytePattern matchingPattern, out int length)
        {
            if (data.Length < MinLength)
            {
                length = 0;
                matchingPattern = null;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            // first go through the collections, and try matching those
            for (var i = 0; i < patternCollections.Length; i++)
            {
                ref var coll = ref patternCollections[i];

                if (data.Length < coll.Offset + coll.MinLength)
                    continue;

                var firstByte = data[coll.Offset];
                var patterns = coll.Lut[firstByte];
                if (patterns is null)
                    continue;

                foreach (var pattern in patterns)
                {
                    if (pattern.TryMatchAt(data, addrBuf, out length))
                    {
                        matchingPattern = pattern;
                        return true;
                    }
                }
            }

            // then through the empty patterns, if any
            if (emptyPatterns is not null)
            {
                foreach (var pattern in emptyPatterns)
                {
                    if (pattern.TryMatchAt(data, addrBuf, out length))
                    {
                        matchingPattern = pattern;
                        return true;
                    }
                }
            }

            // otherwise, we didn't find a match, fail
            matchingPattern = null;
            length = 0;
            return false;
        }

        /// <summary>
        /// Tries to find a match of this pattern within the provided span.
        /// </summary>
        /// <remarks>
        /// <paramref name="address"/> is constructed starting at the byte with the lowest address. This means that
        /// big-endian machines may need the address to be shifted if the address is smaller than 64 bits.
        /// </remarks>
        /// <param name="data">The data to find a match in.</param>
        /// <param name="address">The address which is parsed out of the data.</param>
        /// <param name="matchingPattern">The <see cref="BytePattern"/> which matched the buffer.</param>
        /// <param name="offset">The offset within the span that the pattern matched at.</param>
        /// <param name="length">The length of the matched pattern.</param>
        /// <returns><see langword="true"/> if a match was found; <see langword="false"/> otherwise.</returns>
        /// <seealso cref="BytePattern.TryFindMatch(ReadOnlySpan{byte}, out ulong, out int, out int)"/>
        public bool TryFindMatch(ReadOnlySpan<byte> data, out ulong address, [MaybeNullWhen(false)] out BytePattern matchingPattern, out int offset, out int length)
        {
            if (data.Length < MinLength)
            {
                length = offset = 0;
                address = 0;
                matchingPattern = null;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            Span<byte> addr = stackalloc byte[sizeof(ulong)];
            var result = TryFindMatch(data, addr, out matchingPattern, out offset, out length);
            address = Unsafe.ReadUnaligned<ulong>(ref addr[0]);
            return result;
        }

        /// <summary>
        /// Tries to find a match of this pattern within the provided span.
        /// </summary>
        /// <param name="data">The data to find a match in.</param>
        /// <param name="addrBuf">A buffer to write address bytes to.</param>
        /// <param name="matchingPattern">The <see cref="BytePattern"/> which matched the buffer.</param>
        /// <param name="offset">The offset within the span that the pattern matched at.</param>
        /// <param name="length">The length of the matched pattern.</param>
        /// <returns><see langword="true"/> if a match was found; <see langword="false"/> otherwise.</returns>
        /// <seealso cref="BytePattern.TryFindMatch(ReadOnlySpan{byte}, Span{byte}, out int, out int)"/>
        public bool TryFindMatch(ReadOnlySpan<byte> data, Span<byte> addrBuf, [MaybeNullWhen(false)] out BytePattern matchingPattern, out int offset, out int length)
        {
            if (data.Length < MinLength)
            {
                length = offset = 0;
                matchingPattern = null;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            var possibleFirstBytes = PossibleFirstBytes.Span;

            var scanBase = 0;
            do
            {
                var index = data.Slice(scanBase).IndexOfAny(possibleFirstBytes);
                if (index < 0)
                    break;

                offset = scanBase + index;
                var valueAtOffs = data[offset];

                // try the value in all collections
                for (var i = 0; i < patternCollections.Length; i++)
                {
                    ref var coll = ref patternCollections[i];

                    if (offset < coll.Offset)
                        continue;

                    if (data.Length < offset + coll.MinLength)
                        continue;

                    var patterns = coll.Lut[valueAtOffs];
                    if (patterns is null)
                        continue;

                    foreach (var pattern in patterns)
                    {
                        if (offset != 0 && pattern.MustMatchAtStart)
                            continue;
                        if (pattern.TryMatchAt(data.Slice(offset - coll.Offset), addrBuf, out length))
                        {
                            offset -= coll.Offset;
                            matchingPattern = pattern;
                            return true;
                        }
                    }
                }

                // we didn't match any collections, update the scan base
                scanBase = offset + 1;
            } while (true);

            // only after we've fully exhausted the search space do we even bother trying to match the empty patterns
            if (emptyPatterns is not null)
            {
                foreach (var pattern in emptyPatterns)
                {
                    if (pattern.TryFindMatch(data, addrBuf, out offset, out length))
                    {
                        matchingPattern = pattern;
                        return true;
                    }
                }
            }

            matchingPattern = null;
            offset = 0;
            length = 0;
            return false;
        }

        private ReadOnlyMemory<byte>? lazyPossibleFirstBytes;
        private ReadOnlyMemory<byte> PossibleFirstBytes => lazyPossibleFirstBytes ??= GetPossibleFirstBytes();

        private ReadOnlyMemory<byte> GetPossibleFirstBytes()
        {
            var alloc = new byte[FirstByteCollection.SingleAllocationSize].AsMemory();

            FirstByteCollection collection = new(alloc.Span);
            for (var i = 0; i < patternCollections.Length; i++)
            {
                patternCollections[i].AddFirstBytes(ref collection);
            }

            return alloc.Slice(0, collection.FirstBytes.Length);
        }

        private ref struct FirstByteCollection
        {
            private Span<byte> firstByteStore;
            private Span<byte> byteIndicies;
            private int firstBytesRecorded;

            public ReadOnlySpan<byte> FirstBytes => firstByteStore.Slice(0, firstBytesRecorded);

            public const int SingleAllocationSize = 256 * 2;

            public FirstByteCollection(Span<byte> store) : this(store.Slice(0, 256), store.Slice(256, 256)) { }

            public FirstByteCollection(Span<byte> store, Span<byte> indicies)
            {
                Helpers.DAssert(store.Length >= 256 && indicies.Length >= 256);
                firstByteStore = store;
                byteIndicies = indicies;
                firstBytesRecorded = 0;
                byteIndicies.Fill(255); // i hope to God that we don't need to keep track of this many first bytes
            }

            public void Add(byte value)
            {
                ref var index = ref byteIndicies[value];
                if (index == 255)
                {
                    index = (byte)firstBytesRecorded;
                    firstByteStore[index] = value;
                    firstBytesRecorded = Math.Min(firstBytesRecorded + 1, 256);
                }
            }
        }
    }
}
