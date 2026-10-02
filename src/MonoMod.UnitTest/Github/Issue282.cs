using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Github
{
    [CollectionDefinition(nameof(Issue282), DisableParallelization = true)]
    [Collection(nameof(Issue282))]
    public class Issue282 : TestBase
    {
        public Issue282(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void DMDGenerateDoesNotOverrideParameters()
        {
            using var dmd = new DynamicMethodDefinition(((Delegate)func).Method);
            // modify to func(a, b) => a + b;
            dmd.Definition.Parameters.Add(new(dmd.Module.TypeSystem.Int32));
            using (var il = new ILContext(dmd.Definition))
            {
                var c = new ILCursor(il);
                c.Index += 1;
                c.Emit(OpCodes.Ldarg_1);
                c.Emit(OpCodes.Add);
            }

            Assert.Equal(1, func(1));
            Test("cecil");
            Test("dynamicmethod");
#if NETFRAMEWORK
            Test("methodbuilder");
#endif

            static int func(int a) => a;
            void Test(string dmdtype)
            {
                try
                {
                    Switches.SetSwitchValue(Switches.DMDType, dmdtype);
                    var newfunc = dmd.Generate().CreateDelegate<Func<int, int, int>>();
                    Assert.Equal(3, newfunc(1, 2));
                }
                finally
                {
                    Switches.ClearSwitchValue(Switches.DMDType);
                }
            }
        }
    }
}