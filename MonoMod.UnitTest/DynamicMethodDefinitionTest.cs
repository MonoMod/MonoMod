using Xunit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.ModInterop;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Text;
using MonoMod.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace MonoMod.UnitTest {
    public class DynamicMethodDefinitionTest {
        [Fact]
        public void TestDMD() {
            Counter = 0;

            // Run the original method.
            Assert.Equal(Tuple.Create(StringOriginal, 1), ExampleMethod(1));

            MethodInfo original = typeof(DynamicMethodDefinitionTest).GetMethod(nameof(ExampleMethod));
            MethodBase patched;
            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(original)) {
                Assert.Equal("i", dmd.Definition.Parameters[0].Name);

                // Modify the MethodDefinition.
                foreach (Instruction instr in dmd.Definition.Body.Instructions) {
                    if (instr.Operand as string == StringOriginal)
                        instr.Operand = StringPatched;
                }

                // Generate a DynamicMethod from the modified MethodDefinition.
                patched = dmd.Generate();
            }

            // Generate an entirely new method that just throws.
            DynamicMethod thrower;
            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition("Thrower", typeof(void), new Type[0])) {
                using (ILContext il = new ILContext(dmd.Definition))
                    il.Invoke(_ => {
                        ILCursor c = new ILCursor(il);
                        c.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor(new Type[0]));
                        c.Emit(OpCodes.Throw);
                    });
                thrower = (DynamicMethod) DMDEmitDynamicMethodGenerator.Generate(dmd, null);
            }

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(typeof(ExampleGenericClass<int>).GetMethod(nameof(ExampleMethod)))) {
                Assert.Equal(0, ((Func<int>) dmd.Generate().CreateDelegate(typeof(Func<int>)))());
                Assert.Equal(0, ((Func<int>) DMDCecilGenerator.Generate(dmd).CreateDelegate(typeof(Func<int>)))());
                Assert.Equal(dmd.Name = "SomeManualDMDName", dmd.Generate().Name);
                Counter -= 2;

                // This was indirectly provided by Pathoschild (SMAPI).
                // Microsoft.GeneratedCode can be loaded multiple times and have different contents.
                // This tries to recreate that scenario... and this is the best place to test it at the time of writing.
#if NETFRAMEWORK && true
                AssemblyBuilder abDupeA = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = "MonoMod.UnitTest.AssemblyDupe"
                    },
                    AssemblyBuilderAccess.RunAndSave
                );
                ModuleBuilder mbDupeA = abDupeA.DefineDynamicModule($"{abDupeA.GetName().Name}.dll");
                TypeBuilder tbDupeA = mbDupeA.DefineType(
                    "DupeA",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
                Type tDupeA = tbDupeA.CreateType();

                AssemblyBuilder abDupeB = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = abDupeA.GetName().Name
                    },
                    AssemblyBuilderAccess.RunAndSave
                );
                ModuleBuilder mbDupeB = abDupeB.DefineDynamicModule($"{abDupeB.GetName().Name}.dll");
                TypeBuilder tbDupeB = mbDupeB.DefineType(
                    "DupeB",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
                Type tDupeB = tbDupeB.CreateType();

                Assert.Equal(tDupeA.Assembly.FullName, tDupeB.Assembly.FullName);
                Assert.NotEqual(tDupeA.Assembly, tDupeB.Assembly);

                TypeReference trDupeA = dmd.Module.ImportReference(tDupeA);
                TypeReference trDupeB = dmd.Module.ImportReference(tDupeB);
                Assert.Equal(trDupeA.Scope.Name, trDupeB.Scope.Name);
                // "Fun" fact: both share matching AssemblyNames, so the first scope gets reused!
                // Assert.NotEqual(trDupeA.Scope, trDupeB.Scope);

                Assert.Equal(tDupeA, trDupeA.ResolveReflection());
                Assert.Equal(tDupeB, trDupeB.ResolveReflection());
#endif

            }

            // Run the DynamicMethod.
            Assert.Equal(Tuple.Create(StringPatched, 3), patched.Invoke(null, new object[] { 2 }));

            // Detour the original method to the patched DynamicMethod, then run the patched method.
            using (new Detour(original, patched)) {
                // The detour is only active in this block.
                Assert.Equal(Tuple.Create(StringPatched, 6), ExampleMethod(3));
            }

            // Run the original method again.
            Assert.Equal(Tuple.Create(StringOriginal, 10), ExampleMethod(4));

            // Verify that we can still obtain the real DynamicMethod.
            // .NET uses a wrapping RTDynamicMethod to avoid leaking the mutable DynamicMethod.
            // Mono uses RuntimeMethodInfo without any link to the original DynamicMethod.
            if (Type.GetType("Mono.Runtime") != null)
                thrower.Pin();
            Exception thrown = Assert.Throws<Exception>(thrower.CreateDelegate(typeof(Action)) as Action);
            Assert.NotEqual(thrower, thrown.TargetSite);
            Assert.Equal(thrower, thrown.TargetSite.GetIdentifiable());
            Assert.Equal(thrower.GetNativeStart(), thrown.TargetSite.GetNativeStart());
            // This will always be true on .NET and only be true on Mono if the method is still pinned.
            Assert.IsAssignableFrom<DynamicMethod>(thrown.TargetSite.GetIdentifiable());
            if (Type.GetType("Mono.Runtime") != null)
                thrower.Unpin();
        }

        public const string StringOriginal = "Hello from ExampleMethod!";
        public const string StringPatched = "Hello from DynamicMethodDefinition!";

        public static volatile int Counter;

        public static Tuple<string, int> ExampleMethod(int i) {
            TestObjectGeneric<string> test = new TestObjectGeneric<string>();
            try {
                Console.WriteLine(StringOriginal);

                Counter += new int?(i).Value;
                Counter += new TestObjectInheritsGeneric();

                Console.WriteLine(new List<TestObjectGeneric<TestObject>>() { new TestObjectGeneric<TestObject>() }.GetEnumerator().Current);

                List<string> list = new List<string>();
                list.AddRange(new string[] { "A", "B", "C" });

                string[][] array2d1 = new string[][] { new string[] { "A" } };
                string[,] array2d2 = new string[,] { { "B" } };
                foreach (string str in list) {
                    TargetTest(array2d1[0][0], array2d2[0, 0], str);
                    TargetTest(array2d1[0][0], array2d2[0, 0]);
                    TargetTest(array2d1[0][0], ref array2d2[0, 0]);
                    TargetTest<int>();
                    TargetTest<int, int>();
                }

                switch (i) {
                    case 0:
                        i *= -2;
                        break;
                    case 1:
                        i *= 2;
                        break;
                }

            } catch (Exception e) when (e == null) {
                return Tuple.Create("", -2);
            } catch (Exception) {
                return Tuple.Create("", -1);
            }
            return Tuple.Create(StringOriginal, Counter);
        }

        public class ExampleGenericClass<T> {
            public static T ExampleMethod() {
                Counter++;
                return default;
            }
        }

        public static int TargetTest<T>(string a, string b, string c) {
            return (a + b + c).GetHashCode();
        }

        public static int TargetTest(string a, string b, string c) {
            return (a + b + c).GetHashCode();
        }

        public static int TargetTest<T>(string a, T b) {
            return (a + b).GetHashCode();
        }

        public static int TargetTest<T>(string a, ref T b) {
            return (a + b).GetHashCode();
        }

        public static int TargetTest<TA>() {
            return 1;
        }

        public static int TargetTest<TA, TB>() {
            return 2;
        }

    }
}
