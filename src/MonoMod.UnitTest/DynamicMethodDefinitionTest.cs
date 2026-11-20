extern alias New;

using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using New::MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Xunit.Abstractions;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ReflOpCodes = System.Reflection.Emit.OpCodes;

namespace MonoMod.UnitTest
{
    public class DynamicMethodDefinitionTest : TestBase
    {
        [Fact]
        public void TestDMD()
        {
            Counter = 0;

            // Run the original method.
            Assert.Equal(Tuple.Create(StringOriginal, 1), ExampleMethod(1));

            var original = typeof(DynamicMethodDefinitionTest).GetMethod(nameof(ExampleMethod), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo patched;
            using (var dmd = new DynamicMethodDefinition(original))
            {
                Assert.Equal("i", dmd.Definition.Parameters[0].Name);

                // Modify the MethodDefinition.
                foreach (var instr in dmd.Definition.Body.Instructions)
                {
                    if (instr.Operand as string == StringOriginal)
                        instr.Operand = StringPatched;
                    else if (instr.MatchCallOrCallvirt<DynamicMethodDefinitionTest>(nameof(ExampleMethod)))
                        instr.Operand = dmd.Definition;
                }

                // Generate a DynamicMethod from the modified MethodDefinition.
                patched = dmd.Generate();
            }

            // Generate an entirely new method that just returns a stack trace for further testing.
            DynamicMethod stacker;
            using (var dmd = new DynamicMethodDefinition("Stacker", typeof(StackTrace), Array.Empty<Type>()))
            {
                using (var il = new ILContext(dmd.Definition))
                    il.Invoke(_ =>
                    {
                        var c = new ILCursor(il);
                        for (var i = 0; i < 32; i++)
                            c.Emit(OpCodes.Nop);
                        c.Emit(OpCodes.Newobj, typeof(StackTrace).GetConstructor(Array.Empty<Type>()));
                        for (var i = 0; i < 32; i++)
                            c.Emit(OpCodes.Nop);
                        c.Emit(OpCodes.Ret);
                    });
                stacker = (DynamicMethod)DMDEmitDynamicMethodGenerator.Generate(dmd, null);
            }

            using (var dmd = new DynamicMethodDefinition(typeof(ExampleGenericClass<int>).GetMethod(nameof(ExampleMethod))))
            {
                Assert.Equal(0, ((Func<int>)dmd.Generate().CreateDelegate(typeof(Func<int>)))());
                Assert.Equal(0, ((Func<int>)DMDCecilGenerator.Generate(dmd).CreateDelegate(typeof(Func<int>)))());
                // no
                //Assert.Equal(dmd.Name = "SomeManualDMDName", dmd.Generate().Name);
                Counter -= 2;

                // This was indirectly provided by Pathoschild (SMAPI).
                // Microsoft.GeneratedCode can be loaded multiple times and have different contents.
                // This tries to recreate that scenario... and this is the best place to test it at the time of writing.
#if NETFRAMEWORK && true
                var abDupeA = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = "MonoMod.UnitTest.AssemblyDupe"
                    },
                    AssemblyBuilderAccess.RunAndSave
                );
                var mbDupeA = abDupeA.DefineDynamicModule($"{abDupeA.GetName().Name}.dll");
                var tbDupeA = mbDupeA.DefineType(
                    "DupeA",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
                var tDupeA = tbDupeA.CreateType();

                var abDupeB = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = abDupeA.GetName().Name
                    },
                    AssemblyBuilderAccess.RunAndSave
                );
                var mbDupeB = abDupeB.DefineDynamicModule($"{abDupeB.GetName().Name}.dll");
                var tbDupeB = mbDupeB.DefineType(
                    "DupeB",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
                var tDupeB = tbDupeB.CreateType();

                Assert.Equal(tDupeA.Assembly.FullName, tDupeB.Assembly.FullName);
                Assert.NotEqual(tDupeA.Assembly, tDupeB.Assembly);

                var trDupeA = dmd.Module.ImportReference(tDupeA);
                var trDupeB = dmd.Module.ImportReference(tDupeB);
                Assert.Equal(trDupeA.Scope.Name, trDupeB.Scope.Name);
                // "Fun" fact: both share matching AssemblyNames, so the first scope gets reused!
                // Assert.NotEqual(trDupeA.Scope, trDupeB.Scope);

                Assert.Equal(tDupeA, trDupeA.ResolveReflection());
                Assert.Equal(tDupeB, trDupeB.ResolveReflection());
#endif

            }

