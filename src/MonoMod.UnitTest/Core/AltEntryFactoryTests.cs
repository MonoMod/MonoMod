using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    public class AltEntryFactoryTests : TestBase
    {
        public AltEntryFactoryTests(ITestOutputHelper helper) : base(helper)
        {
        }

        private const int CodeSize = 0x40;

        // Each function takes no arguments and returns an int in eax, so it is callable under every x86/x86-64 calling convention.
        // Gaps are filled with int3.
        private static byte[] Code(params object[] offsetsAndHex)
        {
            var code = new byte[CodeSize];
            code.AsSpan().Fill(0xCC);
            for (var i = 0; i < offsetsAndHex.Length; i += 2)
            {
                var offset = (int)offsetsAndHex[i];
                foreach (var part in ((string)offsetsAndHex[i + 1]).Split(' '))
                {
                    for (var j = 0; j < part.Length; j += 2)
                    {
                        code[offset++] = Convert.ToByte(part.Substring(j, 2), 16);
                    }
                }
            }
            return code;
        }

        public static TheoryData<string, byte[], int, int, bool> Functions => new()
        {
            {
                "jcc rel8 leaving the block",
                // xor eax, eax ; test eax, eax ; je +5 ; mov eax, 1 ; add eax, 42 ; ret
                Code(0, "31C0 85C0 7405 B801000000 052A000000 C3"),
                6, 42, false
            },
            {
                "call rel32",
                // call +0x1B ; add eax, 1 ; ret ; ... ; 0x20: mov eax, 41 ; ret
                Code(0, "E81B000000 83C001 C3", 0x20, "B829000000 C3"),
                5, 42, false
            },
            {
                "jcc rel8 back into the block",
                // xor eax, eax ; mov cl, 7 ; loop: inc eax ; dec cl ; jne loop ; ret
                Code(0, "31C0 B107 FFC0 FEC9 75FA C3"),
                10, 7, false
            },
            {
                "loop rel8 back into the block",
                // mov ecx, 7 ; xor eax, eax ; loop: inc eax ; loop loop ; ret
                Code(0, "B907000000 31C0 FFC0 E2FC C3"),
                11, 7, false
            },
            {
                "rip-relative load",
                // mov eax, [rip+0x0A] ; ret ; ... ; 0x10: dd 0x12345678
                Code(0, "8B050A000000 C3", 0x10, "78563412"),
                6, 0x12345678, true
            },
            {
                "rip-relative operand followed by an immediate",
                // xor eax, eax ; cmp dword [rip+0x0C], 0x12345678 ; sete al ; ret ; ... ; 0x18: dd 0x12345678
                Code(0, "31C0 813D0C00000078563412 0F94C0 C3", 0x18, "78563412"),
                12, 1, true
            },
        };

        [Theory]
        [MemberData(nameof(Functions))]
        public unsafe void AltEntryExecutesRelocatedPrologue(string name, byte[] code, int minLength, int expected, bool x64Only)
        {
            var triple = PlatformTriple.Current;
            if (!triple.SupportedFeatures.Has(ArchitectureFeature.CreateAltEntryPoint)
                || triple.Architecture.Target is not (ArchitectureKind.x86 or ArchitectureKind.x86_64)
                || (x64Only && triple.Architecture.Target != ArchitectureKind.x86_64))
            {
                return;
            }

            Assert.True(triple.System.MemoryAllocator.TryAllocate(new(CodeSize) { Executable = true }, out var codeMem));
            using (codeMem)
            {
                var fn = codeMem.BaseAddress;
                triple.System.PatchData(PatchTargetKind.Executable, fn, code, default);
                Assert.Equal(expected, ((delegate* unmanaged[Cdecl]<int>)fn)());

                var altEntry = triple.Architecture.AltEntryFactory.CreateAlternateEntrypoint(fn, minLength, out var altHandle);
                using (altHandle)
                {
                    // Clobber the relocated prologue like a detour would. Anything that still runs it, rather than its relocated copy,
                    // returns early with the wrong value.
                    var clobber = new byte[minLength];
                    clobber.AsSpan().Fill(0xC3);
                    triple.System.PatchData(PatchTargetKind.Executable, fn, clobber, default);

                    var actual = ((delegate* unmanaged[Cdecl]<int>)altEntry)();
                    Assert.True(expected == actual, $"{name}: expected {expected}, got {actual}");
                }
            }
        }

        // APX encodings (from LLVM). These cannot be executed on most hardware, so the test inspects the generated alt entry instead.
        // RIP-relative displacements are 0x100; dispOffset locates them, or is -1 if there are none.
        public static TheoryData<string, string, int> ApxPrologues => new()
        {
            // push2 %r12, %r13 ; pushp %rbx ; pushp %r16
            { "push2/pushp", "62D41418FFF4 D50853 D51850", -1 },
            // imulq $0x12345678, 0x100(%rip), %r17 (REX2)
            { "REX2 rip-relative with imm32", "D548690D0001000078563412", 4 },
            // {nf} imulq $0x12345678, 0x100(%rip), %rbx (EVEX map 4)
            { "EVEX map 4 rip-relative with imm32", "62F4FC0C691D0001000078563412", 6 },
            // addw $0x1234, 0x100(%rip), %r16w (EVEX map 4 NDD, 16-bit operand size from EVEX.pp)
            { "EVEX map 4 rip-relative with imm16", "62F47D10810500010000 3412", 6 },
            // jmpabs $0x1122334455667788
            { "jmpabs", "D500A18877665544332211", -1 },
        };

        [Theory]
        [MemberData(nameof(ApxPrologues))]
        public unsafe void AltEntryRelocatesApxPrologue(string name, string hex, int dispOffset)
        {
            var triple = PlatformTriple.Current;
            if (!triple.SupportedFeatures.Has(ArchitectureFeature.CreateAltEntryPoint) || triple.Architecture.Target != ArchitectureKind.x86_64)
            {
                return;
            }

            var code = Code(0, hex);
            var length = 0;
            foreach (var part in hex.Split(' '))
                length += part.Length / 2;

            Assert.True(triple.System.MemoryAllocator.TryAllocate(new(CodeSize) { Executable = true }, out var codeMem));
            using (codeMem)
            {
                var fn = codeMem.BaseAddress;
                triple.System.PatchData(PatchTargetKind.Executable, fn, code, default);

                var altEntry = triple.Architecture.AltEntryFactory.CreateAlternateEntrypoint(fn, length, out var altHandle);
                using (altHandle)
                {
                    var alt = new ReadOnlySpan<byte>((void*)altEntry, length + 14);

                    // the instructions are copied verbatim, except that RIP-relative displacements still reach the same address
                    var expected = code.AsSpan(0, length).ToArray();
                    if (dispOffset >= 0)
                    {
                        var disp = 0x100 + ((long)fn - (long)altEntry);
                        Assert.InRange(disp, int.MinValue, int.MaxValue);
                        BitConverter.GetBytes((int)disp).CopyTo(expected, dispOffset);
                    }
                    Assert.True(alt.Slice(0, length).SequenceEqual(expected), $"{name}: relocated code differs");

                    // followed by a jump back to the first instruction after the block
                    var tail = alt.Slice(length);
                    long jumpTarget = tail[0] switch
                    {
                        0xE9 => (long)altEntry + length + 5 + BitConverter.ToInt32(tail.Slice(1, 4).ToArray(), 0),
                        0xFF when tail[1] == 0x25 && BitConverter.ToInt32(tail.Slice(2, 4).ToArray(), 0) == 0 => BitConverter.ToInt64(tail.Slice(6, 8).ToArray(), 0),
                        _ => throw new Xunit.Sdk.XunitException($"{name}: no jump back after the relocated code"),
                    };
                    Assert.Equal((long)fn + length, jumpTarget);
                }
            }
        }
    }
}
