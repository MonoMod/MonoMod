#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class DetourEmptyTest : TestBase
    {
        private bool DidNothing = true;

        public DetourEmptyTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDetoursEmpty()
        {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            //System.Threading.Thread.Sleep(10000);
            //System.Diagnostics.Debugger.Break();

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);

            using (var h = new Hook(
                typeof(DetourEmptyTest).GetMethod("DoNothing"),
                new Action<DetourEmptyTest>(self =>
                {
                    DidNothing = false;
                })
            ))
            {
                DidNothing = true;
                DoNothing();
                Assert.False(DidNothing);
            }

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DoNothing()
        {
        }

    }
}
