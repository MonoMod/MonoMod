#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MC = Mono.Cecil;
using CIL = Mono.Cecil.Cil;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class JitHookRegressionTest {

        static int ID;

        // At the time of writing, this should only affect .NET Core, but let's test almost all runtimes.
        [Fact]
        public void TestJitHookMissingMethod() {
            // ... except for .NET Core on Linux. Doesn't pass locally at all, passes on Azure only with 5.0.
#if !NETFRAMEWORK
            if (PlatformHelper.Is(Platform.Linux))
                return;
#endif

            // The JIT hook might already be set up thanks to previ
            TestJitHookMissingMethodStep();

            Assert.NotNull(DetourHelper.Runtime);

            TestJitHookMissingMethodStep();
        }

        private void TestJitHookMissingMethodStep() {
            int id = ID++;
            string @namespace = "MonoMod.UnitTest";
            string @name = "JitHookRegressionTestHelper" + id;
            string @fullName = @namespace + "." + @name;

            Assembly asm;

            using (ModuleDefinition module = ModuleDefinition.CreateModule(@fullName, new ModuleParameters() {
                Kind = ModuleKind.Dll,
                ReflectionImporterProvider = MMReflectionImporter.Provider
            })) {
                TypeDefinition type = new TypeDefinition(
                    @namespace,
                    @name,
                    MC.TypeAttributes.Public | MC.TypeAttributes.Abstract | MC.TypeAttributes.Sealed
                ) {
                    BaseType = module.TypeSystem.Object
                };
                module.Types.Add(type);

                MethodDefinition method = new MethodDefinition(@name,
                    MC.MethodAttributes.Public | MC.MethodAttributes.Static | MC.MethodAttributes.HideBySig,
                    module.TypeSystem.Void
                );
                type.Methods.Add(method);

                ILProcessor il = method.Body.GetILProcessor();
                il.Emit(OpCodes.Call, module.ImportReference(
                    new MethodReference(
                        "MissingMethod" + id,
                        module.TypeSystem.Void,
                        new TypeReference(
                            "TotallyNotReal", "MissingType",
                            module,
                            new AssemblyNameReference("TotallyNotReal", new Version(0, 0, 0, 0))
                        )
                    )
                ));
                il.Emit(OpCodes.Ret);

                asm = ReflectionHelper.Load(module);
            }

            try {
                (asm.GetType(@fullName).GetMethod(@name).CreateDelegate<Action>())();
            } catch (TypeLoadException) {
            }
        }

    }
}
