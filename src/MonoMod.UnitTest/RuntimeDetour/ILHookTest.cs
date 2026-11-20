#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class ILHookTest : TestBase
    {
        private bool DidNothing = true;

        public ILHookTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestILHooks()
        {
            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);

            using (var h = new ILHook(
                typeof(ILHookTest).GetMethod("DoNothing", BindingFlags.Instance | BindingFlags.NonPublic),
                il =>
                {
                    var c = new ILCursor(il);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(OpCodes.Ldc_I4_0);
                    c.Emit(OpCodes.Stfld, typeof(ILHookTest).GetField("DidNothing", BindingFlags.NonPublic | BindingFlags.Instance));
                }
            ))
            {
                DidNothing = true;
                DoNothing();
                Assert.False(DidNothing);
            }

            DidNothing = true;
            DoNothing();
            Assert.True(DidNothing);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void DoNothing()
        {
        }

        private static bool _hookFirstApplied;
        private static ILHook _hook = null!;

        [Fact]
        public static async Task TestUndoDeadlock()
        {
            var original = typeof(ILHookTest).GetMethod(nameof(Original))!;
            var entryPoint = typeof(ILHookTest).GetMethod(nameof(LoaderEntrypoint))!;
            _hook = new ILHook(original, Manipulator(entryPoint));
            _hookFirstApplied = true;
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var invoking = new Task(_ => original.Invoke(null, []), null);
            var firstTask = await Task.WhenAny(invoking, timeout);
            Assert.True(firstTask == invoking);
        }

        private static ILContext.Manipulator Manipulator(MethodInfo entryPoint)
        {
            return !_hookFirstApplied
                ? il => 
                {
                    ILCursor cursor = new(il);
                    il.CreateLocal<bool>();
                    cursor.EmitLdcI4(1);
                    cursor.EmitStloc0();
                    cursor.EmitCall(entryPoint);
                    cursor.EmitLdloc0();
                    cursor.EmitAnd();
                    cursor.EmitStloc0();
                    cursor.EmitLdloc0();
                    cursor.EmitBrfalse(il.Instrs[il.Instrs.Count - 1]);
                }
                : _ => { };
        }

        public static void Original() { }

        public static bool LoaderEntrypoint()
        {
            _hook.Undo();
            _hook.Apply();

            return true;
        }
    }
}
