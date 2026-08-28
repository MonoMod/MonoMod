using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
            string assemblyName = $"MonoMod.UnitTest.DuplicateFields.{Guid.NewGuid():N}";
            using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0)),
                assemblyName,
                ModuleKind.Dll);
            ModuleDefinition module = assembly.MainModule;
            var type = new TypeDefinition(
                string.Empty,
                "DuplicateFieldTarget",
                Mono.Cecil.TypeAttributes.Public |
                Mono.Cecil.TypeAttributes.Abstract |
                Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(type);
            type.Fields.Add(new FieldDefinition(
                "B",
                Mono.Cecil.FieldAttributes.Public,
                module.TypeSystem.Single));
            var stopwatchField = new FieldDefinition(
                "B",
                Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static,
                module.ImportReference(typeof(Stopwatch)));
            type.Fields.Add(stopwatchField);
            var restartMethod = new MethodDefinition(
                "Restart",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            type.Methods.Add(restartMethod);
            var il = restartMethod.Body.GetILProcessor();
            il.Emit(CecilOpCodes.Ldsfld, stopwatchField);
            il.Emit(
                CecilOpCodes.Callvirt,
                module.ImportReference(typeof(Stopwatch).GetMethod(
                    nameof(Stopwatch.Restart),
                    Type.EmptyTypes)!));
            il.Emit(CecilOpCodes.Ret);

            Type targetType = LoadAssembly(assembly).GetType("DuplicateFieldTarget")!;
            FieldInfo expectedField = targetType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Single(field => field.Name == "B" && field.FieldType == typeof(Stopwatch));
            MethodInfo generatedRestart = targetType.GetMethod(
                "Restart",
                BindingFlags.Public | BindingFlags.Static)!;
            return (expectedField, generatedRestart);
        }

        private static (FieldInfo GenericField, FieldInfo IntegerField) CreateGenericTargetFields()
        {
            string assemblyName = $"MonoMod.UnitTest.GenericDuplicateFields.{Guid.NewGuid():N}";
            using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0)),
                assemblyName,
                ModuleKind.Dll);
            ModuleDefinition module = assembly.MainModule;
            var type = new TypeDefinition(
                string.Empty,
                "GenericDuplicateFieldTarget`1",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(type);
            var genericParameter = new GenericParameter("T", type);
            type.GenericParameters.Add(genericParameter);
            type.Fields.Add(new FieldDefinition(
                "B",
                Mono.Cecil.FieldAttributes.Public,
                genericParameter));
            type.Fields.Add(new FieldDefinition(
                "B",
                Mono.Cecil.FieldAttributes.Public,
                module.TypeSystem.Int32));

            Type genericType = LoadAssembly(assembly).GetType("GenericDuplicateFieldTarget`1")!;
            Type closedType = genericType.MakeGenericType(typeof(int));
            FieldInfo[] fields = closedType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            return (
                fields.Single(field =>
                    field.Module.ResolveField(field.MetadataToken)!.FieldType.IsGenericParameter),
                fields.Single(field =>
                    field.Module.ResolveField(field.MetadataToken)!.FieldType == typeof(int)));
        }

        private static Assembly LoadAssembly(AssemblyDefinition assembly)
        {
            using var stream = new MemoryStream();
            assembly.Write(stream);
            return Assembly.Load(stream.ToArray());
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
