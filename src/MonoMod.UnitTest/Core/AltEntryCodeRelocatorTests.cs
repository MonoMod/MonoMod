using MonoMod.Core.Platforms.Architectures.AltEntryFactories;
using System;
using Xunit;

namespace MonoMod.UnitTest.Core
{
    public class AltEntryCodeRelocatorTests
    {
        [Fact]
        public void X86RelocatesExternalConditionalBranchAndReturnContinuation()
        {
            const ulong source = 0x10000000;
            const ulong destination = 0x10001000;
            var code = RelocateX86([
                0x74, 0x02, // je short +2
            ], source, destination, minLength: 2, is64: true);

            Assert.Equal(new byte[] { 0x0F, 0x84 }, code.AsSpan(0, 2).ToArray()); // jz rel32
            Assert.Equal(source + 4, X86Rel32Target(code, 2, destination + 6));
            Assert.Equal(0xE9, code[6]); // jmp rel32 to the original continuation
            Assert.Equal(source + 2, X86Rel32Target(code, 7, destination + 11));
        }

        [Fact]
        public void X86RetargetsBackwardBranchToMovedInstruction()
        {
            const ulong source = 0x10000000;
            const ulong destination = 0x10001000;
            var code = RelocateX86([
                0x90, // nop
                0xEB, 0xFD, // jmp short -3
            ], source, destination, minLength: 3, is64: true);

            Assert.Equal(0x90, code[0]); // nop
            Assert.Equal(0xE9, code[1]); // relocated jmp rel32
            Assert.Equal(destination, X86Rel32Target(code, 2, destination + 6));
            Assert.Equal(source + 3, X86Rel32Target(code, 7, destination + 11));
        }
        [Fact]
        public void X86RelocatesMultipleReferencesAcrossExpandedPrologue()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x1000;
            var code = RelocateX86([
                0x55, // push rbp
                0x48, 0x89, 0xE5, // mov rbp, rsp
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0x74, 0x06, // je short to the final jmp
                0x8B, 0x05, 0x20, 0x00, 0x00, 0x00, // mov eax, [rip + 0x20]
                0xEB, 0xEE, // jmp short back to the prologue start
            ], source, destination, minLength: 18, is64: true);

            Assert.Equal(new byte[] { 0x55, 0x48, 0x89, 0xE5, 0x48, 0x83, 0xEC, 0x10 }, code.AsSpan(0, 8).ToArray()); // push/mov/sub stack frame
            Assert.Equal(new byte[] { 0x0F, 0x84 }, code.AsSpan(8, 2).ToArray()); // je rel32
            Assert.Equal(destination + 20, X86Rel32Target(code, 10, destination + 14)); // taken branch follows the relocated block
            Assert.Equal(new byte[] { 0x8B, 0x05 }, code.AsSpan(14, 2).ToArray()); // mov eax, [rip + disp32]
            Assert.Equal(source + 0x30, X86Rel32Target(code, 16, destination + 20));
            Assert.Equal(0xE9, code[20]); // relocated jmp rel32 to the block start
            Assert.Equal(destination, X86Rel32Target(code, 21, destination + 25));
            Assert.Equal(0xE9, code[25]); // trampoline jump back
            Assert.Equal(source + 18, X86Rel32Target(code, 26, destination + 30));
        }

        [Fact]
        public void X86RelocatesFramePrologueAndInternalConditionalBranch()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x1000;
            var code = RelocateX86([
                0x55, // push ebp
                0x89, 0xE5, // mov ebp, esp
                0x83, 0xEC, 0x10, // sub esp, 0x10
                0x31, 0xC0, // xor eax, eax
                0x85, 0xC0, // test eax, eax
                0x74, 0x03, // je short to leave
                0x83, 0xC0, 0x01, // add eax, 1
                0xC9, // leave
                0xC3, // ret
            ], source, destination, minLength: 17, is64: false);

