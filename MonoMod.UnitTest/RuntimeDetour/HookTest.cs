#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

using MonoMod.RuntimeDetour;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.UnitTest {
    public static class HookTest {
        [Test]
        public static void TestHooks() {
            Console.WriteLine("Hooks: none");
            DetourExample.TestStep(5, 6, 8);
            Console.WriteLine();

            IDetour hookTestMethodA = new Hook(
                typeof(DetourExample).GetMethod("TestMethod", BindingFlags.Instance | BindingFlags.Public),
                typeof(HookTest).GetMethod("TestMethod_A", BindingFlags.Static | BindingFlags.Public)
            );
            IDetour hookTestStaticMethodA = new Hook(
                typeof(DetourExample).GetMethod("TestStaticMethod", BindingFlags.Static | BindingFlags.Public),
                typeof(HookTest).GetMethod("TestStaticMethod_A", BindingFlags.Static | BindingFlags.Public)
            );
            IDetour hookTestVoidMethodA = new Hook(
                typeof(DetourExample).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                typeof(HookTest).GetMethod("TestVoidMethod_A", BindingFlags.Static | BindingFlags.Public)
            );
            Console.WriteLine("Hooks: A");
            DetourExample.TestStep(42, 12, 1);
            Console.WriteLine();

            IDetour hookTestMethodB = new Hook(
                typeof(DetourExample).GetMethod("TestMethod", BindingFlags.Instance | BindingFlags.Public),
                typeof(HookTest).GetMethod("TestMethod_B", BindingFlags.Static | BindingFlags.Public)
            );
            IDetour hookTestStaticMethodB = new Hook(
                typeof(DetourExample).GetMethod("TestStaticMethod", BindingFlags.Static | BindingFlags.Public),
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) => {
                    return orig(a, b) + 2;
                })
            );
            IDetour hookTestVoidMethodB = new Hook(
                typeof(DetourExample).GetMethod("TestVoidMethod", BindingFlags.Static | BindingFlags.Public),
                new Action<Action<int, int>, int, int>((orig, a, b) => {
                    Console.WriteLine("Hook B");
                    DetourExample.VoidResult += 2;
                    orig(a, b);
                })
            );
            Console.WriteLine("Hooks: A + B");
            DetourExample.TestStep(42 + 42, 12 + 2, 3);
            Console.WriteLine();

            hookTestMethodA.Undo();
            hookTestStaticMethodA.Undo();
            hookTestVoidMethodA.Undo();
            Console.WriteLine("Hooks: B");
            DetourExample.TestStep(5 + 42, 6 + 2, 12);
            Console.WriteLine();

            hookTestMethodB.Undo();
            hookTestStaticMethodB.Undo();
            hookTestVoidMethodB.Undo();
            Console.WriteLine("Detours: none");
            DetourExample.TestStep(5, 6, 8);
            Console.WriteLine();
        }

        public static int TestMethod_A(DetourExample self, int a, int b) {
            return 42;
        }

        public static int TestStaticMethod_A(int a, int b) {
            return a * b * 2;
        }

        public static void TestVoidMethod_A(int a, int b) {
            Console.WriteLine("Hook A");
            DetourExample.VoidResult += 1;
        }

        public static int TestMethod_B(Func<DetourExample, int, int, int> orig, DetourExample self, int a, int b) {
            return orig(self, a, b) + 42;
        }

    }
}
