extern alias New;
using New::MonoMod.RuntimeDetour;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class ILHookPoisonTest : TestBase
    {
        public ILHookPoisonTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestRecoveryFromInvalidProgram()
        {
            Assert.True(ReturnTrue());

            var method = new Func<bool>(ReturnTrue).Method;

            Assert.Throws<InvalidProgramException>(() =>
            {
                using (new ILHook(method, il => new ILCursor(il).Emit(OpCodes.Pop)))
                    Assert.True(ReturnTrue());
            });

            Assert.True(ReturnTrue());

            using (new ILHook(method, il => new ILCursor(il).Remove().EmitLdcI4(0)))
                Assert.False(ReturnTrue());

            Assert.True(ReturnTrue());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal bool ReturnTrue() => true;

    }
}
