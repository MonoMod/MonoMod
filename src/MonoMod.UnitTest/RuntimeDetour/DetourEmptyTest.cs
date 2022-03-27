#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Reflection.Emit;
using System.Text;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class DetourEmptyTest {
        private bool DidNothing = true;

        [Fact]
        public void TestDetoursEmpty() {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);

            using (Hook h = new Hook(
                typeof(DetourEmptyTest).GetMethod("DoNothing"),
                new Action<DetourEmptyTest>(self => {
                    DidNothing = false;
                })
            )) {
                DidNothing = true;
                DoNothing();
                Assert.False(DidNothing);
            }

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DoNothing() {
        }
        
    }
}
