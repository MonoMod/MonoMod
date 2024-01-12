using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using MonoMod.HookGen.V2;

namespace MonoMod.Roslyn.UnitTests.HookGen.V2 {
#pragma warning disable IDE0065
#pragma warning restore IDE0065

    public partial class HelperGeneratorTests {
        public static class TestClass {
            public static void Single() {

            }

            public static void Overloaded() {

            }

            public static void Overloaded(int i) {

            }
        }
    }
}
