using MonoMod.Core;
using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MonoMod.UnitTest.Core {
    public class BytePatternCollectionTests {
        [Fact]
        public void TestBytePattern() {

            var runtimeFlgs = RuntimeFeature.GenericSharing | RuntimeFeature.CompileMethodHook | RuntimeFeature.DisableInlining;

            Assert.True(runtimeFlgs.Has(RuntimeFeature.CompileMethodHook));
            Assert.False(runtimeFlgs.Has(RuntimeFeature.PreciseGC));

            _ = PlatformDetection.OS;

            // -3 is addr, -2 is any repeating, -1 is any
            var collection = new BytePatternCollection(
                    // mov...; nop; call...; jmp {delta}
                    new(new(AddressKind.Rel32, 16), 0xb8, -1, -1, -1, -1, 0x90, 0xe8, -1, -1, -1, -1, 0xe9, -3, -3, -3, -3),
                    // jmp {delta}; pop rdi
                    new(new(AddressKind.Rel32, 5), 0xe9, -3, -3, -3, -3, 0x5f),
                    // text rcx, rcx; je ...; mov rax, [rcx]; cmp rax, r10; je ...; mov {target}
                    new(new(AddressKind.Abs64),
                        0x48, 0x85, 0xc9, 0x74, -1, 
                        0x48, 0x8b, 0x01, 0x49, -1, -1, -1, -1, -1, -1, -1, -1, -1, 
                        0x49, 0x3b, 0xc2, 0x74, -1, 0x48, 0xb8, -3, -3, -3, -3, -3, -3, -3, -3)
                    // TODO: other examples
                );

            bool result = collection.TryMatchAt(new byte[] { 0xe9, 0x01, 0x23, 0x45, 0x67, 0x5f }.AsSpan(), out ulong addr1, out var matchingPattern1, out int len1);
            Assert.True(result);
            Assert.Equal(0x67452301u, addr1 & uint.MaxValue);
            Assert.Equal(6, matchingPattern1.MinLength);
            Assert.Equal(4, matchingPattern1.AddressBytes);
            Assert.Equal(6, len1);
            Assert.Equal(AddressKind.Rel32, matchingPattern1.AddressMeaning.Kind);
            Assert.Equal(5, matchingPattern1.AddressMeaning.RelativeToOffset);

            result = collection.TryFindMatch(new byte[] { 0xb8, 0x01, 0xe9, 0x11, 0x22, 0x90, 0xe8, 0x5f, 0x33, 0x44, 0x55, 0xe9, 0x66, 0x77, 0x88, 0x99 },
                out ulong addr2, out var matchingPattern2, out int offs2, out int len2);
            Assert.True(result);
            Assert.Equal(0x99887766u, addr2 & uint.MaxValue);
            Assert.Equal(16, matchingPattern2.MinLength);
            Assert.Equal(4, matchingPattern2.AddressBytes);
            Assert.Equal(16, len2);
            Assert.Equal(0, offs2);
            Assert.Equal(AddressKind.Rel32, matchingPattern2.AddressMeaning.Kind);
            Assert.Equal(16, matchingPattern2.AddressMeaning.RelativeToOffset);

            result = collection.TryFindMatch(new byte[] { 0xb8, 0x01, 0xe9, 0x11, 0x22, 0x90, 0xe8, 0x5f, 0x33, 0x44, 0x55, 0xe8, 0x66, 0x77, 0x88, 0x99 },
                out ulong addr3, out var matchingPattern3, out int offs3, out int len3);
            Assert.True(result);
            Assert.Equal(0xe8902211u, addr3 & uint.MaxValue);
            Assert.Equal(6, matchingPattern3.MinLength);
            Assert.Equal(4, matchingPattern3.AddressBytes);
            Assert.Equal(6, len3);
            Assert.Equal(2, offs3);
            Assert.Equal(AddressKind.Rel32, matchingPattern3.AddressMeaning.Kind);
            Assert.Equal(5, matchingPattern3.AddressMeaning.RelativeToOffset);
        }
    }
}
