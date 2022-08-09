using System;
using System.Linq;

namespace MonoMod.Core.Utils {
    public sealed class BytePattern {

        private const ushort MaskMask = 0xFF00;

        // one byte with any value
        public const byte BAnyValue = 0x00;
        public const ushort SAnyValue = MaskMask | BAnyValue;
        // zero or more bytes with any value
        public const byte BAnyRepeatingValue = 0x01;
        public const ushort SAnyRepeatingValue = MaskMask | BAnyRepeatingValue;
        // a captured byte, pushed into the address buffer during matching
        public const byte BAddressValue = 0x02;
        public const ushort SAddressValue = MaskMask | BAddressValue;

        private readonly ReadOnlyMemory<byte> pattern;
        private readonly ReadOnlyMemory<byte> bitmask;
        private readonly PatternSegment[] segments;

        public int AddressBytes { get; }
        public int MinLength { get; }

        public AddressMeaning AddressMeaning { get; }
        public bool MustMatchAtStart { get; }

        private enum SegmentKind {
            Literal, MaskedLiteral, Any, AnyRepeating, Address,
        }

        private record struct PatternSegment(int Start, int Length, SegmentKind Kind) {
            public ReadOnlySpan<T> SliceOf<T>(ReadOnlySpan<T> span) => span.Slice(Start, Length);
            public ReadOnlyMemory<T> SliceOf<T>(ReadOnlyMemory<T> mem) => mem.Slice(Start, Length);
        }

        public BytePattern(AddressMeaning meaning, params ushort[] pattern) : this(meaning, false, pattern.AsMemory()) { }
        public BytePattern(AddressMeaning meaning, bool mustMatchAtStart, params ushort[] pattern) : this(meaning, mustMatchAtStart, pattern.AsMemory()) { }
        public BytePattern(AddressMeaning meaning, ReadOnlyMemory<ushort> pattern) : this(meaning, false, pattern) { }
        public BytePattern(AddressMeaning meaning, bool mustMatchAtStart, ReadOnlyMemory<ushort> pattern) {
            AddressMeaning = meaning;
            MustMatchAtStart = mustMatchAtStart;
            (segments, MinLength, AddressBytes) = ComputeSegmentsFromShort(pattern);

            // this mess splits the ushort pattern array into 
            var patternAlloc = new byte[pattern.Length * 2].AsMemory();
            var patternData = patternAlloc.Slice(0, pattern.Length);
            var bitmaskData = patternAlloc.Slice(pattern.Length);
            for (var i = 0; i < pattern.Length; i++) {
                var @byte = pattern.Span[i];
                var mask = (byte) ((@byte & MaskMask) >> 8);
                var data = (byte) (@byte & ~MaskMask);
                if (mask is 0x00 or 0xFF)
                    mask = (byte)~mask;

                patternData.Span[i] = (byte) (data & mask);
                bitmaskData.Span[i] = mask;
            }

            this.pattern = patternData;
            bitmask = bitmaskData;
        }

        public BytePattern(AddressMeaning meaning, ReadOnlyMemory<byte> mask, ReadOnlyMemory<byte> pattern) : this(meaning, false, mask, pattern) { }
        public BytePattern(AddressMeaning meaning, bool mustMatchAtStart, ReadOnlyMemory<byte> mask, ReadOnlyMemory<byte> pattern) {
            AddressMeaning = meaning;
            MustMatchAtStart = mustMatchAtStart;
            (segments, MinLength, AddressBytes) = ComputeSegmentsFromMaskPattern(mask, pattern);
            this.pattern = pattern;
            bitmask = mask;
        }

        private readonly record struct ComputeSegmentsResult(PatternSegment[] Segments, int MinLen, int AddrBytes);

        private unsafe static ComputeSegmentsResult ComputeSegmentsFromShort(ReadOnlyMemory<ushort> pattern) {
            return ComputeSegmentsCore(&KindForShort, pattern.Length, pattern);

            static SegmentKind KindForShort(ReadOnlyMemory<ushort> pattern, int idx) {
                var value = pattern.Span[idx];
                return (value & MaskMask) switch {
                    0x0000 => SegmentKind.Literal, // a normal literal
                    MaskMask => (value & 0x00ff) switch { // a special value
                        (BAnyValue) => SegmentKind.Any,
                        (BAnyRepeatingValue) => SegmentKind.AnyRepeating,
                        (BAddressValue) => SegmentKind.Address,
                        var x => throw new ArgumentException($"Pattern contained unknown special value {x:x2}", nameof(pattern))
                    },
                    _ => SegmentKind.MaskedLiteral,
                };
            }
        }

