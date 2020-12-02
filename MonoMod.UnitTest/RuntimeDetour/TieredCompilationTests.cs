using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

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
            if (Environment.GetEnvironmentVariable("MM_TEST_DEBUG_TIERED_COMP") != null) {
                Console.WriteLine("Attach the debugger then press enter");
                Console.ReadLine();
            }

            using (new Detour(() => From(), () => To())) {
                TestFrom();
            }
        }

        private static void TestFrom() {
            Stopwatch sw = new Stopwatch();
            for (int loop = 0; loop < 5; loop++) {
                // first we make sure From qualifies for recomp
                for (int i = 0; i < 1000; i++) {
                    TargetHit = false;
                    From();
                    if (!TargetHit) {
                        Assert.True(TargetHit, $"iteration {i} of loop {loop}");
                    }
                }
                // then we wait for it by spinning
                sw.Start();
                while (sw.ElapsedMilliseconds < 100)
                    Empty();
                sw.Reset();
                // and then try again
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Empty() { }

        // Importantly, the quick JIT is disabled when it contains loops (in 3, seemingly not in 5)
        private static void From() {
            Empty();
            Empty();
            Empty();
            Empty();
            Empty();
            TargetHit = false;
            Empty();
            Empty();
            Empty();
            Empty();
            Empty();
        }

        private static void To() {
            Empty();
            Empty();
            Empty();
            Empty();
            Empty();
            TargetHit = true;
            Empty();
            Empty();
            Empty();
            Empty();
            Empty();
        }
    }
}
