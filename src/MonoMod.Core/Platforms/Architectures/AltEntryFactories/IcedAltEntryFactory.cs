using Iced.Intel;
using MonoMod.Utils;
using System;
using System.Buffers;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    internal sealed class IcedAltEntryFactory : IAltEntryFactory
    {
        private readonly ISystem system;
        private readonly IMemoryAllocator alloc;
        private readonly int bitness;

        public IcedAltEntryFactory(ISystem system, int bitness)
        {
            this.system = system;
            this.bitness = bitness;

            alloc = system.MemoryAllocator;
        }

        private sealed class PtrCodeReader : CodeReader
        {
            public PtrCodeReader(IntPtr basePtr)
            {
                Base = basePtr;
                Position = 0;
            }

            public IntPtr Base { get; }
            public int Position { get; private set; }

            public override unsafe int ReadByte()
            {
                return *(byte*)((nint)Base + (Position++));
            }
        }

        private sealed class NullCodeWriter : CodeWriter
        {
            public override void WriteByte(byte value) { }
        }

        private sealed class BufferCodeWriter : CodeWriter, IDisposable
        {
            private readonly ArrayPool<byte> pool;
            private byte[]? buffer;
            private int pos;

            public BufferCodeWriter()
            {
                pool = ArrayPool<byte>.Shared;
            }

            public ReadOnlyMemory<byte> Data => buffer.AsMemory().Slice(0, pos);

            public override unsafe void WriteByte(byte value)
            {
                if (buffer is null)
                {
                    buffer = pool.Rent(8);
                }

                if (buffer.Length <= pos)
                {
                    var newBuf = pool.Rent(buffer.Length * 2);
                    Array.Copy(buffer, newBuf, buffer.Length);
                    pool.Return(buffer);
                    buffer = newBuf;
                }

                buffer[pos++] = value;
            }

            public void Reset() => pos = 0;

            public void Dispose()
            {
                if (buffer is not null)
                {
                    var buf = buffer;
                    buffer = null;
                    pool.Return(buf);
                }
            }
        }

        public IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle)
        {
            var codeReader = new PtrCodeReader(entrypoint);
            var decoder = Decoder.Create(bitness, codeReader, (ulong)entrypoint, DecoderOptions.NoInvalidCheck | DecoderOptions.AMD);

            var insns = new InstructionList();
            while (codeReader.Position < minLength)
            {
                decoder.Decode(out insns.AllocUninitializedElement());
            }

            var hasRipRelAddress = false;
            foreach (ref var insn in insns)
            {
                if (insn.IsIPRelativeMemoryOperand)
                {
                    hasRipRelAddress = true;
                    break;
                }
            }

            var lastInsn = insns[insns.Count - 1];
            if (lastInsn.Mnemonic is Mnemonic.Call)
            {
                // we want to replace trailing calls with a push <ret addr> ; jmp pair, and not add an extra trailing jump

                var enc = Encoder.Create(bitness, new NullCodeWriter());

                var jmpInsn = lastInsn;
                jmpInsn.Code = lastInsn.Code switch
                {
                    Code.Call_rel16 => Code.Jmp_rel16,
                    Code.Call_rel32_32 => Code.Jmp_rel32_32,
                    Code.Call_rel32_64 => Code.Jmp_rel32_64,
                    Code.Jmp_rm16 => Code.Jmp_rm16,
                    Code.Jmp_rm32 => Code.Jmp_rm32,
                    Code.Jmp_rm64 => Code.Jmp_rm64,
                    Code.Call_m1616 => Code.Jmp_m1616,
                    Code.Call_m1632 => Code.Jmp_m1632,
                    Code.Call_m1664 => Code.Jmp_m1664,
                    Code.Call_ptr1616 => Code.Jmp_ptr1616,
                    Code.Call_ptr1632 => Code.Jmp_ptr1632,
                    _ => throw new InvalidOperationException($"Unrecognized call opcode {lastInsn.Code}")
                };
                jmpInsn.Length = (int)enc.Encode(jmpInsn, jmpInsn.IP);

                var retAddr = lastInsn.NextIP;

                bool useQword;
                Instruction pushInsn, qword;
                if (bitness == 32)
                {
                    pushInsn = Instruction.Create(Code.Pushd_imm32, (uint)retAddr);
                    pushInsn.Length = (int)enc.Encode(pushInsn, jmpInsn.IP);
                    pushInsn.IP = jmpInsn.IP;
                    jmpInsn.IP += (ulong)pushInsn.Length;
                    useQword = false;
                    qword = default;
                }
                else
                {
                    // we have to also use the qword slot to hold the addr
                    useQword = true;
                    qword = Instruction.CreateDeclareQword(retAddr);

                    pushInsn = Instruction.Create(Code.Push_rm64, new MemoryOperand(Register.RIP, (long)jmpInsn.NextIP));
                    pushInsn.Length = (int)enc.Encode(pushInsn, jmpInsn.IP);
                    pushInsn.IP = jmpInsn.IP;
                    jmpInsn.IP += (ulong)pushInsn.Length;
                    qword.IP = jmpInsn.NextIP;
                    pushInsn.MemoryDisplacement64 = qword.IP;
                }

                insns.RemoveAt(insns.Count - 1);
                insns.Add(pushInsn);
                insns.Add(jmpInsn);
                if (useQword)
                {
                    insns.Add(qword);
                }

            }
            else
            {
                insns.Add(Instruction.CreateBranch(bitness == 64 ? Code.Jmp_rel32_64 : Code.Jmp_rel32_32, decoder.IP));
            }

            var readSize = codeReader.Position;
            var estTotalSize = readSize + 5;

            // Now we do some incredibly messy work...
            // We need to use BlockEncoder to encode to a new location, but we don't know the actual full size that the instructions will encode to,
            // so we have to guess. This guess could be wrong for several reasons, including the allocated memory simply being at an address which makes
            // the code too big. To handle this, we will simply try and retry, releasing memory back to the allocator repeatedly in the hopes that it will
            // return fairly consistent addresses. Once we've found a combination that matches size, then we patch the data in, and are finished.

            using var bufWriter = new BufferCodeWriter();
            while (true)
            {
                bufWriter.Reset();

                IAllocatedMemory? allocated;
                // first, allocate with our estimated size
                if (hasRipRelAddress)
                {
                    // if we have an RIP relative address (that wasn't created by us) try to allocate close to the original location
                    Helpers.Assert(alloc.TryAllocateInRange(
                        new(entrypoint, (nint)entrypoint + int.MinValue, (nint)entrypoint + int.MaxValue,
                        new(estTotalSize) { Executable = true }), out allocated));
                }
                else
                {
                    Helpers.Assert(alloc.TryAllocate(new(estTotalSize) { Executable = true }, out allocated));
                }

                // now that we have a target address, try to assemble at that address
                var target = allocated.BaseAddress;
                if (!BlockEncoder.TryEncode(bitness, new InstructionBlock(bufWriter, insns, (ulong)target), out var error, out _))
                {
                    allocated.Dispose();
                    MMDbgLog.Error($"BlockEncoder failed to encode instructions: {error}");
                    throw new InvalidOperationException($"BlockEncoder failed to encode instructions: {error}");
                }

                // now we check what size the generated code is, and compare it to the size of our allocation
                if (bufWriter.Data.Length != allocated.Size)
                {
                    // if the sizes are different, update our estimate, free the alloc, and try again
                    estTotalSize = bufWriter.Data.Length;
                    allocated.Dispose();
                    continue;
                }
                else
                {
                    // if the sizes are the same, we have a match! patch the data into the allocated memory and return
                    system.PatchData(PatchTargetKind.Executable, allocated.BaseAddress, bufWriter.Data.Span, default);
                    handle = allocated;
                    return allocated.BaseAddress;
                }
            }
        }
    }
}
