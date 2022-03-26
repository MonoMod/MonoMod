using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.Core.Utils {
    public sealed class BytePattern {

        // one byte with any value
        public const short AnyValue = -1;
        // zero or more bytes with any value
        public const short AnyRepeatingValue = -2;
        // a captured byte, pushed into the address buffer during matching
        public const short AddressValue = -3;

        private readonly byte[] pattern;
        private readonly PatternSegment[] segments;

        public int AddressBytes { get; }
        public int MinLength { get; }

        private enum SegmentKind {
            Literal, Any, AnyRepeating, Address,
        }

        private record struct PatternSegment(int Start, int Length, SegmentKind Kind) {
            public SimpleSpan<byte> SliceOf(SimpleSpan<byte> span) => span.Slice(Start, Length);
        }

        public BytePattern(params short[] pattern) {
            (segments, MinLength, AddressBytes) = ComputeSegments(pattern);

            byte[] bytePattern = new byte[pattern.Length];
            for (int i = 0; i < pattern.Length; i++) {
                bytePattern[i] = (byte) (pattern[i] & 0xFF);
            }
            this.pattern = bytePattern;
        }

        private static (PatternSegment[] Segments, int MinLen, int AddrBytes) ComputeSegments(short[] pattern) {
            if (pattern is null)
                throw new ArgumentNullException(nameof(pattern));
            if (pattern.Length == 0)
                throw new ArgumentException("Pattern cannot be empty", nameof(pattern));

            // first, we do a pass to compute how many segments we need
            int segmentCount = 0;
            SegmentKind lastKind = SegmentKind.AnyRepeating; // start with an AnyRepeating so that we implicitly ignore leading AnyRepeating
            int segmentLength = 0;
            int addrLength = 0;
            int minLength = 0;
            int firstSegmentStart = -1;

            static SegmentKind KindForByte(short value) => value switch {
                >= 0 and <= 0xff => SegmentKind.Literal,
                AnyValue => SegmentKind.Any,
                AnyRepeatingValue => SegmentKind.AnyRepeating,
                AddressValue => SegmentKind.Address,
                _ => throw new ArgumentException($"Pattern contains unknown special value {value}", nameof(pattern))
            };

            for (int i = 0; i < pattern.Length; i++) {
                SegmentKind thisSegmentKind = KindForByte(pattern[i]);

                minLength += thisSegmentKind switch {
                    SegmentKind.Literal => 1,
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

            // TODO: support >8 address bytes somehow
            if (addrLength > sizeof(ulong))
                throw new ArgumentException("Pattern has more than 8 address bytes", nameof(pattern));

            // TODO: do we want to require an address?

            // we now know how many segments we need, so lets allocate our array
            var segments = new PatternSegment[segmentCount];
            segmentCount = 0;
            lastKind = SegmentKind.AnyRepeating;
            segmentLength = 0;

            for (int i = firstSegmentStart; i < pattern.Length && segmentCount <= segments.Length; i++) {
                SegmentKind thisSegmentKind = KindForByte(pattern[i]);

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
                segments[segmentCount - 1] = new(pattern.Length - segmentLength, segmentLength, lastKind);
            }

            return new(segments, minLength, addrLength);
        }

        // the address is read in machine byte order
        //   note though, that if there are fewer than 8 bytes of address in the pattern, 
        // the result is whatever it would be with it byte-padded to 8 bytes on the end
        //   this means that on big-endian, the resulting address may need to be shifted
        // around some to be useful. fortunately, no supported platforms *are* big-endian
        // as far as I am aware.
        public unsafe bool TryMatchAt(SimpleSpan<byte> data, out ulong address, out int length) {
            if (data.Length < MinLength) {
                length = 0;
                address = 0;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            fixed (byte* patternBufferPtr = pattern) {
                SimpleSpan<byte> patternSpan = new(patternBufferPtr, pattern.Length);
                // set up address buffer
                ulong* addr = stackalloc ulong[1];
                bool result = TryMatchAtImpl(patternSpan, data, new((byte*) addr, sizeof(ulong)), out length, 0);
                address = *addr;
                return result;
            }
        }

        public unsafe bool TryMatchAt(SimpleSpan<byte> data, SimpleSpan<byte> addrBuf, out int length) {
            if (data.Length < MinLength) {
                length = 0;
                return false; // the input data is less than this pattern's minimum length, so it can't possibly match
            }

            fixed (byte* patternBufferPtr = pattern) {
                SimpleSpan<byte> patternSpan = new(patternBufferPtr, pattern.Length);
                return TryMatchAtImpl(patternSpan, data, addrBuf, out length, 0);
            }
        }

        private unsafe bool TryMatchAtImpl(SimpleSpan<byte> patternSpan, SimpleSpan<byte> data, SimpleSpan<byte> addrBuf, out int length, int startAtSegment) {
            length = 0;

            int pos = 0;
            int segmentIdx = startAtSegment;

            while (segmentIdx < segments.Length) {
                PatternSegment segment = segments[segmentIdx];
                switch (segment.Kind) {
                    case SegmentKind.Literal: {
                            if (data.Length - pos < segment.Length)
                                return false; // if we don't have enough space left for the match, then just fail out

                            SimpleSpan<byte> pattern = segment.SliceOf(patternSpan);
                            if (!Buffer.MemCmp(pattern, data.Slice(pos, pattern.Length)))
                                return false; // the literal didn't match here, oopsie
                            // we successfully matched the literal, lets advance our position, and we're done here
                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.Any: {
                            // this is easily the simplest pattern to match
                            // we just need to make sure that there's enough space left in the input
                            if (data.Length - pos < segment.Length)
                                return false;
                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.Address: {
                            // this is almost as simple as Any, we just *also* need to copy into the addrBuf
                            if (data.Length - pos < segment.Length)
                                return false;

                            SimpleSpan<byte> pattern = segment.SliceOf(patternSpan);
                            Buffer.MemoryCopy(pattern.Start, addrBuf.Start, (ulong) addrBuf.Length, (ulong) pattern.Length);
                            addrBuf = addrBuf.Slice(Math.Min(addrBuf.Length, pattern.Length));

                            pos += segment.Length;
                            break;
                        }
                    case SegmentKind.AnyRepeating: {
                            // this is far and away the most difficult segment to process; we need to scan forward for the next 
                            // literal, then possibly back up some for Any or Address segments

                            // to do this, we can just forward to the ScanForNextLiteral method
                            // this has the advantage of giving us automatic backtracking due to the mutual recursion
                            bool result = ScanForNextLiteral(patternSpan, data.Slice(pos), addrBuf, out int offs, out int sublen, segmentIdx);
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
        }

        public unsafe bool TryFindMatch(SimpleSpan<byte> data, out ulong address, out int offset, out int length) {
            fixed (byte* patternBufferPtr = pattern) {
                SimpleSpan<byte> patternSpan = new(patternBufferPtr, pattern.Length);
                ulong* addr = stackalloc ulong[1];

                bool result = ScanForNextLiteral(patternSpan, data, new((byte*)addr, sizeof(ulong)), out offset, out length, 0);
                address = *addr;
                return result;
            }
        }

        public unsafe bool TryFindMatch(SimpleSpan<byte> data, SimpleSpan<byte> addrBuf, out int offset, out int length) {
            fixed (byte* patternBufferPtr = pattern) {
                SimpleSpan<byte> patternSpan = new(patternBufferPtr, pattern.Length);

                return ScanForNextLiteral(patternSpan, data, addrBuf, out offset, out length, 0);
            }
        }

        private bool ScanForNextLiteral(SimpleSpan<byte> patternSpan, SimpleSpan<byte> data, SimpleSpan<byte> addrBuf, out int offset, out int length, int segmentIndex) {
            var (literalSegment, baseOffs) = GetNextLiteralSegment(segmentIndex);
            if (baseOffs + literalSegment.Length > data.Length) {
                // we literally *cannot* match this data based on the segments, so fail out
                offset = length = 0;
                return false;
            }

            int scanOffsFromBase = 0;
            do {
                int scannedOffs = Buffer.IndexOf(data.Slice(baseOffs + scanOffsFromBase), literalSegment.SliceOf(patternSpan));
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

        private (PatternSegment Segment, int LiteralOffset) GetNextLiteralSegment(int segmentIndexId = 0) {
            if (segmentIndexId < 0 || segmentIndexId >= segments.Length) {
                throw new ArgumentOutOfRangeException(nameof(segmentIndexId));
            }

            int litOffset = 0;
            for (; segmentIndexId < segments.Length; segmentIndexId++) {
                PatternSegment segment = segments[segmentIndexId];
                if (segment.Kind is SegmentKind.Literal) {
                    return (segment, litOffset);
                } else if (segment.Kind is SegmentKind.Any or SegmentKind.Address) {
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
