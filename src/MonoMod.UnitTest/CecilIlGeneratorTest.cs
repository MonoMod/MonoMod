using Mono.Cecil;
using MonoMod.Utils;
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

        [Theory]
        [InlineData(-128, false)]
        [InlineData(-128, true)]
        [InlineData(-1, false)]
        [InlineData(-1, true)]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(9, false)]
        [InlineData(9, true)]
        [InlineData(127, false)]
        [InlineData(127, true)]
        public void TestLdcI4SEmit(int value, bool useByteOverload)
        {
            using var dmd = new DynamicMethodDefinition("TestLdcI4S", typeof(int), []);
            var il = dmd.GetILGenerator();
            if (useByteOverload)
                il.Emit(OpCodes.Ldc_I4_S, unchecked((byte)value));
            else
#if NET8_0_OR_GREATER
                il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
#else
                // Before .NET 8, the nonvirtual sbyte overload does not forward to the proxy.
                il.GetProxiedShim().Emit(OpCodes.Ldc_I4_S, (sbyte)value);
#endif
            il.Emit(OpCodes.Ret);

            Assert.Equal((sbyte)value, Assert.IsType<sbyte>(dmd.Definition.Body.Instructions[0].Operand));
            Assert.Equal(value, dmd.Generate().Invoke(null, null));
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