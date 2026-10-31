using System;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    /// <summary>
    /// The way an A64 instruction depends on its own address, and so how it must be rewritten when it is moved.
    /// </summary>
    internal enum Arm64RelocKind : byte
    {
        /// <summary>The instruction is position-independent and can be copied verbatim.</summary>
        None,
        /// <summary><c>B imm26</c>.</summary>
        Branch,
        /// <summary><c>BL imm26</c>.</summary>
        BranchLink,
        /// <summary><c>B.cond</c>/<c>BC.cond</c>, <c>CBZ</c>/<c>CBNZ</c> (imm19), <c>TBZ</c>/<c>TBNZ</c> (imm14), and FEAT_CMPBR <c>CB&lt;cc&gt;</c>/<c>CBB&lt;cc&gt;</c>/<c>CBH&lt;cc&gt;</c> (imm9).</summary>
        ConditionalBranch,
        /// <summary><c>ADR Xd, label</c>.</summary>
        Adr,
        /// <summary><c>ADRP Xd, label</c>.</summary>
        Adrp,
        /// <summary><c>LDR</c>/<c>LDRSW</c> (literal) into a general or SIMD/FP register.</summary>
        LoadLiteral,
        /// <summary><c>PRFM</c> (literal).</summary>
        Prefetch,
    }

    /// <summary>An opaque decoded A64 instruction passed between decoding and relocation.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815", Justification = "Opaque transport type for the allocation-free relocation API.")]
    public struct Arm64Instruction
    {
        internal uint Raw;
        internal Arm64RelocKind Kind;
        internal ulong Target;

        // Relocator layout state.
        internal int NewOffset;
        internal int TargetIndex;
        internal Arm64Form Form;
        internal bool AlignLiteral;
    }

    internal enum Arm64Form : byte
    {
        /// <summary>The original instruction, with its offset re-encoded if needed.</summary>
        Short,
        /// <summary>Conditional branch inverted to skip over a direct <c>B</c>.</summary>
        SkipDirect,
        /// <summary>A sequence relative to the 4KiB page of the target (<c>ADRP</c>-based).</summary>
        Page,
        /// <summary>A sequence which reaches any address (literal-pool branch, or <c>MOVZ</c>/<c>MOVK</c>).</summary>
        Absolute,
        /// <summary>Replaced with <c>NOP</c>.</summary>
        Nop,
    }

    /// <summary>
    /// Moves a block of A64 instructions to a new address, rewriting everything which depends on the program counter,
    /// and appends a branch back to the instruction following the block.
    /// </summary>
    /// <remarks>
    /// <para>Branches use their own encoding while their target is in range, then a direct <c>B</c> (±128MiB), and only then an indirect
    /// branch through <c>x16</c>. Indirect branches clobber <c>x16</c>, and fault on BTI-guarded pages when the target is not a landing
    /// pad, so callers should place the new block within direct-branch range of the original whenever possible.</para>
    /// <para>Address materialization preserves other general-purpose registers on successful completion. Out-of-range SIMD and XZR literal loads use x17 and restore it from a stack spill after the load.</para>
    /// </remarks>
    public static class Arm64CodeRelocator
    {
        private const uint Nop = 0xD503201F;
        private const uint BranchOpcode = 0x14000000;
        private const uint BrX16 = 0xD61F0200;
        private const uint BlrX16 = 0xD63F0200;
        private const uint LdrLiteralX16 = 0x58000000 | 16;
        private const uint SubSp16 = 0xD10043FF;
        private const uint AddSp16 = 0x910043FF;
        private const uint StrX17Sp = 0xF90003F1;
        private const uint LdrX17Sp = 0xF94003F1;
        private const int ScratchRegister = 17;

        private const int BranchAbsoluteSize = 16; // ldr x16, #8 ; br x16 ; .quad target (plus an alignment nop if needed)
        private const int BranchLinkAbsoluteSize = 20; // ldr x16, #12 ; blr x16 ; b #12 ; .quad target (plus an alignment nop if needed)

        /// <summary>
        /// Decodes the instruction <paramref name="raw"/> located at <paramref name="pc"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The instruction is PC-relative in a way this relocator does not support.</exception>
        public static Arm64Instruction Decode(uint raw, ulong pc)
        {
            var insn = new Arm64Instruction { Raw = raw, TargetIndex = -1 };

            if ((raw & 0x7C000000) == 0x14000000)
            {
                // B / BL imm26
                insn.Kind = (raw & 0x80000000) != 0 ? Arm64RelocKind.BranchLink : Arm64RelocKind.Branch;
                insn.Target = pc + (ulong)(SignExtend(raw & 0x03FFFFFF, 26) << 2);
            }
            else if ((raw & 0xFF000000) == 0x54000000)
            {
                // B.cond / BC.cond imm19; AL and NV always branch
                insn.Kind = (raw & 0xE) == 0xE ? Arm64RelocKind.Branch : Arm64RelocKind.ConditionalBranch;
                insn.Target = pc + (ulong)(SignExtend((raw >> 5) & 0x7FFFF, 19) << 2);
            }
            else if ((raw & 0x7E000000) == 0x34000000)
            {
                // CBZ / CBNZ imm19
                insn.Kind = Arm64RelocKind.ConditionalBranch;
                insn.Target = pc + (ulong)(SignExtend((raw >> 5) & 0x7FFFF, 19) << 2);
            }
            else if ((raw & 0x7E000000) == 0x36000000)
            {
                // TBZ / TBNZ imm14
                insn.Kind = Arm64RelocKind.ConditionalBranch;
                insn.Target = pc + (ulong)(SignExtend((raw >> 5) & 0x3FFF, 14) << 2);
            }
            else if ((raw & 0x7E000000) == 0x74000000)
            {
                // FEAT_CMPBR CB<cc> / CBB<cc> / CBH<cc> imm9 (Armv9.6); every form keeps imm9 in [13:5]
                insn.Kind = Arm64RelocKind.ConditionalBranch;
                insn.Target = pc + (ulong)(SignExtend((raw >> 5) & 0x1FF, 9) << 2);
            }
            else if ((raw & 0x1F000000) == 0x10000000)
            {
                // ADR / ADRP imm21
                var imm = SignExtend(((raw >> 5) & 0x7FFFF) << 2 | ((raw >> 29) & 3), 21);
                if ((raw & 0x80000000) == 0)
                {
                    insn.Kind = Arm64RelocKind.Adr;
                    insn.Target = pc + (ulong)imm;
                }
                else
                {
                    insn.Kind = Arm64RelocKind.Adrp;
                    insn.Target = (pc & ~0xFFFUL) + (ulong)(imm << 12);
                }
            }
            else if ((raw & 0x3B000000) == 0x18000000)
            {
                // LDR (literal), LDRSW (literal), PRFM (literal) imm19
                var opc = raw >> 30;
                var isSimd = (raw & 0x04000000) != 0;
                if (isSimd && opc == 3)
                    throw new InvalidOperationException($"Invalid literal load 0x{raw:x8} at 0x{pc:x16}");
                insn.Kind = !isSimd && opc == 3 ? Arm64RelocKind.Prefetch : Arm64RelocKind.LoadLiteral;
                insn.Target = pc + (ulong)(SignExtend((raw >> 5) & 0x7FFFF, 19) << 2);
            }

            return insn;
        }

        /// <summary>
        /// Gets an upper bound for the size of the code produced by <see cref="Encode"/>, regardless of target address.
        /// </summary>
        public static int GetMaxEncodedSize(ReadOnlySpan<Arm64Instruction> insns)
        {
            var size = 0;
            for (var i = 0; i < insns.Length; i++)
            {
                size += insns[i].Kind switch
                {
                    Arm64RelocKind.Branch => BranchAbsoluteSize + 4,
                    Arm64RelocKind.BranchLink => BranchLinkAbsoluteSize + 4,
                    Arm64RelocKind.ConditionalBranch => 8 + BranchAbsoluteSize + 4,
                    Arm64RelocKind.Adr or Arm64RelocKind.Adrp => 16,
                    Arm64RelocKind.LoadLiteral => NeedsScratchForLiteral(insns[i].Raw) ? 36 : 20,
                    _ => 4,
                };
            }
            return size + BranchAbsoluteSize + 4;
        }

        /// <summary>
        /// Encodes <paramref name="insns"/>, decoded from consecutive words at <paramref name="srcPc"/>, to run at <paramref name="dstPc"/>,
        /// followed by a branch back to the instruction after the block.
        /// </summary>
        /// <returns>The number of bytes written to <paramref name="output"/>.</returns>
        public static int Encode(ulong srcPc, Span<Arm64Instruction> insns, ulong dstPc, Span<byte> output)
        {
            if ((srcPc & 3) != 0 || (dstPc & 3) != 0)
                throw new ArgumentException("A64 code must be 4-byte aligned");

            var blockLength = (ulong)insns.Length * 4;

            // branches into the block itself must follow the moved instructions
            for (var i = 0; i < insns.Length; i++)
            {
                ref var insn = ref insns[i];
                insn.TargetIndex = -1;
                if (insn.Kind is Arm64RelocKind.Branch or Arm64RelocKind.BranchLink or Arm64RelocKind.ConditionalBranch
                    && insn.Target - srcPc < blockLength)
                {
                    insn.TargetIndex = (int)((insn.Target - srcPc) / 4);
                }
            }

            // lay out the new code; external targets are fixed and internal branches always use their short form, so one pass suffices
            var offset = 0;
            for (var i = 0; i < insns.Length; i++)
            {
                ref var insn = ref insns[i];
                insn.NewOffset = offset;
                ChooseForm(ref insn, dstPc + (ulong)offset);
                offset += GetSize(insn);
            }

            var back = new Arm64Instruction { Raw = BranchOpcode, Kind = Arm64RelocKind.Branch, Target = srcPc + blockLength, TargetIndex = -1, NewOffset = offset };
            ChooseForm(ref back, dstPc + (ulong)offset);
            var totalSize = offset + GetSize(back);
            if (output.Length < totalSize)
                throw new ArgumentException($"Output buffer is too small; {totalSize} bytes are required", nameof(output));

            for (var i = 0; i < insns.Length; i++)
            {
                ref readonly var insn = ref insns[i];
                var target = insn.TargetIndex >= 0 ? dstPc + (ulong)insns[insn.TargetIndex].NewOffset : insn.Target;
                Emit(insn, target, dstPc + (ulong)insn.NewOffset, output.Slice(insn.NewOffset));
            }
            Emit(back, back.Target, dstPc + (ulong)offset, output.Slice(offset));

            return totalSize;
        }

        private static void ChooseForm(ref Arm64Instruction insn, ulong pc)
        {
            insn.AlignLiteral = false;
            switch (insn.Kind)
            {
                case Arm64RelocKind.None:
                    insn.Form = Arm64Form.Short;
                    break;

                case Arm64RelocKind.Branch:
                case Arm64RelocKind.BranchLink:
                    // internal targets are always within the block, so always in range
                    if (insn.TargetIndex >= 0 || FitsBranch(insn.Target, pc, 26))
                    {
                        insn.Form = Arm64Form.Short;
                    }
                    else
                    {
                        insn.Form = Arm64Form.Absolute;
                        // the literal is at +8 for B and +12 for BL; keep it 8-byte aligned
                        insn.AlignLiteral = ((pc + (insn.Kind == Arm64RelocKind.Branch ? 8UL : 12UL)) & 7) != 0;
                    }
                    break;

                case Arm64RelocKind.ConditionalBranch:
                    if (insn.TargetIndex >= 0 || FitsBranch(insn.Target, pc, ConditionalImmBits(insn.Raw)))
                    {
                        insn.Form = Arm64Form.Short;
                    }
                    else if (FitsBranch(insn.Target, pc + (ulong)ConditionalHeadSize(insn.Raw), 26))
                    {
                        insn.Form = Arm64Form.SkipDirect;
                    }
                    else
                    {
                        insn.Form = Arm64Form.Absolute;
                        insn.AlignLiteral = ((pc + (ulong)ConditionalHeadSize(insn.Raw) + 8) & 7) != 0;
                    }
                    break;

                case Arm64RelocKind.Adr:
                    insn.Form = (insn.Raw & 0x1F) == 31 ? Arm64Form.Nop
                        : FitsSigned((long)(insn.Target - pc), 21) ? Arm64Form.Short
                        : FitsPage(insn.Target, pc) ? Arm64Form.Page
                        : Arm64Form.Absolute;
                    break;

                case Arm64RelocKind.Adrp:
                    insn.Form = (insn.Raw & 0x1F) == 31 ? Arm64Form.Nop
                        : FitsPage(insn.Target, pc) ? Arm64Form.Short
                        : Arm64Form.Absolute;
                    break;

                case Arm64RelocKind.LoadLiteral:
                    // Keep loads to XZR as memory accesses; they can fault.
                    insn.Form = FitsBranch(insn.Target, pc, 19) ? Arm64Form.Short
                        : FitsPage(insn.Target, pc) ? Arm64Form.Page
                        : Arm64Form.Absolute;
                    break;

                case Arm64RelocKind.Prefetch:
                    // a prefetch is only a hint, so drop it if it cannot reach
                    insn.Form = FitsBranch(insn.Target, pc, 19) ? Arm64Form.Short : Arm64Form.Nop;
                    break;
            }
        }

        private static int GetSize(in Arm64Instruction insn)
        {
            var align = insn.AlignLiteral ? 4 : 0;
            return (insn.Kind, insn.Form) switch
            {
                (_, Arm64Form.Short or Arm64Form.Nop) => 4,
                (Arm64RelocKind.Branch, _) => align + BranchAbsoluteSize,
                (Arm64RelocKind.BranchLink, _) => align + BranchLinkAbsoluteSize,
                (Arm64RelocKind.ConditionalBranch, Arm64Form.SkipDirect) => ConditionalHeadSize(insn.Raw) + 4,
                (Arm64RelocKind.ConditionalBranch, _) => ConditionalHeadSize(insn.Raw) + align + BranchAbsoluteSize,
                (Arm64RelocKind.Adr, Arm64Form.Page) => 8,
                (Arm64RelocKind.Adr or Arm64RelocKind.Adrp, _) => 16,
                (Arm64RelocKind.LoadLiteral, Arm64Form.Page) => 12 + (NeedsScratchForLiteral(insn.Raw) ? 16 : 0),
                (Arm64RelocKind.LoadLiteral, _) => 20 + (NeedsScratchForLiteral(insn.Raw) ? 16 : 0),
                _ => throw new InvalidOperationException($"Invalid form {insn.Form} for {insn.Kind}"),
            };
        }

        private static void Emit(in Arm64Instruction insn, ulong target, ulong pc, Span<byte> dst)
        {
            var raw = insn.Raw;
            var rd = (int)(raw & 0x1F);
            switch (insn.Form)
            {
                case Arm64Form.Nop:
                    Write(dst, 0, Nop);
                    return;

                case Arm64Form.Short:
                    Write(dst, 0, insn.Kind switch
                    {
                        Arm64RelocKind.None => raw,
                        Arm64RelocKind.Branch when (raw & 0xFF000000) == 0x54000000 => EncodeBranch(BranchOpcode, target, pc), // B.AL/B.NV
                        Arm64RelocKind.Branch or Arm64RelocKind.BranchLink => EncodeBranch(raw & 0xFC000000, target, pc),
                        Arm64RelocKind.ConditionalBranch => ReencodeConditional(raw, target, pc),
                        Arm64RelocKind.Adr => EncodeAdr(rd, (long)(target - pc), page: false),
                        Arm64RelocKind.Adrp => EncodeAdr(rd, (long)((target >> 12) - (pc >> 12)), page: true),
                        Arm64RelocKind.LoadLiteral or Arm64RelocKind.Prefetch => ReplaceImm19(raw, target, pc),
                        _ => throw new InvalidOperationException($"Unknown relocation kind {insn.Kind}"),
                    });
                    return;
            }

            switch (insn.Kind)
            {
                case Arm64RelocKind.Branch:
                    EmitBranchAbsolute(dst, target, link: false, insn.AlignLiteral);
                    return;

                case Arm64RelocKind.BranchLink:
                    EmitBranchAbsolute(dst, target, link: true, insn.AlignLiteral);
                    return;

                case Arm64RelocKind.ConditionalBranch:
                {
                    var head = ConditionalHeadSize(raw);
                    var size = GetSize(insn);
                    if (IsCompareAndBranch(raw))
                    {
                        // CB<cc> has no simple inverse (register forms would need swapped operands, immediate forms an adjusted
                        // immediate), so branch over a skip instead: cb<cc> taken ; b over ; taken: <long branch> ; over:
                        Write(dst, 0, ReencodeConditional(raw, pc + 8, pc));
                        Write(dst, 4, EncodeBranch(BranchOpcode, pc + (ulong)size, pc + 4));
                    }
                    else
                    {
                        // inverted condition skips over the long branch
                        Write(dst, 0, ReencodeConditional(InvertCondition(raw), pc + (ulong)size, pc));
                    }

                    if (insn.Form == Arm64Form.SkipDirect)
                        Write(dst, head, EncodeBranch(BranchOpcode, target, pc + (ulong)head));
                    else
                        EmitBranchAbsolute(dst.Slice(head), target, link: false, insn.AlignLiteral);
                    return;
                }

                case Arm64RelocKind.Adr:
                case Arm64RelocKind.Adrp:
                    if (insn.Form == Arm64Form.Page)
                    {
                        Write(dst, 0, EncodeAdr(rd, (long)((target >> 12) - (pc >> 12)), page: true));
                        Write(dst, 4, EncodeAddImm(rd, rd, (uint)(target & 0xFFF)));
                    }
                    else
                    {
                        EmitMovAbsolute(dst, rd, target);
                    }
                    return;

                case Arm64RelocKind.LoadLiteral:
                {
                    var needsScratch = NeedsScratchForLiteral(raw);
                    var rb = needsScratch ? ScratchRegister : rd;
                    var pos = 0;
                    if (needsScratch)
                    {
                        Write(dst, pos, SubSp16);
                        Write(dst, pos + 4, StrX17Sp);
                        pos += 8;
                    }

                    if (insn.Form == Arm64Form.Page)
                    {
                        Write(dst, pos, EncodeAdr(rb, (long)((target >> 12) - (pc >> 12)), page: true));
                        Write(dst, pos + 4, EncodeAddImm(rb, rb, (uint)(target & 0xFFF)));
                        pos += 8;
                    }
                    else
                    {
                        EmitMovAbsolute(dst.Slice(pos), rb, target);
                        pos += 16;
                    }

                    Write(dst, pos, EncodeLoadFromBase(raw, rb));
                    pos += 4;
                    if (needsScratch)
                    {
                        Write(dst, pos, LdrX17Sp);
                        Write(dst, pos + 4, AddSp16);
                    }
                    return;
                }
            }

            throw new InvalidOperationException($"Invalid form {insn.Form} for {insn.Kind}");
        }

        private static void EmitBranchAbsolute(Span<byte> dst, ulong target, bool link, bool align)
        {
            var pos = 0;
            if (align)
            {
                Write(dst, 0, Nop);
                pos = 4;
            }

            if (link)
            {
                // ldr x16, #12 ; blr x16 ; b #12 ; .quad target
                Write(dst, pos, LdrLiteralX16 | (3u << 5));
                Write(dst, pos + 4, BlrX16);
                Write(dst, pos + 8, BranchOpcode | 3u);
                WriteUInt64(dst, pos + 12, target);
            }
            else
            {
                // ldr x16, #8 ; br x16 ; .quad target
                Write(dst, pos, LdrLiteralX16 | (2u << 5));
                Write(dst, pos + 4, BrX16);
                WriteUInt64(dst, pos + 8, target);
            }
        }

        private static void EmitMovAbsolute(Span<byte> dst, int rd, ulong value)
        {
            // movz xd, #v0 ; movk xd, #v1, lsl #16 ; movk xd, #v2, lsl #32 ; movk xd, #v3, lsl #48
            for (var hw = 0; hw < 4; hw++)
            {
                var imm16 = (uint)(value >> (16 * hw)) & 0xFFFF;
                var opcode = hw == 0 ? 0xD2800000u : 0xF2800000u;
                Write(dst, hw * 4, opcode | ((uint)hw << 21) | (imm16 << 5) | (uint)rd);
            }
        }

        private static uint EncodeLoadFromBase(uint literalLoad, int rb)
        {
            // the unsigned-offset form with offset 0 and the same destination and size as the literal load
            var rt = literalLoad & 0x1F;
            var opc = literalLoad >> 30;
            var opcode = (literalLoad & 0x04000000) != 0
                ? opc switch { 0 => 0xBD400000u, 1 => 0xFD400000u, _ => 0x3DC00000u } // LDR St, Dt, Qt
                : opc switch { 0 => 0xB9400000u, 1 => 0xF9400000u, _ => 0xB9800000u }; // LDR Wt, Xt, LDRSW Xt
            return opcode | ((uint)rb << 5) | rt;
        }

        private static bool NeedsScratchForLiteral(uint raw)
            => (raw & 0x04000000) != 0 || (raw & 0x1F) == 31;
        private static uint EncodeAddImm(int rd, int rn, uint imm12)
            => 0x91000000u | (imm12 << 10) | ((uint)rn << 5) | (uint)rd;

        private static uint EncodeAdr(int rd, long imm21, bool page)
        {
            var imm = (uint)imm21 & 0x1FFFFF;
            return (page ? 0x90000000u : 0x10000000u) | ((imm & 3) << 29) | ((imm >> 2) << 5) | (uint)rd;
        }

        private static uint EncodeBranch(uint opcode, ulong target, ulong pc)
            => opcode | ((uint)((long)(target - pc) >> 2) & 0x03FFFFFF);

        private static bool IsCompareAndBranch(uint raw)
            => (raw & 0x7E000000) == 0x74000000;

        // the instructions before the long branch in a conditional branch's out-of-range forms
        private static int ConditionalHeadSize(uint raw)
            => IsCompareAndBranch(raw) ? 8 : 4;

        private static int ConditionalImmBits(uint raw)
            => (raw & 0x7E000000) == 0x36000000 ? 14
                : IsCompareAndBranch(raw) ? 9
                : 19;

        private static uint ReencodeConditional(uint raw, ulong target, ulong pc)
        {
            var imm = (uint)((long)(target - pc) >> 2);
            if ((raw & 0x7E000000) == 0x36000000)
            {
                // TBZ / TBNZ imm14
                return (raw & ~(0x3FFFu << 5)) | ((imm & 0x3FFF) << 5);
            }
            if (IsCompareAndBranch(raw))
            {
                // CB<cc> imm9
                return (raw & ~(0x1FFu << 5)) | ((imm & 0x1FF) << 5);
            }
            return ReplaceImm19(raw, target, pc);
        }

        private static uint ReplaceImm19(uint raw, ulong target, ulong pc)
        {
            var imm19 = (uint)((long)(target - pc) >> 2) & 0x7FFFF;
            return (raw & ~(0x7FFFFu << 5)) | (imm19 << 5);
        }

        private static uint InvertCondition(uint raw)
            => (raw & 0xFF000000) == 0x54000000
                ? raw ^ 1 // B.cond / BC.cond: flip the low bit of the condition
                : raw ^ (1u << 24); // CBZ <-> CBNZ, TBZ <-> TBNZ

        private static bool FitsBranch(ulong target, ulong pc, int immBits)
            => FitsSigned((long)(target - pc), immBits + 2);

        private static bool FitsPage(ulong target, ulong pc)
            => FitsSigned((long)((target >> 12) - (pc >> 12)), 21);

        private static bool FitsSigned(long value, int bits)
            => value >= -(1L << (bits - 1)) && value < (1L << (bits - 1));

        private static long SignExtend(uint value, int bits)
            => (long)((ulong)value << (64 - bits)) >> (64 - bits);

        private static void Write(Span<byte> dst, int offset, uint value)
        {
            dst[offset] = (byte)value;
            dst[offset + 1] = (byte)(value >> 8);
            dst[offset + 2] = (byte)(value >> 16);
            dst[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(Span<byte> dst, int offset, ulong value)
        {
            Write(dst, offset, unchecked((uint)value));
            Write(dst, offset + 4, unchecked((uint)(value >> 32)));
        }
    }
}
