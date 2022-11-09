using Iced.Intel;
using MonoMod.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace MonoMod.Core.Platforms.NativeDetours {
    internal class IcedNativeDetourFactory : INativeDetourFactory {
        private readonly ISystem system;
        private readonly IMemoryAllocator alloc;
        private readonly int bitness;

        public IcedNativeDetourFactory(ISystem system, int bitness) {
            this.system = system;
            this.bitness = bitness;

            alloc = system.MemoryAllocator;
        }

        private sealed class PtrCodeReader : CodeReader {
            public PtrCodeReader(IntPtr basePtr) {
                Base = basePtr;
                Position = 0;
            }

            public IntPtr Base { get; }
            public int Position { get; private set; }

            public override unsafe int ReadByte() {
                return *(byte*) ((nint) Base + (Position++));
            }
        }

        private sealed class BufferCodeWriter : CodeWriter, IDisposable {
            private readonly ArrayPool<byte> pool;
            private byte[]? buffer;
            private int pos;

            public BufferCodeWriter() {
                pool = ArrayPool<byte>.Shared;
            }

            public ReadOnlyMemory<byte> Data => buffer.AsMemory().Slice(0, pos);

            public override unsafe void WriteByte(byte value) {
                if (buffer is null) {
                    buffer = pool.Rent(8);
                }

                if (buffer.Length <= pos) {
                    var newBuf = pool.Rent(buffer.Length * 2);
                    Array.Copy(buffer, newBuf, buffer.Length);
                    pool.Return(buffer);
                    buffer = newBuf;
                }

                buffer[pos++] = value;
            }

            public void Reset() => pos = 0;

            public void Dispose() {
                if (buffer is not null) {
                    var buf = buffer;
                    buffer = null;
                    pool.Return(buf);
                }
            }
        }

        public IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle) {
            var codeReader = new PtrCodeReader(entrypoint);
            var decoder = Decoder.Create(bitness, codeReader, (ulong) entrypoint, DecoderOptions.NoInvalidCheck | DecoderOptions.AMD);

            var insns = new List<Instruction>();
            while (codeReader.Position < minLength) {
                decoder.Decode(out var insn);
                insns.Add(insn);
            }
            insns.Add(Instruction.CreateBranch(bitness == 64 ? Code.Jmp_rel32_64 : Code.Jmp_rel32_32, decoder.IP));

            var readSize = codeReader.Position;
            var estTotalSize = readSize + 5;

            // Now we do some incredibly messy work...
            // We need to use BlockEncoder to encode to a new location, but we don't know the actual full size that the instructions will encode to,
            // so we have to guess. This guess could be wrong for several reasons, including the allocated memory simply being at an address which makes
            // the code too big. To handle this, we will simply try and retry, releasing memory back to the allocator repeatedly in the hopes that it will
            // return fairly consistent addresses. Once we've found a combination that matches size, then we patch the data in, and are finished.

            using var bufWriter = new BufferCodeWriter();
            while (true) {
                bufWriter.Reset();

                // first, allocate with our estimated size
                Helpers.Assert(alloc.TryAllocate(new(estTotalSize) { Executable = true }, out var allocated));

                // now that we have a target address, try to assemble at that address
                var target = allocated.BaseAddress;
                if (!BlockEncoder.TryEncode(bitness, new InstructionBlock(bufWriter, insns, (ulong)target), out var error, out _)) {
                    allocated.Dispose();
                    MMDbgLog.Error($"BlockEncoder failed to encode instructions: {error}");
                    throw new InvalidOperationException($"BlockEncoder failed to encode instructions: {error}");
                }

                // now we check what size the generated code is, and compare it to the size of our allocation
                if (bufWriter.Data.Length != allocated.Size) {
                    // if the sizes are different, update our estimate, free the alloc, and try again
                    estTotalSize = bufWriter.Data.Length;
                    allocated.Dispose();
                    continue;
                } else {
                    // if the sizes are the same, we have a match! patch the data into the allocated memory and return
                    system.PatchData(PatchTargetKind.Executable, allocated.BaseAddress, bufWriter.Data.Span, default);
                    handle = allocated;
                    return allocated.BaseAddress;
                }
            }
        }
    }
}
