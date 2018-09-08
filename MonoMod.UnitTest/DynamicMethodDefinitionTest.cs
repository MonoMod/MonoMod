using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.ModInterop;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public static class DynamicMethodDefinitionTest {
        [Test]
        public static void TestDMD() {
            Counter = 0;

            Assert.AreEqual(1, ExampleMethod(1));

            using (ModuleDefinition module = ModuleDefinition.ReadModule(Assembly.GetExecutingAssembly().Location)) {
                MethodDefinition definition = module.GetType("MonoMod.UnitTest.DynamicMethodDefinitionTest").FindMethod(nameof(ExampleMethod));

                foreach (Instruction instr in definition.Body.Instructions) {
                    if (instr.Operand as string == StringOriginal)
                        instr.Operand = StringPatched;
                }

                DynamicMethodDefinition dmd = new DynamicMethodDefinition(definition);
                Assert.AreEqual(3, dmd.Dynamic.Invoke(null, new object[] { 2 }));
            }
        }

        public const string StringOriginal = "Hello from ExampleMethod!";
        public const string StringPatched = "Hello from DynamicMethodDefinition!";

        public static int Counter;

        public static int ExampleMethod(int i) {
            Console.WriteLine(StringOriginal);
            Counter += i;
            return Counter;
        }
    }
}
