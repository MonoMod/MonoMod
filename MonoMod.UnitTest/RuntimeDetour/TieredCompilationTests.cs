using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MonoMod.UnitTest {
    public class TieredCompilationTests {

        private static bool TargetHit = false;

        //
        // So, it turns out that methods are only eligible for recompilation if they are not in an assembly
        //   marked debuggable. In other words, this test will only ever fail when this assembly is built
        //   in release mode. In debug mode, it will never fail.
        //

        [Fact]
        public void WithTieredCompilation() {
            using (new Detour(() => From(), () => To())) {
                TestFrom();
            }
        }

        private static void TestFrom() {
            for (int loop = 0; loop < 5; loop++) {
                // first we make sure From qualifies for recomp
                for (int i = 0; i < 1000; i++) {
                    TargetHit = false;
                    From();
                    Assert.True(TargetHit, $"iteration {i} of loop {loop}");
                }
                // then we wait for it
                Thread.Sleep(1000);
                // and then try again
            }
        }

        private static void From() {
            TargetHit = false;
        }

        private static void To() {
            TargetHit = true;
        }
    }
}
