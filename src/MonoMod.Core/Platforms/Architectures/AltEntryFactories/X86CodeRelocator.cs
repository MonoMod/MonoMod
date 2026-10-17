using System;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    /// <summary>
    /// Moves a block of x86/x86-64 instructions to a new address, rewriting everything which depends on the instruction pointer,
    /// and appends a jump back to the instruction following the block.
    /// </summary>
    /// <remarks>
    /// <para>Relative branches are always re-encoded in their 32-bit form. In 64-bit mode, branches whose targets are out of range of
    /// a 32-bit displacement are re-encoded as absolute jumps through an inline 64-bit address. Branches which target an instruction
    /// within the block are retargeted to that instruction's new location.</para>
    /// <para>RIP-relative memory operands and XBEGIN have no absolute form; the caller must place the new block within range of their targets.</para>
    /// </remarks>
    internal static class X86CodeRelocator
    {
        private const int JmpRel32Size = 5;     // E9 rel32
        private const int JmpAbs64Size = 14;    // FF 25 00000000 ; dq target
        private const int CallRel32Size = 5;    // E8 rel32
        private const int CallAbs64Size = 16;   // FF 15 02000000 ; EB 08 ; dq target
        private const int JccRel32Size = 6;     // 0F 8x rel32
        private const int JccAbs64Size = 16;    // 7x^1 0E ; jmp abs64
        private const int CounterHeadSize = 4;  // [67] Ex 02 ; EB xx ; jmp

        /// <summary>
        /// Decodes whole instructions from the start of <paramref name="code"/> until at least <paramref name="minLength"/> bytes are covered.
        /// </summary>
        /// <param name="code">The code to decode. This must extend at least <see cref="X86InstructionDecoder.MaxInstructionLength"/> bytes past <paramref name="minLength"/>.</param>
        /// <param name="ip">The address of <paramref name="code"/>.</param>
        /// <param name="is64"><see langword="true"/> to decode 64-bit code, <see langword="false"/> to decode 32-bit code.</param>
        /// <param name="minLength">The minimum number of bytes to decode.</param>
        /// <param name="insns">Receives the instructions. Every instruction is at least 1 byte, so <paramref name="minLength"/> elements always suffice.</param>
        /// <param name="blockLength">The number of bytes decoded.</param>
        /// <returns>The number of instructions decoded.</returns>
        public static int DecodeBlock(ReadOnlySpan<byte> code, ulong ip, bool is64, int minLength, Span<X86Instruction> insns, out int blockLength)
        {
            var count = 0;
            var offset = 0;
            while (offset < minLength)
            {
                X86InstructionDecoder.Decode(code.Slice(offset), ip + (ulong)offset, is64, out var insn);
                insn.Offset = offset;
                insns[count++] = insn;
                offset += insn.Length;
            }

            blockLength = offset;
            return count;
        }

        /// <summary>
        /// Gets an upper bound for the size of the code produced by <see cref="Encode"/>, regardless of target address.
        /// </summary>
        public static int GetMaxEncodedSize(ReadOnlySpan<X86Instruction> insns, bool is64)
        {
            var size = 0;
            for (var i = 0; i < insns.Length; i++)
            {
                size += GetEncodedSize(insns[i], useAbsolute: is64);
            }
            return size + (is64 ? JmpAbs64Size : JmpRel32Size);
        }

        /// <summary>
        /// Encodes <paramref name="insns"/>, decoded from <paramref name="srcIp"/> by <see cref="DecodeBlock"/>, to run at <paramref name="dstIp"/>,
        /// followed by a jump back to <paramref name="srcIp"/> + <paramref name="blockLength"/>.
        /// </summary>
        /// <returns>The number of bytes written to <paramref name="output"/>.</returns>
        /// <exception cref="InvalidOperationException">The instructions cannot be relocated to <paramref name="dstIp"/>.</exception>
        public static int Encode(ReadOnlySpan<byte> code, ulong srcIp, Span<X86Instruction> insns, int blockLength, bool is64, ulong dstIp, Span<byte> output)
        {
            // resolve branches into the block itself; those must follow the moved instructions
            for (var i = 0; i < insns.Length; i++)
            {
                ref var insn = ref insns[i];
                insn.TargetIndex = -1;
                if (insn.Kind is X86RelocKind.None or X86RelocKind.RipRelative)
                    continue;

                if ((insn.Flags & X86InstructionFlags.OperandSize16) != 0)
                    throw new InvalidOperationException($"Cannot relocate 16-bit relative operand at 0x{srcIp + (ulong)insn.Offset:x16}");

                var targetOffset = insn.Target - srcIp;
                if (targetOffset < (ulong)blockLength)
                {
                    insn.TargetIndex = FindInstruction(insns, (int)targetOffset);
                    if (insn.TargetIndex < 0)
                        throw new InvalidOperationException($"Branch at 0x{srcIp + (ulong)insn.Offset:x16} targets the middle of an instruction (0x{insn.Target:x16})");
                }
            }

            // lay out the new code; every external branch target is fixed, so one pass suffices
            var offset = 0;
            for (var i = 0; i < insns.Length; i++)
            {
                ref var insn = ref insns[i];
                insn.NewOffset = offset;
                insn.UseAbsolute = is64 && IsBranch(insn.Kind) && insn.TargetIndex < 0
                    && !FitsRel32(insn.Target, dstIp + (ulong)(offset + GetEncodedSize(insn, useAbsolute: false)));
                offset += GetEncodedSize(insn, insn.UseAbsolute);
            }

            var returnTarget = srcIp + (ulong)blockLength;
            var returnAbsolute = is64 && !FitsRel32(returnTarget, dstIp + (ulong)(offset + JmpRel32Size));
            var totalSize = offset + (returnAbsolute ? JmpAbs64Size : JmpRel32Size);
            if (output.Length < totalSize)
                throw new ArgumentException($"Output buffer is too small; {totalSize} bytes are required", nameof(output));

            for (var i = 0; i < insns.Length; i++)
            {
                ref readonly var insn = ref insns[i];
                var dst = output.Slice(insn.NewOffset);
                var newIp = dstIp + (ulong)insn.NewOffset;
                var target = insn.TargetIndex >= 0 ? dstIp + (ulong)insns[insn.TargetIndex].NewOffset : insn.Target;

                switch (insn.Kind)
                {
                    case X86RelocKind.None:
                        code.Slice(insn.Offset, insn.Length).CopyTo(dst);
                        break;

                    case X86RelocKind.RipRelative:
                    case X86RelocKind.RelOperand:
                        if ((insn.Flags & X86InstructionFlags.AddressSizePrefix) != 0 && insn.Kind == X86RelocKind.RipRelative)
                            throw new InvalidOperationException($"Cannot relocate EIP-relative operand at 0x{srcIp + (ulong)insn.Offset:x16}");

                        code.Slice(insn.Offset, insn.Length).CopyTo(dst);
                        var end = newIp + (ulong)insn.Length;
                        if (is64 && !FitsRel32(target, end))
                            throw new InvalidOperationException($"Relative operand at 0x{srcIp + (ulong)insn.Offset:x16} cannot reach 0x{target:x16} from 0x{newIp:x16}");
                        WriteInt32(dst.Slice(insn.RelOffset), unchecked((int)(target - end)));
                        break;

                    case X86RelocKind.Jmp:
                        EmitJmp(dst, newIp, target, insn.UseAbsolute);
                        break;

                    case X86RelocKind.Call:
                        if (insn.UseAbsolute)
                        {
                            // call [rip+2] ; jmp short +8 ; dq target
                            dst[0] = 0xFF;
                            dst[1] = 0x15;
                            WriteInt32(dst.Slice(2), 2);
                            dst[6] = 0xEB;
                            dst[7] = 0x08;
                            WriteUInt64(dst.Slice(8), target);
                        }
                        else
                        {
                            dst[0] = 0xE8;
                            WriteInt32(dst.Slice(1), Rel32(target, newIp + CallRel32Size));
                        }
                        break;

                    case X86RelocKind.Jcc:
                        if (insn.UseAbsolute)
                        {
                            // j!cc short +14 ; jmp abs64
                            dst[0] = (byte)(0x70 | (insn.Opcode ^ 1));
                            dst[1] = JmpAbs64Size;
                            EmitJmp(dst.Slice(2), newIp + 2, target, absolute: true);
                        }
                        else
                        {
                            dst[0] = 0x0F;
                            dst[1] = (byte)(0x80 | insn.Opcode);
                            WriteInt32(dst.Slice(2), Rel32(target, newIp + JccRel32Size));
                        }
                        break;

                    case X86RelocKind.CounterBranch:
                    {
                        // loop/jrcxz taken ; jmp short over_taken ; taken: jmp target ; over_taken:
                        // the address-size prefix selects the counter register (CX/ECX/RCX), so it must be preserved
                        var pos = 0;
                        if ((insn.Flags & X86InstructionFlags.AddressSizePrefix) != 0)
                            dst[pos++] = 0x67;
                        dst[pos++] = insn.Opcode;
                        dst[pos++] = 2;
                        dst[pos++] = 0xEB;
                        dst[pos++] = (byte)(insn.UseAbsolute ? JmpAbs64Size : JmpRel32Size);
                        EmitJmp(dst.Slice(pos), newIp + (ulong)pos, target, insn.UseAbsolute);
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unknown relocation kind {insn.Kind}");
                }
            }

            EmitJmp(output.Slice(offset), dstIp + (ulong)offset, returnTarget, returnAbsolute);
            return totalSize;
        }

        private static bool IsBranch(X86RelocKind kind)
            => kind is X86RelocKind.Jmp or X86RelocKind.Call or X86RelocKind.Jcc or X86RelocKind.CounterBranch;

        private static int GetEncodedSize(in X86Instruction insn, bool useAbsolute)
            => insn.Kind switch
            {
                X86RelocKind.Jmp => useAbsolute ? JmpAbs64Size : JmpRel32Size,
                X86RelocKind.Call => useAbsolute ? CallAbs64Size : CallRel32Size,
                X86RelocKind.Jcc => useAbsolute ? JccAbs64Size : JccRel32Size,
                X86RelocKind.CounterBranch => ((insn.Flags & X86InstructionFlags.AddressSizePrefix) != 0 ? 1 : 0)
                    + CounterHeadSize + (useAbsolute ? JmpAbs64Size : JmpRel32Size),
                _ => insn.Length,
            };

        private static int FindInstruction(ReadOnlySpan<X86Instruction> insns, int offset)
        {
            for (var i = 0; i < insns.Length; i++)
            {
                if (insns[i].Offset == offset)
                    return i;
            }
            return -1;
        }

        private static bool FitsRel32(ulong target, ulong end)
        {
            var rel = unchecked((long)(target - end));
            return rel is >= int.MinValue and <= int.MaxValue;
        }

        // In 32-bit mode, addresses wrap at 4GiB, so truncation gives the correct displacement.
        private static int Rel32(ulong target, ulong end) => unchecked((int)(target - end));

        private static void EmitJmp(Span<byte> dst, ulong ip, ulong target, bool absolute)
        {
            if (absolute)
            {
                // jmp [rip+0] ; dq target
                dst[0] = 0xFF;
                dst[1] = 0x25;
                WriteInt32(dst.Slice(2), 0);
                WriteUInt64(dst.Slice(6), target);
            }
            else
            {
                dst[0] = 0xE9;
                WriteInt32(dst.Slice(1), Rel32(target, ip + JmpRel32Size));
            }
        }

        private static void WriteInt32(Span<byte> dst, int value)
        {
            dst[0] = (byte)value;
            dst[1] = (byte)(value >> 8);
            dst[2] = (byte)(value >> 16);
            dst[3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(Span<byte> dst, ulong value)
        {
            WriteInt32(dst, unchecked((int)value));
            WriteInt32(dst.Slice(4), unchecked((int)(value >> 32)));
        }
    }
}
