using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Xunit.Abstractions;
using CecilOpCodes = Mono.Cecil.Cil.OpCodes;

namespace MonoMod.UnitTest
{
    public class DuplicateFieldResolutionTest : TestBase
    {
        public DuplicateFieldResolutionTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDuplicateFieldNameResolutionUsesFieldType()
        {
            (FieldInfo expectedField, MethodInfo restartMethod) = CreateTargetType();
            expectedField.SetValue(null, new Stopwatch());
            restartMethod.Invoke(null, null);

            using var dmd = new DynamicMethodDefinition(restartMethod);
            FieldReference fieldReference = (FieldReference)dmd.Definition.Body.Instructions
                .Single(instruction => instruction.OpCode == CecilOpCodes.Ldsfld)
                .Operand;

            AssertSameField(expectedField, fieldReference.ResolveReflection());

            MethodInfo generated = DMDEmitDynamicMethodGenerator.Generate(dmd, null);
            var restart = (Action)generated.CreateDelegate(typeof(Action));
            restart();
        }

        [Fact]
        public void TestGenericFieldTypeIsResolvedInDeclaringTypeContext()
        {
            _ = new GenericFieldTarget<int>();
            FieldInfo expectedField = typeof(GenericFieldTarget<int>).GetField(
                nameof(GenericFieldTarget<int>.Value))!;
            using var dmd = new DynamicMethodDefinition("GenericField", typeof(void), Type.EmptyTypes);
            FieldReference fieldReference = dmd.Module.ImportReference(expectedField);

            AssertSameField(expectedField, fieldReference.ResolveReflection());
        }

        [Fact]
        public void TestGenericFieldsRemainDistinctAfterTypeSubstitution()
        {
            (FieldInfo genericField, FieldInfo integerField) = CreateGenericTargetFields();
            using var dmd = new DynamicMethodDefinition("GenericFields", typeof(void), Type.EmptyTypes);

            AssertSameField(
                genericField,
                dmd.Module.ImportReference(genericField).ResolveReflection());
            AssertSameField(
                integerField,
                dmd.Module.ImportReference(integerField).ResolveReflection());
        }

        private static (FieldInfo StopwatchField, MethodInfo RestartMethod) CreateTargetType()
        {
            var assemblyName = new AssemblyName(
                $"MonoMod.UnitTest.DuplicateFields.{Guid.NewGuid():N}");
#if NETFRAMEWORK
            AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
#else
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
#endif
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name!);
            TypeBuilder type = module.DefineType(
                "DuplicateFieldTarget",
                System.Reflection.TypeAttributes.Public |
                System.Reflection.TypeAttributes.Abstract |
                System.Reflection.TypeAttributes.Sealed);
            _ = type.DefineField("B", typeof(float), System.Reflection.FieldAttributes.Public);
            FieldBuilder stopwatchField = type.DefineField(
                "B",
                typeof(Stopwatch),
                System.Reflection.FieldAttributes.Public |
                System.Reflection.FieldAttributes.Static);
            MethodBuilder restartMethod = type.DefineMethod(
                "Restart",
                System.Reflection.MethodAttributes.Public |
                System.Reflection.MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            ILGenerator il = restartMethod.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, stopwatchField);
            il.Emit(
                System.Reflection.Emit.OpCodes.Callvirt,
                typeof(Stopwatch).GetMethod(nameof(Stopwatch.Restart), Type.EmptyTypes)!);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            Type targetType = type.CreateType()!;
            FieldInfo expectedField = targetType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Single(field => field.Name == "B" && field.FieldType == typeof(Stopwatch));
            MethodInfo generatedRestart = targetType.GetMethod(
                "Restart",
                BindingFlags.Public | BindingFlags.Static)!;
            return (expectedField, generatedRestart);
        }

        private static (FieldInfo GenericField, FieldInfo IntegerField) CreateGenericTargetFields()
        {
            var assemblyName = new AssemblyName(
                $"MonoMod.UnitTest.GenericDuplicateFields.{Guid.NewGuid():N}");
#if NETFRAMEWORK
            AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
#else
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
#endif
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name!);
            TypeBuilder type = module.DefineType(
                "GenericDuplicateFieldTarget",
                System.Reflection.TypeAttributes.Public);
            GenericTypeParameterBuilder genericParameter = type.DefineGenericParameters("T")[0];
            _ = type.DefineField("B", genericParameter, System.Reflection.FieldAttributes.Public);
            _ = type.DefineField("B", typeof(int), System.Reflection.FieldAttributes.Public);

            Type closedType = type.CreateType()!.MakeGenericType(typeof(int));
            FieldInfo[] fields = closedType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            return (
                fields.Single(field =>
                    field.Module.ResolveField(field.MetadataToken)!.FieldType.IsGenericParameter),
                fields.Single(field =>
                    field.Module.ResolveField(field.MetadataToken)!.FieldType == typeof(int)));
        }

        private static void AssertSameField(FieldInfo expected, FieldInfo actual)
        {
            Assert.Same(expected.Module, actual.Module);
            Assert.Equal(expected.MetadataToken, actual.MetadataToken);
        }

        private sealed class GenericFieldTarget<T>
        {
#pragma warning disable CS0649 // Used through reflection.
            public T? Value;
#pragma warning restore CS0649
        }
    }
}
