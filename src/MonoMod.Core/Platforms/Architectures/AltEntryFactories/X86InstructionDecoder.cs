using System;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    /// <summary>
    /// The way an instruction depends on its own address, and so how it must be rewritten when it is moved.
    /// </summary>
    internal enum X86RelocKind : byte
    {
        /// <summary>The instruction is position-independent and can be copied verbatim.</summary>
        None,
        /// <summary>RIP-relative memory operand. The 32-bit displacement at <see cref="X86Instruction.RelOffset"/> is relative to the end of the instruction.</summary>
        RipRelative,
        /// <summary>A 32-bit relative operand (XBEGIN) which can only be adjusted in place.</summary>
        RelOperand,
        /// <summary><c>JMP rel8/rel32</c>.</summary>
        Jmp,
        /// <summary><c>CALL rel32</c>.</summary>
        Call,
        /// <summary><c>Jcc rel8/rel32</c>. <see cref="X86Instruction.Opcode"/> holds the condition code.</summary>
        Jcc,
        /// <summary><c>LOOP</c>/<c>LOOPE</c>/<c>LOOPNE</c>/<c>JrCXZ rel8</c>. <see cref="X86Instruction.Opcode"/> holds the opcode byte.</summary>
        CounterBranch,
    }

    [Flags]
    internal enum X86InstructionFlags : byte
    {
        None = 0,
        /// <summary>The operand size is 16 bits (66 prefix without REX.W). Relative operands are 16 bits wide and truncate the instruction pointer.</summary>
        OperandSize16 = 1 << 0,
        /// <summary>The instruction has an address-size (67) prefix.</summary>
        AddressSizePrefix = 1 << 1,
    }

    internal struct X86Instruction
    {
        /// <summary>The offset of the instruction from the start of the decoded block.</summary>
        public int Offset;
        public int Length;
        public X86RelocKind Kind;
        public X86InstructionFlags Flags;
        /// <summary>The condition code for <see cref="X86RelocKind.Jcc"/>, or the opcode byte for <see cref="X86RelocKind.CounterBranch"/>.</summary>
        public byte Opcode;
        /// <summary>The offset of the 32-bit displacement within the instruction for <see cref="X86RelocKind.RipRelative"/> and <see cref="X86RelocKind.RelOperand"/>.</summary>
        public byte RelOffset;
        /// <summary>The absolute address referenced by the instruction, for every <see cref="Kind"/> other than <see cref="X86RelocKind.None"/>.</summary>
        public ulong Target;

        // Relocator layout state.
        internal int NewOffset;
        internal int TargetIndex;
        internal bool UseAbsolute;
    }

    /// <summary>
    /// A minimal x86/x86-64 decoder which only extracts what is needed to move an instruction: its length and how it references its own address.
    /// </summary>
    internal static class X86InstructionDecoder
    {
        public const int MaxInstructionLength = 15;

        [Flags]
        private enum Op : ushort
        {
            None = 0,
            ModRm = 1 << 0,
            Imm8 = 1 << 1,
            Imm16 = 1 << 2,
            Imm32 = 1 << 3,
            /// <summary>16 or 32 bits, by operand size.</summary>
            ImmZ = 1 << 4,
            /// <summary>16, 32, or 64 bits, by operand size (<c>MOV r, imm</c>).</summary>
            ImmV = 1 << 5,
            /// <summary>Address-sized absolute offset (<c>MOV AL, moffs</c>).</summary>
            Moffs = 1 << 6,
            /// <summary><c>ptr16:16</c> or <c>ptr16:32</c>.</summary>
            FarPtr = 1 << 7,
            Rel8 = 1 << 8,
            /// <summary>16 or 32 bits, by operand size.</summary>
            RelZ = 1 << 9,
            /// <summary><c>F6</c>/<c>F7</c>: the immediate is only present for <c>/0</c> and <c>/1</c> (<c>TEST</c>).</summary>
            Group3 = 1 << 10,
            Invalid64 = 1 << 11,
            Invalid = 1 << 12,
            /// <summary>The ModRM byte always encodes registers, regardless of its mod field (<c>MOV CRn/DRn</c>, VIA PadLock).</summary>
            RegOnly = 1 << 13,
        }

        private static readonly Op[] OneByteMap = CreateOneByteMap();
        private static readonly Op[] TwoByteMap = CreateTwoByteMap();

        private static Op[] CreateOneByteMap()
        {
            var map = new Op[256];

            // ALU ops: op r/m,r / op r,r/m / op AL,imm8 / op eAX,immz. Slots 6 and 7 are push/pop seg and the BCD ops, which are invalid in 64-bit mode.
            // 26/2E/36/3E are segment prefixes and 0F is the escape; both are handled before the table lookup.
            for (var op = 0; op < 0x40; op++)
            {
                map[op] = (op & 7) switch
                {
                    < 4 => Op.ModRm,
                    4 => Op.Imm8,
                    5 => Op.ImmZ,
                    _ => Op.Invalid64,
                };
            }

            // 40-5F: INC/DEC/PUSH/POP r. In 64-bit mode, 40-4F are REX prefixes, which are handled before the table lookup.
            map[0x60] = map[0x61] = Op.Invalid64;
            map[0x62] = Op.ModRm | Op.Invalid64; // BOUND; EVEX is handled before the table lookup
            map[0x63] = Op.ModRm;
            map[0x68] = Op.ImmZ;
            map[0x69] = Op.ModRm | Op.ImmZ;
            map[0x6A] = Op.Imm8;
            map[0x6B] = Op.ModRm | Op.Imm8;
            for (var op = 0x70; op <= 0x7F; op++)
                map[op] = Op.Rel8;
            map[0x80] = Op.ModRm | Op.Imm8;
            map[0x81] = Op.ModRm | Op.ImmZ;
            map[0x82] = Op.ModRm | Op.Imm8 | Op.Invalid64;
            map[0x83] = Op.ModRm | Op.Imm8;
            for (var op = 0x84; op <= 0x8F; op++)
                map[op] = Op.ModRm; // 8F is POP r/m; XOP is handled before the table lookup
            map[0x9A] = Op.FarPtr | Op.Invalid64;
            for (var op = 0xA0; op <= 0xA3; op++)
                map[op] = Op.Moffs;
            map[0xA8] = Op.Imm8;
            map[0xA9] = Op.ImmZ;
            for (var op = 0xB0; op <= 0xB7; op++)
                map[op] = Op.Imm8;
            for (var op = 0xB8; op <= 0xBF; op++)
                map[op] = Op.ImmV;
            map[0xC0] = map[0xC1] = Op.ModRm | Op.Imm8;
            map[0xC2] = Op.Imm16;
            map[0xC4] = map[0xC5] = Op.ModRm | Op.Invalid64; // LES/LDS; VEX is handled before the table lookup
            map[0xC6] = Op.ModRm | Op.Imm8;
            map[0xC7] = Op.ModRm | Op.ImmZ; // includes XBEGIN (C7 F8 relz)
            map[0xC8] = Op.Imm16 | Op.Imm8;
            map[0xCA] = Op.Imm16;
            map[0xCD] = Op.Imm8;
            map[0xCE] = Op.Invalid64;
            for (var op = 0xD0; op <= 0xD3; op++)
                map[op] = Op.ModRm;
            map[0xD4] = map[0xD5] = Op.Imm8 | Op.Invalid64;
            map[0xD6] = Op.Invalid64;
            for (var op = 0xD8; op <= 0xDF; op++)
                map[op] = Op.ModRm;
            for (var op = 0xE0; op <= 0xE3; op++)
                map[op] = Op.Rel8;
            for (var op = 0xE4; op <= 0xE7; op++)
                map[op] = Op.Imm8;
            map[0xE8] = map[0xE9] = Op.RelZ;
            map[0xEA] = Op.FarPtr | Op.Invalid64;
            map[0xEB] = Op.Rel8;
            map[0xF6] = map[0xF7] = Op.ModRm | Op.Group3;
            map[0xFE] = map[0xFF] = Op.ModRm;

            return map;
        }

        private static Op[] CreateTwoByteMap()
        {
            var map = new Op[256];
            for (var op = 0; op < 256; op++)
                map[op] = Op.ModRm;

            foreach (var op in new byte[] {
                0x05, 0x06, 0x07, 0x08, 0x09, 0x0B, 0x0E,
                0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x37,
                0x77, 0xA0, 0xA1, 0xA2, 0xA8, 0xA9, 0xAA,
                0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xCE, 0xCF,
            })
            {
                map[op] = Op.None;
            }

            foreach (var op in new byte[] {
                0x04, 0x0A, 0x0C, 0x25, 0x27, 0x36, 0x39, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x7A, 0x7B,
            })
            {
                map[op] = Op.Invalid;
            }

            // 0F 0F is 3DNow!, whose real opcode is the trailing imm8
            foreach (var op in new byte[] { 0x0F, 0x70, 0x71, 0x72, 0x73, 0xA4, 0xAC, 0xBA, 0xC2, 0xC4, 0xC5, 0xC6 })
            {
                map[op] = Op.ModRm | Op.Imm8;
            }

            foreach (var op in new byte[] { 0x20, 0x21, 0x22, 0x23, 0xA6, 0xA7 })
            {
                map[op] = Op.ModRm | Op.RegOnly;
            }

            for (var op = 0x80; op <= 0x8F; op++)
                map[op] = Op.RelZ;

            return map;
        }

        // Opcodes in map 0F which take an imm8 when VEX or EVEX encoded.
        private static bool HasVexMap1Imm8(byte opcode)
            => opcode is (>= 0x70 and <= 0x73) or 0xC2 or (>= 0xC4 and <= 0xC6);

        private static InvalidOperationException Unsupported(ulong ip, string reason)
            => new($"Cannot decode x86 instruction at 0x{ip:x16}: {reason}");

        private static byte Next(ReadOnlySpan<byte> code, ref int pos, ulong ip)
        {
            if (pos >= MaxInstructionLength)
                throw Unsupported(ip, "instruction is longer than 15 bytes");
            return code[pos++];
        }

        private static int ReadInt32(ReadOnlySpan<byte> code, int pos)
            => code[pos] | (code[pos + 1] << 8) | (code[pos + 2] << 16) | (code[pos + 3] << 24);

        /// <summary>
        /// Decodes the instruction at the start of <paramref name="code"/>, located at <paramref name="ip"/>.
        /// </summary>
        /// <remarks>
        /// Only the bytes making up the instruction are read, so <paramref name="code"/> may extend past readable memory.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The instruction is invalid, or cannot be decoded by this decoder.</exception>
        public static void Decode(ReadOnlySpan<byte> code, ulong ip, bool is64, out X86Instruction insn)
        {
            insn = default;
            insn.TargetIndex = -1;

            var pos = 0;
            var p66 = false;
            var p67 = false;
            var pF2 = false;
            var vexInvalidPrefix = false;
            byte rex = 0;
            byte b;

            // legacy prefixes; a REX prefix is only effective if it immediately precedes the opcode
            while (true)
            {
                b = Next(code, ref pos, ip);
                switch (b)
                {
                    case 0x26 or 0x2E or 0x36 or 0x3E or 0x64 or 0x65:
                        rex = 0;
                        continue;
                    case 0xF0 or 0xF3:
                        vexInvalidPrefix = true;
                        rex = 0;
                        continue;
                    case 0x66:
                        p66 = true;
                        vexInvalidPrefix = true;
                        rex = 0;
                        continue;
                    case 0x67:
                        p67 = true;
                        rex = 0;
                        continue;
                    case 0xF2:
                        pF2 = true;
                        vexInvalidPrefix = true;
                        rex = 0;
                        continue;
                }

                if (is64 && (b & 0xF0) == 0x40)
                {
                    rex = b;
                    continue;
                }

                break;
            }

            var rexW = (rex & 0x08) != 0;
            var opSize16 = p66 && !rexW;
            var addrBits = is64 ? (p67 ? 32 : 64) : (p67 ? 16 : 32);

            Op op;
            var opcode = b;
            var isLegacyMap0 = false;
            var isLegacyMap1 = false;

            if ((b is 0xC4 or 0xC5 or 0x62) && (is64 || code[pos] >= 0xC0))
            {
                // VEX (C4/C5) or EVEX (62). Outside 64-bit mode, these are LES/LDS/BOUND unless the following byte has mod == 11.
                if (rex != 0 || vexInvalidPrefix)
                    throw Unsupported(ip, "VEX/EVEX with REX, 66, F2, F3, or LOCK prefix");

                int map;
                if (b == 0xC5)
                {
                    _ = Next(code, ref pos, ip);
                    map = 1;
                }
                else if (b == 0xC4)
                {
                    map = Next(code, ref pos, ip) & 0x1F;
                    _ = Next(code, ref pos, ip);
                }
                else
                {
                    map = Next(code, ref pos, ip) & 0x07;
                    _ = Next(code, ref pos, ip);
                    _ = Next(code, ref pos, ip);
                }

                opcode = Next(code, ref pos, ip);
                op = map switch
                {
                    1 when b != 0x62 && opcode == 0x77 => Op.None, // VZEROUPPER/VZEROALL
                    1 => Op.ModRm | (HasVexMap1Imm8(opcode) ? Op.Imm8 : Op.None),
                    2 => Op.ModRm,
                    3 => Op.ModRm | Op.Imm8,
                    5 or 6 when b == 0x62 => Op.ModRm,
                    _ => Op.Invalid,
                };
            }
            else if (b == 0x8F && (code[pos] & 0x1F) >= 8)
            {
                // XOP; otherwise this is POP r/m
                if (rex != 0 || vexInvalidPrefix)
                    throw Unsupported(ip, "XOP with REX, 66, F2, F3, or LOCK prefix");

                var map = Next(code, ref pos, ip) & 0x1F;
                _ = Next(code, ref pos, ip);
                opcode = Next(code, ref pos, ip);
                op = map switch
                {
                    8 => Op.ModRm | Op.Imm8,
                    9 => Op.ModRm,
                    10 => Op.ModRm | Op.Imm32,
                    _ => Op.Invalid,
                };
            }
            else if (b == 0x0F)
            {
                opcode = Next(code, ref pos, ip);
                if (opcode == 0x38)
                {
                    opcode = Next(code, ref pos, ip);
                    op = Op.ModRm;
                }
                else if (opcode == 0x3A)
                {
                    opcode = Next(code, ref pos, ip);
                    op = Op.ModRm | Op.Imm8;
                }
                else
                {
                    isLegacyMap1 = true;
                    op = TwoByteMap[opcode];
                    if (opcode == 0x78 && (p66 || pF2))
                    {
                        // EXTRQ/INSERTQ xmm, [xmm,] imm8, imm8
                        op |= Op.Imm16;
                    }
                }
            }
            else
            {
                isLegacyMap0 = true;
                op = OneByteMap[b];
            }

            if ((op & Op.Invalid) != 0 || (is64 && (op & Op.Invalid64) != 0))
                throw Unsupported(ip, $"invalid opcode 0x{opcode:x2}");

            byte modrm = 0;
            var ripDispOffset = -1;
            if ((op & Op.ModRm) != 0)
            {
                modrm = Next(code, ref pos, ip);
                var mod = modrm >> 6;
                var rm = modrm & 7;
                if (mod != 3 && (op & Op.RegOnly) == 0)
                {
                    if (addrBits == 16)
                    {
                        pos += mod switch
                        {
                            0 => rm == 6 ? 2 : 0,
                            1 => 1,
                            _ => 2,
                        };
                    }
                    else
                    {
                        var hasDisp32 = mod == 2;
                        if (rm == 4)
                        {
                            var sib = Next(code, ref pos, ip);
                            hasDisp32 |= mod == 0 && (sib & 7) == 5;
                        }
                        else if (mod == 0 && rm == 5)
                        {
                            hasDisp32 = true;
                            if (is64)
                            {
                                ripDispOffset = pos;
                            }
                        }

                        pos += hasDisp32 ? 4 : mod == 1 ? 1 : 0;
                    }
                }
            }

            var immOffset = pos;
            var zSize = opSize16 ? 2 : 4;
            if ((op & Op.Imm8) != 0)
                pos += 1;
            if ((op & Op.Imm16) != 0)
                pos += 2;
            if ((op & Op.Imm32) != 0)
                pos += 4;
            if ((op & Op.ImmZ) != 0)
                pos += zSize;
            if ((op & Op.ImmV) != 0)
                pos += rexW ? 8 : zSize;
            if ((op & Op.Moffs) != 0)
                pos += addrBits / 8;
            if ((op & Op.FarPtr) != 0)
                pos += zSize + 2;
            if ((op & Op.Rel8) != 0)
                pos += 1;
            if ((op & Op.RelZ) != 0)
                pos += zSize;
            if ((op & Op.Group3) != 0 && ((modrm >> 3) & 7) < 2)
                pos += opcode == 0xF6 ? 1 : zSize;

            if (pos > MaxInstructionLength)
                throw Unsupported(ip, "instruction is longer than 15 bytes");

            insn.Length = pos;
            insn.Flags = (opSize16 ? X86InstructionFlags.OperandSize16 : 0) | (p67 ? X86InstructionFlags.AddressSizePrefix : 0);

            var nextIp = ip + (ulong)pos;
            long rel;
            if (ripDispOffset >= 0)
            {
                insn.Kind = X86RelocKind.RipRelative;
                insn.RelOffset = (byte)ripDispOffset;
                rel = ReadInt32(code, ripDispOffset);
            }
            else if (isLegacyMap0 && opcode == 0xC7 && modrm == 0xF8)
            {
                // XBEGIN relz
                insn.Kind = X86RelocKind.RelOperand;
                insn.RelOffset = (byte)immOffset;
                rel = opSize16 ? (short)(code[immOffset] | (code[immOffset + 1] << 8)) : ReadInt32(code, immOffset);
            }
            else if ((op & (Op.Rel8 | Op.RelZ)) != 0)
            {
                if (isLegacyMap1)
                {
                    insn.Kind = X86RelocKind.Jcc;
                    insn.Opcode = (byte)(opcode & 0x0F);
                }
                else
                {
                    switch (opcode)
                    {
                        case >= 0x70 and <= 0x7F:
                            insn.Kind = X86RelocKind.Jcc;
                            insn.Opcode = (byte)(opcode & 0x0F);
                            break;
                        case >= 0xE0 and <= 0xE3:
                            insn.Kind = X86RelocKind.CounterBranch;
                            insn.Opcode = opcode;
                            break;
                        case 0xE8:
                            insn.Kind = X86RelocKind.Call;
                            break;
                        default: // E9, EB
                            insn.Kind = X86RelocKind.Jmp;
                            break;
                    }
                }

                rel = (op & Op.Rel8) != 0 ? (sbyte)code[immOffset]
                    : opSize16 ? (short)(code[immOffset] | (code[immOffset + 1] << 8))
                    : ReadInt32(code, immOffset);
            }
            else
            {
                return;
            }

            var target = nextIp + (ulong)rel;
            if (opSize16 && insn.Kind != X86RelocKind.RipRelative)
            {
                // a 16-bit operand size truncates the new instruction pointer
                target &= ushort.MaxValue;
            }
            else if (!is64 || (insn.Kind == X86RelocKind.RipRelative && p67))
            {
                // 32-bit instruction pointer arithmetic wraps
                target &= uint.MaxValue;
            }
            insn.Target = target;
        }
    }
}
