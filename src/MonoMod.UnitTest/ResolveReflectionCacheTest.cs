using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public class ResolveReflectionCacheTest : TestBase
    {
        public ResolveReflectionCacheTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void MethodOnGenericInstanceOfDuplicateAssemblyTypesResolvesPerCopy()
        {
            var dupeA = DefineDupe();
            var dupeB = DefineDupe();
            Assert.Equal(dupeA.Assembly.FullName, dupeB.Assembly.FullName);
            Assert.NotEqual(dupeA.Assembly, dupeB.Assembly);

            using var module = ModuleDefinition.CreateModule("ResolveReflectionCacheTest", new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                ReflectionImporterProvider = MMReflectionImporter.ProviderNoDefault,
            });

            var getEnumeratorA = module.ImportReference(typeof(List<>).MakeGenericType(dupeA).GetMethod("GetEnumerator")!);
            var getEnumeratorB = module.ImportReference(typeof(List<>).MakeGenericType(dupeB).GetMethod("GetEnumerator")!);

            var resolvedA = (MethodInfo)getEnumeratorA.ResolveReflection();
            var resolvedB = (MethodInfo)getEnumeratorB.ResolveReflection();

            Assert.Equal(dupeA, resolvedA.DeclaringType!.GetGenericArguments()[0]);
            Assert.Equal(dupeB, resolvedB.DeclaringType!.GetGenericArguments()[0]);
        }

        private static Type DefineDupe()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("MonoMod.UnitTest.ResolveCacheDupe"),
                AssemblyBuilderAccess.Run);
            var moduleBuilder = assembly.DefineDynamicModule("MonoMod.UnitTest.ResolveCacheDupe.dll");
            var typeBuilder = moduleBuilder.DefineType("Dupe", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
            return typeBuilder.CreateType()!;
        }
    }
}