            // Run the DynamicMethod.
            Assert.Equal(Tuple.Create(StringPatched, 3), patched.Invoke(null, new object[] { 2 }));
            Assert.Equal(Tuple.Create(StringPatched, 3), patched.Invoke(null, new object[] { 1337 }));

            // Detour the original method to the patched DynamicMethod, then run the patched method.
            using (new Hook(original, patched))
            {
                // The detour is only active in this block.
                Assert.Equal(Tuple.Create(StringPatched, 6), ExampleMethod(3));
            }

            // Run the original method again.
            Assert.Equal(Tuple.Create(StringOriginal, 10), ExampleMethod(4));

            // Verify that we can still obtain the real DynamicMethod.
            // .NET uses a wrapping RTDynamicMethod to avoid leaking the mutable DynamicMethod.
            // Mono uses RuntimeMethodInfo without any link to the original DynamicMethod.
            var triple = PlatformTriple.Current;
            using var pin = triple.PinMethodIfNeeded(stacker);
            var stack = ((Func<StackTrace>)stacker.CreateDelegate(typeof(Func<StackTrace>)))();
            var stacked = stack.GetFrames().First(f => f.GetMethod()?.IsDynamicMethod() ?? false).GetMethod();
#if !NET8_0_OR_GREATER // .NET 8 removes RTDynamicMethod, as it was a leftover from .NET Framework CAS: https://github.com/dotnet/runtime/pull/79427
            Assert.NotEqual(stacker, stacked);
#endif
            Assert.Equal(stacker, triple.GetIdentifiable(stacked));
            Assert.Equal(triple.GetNativeMethodBody(stacker), triple.GetNativeMethodBody(stacked));
            // This will always be true on .NET and only be true on Mono if the method is still pinned.
            Assert.IsAssignableFrom<DynamicMethod>(triple.GetIdentifiable(stacked));
        }

        private const string StringOriginal = "Hello from ExampleMethod!";
        private const string StringPatched = "Hello from DynamicMethodDefinition!";

        private static volatile int Counter;

        public DynamicMethodDefinitionTest(ITestOutputHelper helper) : base(helper)
        {
        }

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable CA1031 // Do not catch general exception types

        private static Tuple<string, int> ExampleMethod(int i)
        {
            if (i == 1337)
                return ExampleMethod(0);

            _ = new TestObjectGeneric<string>();
            try
            {
                Console.WriteLine(StringOriginal);

                Counter += new int?(i).Value;
                Counter += new TestObjectInheritsGeneric();

                Console.WriteLine(new List<TestObjectGeneric<TestObject>>() { new TestObjectGeneric<TestObject>() }.GetEnumerator().Current);

                var list = new List<string>();
                list.AddRange(["A", "B", "C"]);

                var array2d1 = new string[][] { new string[] { "A" } };
                var array2d2 = new string[,] { { "B" } };
                foreach (var str in list)
                {
                    TargetTest(array2d1[0][0], array2d2[0, 0], str);
                    TargetTest(array2d1[0][0], array2d2[0, 0]);
                    TargetTest(array2d1[0][0], ref array2d2[0, 0]);
                    TargetTest<int>();
                    TargetTest<int, int>();
                }

                switch (i)
                {
                    case 0:
                        i *= -2;
                        break;
                    case 1:
                        i *= 2;
                        break;
                }

            }
            catch (Exception e) when (e is null)
            {
                return Tuple.Create("", -2);
            }
            catch (Exception)
            {
                return Tuple.Create("", -1);
            }
            return Tuple.Create(StringOriginal, Counter);
        }

        private class ExampleGenericClass<T>
        {
            public static T ExampleMethod()
            {
                Counter++;
                return default;
            }
        }

        private static int TargetTest<T>(string a, string b, string c)
        {
            return (a + b + c).GetHashCode(StringComparison.Ordinal);
        }

        private static int TargetTest(string a, string b, string c)
        {
            return (a + b + c).GetHashCode(StringComparison.Ordinal);
        }

        private static int TargetTest<T>(string a, T b)
        {
            return (a + b).GetHashCode(StringComparison.Ordinal);
        }

        private static int TargetTest<T>(string a, ref T b)
        {
            return (a + b).GetHashCode(StringComparison.Ordinal);
        }

        private static int TargetTest<TA>()
        {
            return 1;
        }

        private static int TargetTest<TA, TB>()
        {
            return 2;
        }