        private unsafe static ComputeSegmentsResult ComputeSegmentsFromMaskPattern(ReadOnlyMemory<byte> mask, ReadOnlyMemory<byte> pattern) {
            if (mask.Length < pattern.Length)
                throw new ArgumentException("Mask buffer shorter than pattern", nameof(mask));

            return ComputeSegmentsCore(&KindForIdx, pattern.Length, (mask, pattern));

            static SegmentKind KindForIdx((ReadOnlyMemory<byte> mask, ReadOnlyMemory<byte> pattern) t, int idx) {
                return t.mask.Span[idx] switch {
                    0x00 => t.pattern.Span[idx] switch { // the mask hides this byte, it means something special
                        BAnyValue => SegmentKind.Any,
                        BAnyRepeatingValue => SegmentKind.AnyRepeating,
                        BAddressValue => SegmentKind.Address,
                        var x => throw new ArgumentException($"Pattern contained unknown special value {x:x2}", nameof(pattern))
                    },
                    0xFF => SegmentKind.Literal, // its a normal, unmasked literal
                    _ => SegmentKind.MaskedLiteral, // otherwise, it's a masked literal
                };
            }
        }

        private unsafe static ComputeSegmentsResult ComputeSegmentsCore<TPattern>(delegate*<TPattern, int, SegmentKind> kindForIdx, int patternLength, TPattern pattern) {
            if (patternLength == 0)
                throw new ArgumentException("Pattern cannot be empty", nameof(pattern));

            // first, we do a pass to compute how many segments we need
            var segmentCount = 0;
            var lastKind = SegmentKind.AnyRepeating; // start with an AnyRepeating so that we implicitly ignore leading AnyRepeating
            var segmentLength = 0;
            var addrLength = 0;
            var minLength = 0;
            var firstSegmentStart = -1;

            for (var i = 0; i < patternLength; i++) {
                var thisSegmentKind = kindForIdx(pattern, i);

                minLength += thisSegmentKind switch {
                    SegmentKind.Literal => 1,
                    SegmentKind.MaskedLiteral => 1,
                    SegmentKind.Any => 1,
                    SegmentKind.AnyRepeating => 0, // AnyRepeating matches zero or more
                    SegmentKind.Address => 1,
                    _ => 0,
                };

                if (thisSegmentKind != lastKind) {
                    if (firstSegmentStart < 0)
                        firstSegmentStart = i;
                    segmentCount++;
                    segmentLength = 1;
                } else {
                    segmentLength++;
                }

                if (thisSegmentKind == SegmentKind.Address)
                    addrLength++;

                lastKind = thisSegmentKind;
            }

            if (segmentCount > 0 && lastKind == SegmentKind.AnyRepeating) {
                // if we ended with an AnyRepeating, we want to decrement segmentCount, as we want to ignore trailing AnyRepeating
                segmentCount--;
            }

            if (segmentCount == 0 || minLength <= 0) {
                throw new ArgumentException("Pattern has no meaningful segments", nameof(pattern));
            }

            // we now know how many segments we need, so lets allocate our array
            var segments = new PatternSegment[segmentCount];
            segmentCount = 0;
            lastKind = SegmentKind.AnyRepeating;
            segmentLength = 0;

            for (var i = firstSegmentStart; i < patternLength && segmentCount <= segments.Length; i++) {
                var thisSegmentKind = kindForIdx(pattern, i);

                if (thisSegmentKind != lastKind) {
                    if (segmentCount > 0) {
                        segments[segmentCount - 1] = new(i - segmentLength, segmentLength, lastKind);

                        if (segmentCount > 1 && lastKind is SegmentKind.Any && segments[segmentCount - 2].Kind is SegmentKind.AnyRepeating) {
                            // if we have an Any after an AnyRepeating, swap them, so that we will always have a fixed-length address or a literal after an AnyRepeating
                            Helpers.Swap(ref segments[segmentCount - 2], ref segments[segmentCount - 1]);
                        }
                    }

                    segmentCount++;
                    segmentLength = 1;
                } else {
                    segmentLength++;
                }

                lastKind = thisSegmentKind;
            }

            if (lastKind is not SegmentKind.AnyRepeating && segmentCount > 0) {
                segments[segmentCount - 1] = new(patternLength - segmentLength, segmentLength, lastKind);
            }

            return new(segments, minLength, addrLength);
        }

