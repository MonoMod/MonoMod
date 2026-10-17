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
    }
}
