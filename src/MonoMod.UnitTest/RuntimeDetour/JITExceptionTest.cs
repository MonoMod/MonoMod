

using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest {
    public class JITExceptionTest : TestBase {
        public JITExceptionTest(ITestOutputHelper helper) : base(helper) { }

        [Fact]
        public void TestJITExceptions() {
            // The JIT (on FW/CoreCLR) will propagate exceptions into the EE when it tries to reference a method/field that doesn't exist.
            // On Linux/MaxOS, exceptions cannot propagate across P/Invoke boundaries by default, so we use INativeExceptionHelper to create
            // wrappers which catch and rethrow the exceptions as appropriate.

            using var dmd = new DynamicMethodDefinition(nameof(TestJITExceptions), typeof(void), ArrayEx.Empty<Type?>());
            var il = dmd.GetILProcessor();
            var module = dmd.Module!;
            var method = dmd.Definition;
            
            // we'll load a nonexistent field
            var typeref = module.ImportReference(typeof(JITExceptionTest));
            var fieldref = new FieldReference("NonExistentField", typeref, typeref);
            il.Emit(OpCodes.Ldsfld, fieldref);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            
            // it'll throw in here
            try {
                // to generate, we need to NOT use the DynamicMethod backend, because that will fail in generation
                DMDCecilGenerator.Generate(dmd).CreateDelegate<Action>()();
            } catch (MissingFieldException) {
                // all is good :)
            }
            
            // if the test fails, the runtime crashes...
        }
    }
}