            Assert.Equal(new byte[] { 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x10, 0x31, 0xC0, 0x85, 0xC0 }, code.AsSpan(0, 10).ToArray()); // push/mov/sub/xor/test
            Assert.Equal(new byte[] { 0x0F, 0x84 }, code.AsSpan(10, 2).ToArray()); // je rel32
            Assert.Equal(destination + 19, X86Rel32Target(code, 12, destination + 16)); // branch to relocated leave
            Assert.Equal(new byte[] { 0x83, 0xC0, 0x01 }, code.AsSpan(16, 3).ToArray()); // add eax, 1
            Assert.Equal(0xC9, code[19]); // leave
            Assert.Equal(0xC3, code[20]); // ret
            Assert.Equal(0xE9, code[21]); // trampoline jump back
            Assert.Equal(source + 17, X86Rel32Target(code, 22, destination + 26));
        }



        [Fact]
        public void X86PreservesRipRelativeTargetAndExpandsFarCall()
        {
            const ulong source = 0x10000000;
            const ulong nearDestination = 0x20000000;
            var ripRelative = RelocateX86([
                0x8B, 0x05, 0x0A, 0x00, 0x00, 0x00, // mov eax, [rip + 0x0A]
            ], source, nearDestination, minLength: 6, is64: true);

            Assert.Equal(new byte[] { 0x8B, 0x05 }, ripRelative.AsSpan(0, 2).ToArray()); // mov eax, [rip + disp32]
            Assert.Equal(source + 16, X86Rel32Target(ripRelative, 2, nearDestination + 6));
            Assert.Equal(source + 6, X86Rel32Target(ripRelative, 7, nearDestination + 11));

            const ulong farDestination = 0x1000000000;
            var farCall = RelocateX86([
                0xE8, 0, 0, 0, 0, // call rel32 to the next instruction
            ], source, farDestination, minLength: 5, is64: true);

            Assert.Equal(new byte[] { 0xFF, 0x15, 0x02, 0x00, 0x00, 0x00, 0xEB, 0x08 }, farCall.AsSpan(0, 8).ToArray()); // call [rip + 2]; jmp short +8
            Assert.Equal(source + 5, ReadUInt64(farCall, 8)); // inline call target
            Assert.Equal(new byte[] { 0xFF, 0x25, 0, 0, 0, 0 }, farCall.AsSpan(16, 6).ToArray()); // jmp [rip]
            Assert.Equal(source + 5, ReadUInt64(farCall, 22)); // inline continuation target
        }

        [Fact]
        public void X86CounterBranchKeepsAddressSizePrefixAndBothTargets()
        {
            const ulong source = 0x10000000;
            const ulong destination = 0x10001000;
            var code = RelocateX86([
                0x67, 0xE3, 0x00, // jecxz short +0
            ], source, destination, minLength: 3, is64: true);

            Assert.Equal(new byte[] { 0x67, 0xE3, 0x02, 0xEB, 0x05, 0xE9 }, code.AsSpan(0, 6).ToArray()); // jecxz; jmp short; jmp rel32
            Assert.Equal(source + 3, X86Rel32Target(code, 6, destination + 10));
            Assert.Equal(0xE9, code[10]); // trampoline jump back
            Assert.Equal(source + 3, X86Rel32Target(code, 11, destination + 15));
        }

        [Fact]
        public void X86Rel32WrapsIn32BitMode()
        {
            const ulong source = 0xFFFFFFF0;
            const ulong destination = 0x00001000;
            var code = RelocateX86([
                0xE9, 0x0B, 0x00, 0x00, 0x00, // jmp rel32 +0x0B; target wraps to zero in 32-bit mode
            ], source, destination, minLength: 5, is64: false);

            Assert.Equal(0xE9, code[0]); // relocated jmp rel32
            Assert.Equal(0UL, X86Rel32Target(code, 1, destination + 5) & uint.MaxValue);
            Assert.Equal((source + 5) & uint.MaxValue, X86Rel32Target(code, 6, destination + 10) & uint.MaxValue);
        }

        [Fact]
        public void Arm64ExpandsLiteralLoadAndRetargetsBranchAfterExpansion()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x200000;
            var code = RelocateArm64([
                0x18000100, // ldr w0, [pc, #0x20]
                0x14000001, // b +4
                0xD65F03C0, // ret
            ], source, destination);

            Assert.Equal(source & ~0xFFFUL, Arm64AdrTarget(code[0], destination, page: true)); // adrp x0, literal page
            Assert.Equal(0x20u, (code[1] >> 10) & 0xFFF); // add x0, x0, #0x20
            Assert.Equal(0xB9400000u, code[2]); // ldr w0, [x0]
            Assert.Equal(destination + 16, Arm64BranchTarget(code[3], destination + 12, 26, 0)); // b to moved ret
            Assert.Equal(0xD65F03C0, code[4]); // ret
            Assert.Equal(source + 12, Arm64BranchTarget(code[5], destination + 20, 26, 0)); // branch to continuation
        }

        [Fact]
        public void Arm64RelocatesMultiplePcRelativeOperandsAcrossExpandedBlock()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x200000000;
            var code = RelocateArm64([
                0x10000801, // adr x1, source + 0x100
                0x18000200, // ldr w0, [pc, #0x40]
                0x14000001, // b +4 to ret
                0xD65F03C0, // ret
            ], source, destination);

            Assert.Equal(1u, code[0] & 0x1F); // movz x1, address low half
            Assert.Equal(source + 0x100, ReadMovWideConstant(code, 0));
            Assert.Equal(0u, code[4] & 0x1F); // movz x0, literal address low half
            Assert.Equal(source + 0x44, ReadMovWideConstant(code, 4));
            Assert.Equal(0xB9400000u, code[8]); // ldr w0, [x0]
            Assert.Equal(0x14000001u, code[9]); // b to the relocated ret
            Assert.Equal(destination + 40, Arm64BranchTarget(code[9], destination + 36, 26, 0));
            Assert.Equal(0xD65F03C0u, code[10]); // ret
            Assert.Equal(0xD503201Fu, code[11]); // nop to align the return branch literal
            Assert.Equal(0x58000050u, code[12]); // ldr x16, #8
            Assert.Equal(0xD61F0200u, code[13]); // br x16
            Assert.Equal(source + 16, ReadArm64UInt64(code, 14)); // branch-back literal
        }
        [Fact]
        public void Arm64RelocatesStackPrologueWithAdrLiteralAndInternalBranch()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x200000;
            var code = RelocateArm64([
                0xA9BF7BFD, // stp x29, x30, [sp, #-16]!
                0x910003FD, // mov x29, sp
                0x100007C9, // adr x9, source + 0x100
                0x9C000100, // ldr q0, [pc, #0x20]
                0x14000001, // b +4 to ret
                0xD65F03C0, // ret
            ], source, destination);

            Assert.Equal(0xA9BF7BFDu, code[0]); // stp x29, x30, [sp, #-16]!
            Assert.Equal(0x910003FDu, code[1]); // mov x29, sp
            Assert.Equal(source & ~0xFFFUL, Arm64AdrTarget(code[2], destination + 8, page: true)); // adrp x9, target page
            Assert.Equal(9u, code[2] & 0x1F);
            Assert.Equal(0x100u, (code[3] >> 10) & 0xFFF); // add x9, x9, #0x100
            Assert.Equal(0xD10043FFu, code[4]); // sub sp, sp, #16 for x17 spill
            Assert.Equal(0xF90003F1u, code[5]); // str x17, [sp]
            Assert.Equal(source & ~0xFFFUL, Arm64AdrTarget(code[6], destination + 24, page: true)); // adrp x17, literal page
            Assert.Equal(0x2Cu, (code[7] >> 10) & 0xFFF); // add x17, x17, #0x2C
            Assert.Equal(0x3DC00220u, code[8]); // ldr q0, [x17]
            Assert.Equal(0xF94003F1u, code[9]); // ldr x17, [sp]
            Assert.Equal(0x910043FFu, code[10]); // add sp, sp, #16
            Assert.Equal(destination + 48, Arm64BranchTarget(code[11], destination + 44, 26, 0)); // b to the moved ret
            Assert.Equal(0xD65F03C0u, code[12]); // ret
            Assert.Equal(source + 24, Arm64BranchTarget(code[13], destination + 52, 26, 0)); // branch to continuation
        }


        [Fact]
        public void Arm64PreservesX17WhenRelocatingSimdLiteralPrologue()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x200000;
            var code = RelocateArm64([
                0xA9BF7BFD, // stp x29, x30, [sp, #-16]!
                0xAA0003F1, // mov x17, x0
                0x9C000100, // ldr q0, [pc, #0x20]
            ], source, destination);

            Assert.Equal(0xA9BF7BFD, code[0]); // stp x29, x30, [sp, #-16]!
            Assert.Equal(0xAA0003F1, code[1]); // mov x17, x0
            Assert.Equal(0xD10043FFu, code[2]); // sub sp, sp, #16
            Assert.Equal(0xF90003F1u, code[3]); // str x17, [sp]
            Assert.Equal(17u, code[4] & 0x1F); // adrp x17, literal page
            var literalPage = Arm64AdrTarget(code[4], destination + 16, page: true);
            Assert.Equal(source + 0x28, literalPage + ((code[5] >> 10) & 0xFFF)); // add x17, x17, #low12
            Assert.Equal(0x3DC00220u, code[6]); // ldr q0, [x17]
            Assert.Equal(0xF94003F1u, code[7]); // ldr x17, [sp]
            Assert.Equal(0x910043FFu, code[8]); // add sp, sp, #16
            Assert.Equal(source + 12, Arm64BranchTarget(code[9], destination + 36, 26, 0));
        }

        [Fact]
        public void Arm64PreservesX17ForAbsoluteSimdLiteralLoad()
        {
            const ulong source = 0x10000000;
            const ulong destination = 0x200000000;
            var code = RelocateArm64([0x9C000100], source, destination); // ldr q0, [pc, #0x20]

            Assert.Equal(0xD10043FFu, code[0]); // sub sp, sp, #16
            Assert.Equal(0xF90003F1u, code[1]); // str x17, [sp]
            Assert.Equal(source + 0x20, ReadMovWideConstant(code, 2)); // movz/movk x17, literal address
            Assert.Equal(0x3DC00220u, code[6]); // ldr q0, [x17]
            Assert.Equal(0xF94003F1u, code[7]); // ldr x17, [sp]
            Assert.Equal(0x910043FFu, code[8]); // add sp, sp, #16
            Assert.Equal(0xD503201Fu, code[9]); // nop to align the absolute branch literal
            Assert.Equal(0x58000050u, code[10]); // ldr x16, #8
            Assert.Equal(0xD61F0200u, code[11]); // br x16
            Assert.Equal(source + 4, ReadArm64UInt64(code, 12)); // branch-back literal
        }

        [Fact]
        public void Arm64KeepsOutOfRangeLoadToXzrAsAMemoryAccess()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x200000;
            var code = RelocateArm64([
                0xA9BF7BFD, // stp x29, x30, [sp, #-16]!
                0x910003FD, // mov x29, sp
                0x1800011F, // ldr wzr, [pc, #0x20]
            ], source, destination);

            Assert.Equal(0xA9BF7BFD, code[0]); // stp x29, x30, [sp, #-16]!
            Assert.Equal(0x910003FD, code[1]); // mov x29, sp
            Assert.Equal(0xD10043FFu, code[2]); // sub sp, sp, #16
            Assert.Equal(0xF90003F1u, code[3]); // str x17, [sp]
            var literalPage = Arm64AdrTarget(code[4], destination + 16, page: true);
            Assert.Equal(source + 0x28, literalPage + ((code[5] >> 10) & 0xFFF)); // add x17, x17, #low12
            Assert.Equal(0xB940023Fu, code[6]); // ldr wzr, [x17], preserving the faulting memory access
            Assert.Equal(0xF94003F1u, code[7]); // ldr x17, [sp]
            Assert.Equal(0x910043FFu, code[8]); // add sp, sp, #16
            Assert.Equal(source + 12, Arm64BranchTarget(code[9], destination + 36, 26, 0));
        }

        [Fact]
        public void Arm64ExpandsOutOfRangeConditionalBranchToConditionSkipAndDirectBranch()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x07800000;
            var code = RelocateArm64([0x54000800], source, destination); // b.eq source + 0x100

            Assert.Equal(0x54000041u, code[0]); // b.ne destination + 8
            Assert.Equal(destination + 8, Arm64BranchTarget(code[0], destination, 19, 5));
            Assert.Equal(source + 0x100, Arm64BranchTarget(code[1], destination + 4, 26, 0)); // b to the original target
            Assert.Equal(source + 4, Arm64BranchTarget(code[2], destination + 8, 26, 0)); // b to the original continuation
        }

        [Fact]
        public void Arm64ExpandsCompareBranchWithoutInvertingItsCondition()
        {
            const ulong source = 0x10000000;
            const ulong destination = source + 0x2000;
            var code = RelocateArm64([0x74000020], source, destination); // FEAT_CMPBR immediate +4

            Assert.Equal(0x74000040u, code[0]); // original condition branches to the long branch
            Assert.Equal(destination + 8, Arm64BranchTarget(code[0], destination, 9, 5));
            Assert.Equal(0x14000002u, code[1]); // b over the long-branch slot
            Assert.Equal(destination + 12, Arm64BranchTarget(code[1], destination + 4, 26, 0));
            Assert.Equal(source + 4, Arm64BranchTarget(code[2], destination + 8, 26, 0)); // b to continuation
        }

        [Fact]
        public void Arm64MaterializesFarAdrAndReturnsToOriginalContinuation()
        {
            const ulong source = 0x10000000;
            const ulong destination = 0x200000000;
            var code = RelocateArm64([0x10000803], source, destination); // adr x3, source + 0x100

            Assert.Equal(0xD2802003u, code[0]); // movz x3, #0x100
            Assert.Equal(0xF2A20003u, code[1]); // movk x3, #0x1000, lsl #16
            Assert.Equal(0xF2C00003u, code[2]); // movk x3, #0, lsl #32
            Assert.Equal(0xF2E00003u, code[3]); // movk x3, #0, lsl #48
            Assert.Equal(0x58000050u, code[4]); // ldr x16, #8
            Assert.Equal(0xD61F0200u, code[5]); // br x16
            Assert.Equal(source + 0x100, ReadMovWideConstant(code, 0));
            Assert.Equal(source + 4, ReadArm64UInt64(code, 6));
        }

        private static byte[] RelocateX86(byte[] instructions, ulong source, ulong destination, int minLength, bool is64)
        {
            var code = new byte[minLength + 15];
            Array.Copy(instructions, code, instructions.Length);
            var insns = new X86Instruction[minLength];
            var count = X86CodeRelocator.DecodeBlock(code, source, is64, minLength, insns, out var blockLength);
            var output = new byte[X86CodeRelocator.GetMaxEncodedSize(insns.AsSpan(0, count), is64)];
            var size = X86CodeRelocator.Encode(code, source, insns.AsSpan(0, count), blockLength, is64, destination, output);
            Array.Resize(ref output, size);
            return output;
        }

        private static uint[] RelocateArm64(uint[] instructions, ulong source, ulong destination)
        {
            var insns = new Arm64Instruction[instructions.Length];
            for (var i = 0; i < instructions.Length; i++)
                insns[i] = Arm64CodeRelocator.Decode(instructions[i], source + (ulong)(i * 4));

            var output = new byte[Arm64CodeRelocator.GetMaxEncodedSize(insns)];
            var size = Arm64CodeRelocator.Encode(source, insns, destination, output);
            var words = new uint[size / 4];
            for (var i = 0; i < words.Length; i++)
                words[i] = ReadUInt32(output, i * 4);
            return words;
        }

        private static ulong X86Rel32Target(byte[] code, int displacementOffset, ulong instructionEnd)
            => unchecked((ulong)((long)instructionEnd + ReadInt32(code, displacementOffset)));

        private static ulong Arm64BranchTarget(uint instruction, ulong pc, int immediateBits, int immediateOffset)
        {
            var mask = (1u << immediateBits) - 1;
            var immediate = (long)((ulong)((instruction >> immediateOffset) & mask) << (64 - immediateBits)) >> (64 - immediateBits);
            return unchecked((ulong)((long)pc + (immediate << 2)));
        }

        private static ulong Arm64AdrTarget(uint instruction, ulong pc, bool page)
        {
            var immediate = ((instruction >> 5) & 0x7FFFF) << 2 | ((instruction >> 29) & 3);
            var signedImmediate = (long)((ulong)immediate << (64 - 21)) >> (64 - 21);
            return page
                ? unchecked((ulong)((long)(pc & ~0xFFFUL) + (signedImmediate << 12)))
                : unchecked((ulong)((long)pc + signedImmediate));
        }

        private static ulong ReadMovWideConstant(uint[] words, int wordOffset)
        {
            ulong value = 0;
            for (var i = 0; i < 4; i++)
            {
                var instruction = words[wordOffset + i];
                value |= (ulong)((instruction >> 5) & 0xFFFF) << (int)(((instruction >> 21) & 3) * 16);
            }
            return value;
        }
        private static ulong ReadArm64UInt64(uint[] words, int wordOffset)
            => words[wordOffset] | ((ulong)words[wordOffset + 1] << 32);

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));

        private static ulong ReadUInt64(byte[] bytes, int offset)
            => ReadUInt32(bytes, offset) | ((ulong)ReadUInt32(bytes, offset + 4) << 32);

        private static int ReadInt32(byte[] bytes, int offset)
            => bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
    }
}
