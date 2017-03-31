using System;
using MonoMod;
using MonoMod.InlineRT;

namespace MonoMod {
    internal class QuickDebugTestObject {
        public int Value;
        public override string ToString()
            => $"{{QuickDebugTestObject:{Value}}}";
    }
    internal static class QuickDebugTest {

        public static int Run(string[] _args) {
            object[] args = new object[] { 1, 0, 0, null };
            Console.WriteLine($"args: {args[0]} {args[1]} {args[2]} {(args[3] == null ? "null" : args[3])}");

            ReflectionHelper.GetDelegate(typeof(QuickDebugTest).GetMethod("Test"))(null, args);
            Console.WriteLine($"args after Test via ReflectionHelper: {args[0]} {args[1]} {args[2]} {(args[3] == null ? "null" : args[3])}");

            return (
                (int) args[0] == 1 && (int) args[1] == 1 && (int) args[2] == 2 && ((QuickDebugTestObject) args[3])?.Value == 1
                ) ? 0 : -1;
        }

        public static void Test(int a, ref int b, out int c, out QuickDebugTestObject d) {
            b = b + 1;
            c = b * 2;
            d = new QuickDebugTestObject();
            d.Value = a;
        }

    }
}