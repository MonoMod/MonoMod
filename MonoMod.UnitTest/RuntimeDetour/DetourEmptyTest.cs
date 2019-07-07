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

            Assert.True(DidNothing);

            using (Hook h = new Hook(
                // .GetNativeStart() to enforce a native detour.
                typeof(DetourEmptyTest).GetMethod("DoNothing"),
                new Action<DetourEmptyTest>(self => {
                    DidNothing = false;
                })
            )) {
                DoNothing();
                Assert.False(DidNothing);
            }
        }

        public void DoNothing() {
        }
        
    }
}
