extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class ReturnBufferSysVTest : TestBase {
        public ReturnBufferSysVTest(ITestOutputHelper helper) : base(helper) { }

        [Fact]
        public void TestReturnBufferDetour() {
            Assert.True(Source(0, 0, 0, 0) is { f1: 1, f2: 2, f3: 3 });

            using var hook = new Hook(
                typeof(ReturnBufferSysVTest).GetMethod("Source")!,
                typeof(ReturnBufferSysVTest).GetMethod("Target")!,
                true
            );
            
            Assert.True(Source(0, 0, 0, 0) is { f1: 4, f2: 5, f3: 6 });
        }

        public struct TestStruct {
            public ulong f1, f2, f3; // 24 bytes
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public TestStruct Source(int a, int b, int c, int d) {
            return new TestStruct() { f1 = 1, f2 = 2, f3 = 3 };
        }

        public static TestStruct Target(Func<ReturnBufferSysVTest, int, int, int, int, TestStruct> orig, ReturnBufferSysVTest self, int a, int b, int c, int d) {
            var s = orig(self, a, b, c, d);
            s.f1 += 3;
            s.f2 += 3;
            s.f3 += 3;
            return s;
        }
    }
}
