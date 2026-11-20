using System;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.MomoModPatch
{
    public class MonoModPatchTest : TestBase
    {
        private readonly string aModDllPath;
        private readonly string bModDllPath;
        private readonly string targetDllPath;

        public MonoModPatchTest(ITestOutputHelper helper) : base(helper)
        {
            var resourcePath = Path.Combine(Path.GetDirectoryName(typeof(MonoModPatchTest).Assembly.Location),
                "MomoModPatch", "Resources");

            //ILSpy can view source code.
            targetDllPath = Path.Combine(resourcePath, "DemoLib.dll");
            aModDllPath = Path.Combine(resourcePath, "DemoLib.ModA.mm.dll");
            bModDllPath = Path.Combine(resourcePath, "DemoLib.ModB.mm.dll");
        }

        private MonoModder BuildMonoModder(string targetDllPath, params string[] modDllPaths)
        {
            var modder = new MonoModder();
            modder.InputPath = targetDllPath;
            modder.MissingDependencyThrow = false; //disable because target and mods built by .netfx35

            modder.Read();

            foreach (var modDllPath in modDllPaths)
            {
                modder.ReadMod(modDllPath);
            }

            return modder;
        }

        private Assembly LoadPatchedAssembly(MonoModder modder)
        {
            var filePath = Path.GetTempFileName() + ".dll";
            modder.Write(default, filePath);
            //Console.WriteLine($"patched dll saved at {filePath}");
            var patchedAssembly = Assembly.LoadFile(filePath);
            return patchedAssembly;
        }

        private object CreateMyLibObject(MonoModder modder, out Type type)
        {
            //patch by ModA
            modder.MapDependencies();
            modder.AutoPatch();

            //load patched
            var patchedAssembly = LoadPatchedAssembly(modder);

            type = patchedAssembly.GetType("DemoLib.MyLib");
            var obj = Activator.CreateInstance(type);

            Assert.NotNull(obj);
            return obj;
        }

        [Fact]
        public void OneModNormalTest()
        {
            //patch by ModA
            using var modder = BuildMonoModder(targetDllPath, aModDllPath);
            var obj = CreateMyLibObject(modder, out var type);

            //do asset
            var ctorResult = type.GetProperty("CtorResult").GetValue(obj) as string;
            Assert.Equal("50A", ctorResult);

            var calcResult = (int)type.GetMethod("Calculate").Invoke(obj, new object[] {1, 5});
            Assert.Equal((1 + 5) * 10, calcResult);
        }

        [Fact]
        public void TwoModButNotCombineTest()
        {
            //patch by ModA and ModB
            using var modder = BuildMonoModder(targetDllPath, aModDllPath, bModDllPath);
            //modder.CombineSameMethodMultiModPatches = false; //default false
            var obj = CreateMyLibObject(modder, out var type);

            //do asset
            var ctorResult = type.GetProperty("CtorResult").GetValue(obj) as string;
            Assert.Contains(ctorResult, ["50A", "B50"]);

            var calcResult = (int)type.GetMethod("Calculate").Invoke(obj, new object[] {1, 5});
            Assert.Contains(calcResult, [(1 + 5) * 10, 10000000 + 1 + 5]);
        }

        [Fact]
        public void TwoModButCombineTest()
        {
            //patch by ModA and ModB
            using var modder = BuildMonoModder(targetDllPath, aModDllPath, bModDllPath);
            modder.CombineSameMethodMultiModPatches = true; //combine
            var obj = CreateMyLibObject(modder, out var type);

            //do asset
            var ctorResult = type.GetProperty("CtorResult").GetValue(obj) as string;
            Assert.Equal("B50A", ctorResult);

            var calcResult = (int)type.GetMethod("Calculate").Invoke(obj, new object[] {1, 5});
            Assert.Contains(calcResult, [(1 + 5) * 10 + 1000000, (1000000 + 1 + 5) * 10]);
        }
    }
}