        [Fact]
        public void DMDUsesSuppliedReflectionImporterProvider()
        {
            var provider = new CountingImporterProvider();
            using var dmd = new DynamicMethodDefinition("Imported", typeof(string), [typeof(int)], provider);

            Assert.Equal(1, provider.Created);
            Assert.Equal(2, provider.Imported);
            Assert.Equal("System.String", dmd.Definition.ReturnType.FullName);

            dmd.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ldtoken, typeof(List<int>));
            Assert.Equal(3, provider.Imported);
        }

        private sealed class CountingImporterProvider : IReflectionImporterProvider
        {
            public int Created;
            public int Imported;

            public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
            {
                Created++;
                return new CountingImporter(this, new MMReflectionImporter(module) { UseDefault = false });
            }

            private sealed class CountingImporter(CountingImporterProvider owner, IReflectionImporter inner) : IReflectionImporter
            {
                public AssemblyNameReference ImportReference(AssemblyName name) => inner.ImportReference(name);
                public TypeReference ImportReference(Type type, IGenericParameterProvider context)
                {
                    owner.Imported++;
                    return inner.ImportReference(type, context);
                }
                public FieldReference ImportReference(FieldInfo field, IGenericParameterProvider context) => inner.ImportReference(field, context);
                public MethodReference ImportReference(MethodBase method, IGenericParameterProvider context) => inner.ImportReference(method, context);
            }
        }

        [Fact]
        public void DMDGenerateDoesNotModifyDMD()
        {
            using var origDmd = new DynamicMethodDefinition("Test DMD", null, []);
            var il = origDmd.GetILProcessor();
            var targetIns = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Br_S, targetIns);
            il.Append(targetIns);

            using var genClone = new DynamicMethodDefinition(origDmd);
            _ = genClone.Generate();

            Assert.Equal(genClone.Definition.Body.Instructions[1], genClone.Definition.Body.Instructions[0].Operand);
            Assert.Equal(OpCodes.Br_S, genClone.Definition.Body.Instructions[0].OpCode);
        }

        [Fact]
        public void ClonedDMDRelinksVariableDefinitions()
        {
            using var origDmd = new DynamicMethodDefinition("Test DMD", null, []);
            var il = origDmd.GetILProcessor();
            var varDef = new VariableDefinition(il.Import(typeof(string)));
            il.Body.Variables.Add(varDef);
            il.Emit(OpCodes.Ldstr, "hello");
            il.Emit(OpCodes.Stloc, varDef);
            il.Emit(OpCodes.Ldloc, varDef);
            il.Emit(OpCodes.Ret);

            using var genClone = new DynamicMethodDefinition(origDmd);

            Assert.NotEqual(varDef, genClone.Definition.Body.Variables[0]);
            var newVarDef = genClone.Definition.Body.Variables[0];
            Assert.Equal(newVarDef, genClone.Definition.Body.Instructions[1].Operand);
            Assert.Equal(newVarDef, genClone.Definition.Body.Instructions[2].Operand);
        }
#if NETFRAMEWORK
        // https://github.com/MonoMod/MonoMod/issues/299
        [Fact]
        public void TestSaveDMDEmitMethodBuilderGenerator()
        {
            Console.WriteLine("TestSaveDMDEmitMethodBuilderGenerator Started");
            var assemblyName = new AssemblyName($"MonoMod.UnitTest.DMDSave_{Guid.NewGuid():N}");
            var assemblyFileName = $"{assemblyName.Name}.dll";

            if (System.IO.File.Exists(assemblyFileName))
            {
                System.IO.File.Delete(assemblyFileName);
            }

            try
            {
                var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave);
                var dynamicModule = assemblyBuilder.DefineDynamicModule(assemblyName.Name, assemblyFileName);
                var typeBuilder = dynamicModule.DefineType("TestType", TypeAttributes.Public);

                using var dmd = new DynamicMethodDefinition("Test", typeof(void), []);
                var il = dmd.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ret);

                using var dmd2 = new DynamicMethodDefinition("Test2", typeof(void), [typeof(int)]);
                var il2 = dmd2.GetILGenerator();
                il2.Emit(System.Reflection.Emit.OpCodes.Ret);

                using var dmd3 = new DynamicMethodDefinition("Test3", typeof(int), []);
                var il3 = dmd3.GetILGenerator();
                il3.Emit(System.Reflection.Emit.OpCodes.Ret);

                using var dmd4 = new DynamicMethodDefinition("Test4", typeof(int), [typeof(int)]);
                var il4 = dmd4.GetILGenerator();
                il4.Emit(System.Reflection.Emit.OpCodes.Ret);

                var methodBuilder = DMDEmitMethodBuilderGenerator.GenerateMethodBuilder(dmd, typeBuilder, out _);
                Assert.NotNull(methodBuilder);
                var methodBuilder2 = DMDEmitMethodBuilderGenerator.GenerateMethodBuilder(dmd2, typeBuilder, out _);
                Assert.NotNull(methodBuilder2);
                var methodBuilder3 = DMDEmitMethodBuilderGenerator.GenerateMethodBuilder(dmd3, typeBuilder, out _);
                Assert.NotNull(methodBuilder3);
                var methodBuilder4 = DMDEmitMethodBuilderGenerator.GenerateMethodBuilder(dmd3, typeBuilder, out _);
                Assert.NotNull(methodBuilder4);

                var generatedType = typeBuilder.CreateType();
                Assert.NotNull(generatedType);

                assemblyBuilder.Save(assemblyFileName);
                Assert.True(System.IO.File.Exists(assemblyFileName));
            }
            finally
            {
                if (System.IO.File.Exists(assemblyFileName))
                {
                    System.IO.File.Delete(assemblyFileName);
                }
            }
            Console.WriteLine("TestSaveDMDEmitMethodBuilderGenerator Finished");
        }

