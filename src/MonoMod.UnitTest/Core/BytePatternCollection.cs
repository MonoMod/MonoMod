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
            // -3 is addr, -2 is any repeating, -1 is any
            var collection = new BytePatternCollection(
                    // mov...; nop; call...; jmp {delta}
                    new(0xb8, -1, -1, -1, -1, 0x90, 0xe8, -1, -1, -1, -1, 0xe9, -3, -3, -3, -3),
                    // jmp {delta}; pop rdi
                    new(0xe9, -3, -3, -3, -3, 0x5f),
                    // text rcx, rcx; je ...; mov rax, [rcx]; cmp rax, r10; je ...; mov {target}
                    new(
                        0x48, 0x85, 0xc9, 0x74, -1, 
                        0x48, 0x8b, 0x01, 0x49, -1, -1, -1, -1, -1, -1, -1, -1, -1, 
                        0x49, 0x3b, 0xc2, 0x74, -1, 0x48, 0xb8, -3, -3, -3, -3, -3, -3, -3, -3)
                    // TODO: other examples
                );

            bool result = collection.TryMatchAt(new byte[] { 0xe9, 0x01, 0x23, 0x45, 0x67, 0x5f }.AsSpan(), out ulong addr1, out var matchingPattern, out int len1);

        }
    }
}
