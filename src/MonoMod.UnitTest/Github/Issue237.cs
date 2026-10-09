using MonoMod.Utils;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using Xunit;
using Xunit.Abstractions;
using System.Linq;

namespace MonoMod.UnitTest.Github
{
    public class Issue237 : TestBase
    {
        public Issue237(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void EmitBrShouldNotCreateSelfReferencingBranch()
        {
            // Create a method to modify
            var dmd = new DynamicMethodDefinition("TestMethod", typeof(void), System.Type.EmptyTypes);
            var il = new ILContext(dmd.Definition);
            var c = new ILCursor(il);

            // Emit some initial instruction
            c.Emit(OpCodes.Nop);

            // Attempt to create a branch to the next instruction (which should not create a self-reference)
            c.EmitBr(c.Next!);

            // Verify the resulting IL does not contain a self-referencing branch
            var instructions = dmd.Definition.Body.Instructions.ToArray();

            Assert.True(instructions.Length >= 2, "Should have at least two instructions");
            
            var branchInstr = instructions.FirstOrDefault(i => i.OpCode == OpCodes.Br);
            Assert.NotNull(branchInstr);
            Assert.False(ReferenceEquals(branchInstr.Operand, branchInstr), "Branch should not be to itself");
        }
    }
}