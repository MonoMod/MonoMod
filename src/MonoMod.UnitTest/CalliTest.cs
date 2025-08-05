using Mono.Cecil.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using Xunit;
using Xunit.Abstractions;


namespace MonoMod.UnitTest
{
    public unsafe sealed class CalliTests : TestBase
    {
        public CalliTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void CalliTest()
        {
            Calli<RandomStruct>(VerifyStruct);
            Calli<RandomClass>(VerifyClass);
        }
        static unsafe RandomStruct VerifyStruct(RandomStruct o, ref RandomStruct r, RandomStruct[] a, RandomStruct* p)
        {
            // test if it receives valid arguments
            // should only fail when runtime is corrupted
            Assert.Equal(typeof(RandomStruct[]), a.GetType());
            Assert.Equal(r, *p);
            r = new RandomStruct();
            Assert.Equal(r, *p);
            return o;
        }
#pragma warning disable CS8500
        static unsafe RandomClass VerifyClass(RandomClass o, ref RandomClass r, RandomClass[] a, RandomClass* p)
        {
            // test if it receives valid arguments
            // should only fail when runtime is corrupted
            Assert.Equal(typeof(RandomClass), o.GetType());
            Assert.Equal(typeof(RandomClass[]), a.GetType());
            Assert.Equal(r, *p);
            r = new RandomClass();
            Assert.Equal(r, *p);
            return o;
        }
        delegate T Helper<T>(T o, ref T r, T[] a, T* p) where T : new();
        unsafe void Calli<T>(Helper<T> func) where T : new()
        {
            T obj = new();
            func(obj, ref obj, [obj], &obj);

            var type = typeof(T);
            var method = func.Method;
            using var pin = PlatformTriple.Current.PinMethodIfNeeded(method);
            var i = method.MethodHandle.GetFunctionPointer();
            using DynamicMethodDefinition dmd = new("a", null, [typeof(nint), type]);
            var il = dmd.GetILProcessor();
            var c = new Mono.Cecil.CallSite(dmd.Module.ImportReference(type));
            c.Parameters.Add(new(dmd.Module.ImportReference(type)));
            c.Parameters.Add(new(dmd.Module.ImportReference(type.MakeByRefType())));
            c.Parameters.Add(new(dmd.Module.ImportReference(type.MakeArrayType())));
            c.Parameters.Add(new(dmd.Module.ImportReference(type.MakePointerType())));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarga, 1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, type);
            il.Emit(OpCodes.Ldarga, 1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Calli, c);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            //Switches.SetSwitchValue(Switches.DMDType, "cecil");
            var ret = dmd.Generate();
            PlatformTriple.Current.Compile(ret);
            ret.Invoke(null, [i, Activator.CreateInstance(type, [32])]);
        }
#pragma warning restore CS8500
        struct RandomStruct(int v)
        {
            public int u = v;

            public RandomStruct() : this(0)
            {
            }
        }
        class RandomClass(int v)
        {
            public int u = v;

            public RandomClass() : this(0)
            {
            }
        }
    }
}