#endif
#if NETSTANDARD2_1 || NETCOREAPP3_0_OR_GREATER || NET5_0_OR_GREATER
        [Fact]
        public void TestInterfaceDefault()
        {
            IDefaultInterfaceMethodTarget target = new DefaultInterfaceMethodTarget();

            Assert.Equal(11, target.AddTen(1));

            MethodInfo dynamicDetour;
            using (var dmd = new DynamicMethodDefinition(typeof(DynamicMethodDefinitionTest).GetMethod(nameof(DefaultInterfaceMethod_Detour), BindingFlags.Static | BindingFlags.NonPublic)))
            {
                dynamicDetour = dmd.Generate();
            }

            using (var h = new Hook(typeof(IDefaultInterfaceMethodTarget).GetMethod(nameof(IDefaultInterfaceMethodTarget.AddTen), BindingFlags.Instance | BindingFlags.Public), dynamicDetour))
            {
                Assert.Equal(42, target.AddTen(1));
            }

            Assert.Equal(11, target.AddTen(1));
        }

        private interface IDefaultInterfaceMethodTarget
        {
            int AddTen(int value) => value + 10;
        }
        private sealed class DefaultInterfaceMethodTarget : IDefaultInterfaceMethodTarget
        {
        }

        private static int DefaultInterfaceMethod_Detour(IDefaultInterfaceMethodTarget self, int value)
        {
            return value + 41;
        }
#endif

        private static DynamicMethod AddOneDM()
        {
            var dm = new DynamicMethod("AddOne", typeof(int), [typeof(int)], typeof(DynamicMethodDefinitionTest), true);
            var il = dm.GetILGenerator();
            il.Emit(ReflOpCodes.Ldarg_0);
            il.Emit(ReflOpCodes.Ldc_I4_1);
            il.Emit(ReflOpCodes.Add);
            il.Emit(ReflOpCodes.Ret);
            return dm;
        }

        [Fact]
        public void CallDynamicMethodWithSpanParameter()
        {
            var dm = new DynamicMethod("SpanLength", typeof(int), [typeof(Span<int>)], typeof(DynamicMethodDefinitionTest), true);
            var dmil = dm.GetILGenerator();
            dmil.Emit(ReflOpCodes.Ldc_I4_7);
            dmil.Emit(ReflOpCodes.Ret);

            using var dmd = new DynamicMethodDefinition("CallSpanLength", typeof(int), []);
            var span = new Mono.Cecil.Cil.VariableDefinition(dmd.Module.ImportReference(typeof(Span<int>)));
            dmd.Definition.Body.Variables.Add(span);
            var il = dmd.GetILProcessor();
            il.Emit(OpCodes.Ldloca, span);
            il.Emit(OpCodes.Initobj, dmd.Module.ImportReference(typeof(Span<int>)));
            il.Emit(OpCodes.Ldloc, span);
            il.Emit(OpCodes.Call, dmd.Module.ImportReference(dm));
            il.Emit(OpCodes.Ret);

            Assert.Equal(7, DMDCecilGenerator.Generate(dmd).Invoke(null, null));
        }

#if NETFRAMEWORK || NET9_0_OR_GREATER
        [Fact]
        public void MethodBuilderGeneratorCallsDynamicMethod()
        {
            using var dmd = new DynamicMethodDefinition("CallAddOne", typeof(int), [typeof(int)]);
            var il = dmd.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, dmd.Module.ImportReference(AddOneDM()));
            il.Emit(OpCodes.Ret);

            Assert.Equal(42, DMDEmitMethodBuilderGenerator.Generate(dmd).Invoke(null, [41]));
        }
