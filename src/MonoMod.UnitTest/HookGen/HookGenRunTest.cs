#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Mono.Cecil;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("HookGen")]
    public class HookGenRunTest : TestBase
    {
        public HookGenRunTest(ITestOutputHelper helper) : base(helper)
        {
        }

        // TODO: re-enable when HookGen uses new RuntimeDetour
        [Fact(Skip = "HookGen still uses old RuntimeDetour")]
        public void TestHookGenRun()
        {
            var outputPath = Path.Combine(Environment.CurrentDirectory, "testdump", "MonoMod.UnitTest.Hooks.dll");
            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(outputPath))
                {
                    File.SetAttributes(outputPath, FileAttributes.Normal);
                    File.Delete(outputPath);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Couldn't create testdump.");
                Console.WriteLine(e);
            }

            using (var mm = new MonoModder
            {
                InputPath = typeof(HookGenRunTest).Assembly.Location,
                ReadingMode = ReadingMode.Deferred,

                MissingDependencyThrow = false,
            })
            {
                mm.Read();
                mm.MapDependencies();

                var gen = new HookGenerator(mm, "MonoMod.UnitTest.Hooks")
                {
                    HookPrivate = true,
                };
                using (var mOut = gen.OutputModule)
                {
                    gen.Generate();

                    if (outputPath != null)
                    {
                        mOut.Write(outputPath);
                    }
                    else
                    {
                        using (var ms = new MemoryStream())
                            mOut.Write(ms);
                    }
                }
            }
        }

        // The test above needs to deal with the entire assembly, including the following code.
    }
}

namespace MonoMod.UnitTest.HookGenTrash.Other
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Empty stub class.")]
    class Dummy
    {
        public List<int> A() => default;
        public List<Dummy> B() => default;
        public int C() => default;
        public Dummy D() => default;
        public T E<T>() => default;
    }
}

// Taken from tModLoader. This just needs to not crash.
namespace MonoMod.UnitTest.HookGenTrash.tModLoader
{
    public class ItemDefinition
    {
    }
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Empty stub class.")]
    class DefinitionOptionElement<T> where T : class
    {
    }
    public abstract class ConfigElement<T>
    {
    }
    abstract class DefinitionElement<T> : ConfigElement<T> where T : class
    {
        protected abstract DefinitionOptionElement<T> CreateDefinitionOptionElement();
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Empty stub class.")]
    class ItemDefinitionElement : DefinitionElement<ItemDefinition>
    {
        protected override DefinitionOptionElement<ItemDefinition> CreateDefinitionOptionElement() => null;
    }
}
