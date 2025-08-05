using Mono.Cecil;
using MonoMod.Utils.Cil;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Xunit.Abstractions;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace MonoMod.UnitTest
{
    public class CecilIlGeneratorTest : TestBase
    {
        public CecilIlGeneratorTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestLocalEmit()
        {
            using var moduleStream = new MemoryStream();
            using (var moduleDef = ModuleDefinition.CreateModule("TestModule", ModuleKind.Dll))
            {
                var methodDef = new MethodDefinition("TestMethod",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static,
                    moduleDef.TypeSystem.String);

                moduleDef.Types.Add(new TypeDefinition("Test", "TestType",
                    TypeAttributes.Public | TypeAttributes.AutoClass | TypeAttributes.AnsiClass |
                    TypeAttributes.BeforeFieldInit)
                {
                    Methods = { methodDef },
                    BaseType = moduleDef.TypeSystem.Object
                });

                methodDef.Parameters.Add(new ParameterDefinition(moduleDef.TypeSystem.Boolean));

                {
                    var il = new CecilILGenerator(methodDef.Body.GetILProcessor()).GetProxy();

                    var local = il.DeclareLocal(typeof(string));
                    
                    il.Emit(OpCodes.Ldarga_S, 0);
                    var toStringMethod = typeof(bool).GetMethod(nameof(bool.ToString), []);
                    
                    Assert.NotNull(toStringMethod);
                    
                    il.Emit(OpCodes.Callvirt, toStringMethod);
                    il.Emit(OpCodes.Stloc, local);

                    il.Emit(OpCodes.Ldloc, local);
                    il.Emit(OpCodes.Ret);
                }

                moduleDef.Write(moduleStream);
            }
            
            var assembly = Assembly.Load(moduleStream.ToArray());
            
            var type = assembly.GetType("Test.TestType");
            
            Assert.NotNull(type);
            
            var method = type.GetMethod("TestMethod");
            
            Assert.NotNull(method);
            Assert.Equal(bool.TrueString, method.Invoke(null, new object[] { true }));
            Assert.Equal(bool.FalseString, method.Invoke(null, new object[] { false }));
        }
    }
}