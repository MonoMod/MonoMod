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

            // Run the original method.
            Assert.AreEqual(Tuple.Create(StringOriginal, 1), ExampleMethod(1));

            // Generate the DynamicMethodDefinition from a modified MethodDefinition.
            DynamicMethodDefinition dmd;
            using (ModuleDefinition module = ModuleDefinition.ReadModule(Assembly.GetExecutingAssembly().Location)) {
                MethodDefinition definition = module.GetType("MonoMod.UnitTest.DynamicMethodDefinitionTest").FindMethod(nameof(ExampleMethod));

                foreach (Instruction instr in definition.Body.Instructions) {
                    if (instr.Operand as string == StringOriginal)
                        instr.Operand = StringPatched;
                }

                dmd = new DynamicMethodDefinition(definition);
            }

            // Run the DynamicMethod.
            Assert.AreEqual(Tuple.Create(StringPatched, 3), dmd.Dynamic.Invoke(null, new object[] { 2 }));

            // Detour the original method to the patched DynamicMethod, then run the original.
            using (new RuntimeDetour.Detour(dmd.Original, dmd)) {
                Assert.AreEqual(Tuple.Create(StringPatched, 6), ExampleMethod(3));
            }

            // Run the original method again.
            Assert.AreEqual(Tuple.Create(StringOriginal, 10), ExampleMethod(4));
        }

        public const string StringOriginal = "Hello from ExampleMethod!";
        public const string StringPatched = "Hello from DynamicMethodDefinition!";

        public static int Counter;

        public static Tuple<string, int> ExampleMethod(int i) {
            try {
                Console.WriteLine(StringOriginal);
                Counter += i;
            } catch (Exception e) {
                return Tuple.Create("", -1);
            }
            return Tuple.Create(StringOriginal, Counter);
        }
    }
}
