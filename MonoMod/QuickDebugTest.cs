using System;
using MonoMod;
using MonoMod.InlineRT;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Globalization;
using System.Reflection.Emit;
using MonoMod.Detour;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MonoMod {
    internal delegate void d_TestA(int a, ref int b, out int c, out QuickDebugTestObject d, ref QuickDebugTestStruct e);
    internal delegate void d_PrintA();
    internal class QuickDebugTestObject {
        public int Value;
        public override string ToString()
            => $"{{QuickDebugTestObject:{Value}}}";
    }
    internal struct QuickDebugTestStruct {
        public int Value;
        public override string ToString()
            => $"{{QuickDebugTestStruct:{Value}}}";
    }
    internal static class QuickDebugTest {

        public static int Run(object[] args) {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            return (
                TestReflectionHelperRef() &&
                TestReflectionHelperRefJmp() &&
                TestRuntimeDetourHelper() &&
                // TestReflectionHelperTime() &&
                true) ? 0 : -1;
        }

        public static bool TestRuntimeDetourHelper() {
            PrintA();
            typeof(QuickDebugTest).GetMethod("PrintA").Detour(typeof(QuickDebugTest).GetMethod("PrintB"));
            PrintA();
            typeof(QuickDebugTest).GetMethod("PrintA").GetTrampoline<d_PrintA>()();
            typeof(QuickDebugTest).GetMethod("PrintA").Detour((Action) PrintC);
            PrintA();
            typeof(QuickDebugTest).GetMethod("PrintA").GetTrampoline<d_PrintA>()();
            typeof(QuickDebugTest).GetMethod("PrintA").GetOrigTrampoline<d_PrintA>()();

            typeof(QuickDebugTest).GetMethod("PrintB").Detour(typeof(QuickDebugTest).GetMethod("PrintC"));
            PrintB();
            typeof(QuickDebugTest).GetMethod("PrintB").Detour((Action) PrintD);
            PrintB();

            typeof(QuickDebugTest).GetMethod("PrintA").Undetour();
            typeof(QuickDebugTest).GetMethod("PrintA").Undetour();
            PrintA();

            return true;
        }

        // Only affects .NET Framework
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PrintA() => Console.WriteLine("A");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PrintB() => Console.WriteLine("B");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PrintC() => Console.WriteLine("C");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PrintD() => Console.WriteLine("D");

        public static bool TestReflectionHelperRef() {
            object[] args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            Console.WriteLine($"args: {args[0]} {args[1]} {args[2]} {(args[3] == null ? "null" : args[3])} {args[4]}");

            typeof(QuickDebugTest).GetMethod("TestA").GetDelegate()(null, args);
            Console.WriteLine($"args after Test via ReflectionHelper: {args[0]} {args[1]} {args[2]} {(args[3] == null ? "null" : args[3])} {args[4]}");

            return
                (int) args[0] == 1 &&
                (int) args[1] == 1 &&
                (int) args[2] == 2 &&
                ((QuickDebugTestObject) args[3])?.Value == 1 &&
                ((QuickDebugTestStruct) args[4]).Value == 1
                ;
        }

        public static bool TestReflectionHelperRefJmp() {
            int a = 1;
            int b = 0;
            int c = 0;
            QuickDebugTestObject d = null;
            QuickDebugTestStruct e = new QuickDebugTestStruct();
            Console.WriteLine($"args: {a} {b} {c} {(d == null ? "null" : d.ToString())} {e}");

            typeof(QuickDebugTest).GetMethod("TestA").CreateJmpDelegate<d_TestA>()(a, ref b, out c, out d, ref e);
            Console.WriteLine($"args after Test via ReflectionHelper using jmp: {a} {b} {c} {(d == null ? "null" : d.ToString())} {e}");

            return
                a == 1 &&
                b == 1 &&
                c == 2 &&
                d?.Value == 1 &&
                e.Value == 1
                ;
        }

        public static bool TestReflectionHelperTime() {
            object[] args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            Console.WriteLine($"Initial args: {args[0]} {args[1]} {args[2]} {(args[3] == null ? "null" : args[3])} {args[4]}");

            MethodInfo method = typeof(QuickDebugTest).GetMethod("TestA");

            const long runs = 1000000000;

            Console.WriteLine("Test-running Stopwatch");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            sw.Stop();
            TimeSpan timeNewBox = sw.Elapsed;
            TimeSpan timeLocals = sw.Elapsed;
            TimeSpan timeMutable = sw.Elapsed;
            TimeSpan timeJmp = sw.Elapsed;
            sw.Reset();

            Console.WriteLine("Generating local-less delegate");
            DynamicMethodDelegate dmdNewBox = method.CreateDelegate(directBoxValueAccess: false);
            Console.WriteLine("Test-running dmdNewBox");
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            dmdNewBox(null, args);
            if (!(
                (int) args[0] == 1 &&
                (int) args[1] == 1 &&
                (int) args[2] == 2 &&
                ((QuickDebugTestObject) args[3])?.Value == 1 &&
                ((QuickDebugTestStruct) args[4]).Value == 1
                )) return false;
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };

            Console.WriteLine($"Timing dmdNewBox {runs} runs");
            sw.Start();
            for (long i = runs; i > -1; --i) {
                dmdNewBox(null, args);
            }
            sw.Stop();
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            timeNewBox = sw.Elapsed;
            sw.Reset();
            Console.WriteLine($"time: {timeNewBox}");


            Console.WriteLine("Generating mutability-ignoring delegate");
            DynamicMethodDelegate dmdMutable = method.CreateDelegate(directBoxValueAccess: true);
            Console.WriteLine("Test-running dmdMutable");
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            dmdMutable(null, args);
            if (!(
                (int) args[0] == 1 &&
                (int) args[1] == 1 &&
                (int) args[2] == 2 &&
                ((QuickDebugTestObject) args[3])?.Value == 1 &&
                ((QuickDebugTestStruct) args[4]).Value == 1
                )) return false;
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };

            Console.WriteLine($"Timing dmdMutable {runs} runs");
            sw.Start();
            for (long i = runs; i > -1; --i) {
                dmdMutable(null, args);
            }
            sw.Stop();
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            timeMutable = sw.Elapsed;
            sw.Reset();
            Console.WriteLine($"time: {timeMutable}");

            /*
            Console.WriteLine("Generating localed delegate");
            DynamicMethodDelegate dmdLocals = method.CreateDelegateUsingLocals();
            Console.WriteLine("Test-running dmdLocals");
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            dmdLocals(null, args);
            if (!(
                (int) args[0] == 1 &&
                (int) args[1] == 1 &&
                (int) args[2] == 2 &&
                ((QuickDebugTestObject) args[3])?.Value == 1 &&
                ((QuickDebugTestStruct) args[4]).Value == 1
                )) return false;
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };

            Console.WriteLine($"Timing dmdLocals {runs} runs");
            sw.Start();
            for (long i = runs; i > -1; --i) {
                dmdLocals(null, args);
            }
            sw.Stop();
            args = new object[] { 1, 0, 0, null, new QuickDebugTestStruct() };
            timeLocals = sw.Elapsed;
            sw.Reset();
            Console.WriteLine($"time: {timeLocals}");
            */

            Console.WriteLine("Generating jmp delegate");
            d_TestA dmdJmp = method.CreateJmpDelegate<d_TestA>();
            Console.WriteLine("Test-running dmdJmp");
            int a = 1;
            int b = 0;
            int c = 0;
            QuickDebugTestObject d = null;
            QuickDebugTestStruct e = new QuickDebugTestStruct();
            dmdJmp(a, ref b, out c, out d, ref e);
            if (!(
                a == 1 &&
                b == 1 &&
                c == 2 &&
                d?.Value == 1 &&
                e.Value == 1
                )) return false;

            Console.WriteLine($"Timing dmdJmp {runs} runs");
            sw.Start();
            for (long i = runs; i > -1; --i) {
                dmdJmp(a, ref b, out c, out d, ref e);
            }
            sw.Stop();
            timeJmp = sw.Elapsed;
            sw.Reset();
            Console.WriteLine($"time: {timeJmp}");


            Console.WriteLine($"newbox / jmp: {(double) timeNewBox.Ticks / (double) timeJmp.Ticks}");
            Console.WriteLine($"jmp / newbox: {(double) timeJmp.Ticks / (double) timeNewBox.Ticks}");

            Console.WriteLine($"newbox / mutable: {(double) timeNewBox.Ticks / (double) timeMutable.Ticks}");
            Console.WriteLine($"mutable / newbox: {(double) timeMutable.Ticks / (double) timeNewBox.Ticks}");

            Console.WriteLine($"jmp / mutable: {(double) timeJmp.Ticks / (double) timeMutable.Ticks}");
            Console.WriteLine($"mutable / jmp: {(double) timeMutable.Ticks / (double) timeJmp.Ticks}");

            Console.WriteLine("Pass");
            return true;
        }

        public static void TestA(int a, ref int b, out int c, out QuickDebugTestObject d, ref QuickDebugTestStruct e) {
            b = b + 1;
            c = b * 2;
            d = new QuickDebugTestObject();
            d.Value = a;
            e.Value = a;
        }

        private static DynamicMethodDelegate CreateDelegateUsingLocals(this MethodBase method) {
            DynamicMethod dynam = new DynamicMethod(string.Empty, typeof(object), new Type[] { typeof(object), typeof(object[]) }, typeof(ReflectionHelper).Module, true);
            ILGenerator il = dynam.GetILGenerator();

            ParameterInfo[] args = method.GetParameters();

            LocalBuilder[] locals = new LocalBuilder[args.Length];
            for (int i = 0; i < args.Length; i++) {
                if (args[i].ParameterType.IsByRef)
                    locals[i] = il.DeclareLocal(args[i].ParameterType.GetElementType(), true);
                else
                    locals[i] = il.DeclareLocal(args[i].ParameterType, true);
            }

            for (int i = 0; i < args.Length; i++) {
                il.Emit(OpCodes.Ldarg_1);
                il.EmitFast_Ldc_I4(i);

                Type argType = args[i].ParameterType;
                bool argIsByRef = argType.IsByRef;
                if (argIsByRef)
                    argType = argType.GetElementType();
                bool argIsValueType = argType.IsValueType;

                il.Emit(OpCodes.Ldelem_Ref);
                if (argIsValueType) {
                    il.Emit(OpCodes.Unbox_Any, argType);
                } else {
                    il.Emit(OpCodes.Castclass, argType);
                }
                il.Emit(OpCodes.Stloc, locals[i]);
            }

            if (!method.IsStatic && !method.IsConstructor) {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType.IsValueType) {
                    il.Emit(OpCodes.Unbox, method.DeclaringType);
                }
            }

            for (int i = 0; i < args.Length; i++) {
                if (args[i].ParameterType.IsByRef)
                    il.Emit(OpCodes.Ldloca_S, locals[i]);
                else
                    il.Emit(OpCodes.Ldloc, locals[i]);
            }

            if (method.IsConstructor) {
                il.Emit(OpCodes.Newobj, method as ConstructorInfo);
            } else if (method.IsFinal || !method.IsVirtual) {
                il.Emit(OpCodes.Call, method as MethodInfo);
            } else {
                il.Emit(OpCodes.Callvirt, method as MethodInfo);
            }

            Type returnType = method.IsConstructor ? method.DeclaringType : (method as MethodInfo).ReturnType;
            if (returnType != typeof(void)) {
                if (returnType.IsValueType) {
                    il.Emit(OpCodes.Box, returnType);
                }
            } else {
                il.Emit(OpCodes.Ldnull);
            }

            for (int i = 0; i < args.Length; i++) {
                if (args[i].ParameterType.IsByRef) {
                    il.Emit(OpCodes.Ldarg_1);
                    il.EmitFast_Ldc_I4(i);

                    il.Emit(OpCodes.Ldloc, locals[i]);
                    if (locals[i].LocalType.IsValueType)
                        il.Emit(OpCodes.Box, locals[i].LocalType);
                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            il.Emit(OpCodes.Ret);

            return (DynamicMethodDelegate) dynam.CreateDelegate(typeof(DynamicMethodDelegate));
        }

    }
}