#endif

        [Fact]
        public void CecilGeneratorCallsDynamicMethodAtBranchTarget()
        {
            // int CallAddOne(int x) => AddOne(x != 0 ? 41 : 9);
            using var dmd = new DynamicMethodDefinition("CallAddOne", typeof(int), [typeof(int)]);
            var il = dmd.GetILProcessor();
            var call = il.Create(OpCodes.Call, dmd.Module.ImportReference(AddOneDM()));
            var isTrue = il.Create(OpCodes.Ldc_I4, 41);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, isTrue);
            il.Emit(OpCodes.Ldc_I4, 9);
            il.Emit(OpCodes.Br, call);
            il.Append(isTrue);
            il.Append(call);
            il.Emit(OpCodes.Ret);
            var method = DMDCecilGenerator.Generate(dmd);

            Assert.Equal(42, method.Invoke(null, [1]));
            Assert.Equal(10, method.Invoke(null, [0]));
        }

        [Fact]
        public void DmdIgnoresAccessChecksForGenericArgs()
        {
            using var dmd = new DynamicMethodDefinition("IACTGenerics", typeof(List<PrivateType>), []);
            var il = dmd.GetILProcessor();
            il.Emit(OpCodes.Newobj, dmd.Module.ImportReference(typeof(List<PrivateType>).GetConstructor([])!));
            il.Emit(OpCodes.Ret);

            using var dmd2 = new DynamicMethodDefinition(dmd);
            using var dmd3 = new DynamicMethodDefinition(dmd);

            var method1 = DMDCecilGenerator.Generate(dmd);
            _ = Assert.IsType<List<PrivateType>>(method1.Invoke(null, []));

            var method2 = DMDEmitDynamicMethodGenerator.Generate(dmd2);
            _ = Assert.IsType<List<PrivateType>>(method2.Invoke(null, []));

#if NETFRAMEWORK || NET9_0_OR_GREATER
            var method3 = DMDEmitMethodBuilderGenerator.Generate(dmd3);
            _ = Assert.IsType<List<PrivateType>>(method3.Invoke(null, []));
#endif
        }

        [SuppressMessage("", "CA1812", Justification = "It's not supposed to be instantiated; its functionally a marker type.")]
        private sealed class PrivateType { }



        private static DynamicMethod Dm(Type ret, Type[] args, Action<ILGenerator> body)
        {
            var dm = new DynamicMethod("Target", ret, args, typeof(DynamicMethodDefinitionTest), true);
            body(dm.GetILGenerator());
            return dm;
        }

//#if NETCOREAPP2_1_OR_GREATER
        private ref struct Collide_Inner
        {
            public int X;
        }

        private static class Collide
        {
            public ref struct Inner
            {
                public int X;
            }
        }

        // 8. Delegate type names can collide: SanitizeTypeName maps '.', '+', '_' etc. all to '_', so Collide_Inner and Collide.Inner
        // get the same type and assembly name, and the name based AssemblyResolve hands back the first assembly
        // ("Object of type X cannot be converted to type X"). Fails on net8, passes on net9+ only because the dynamic
        // assembly path falls back to Cecil with a different assembly name. Two threads creating the same key at once
        // (GetOrAdd runs the factory twice) end up the same way.
        // Fix: unique names (counter/guid) for the type and the assembly.
        [Fact]
        public void CallDynamicMethodsWithSimilarlyNamedRefStructs()
        {
            Assert.Equal(1, CallWithRefStruct(typeof(Collide_Inner), 1));
            Assert.Equal(2, CallWithRefStruct(typeof(Collide.Inner), 2));
        }

        private static object? CallWithRefStruct(Type refStruct, int value)
        {
            var dm = Dm(typeof(int), [refStruct], il =>
            {
                il.Emit(ReflOpCodes.Ldc_I4, value);
                il.Emit(ReflOpCodes.Ret);
            });

            using var dmd = new DynamicMethodDefinition("CallRefStruct", typeof(int), []);
            var local = new Mono.Cecil.Cil.VariableDefinition(dmd.Module.ImportReference(refStruct));
            dmd.Definition.Body.Variables.Add(local);
            var il = dmd.GetILProcessor();
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Initobj, dmd.Module.ImportReference(refStruct));
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Call, dmd.Module.ImportReference(dm));
            il.Emit(OpCodes.Ret);
            return DMDCecilGenerator.Generate(dmd).Invoke(null, null);
        }
//#endif


    }
}
