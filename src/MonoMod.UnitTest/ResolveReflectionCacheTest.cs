using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [CollectionDefinition(nameof(ResolveReflectionCacheTest), DisableParallelization = true)]
    [Collection(nameof(ResolveReflectionCacheTest))]
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

        [Fact]
        public void ResolveWhileAssembliesAreListed()
        {
            using var stop = new CancellationTokenSource();
            var listers = new Thread[4];
            for (var i = 0; i < listers.Length; i++)
            {
                listers[i] = new Thread(() =>
                {
                    while (!stop.IsCancellationRequested)
                        _ = AppDomain.CurrentDomain.GetAssemblies();
                });
                listers[i].Start();
            }
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    var name = "MonoMod.UnitTest.ResolveWhileListing" + i;
                    var type = DefineDupe(name);

                    using var module = ModuleDefinition.CreateModule("ResolveReflectionCacheTest", ModuleKind.Dll);
                    var typeRef = new TypeReference("", "Dupe", module, new AssemblyNameReference(name, new Version(0, 0, 0, 0)));
                    try
                    {
                        Assert.Equal(type, typeRef.ResolveReflection());
                    }
                    catch (FileNotFoundException) when (PlatformDetection.Runtime is RuntimeKind.Mono)
                    {
                        // listing assemblies while one is defined can make Mono lose its AssemblyBuilder, which can't be resolved then
                    }
                }
            }
            finally
            {
                stop.Cancel();
                foreach (var lister in listers)
                    lister.Join();
            }
        }

        private static Type DefineDupe(string name = "MonoMod.UnitTest.ResolveCacheDupe")
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(name),
                AssemblyBuilderAccess.Run);
            var moduleBuilder = assembly.DefineDynamicModule(name + ".dll");
            var typeBuilder = moduleBuilder.DefineType("Dupe", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
            return typeBuilder.CreateType()!;
        }
    }
}
