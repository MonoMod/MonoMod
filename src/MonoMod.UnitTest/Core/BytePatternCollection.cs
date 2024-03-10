using MonoMod.Core.Utils;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    public class BytePatternCollectionTests : TestBase
    {
        public BytePatternCollectionTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestBytePattern()
        {

            const ushort An = BytePattern.SAnyValue;
            const ushort Ad = BytePattern.SAddressValue;

            var collection = new BytePatternCollection(
                    // mov...; nop; call...; jmp {delta}
                    new(new(AddressKind.Rel32, 16), 0xb8, An, An, An, An, 0x90, 0xe8, An, An, An, An, 0xe9, Ad, Ad, Ad, Ad),
                    // jmp {delta}; pop rdi
                    new(new(AddressKind.Rel32, 5), 0xe9, Ad, Ad, Ad, Ad, 0x5f),
                    // text rcx, rcx; je ...; mov rax, [rcx]; cmp rax, r10; je ...; mov {target}
                    new(new(AddressKind.Abs64),
                        0x48, 0x85, 0xc9, 0x74, An,
                        0x48, 0x8b, 0x01, 0x49, An, An, An, An, An, An, An, An, An,
                        0x49, 0x3b, 0xc2, 0x74, An, 0x48, 0xb8, Ad, Ad, Ad, Ad, Ad, Ad, Ad, Ad)
                // TODO: other examples
                );

            var result = collection.TryMatchAt(new byte[] { 0xe9, 0x01, 0x23, 0x45, 0x67, 0x5f }.AsSpan(), out var addr1, out var matchingPattern1, out var len1);
            Assert.True(result);
            Assert.Equal(0x67452301u, addr1 & uint.MaxValue);
            Assert.Equal(6, matchingPattern1.MinLength);
            Assert.Equal(4, matchingPattern1.AddressBytes);
            Assert.Equal(6, len1);
            Assert.Equal(AddressKind.Rel32, matchingPattern1.AddressMeaning.Kind);
            Assert.Equal(5, matchingPattern1.AddressMeaning.RelativeToOffset);

            result = collection.TryFindMatch(new byte[] { 0xb8, 0x01, 0xe9, 0x11, 0x22, 0x90, 0xe8, 0x5f, 0x33, 0x44, 0x55, 0xe9, 0x66, 0x77, 0x88, 0x99 },
                out var addr2, out var matchingPattern2, out var offs2, out var len2);
            Assert.True(result);
            Assert.Equal(0x99887766u, addr2 & uint.MaxValue);
            Assert.Equal(16, matchingPattern2.MinLength);
            Assert.Equal(4, matchingPattern2.AddressBytes);
            Assert.Equal(16, len2);
            Assert.Equal(0, offs2);
            Assert.Equal(AddressKind.Rel32, matchingPattern2.AddressMeaning.Kind);
            Assert.Equal(16, matchingPattern2.AddressMeaning.RelativeToOffset);

            result = collection.TryFindMatch(new byte[] { 0xb8, 0x01, 0xe9, 0x11, 0x22, 0x90, 0xe8, 0x5f, 0x33, 0x44, 0x55, 0xe8, 0x66, 0x77, 0x88, 0x99 },
                out var addr3, out var matchingPattern3, out var offs3, out var len3);
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