        // the address is read in machine byte order
        //   note though, that if there are fewer than 8 bytes of address in the pattern, 
        // the result is whatever it would be with it byte-padded to 8 bytes on the end
        //   this means that on big-endian, the resulting address may need to be shifted
        // around some to be useful. fortunately, no supported platforms *are* big-endian
        // as far as I am aware.
        public bool TryMatchAt(ReadOnlySpan<byte> data, out ulong address, out int length) {
            if (data.Length < MinLength) {
                length = 0;
                address = 0;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            ReadOnlySpan<byte> patternSpan = pattern.Span;
            // set up address buffer
            Span<byte> addr = stackalloc byte[sizeof(ulong)];
            var result = TryMatchAtImpl(patternSpan, data, addr, out length, 0);
            address = Unsafe.ReadUnaligned<ulong>(ref addr[0]);
            return result;
        }

        public bool TryMatchAt(ReadOnlySpan<byte> data, Span<byte> addrBuf, out int length) {
            if (data.Length < MinLength) {
                length = 0;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            ReadOnlySpan<byte> patternSpan = pattern.Span;
            return TryMatchAtImpl(patternSpan, data, addrBuf, out length, 0);
        }

        private bool TryMatchAtImpl(ReadOnlySpan<byte> patternSpan, ReadOnlySpan<byte> data, Span<byte> addrBuf, out int length, int startAtSegment) {
            var pos = 0;
            var segmentIdx = startAtSegment;

            while (segmentIdx < segments.Length) {
                PatternSegment segment = segments[segmentIdx];
                switch (segment.Kind) {
                    case SegmentKind.Literal: {
                            if (data.Length - pos < segment.Length)
                                goto NoMatch; // if we don't have enough space left for the match, then just fail out

                            ReadOnlySpan<byte> pattern = segment.SliceOf(patternSpan);
                            
                            if (!pattern.SequenceEqual(data.Slice(pos, pattern.Length)))
                                goto NoMatch; // the literal didn't match here, oopsie
                            // we successfully matched the literal, lets advance our position, and we're done here
                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.MaskedLiteral: {
                            if (data.Length - pos < segment.Length)
                                goto NoMatch;

                            var pattern = segment.SliceOf(patternSpan);
                            var mask = segment.SliceOf(bitmask.Span);

                            if (!Helpers.MaskedSequenceEqual(pattern, data.Slice(pos, pattern.Length), mask))
                                goto NoMatch;

                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.Any: {
                            // this is easily the simplest pattern to match
                            // we just need to make sure that there's enough space left in the input
                            if (data.Length - pos < segment.Length)
                                goto NoMatch;
                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.Address: {
                            // this is almost as simple as Any, we just *also* need to copy into the addrBuf
                            if (data.Length - pos < segment.Length)
                                goto NoMatch;

                            ReadOnlySpan<byte> pattern = data.Slice(pos, Math.Min(segment.Length, addrBuf.Length));
                            Buffer.MemoryCopy(pattern, addrBuf);
                            addrBuf = addrBuf.Slice(Math.Min(addrBuf.Length, pattern.Length));

                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.AnyRepeating: {
                            // this is far and away the most difficult segment to process; we need to scan forward for the next 
                            // literal, then possibly back up some for Any or Address segments

                            // to do this, we can just forward to the ScanForNextLiteral method
                            // this has the advantage of giving us automatic backtracking due to the mutual recursion
                            var result = ScanForNextLiteral(patternSpan, data.Slice(pos), addrBuf, out var offs, out var sublen, segmentIdx);
                            // our final length now is just pos+offs+sublen
                            length = pos + offs + sublen;
                            return result;
                        }

                    default:
                        throw new InvalidOperationException();
                }
                // done processing segment, move to the next one
                segmentIdx++;
            }

            length = pos;
            return true;

            // this is a JITted function size optimization to prevent duplicate epilogs
            NoMatch:
            length = 0;
            return false;
        }

        public bool TryFindMatch(ReadOnlySpan<byte> data, out ulong address, out int offset, out int length) {
            if (data.Length < MinLength) {
                length = offset = 0;
                address = 0;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            ReadOnlySpan<byte> patternSpan = pattern.Span;

            Span<byte> addr = stackalloc byte[sizeof(ulong)];
            bool result;
            if (MustMatchAtStart) {
                offset = 0;
                result = TryMatchAtImpl(patternSpan, data, addr, out length, 0);
            } else {
                result = ScanForNextLiteral(patternSpan, data, addr, out offset, out length, 0);
            }
            address = Unsafe.ReadUnaligned<ulong>(ref addr[0]);
            return result;
        }

        public bool TryFindMatch(ReadOnlySpan<byte> data, Span<byte> addrBuf, out int offset, out int length) {
            if (data.Length < MinLength) {
                length = offset = 0;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            ReadOnlySpan<byte> patternSpan = pattern.Span;
            if (MustMatchAtStart) {
                offset = 0;
                return TryMatchAtImpl(patternSpan, data, addrBuf, out length, 0);
            } else {
                return ScanForNextLiteral(patternSpan, data, addrBuf, out offset, out length, 0);
            }
        }

        private bool ScanForNextLiteral(ReadOnlySpan<byte> patternSpan, ReadOnlySpan<byte> data, Span<byte> addrBuf, out int offset, out int length, int segmentIndex) {
            var (literalSegment, baseOffs) = GetNextLiteralSegment(segmentIndex);
            if (baseOffs + literalSegment.Length > data.Length) {
                // we literally *cannot* match this data based on the segments, so fail out
                offset = length = 0;
                return false;
            }

            var scanOffsFromBase = 0;
            do {
                var scannedOffs = data.Slice(baseOffs + scanOffsFromBase).IndexOf(literalSegment.SliceOf(patternSpan));
                if (scannedOffs < 0) {
                    // we didn't find the literal 
                    offset = length = 0;
                    return false;
                }

                // we found the literal at baseOffs + scanOffsFromBase + scannedOffs, so we want to try to match at scanOffsFromBase + scannedOffs
                if (TryMatchAtImpl(patternSpan, data.Slice(offset = scanOffsFromBase + scannedOffs), addrBuf, out length, segmentIndex)) {
                    // we found a full match! we can now exit
                    return true;
                }

                // otherwise, we didn't find a full match, and need to keep going
                // advance scanOffsFromBase by scannedOffs+1 to skip this last match
                scanOffsFromBase += scannedOffs + 1;
            } while (true);
        }

        private (ReadOnlyMemory<byte> Bytes, int Offset)? lazyFirstLiteralSegment;
        public (ReadOnlyMemory<byte> Bytes, int Offset) FirstLiteralSegment => lazyFirstLiteralSegment ??= GetFirstLiteralSegment();

        private (ReadOnlyMemory<byte> Bytes, int Offset) GetFirstLiteralSegment() {
            var (segment, offset) = GetNextLiteralSegment(0);
            return (segment.SliceOf(pattern), offset);
        }

        private (PatternSegment Segment, int LiteralOffset) GetNextLiteralSegment(int segmentIndexId) {
            if (segmentIndexId < 0 || segmentIndexId >= segments.Length) {
                throw new ArgumentOutOfRangeException(nameof(segmentIndexId));
            }

            var litOffset = 0;
            for (; segmentIndexId < segments.Length; segmentIndexId++) {
                PatternSegment segment = segments[segmentIndexId];
                if (segment.Kind is SegmentKind.Literal) {
                    return (segment, litOffset);
                } else if (segment.Kind is SegmentKind.Any or SegmentKind.Address or SegmentKind.MaskedLiteral) { // TODO: enable indexing MaskedLiterals
                    litOffset += segment.Length;
                } else if (segment.Kind is SegmentKind.AnyRepeating) {
                    // no litOffset change, just advance to the next segment
                } else {
                    throw new InvalidOperationException("Unknown segment kind");
                }
            }

            return (default, litOffset); // didn't find anything useful, return an empty segment with our computed offset
        }
    }
}
