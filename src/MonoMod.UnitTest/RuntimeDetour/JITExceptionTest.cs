using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public class JitExceptionTest : TestBase
    {
        public JitExceptionTest(ITestOutputHelper helper) : base(helper) { }

        [Fact]
        public void TestJitExceptions()
        {
            // The JIT (on FW/CoreCLR) will propagate exceptions into the EE when it tries to reference a method/field that doesn't exist.
            // On Linux/MaxOS, exceptions cannot propagate across P/Invoke boundaries by default, so we use INativeExceptionHelper to create
            // wrappers which catch and rethrow the exceptions as appropriate.

            // make sure that the JIT hook is installed, if applicable
            Assert.NotNull(PlatformTriple.Current);


            using var dmd = new DynamicMethodDefinition(nameof(TestJitExceptions), typeof(void), ArrayEx.Empty<Type?>());
            var il = dmd.GetILProcessor();
            var module = dmd.Module!;
            var method = dmd.Definition;

            // we'll load a nonexistent field
            var typeref = module.ImportReference(typeof(JitExceptionTest));
            var fieldref = new FieldReference("NonExistentField", typeref, typeref);
            il.Emit(OpCodes.Ldsfld, fieldref);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            // it'll throw in here
            try
            {
                // to generate, we need to NOT use the DynamicMethod backend, because that will fail in generation
                DMDCecilGenerator.Generate(dmd).CreateDelegate<Action>()();
            }
            catch (MissingFieldException)
            {
                // all is good :)
            }

            // if the test fails, the runtime crashes...
        }
    }
}