using MonoMod.Utils;
using Xunit;
using Mono.Cecil.Cil;
using MonoMod.Core.Platforms;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Github
{
    public class Issue229 : TestBase
    {
        public Issue229(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void CalliInstructionShouldCompileWithoutException()
        {
            using DynamicMethodDefinition dmd = new("a", null, new[] { typeof(nint), typeof(RandomStruct) });
            var il = dmd.GetILProcessor();
            var c = new Mono.Cecil.CallSite(dmd.Module.ImportReference(typeof(void)));
            c.Parameters.Add(new(dmd.Module.ImportReference(typeof(RandomStruct))));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Calli, c);
            il.Emit(OpCodes.Ret);

            // This should not throw an exception
            var exception = Record.Exception(() => PlatformTriple.Current.Compile(dmd.Generate()));
            Assert.Null(exception);
        }

        struct RandomStruct { }
    }
}