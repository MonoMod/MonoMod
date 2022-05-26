#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public unsafe class IssueMMC11Test {

        [Fact(Skip = "New RuntimeDetour doesn't expose a .Pin(). That is instead managed entirely in Core, entirely transparently.")]
        public void TestIssueMMC11() {
            MethodInfo method = typeof(TestStruct).GetMethod("TestMethod");
            method.Pin();
            try {
                Assert.NotEqual(IntPtr.Zero, method.GetNativeStart());
            } finally {
                method.Unpin();
            }
        }

        public struct TestStruct {
            public string Value;
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void TestMethod(string val) {
                Value = val;
            }
        }

    }
}
