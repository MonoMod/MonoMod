extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Github
{
    public class Issue230 : TestBase
    {
        public Issue230(ITestOutputHelper helper) : base(helper)
        {
        }

        private class SomeType
        {
            public int index { get; set; }
        }

        private static readonly List<SomeType>[] _list = new List<SomeType>[4];

        [Fact]
        public void ILHookOnMonoShouldSucceed()
        {
            for (var i = 0; i < 3; i++)
            {
                _list[i] = new List<SomeType>(3);
            }

            var original = typeof(Issue230).GetMethod(nameof(Original), BindingFlags.Static | BindingFlags.NonPublic);

            // Verify that applying multiple ILHooks does not throw an exception
            using (new ILHook(original, Hook1)) 
            using (new ILHook(original, Hook2))
            {
                // Running the method should succeed
                Original();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Original()
        {
            var random = new Random();
            var enumerable = Enumerable.Range(0, 50).Select(i => new SomeType { index = random.Next(50) });

            foreach (var item in enumerable)
            {
                _ = _list[item.index];
            }
        }

        private static void Hook1(ILContext il)
        {
            var cursor = new ILCursor(il);

            cursor.GotoNext(
                MoveType.After,
                x => x.MatchLdloc(out _),
                x => x.Match(OpCodes.Callvirt),
                x => x.Match(OpCodes.Ldelem_Ref));

            cursor.Index--;

            cursor.Emit(OpCodes.Ldc_I4_0);
            cursor.Emit(OpCodes.Ldc_I4_3);
            cursor.EmitCall(typeof(Issue230).GetMethod(nameof(Clamp), new[] { typeof(int), typeof(int), typeof(int) }));
        }

        private static void Hook2(ILContext il)
        {
            var cursor = new ILCursor(il);

            if (!cursor.TryGotoNext(
                    MoveType.After,
                    x => x.MatchLdloc(out _),
                    x => x.Match(OpCodes.Callvirt),
                    x => x.Match(OpCodes.Ldelem_Ref)))
            {
                cursor.Index = cursor.Instrs.Count;
            }
            else
            {
                cursor.Index--;
            }

            cursor.Emit(OpCodes.Ldc_I4_0);
            cursor.Emit(OpCodes.Ldc_I4_3);
            cursor.EmitCall(typeof(Issue230).GetMethod(nameof(Clamp), new[] { typeof(int), typeof(int), typeof(int) })!);

            if (!cursor.TryGotoNext(
                    MoveType.Before,
                    x => x.MatchLdloc(out _),
                    x => x.Match(OpCodes.Callvirt),
                    x => x.MatchStloc(out _)))
            {
                cursor.Index = cursor.Instrs.Count;
            }

            cursor.Emit(OpCodes.Ret);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (min > max)
                throw new ArgumentException($"'{min}' cannot be greater than {max}.");
            if (value < min)
                return min;
            return value > max ? max : value;
        }
    }
}
