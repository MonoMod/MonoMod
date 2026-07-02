extern alias New;
using New::MonoMod.RuntimeDetour.Generics;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.RuntimeDetour.Generics
{
    [Collection("RuntimeDetour")]
    public class GenericHookConcurrencyTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {
        private static MethodInfo GenSrc => GetMethod(typeof(ConcSrc), nameof(ConcSrc.Gen));
        private static MethodInfo TierGenSrc => GetMethod(typeof(ConcSrc), nameof(ConcSrc.TierGen));
        private static MethodInfo TierGenWSrc => GetMethod(typeof(ConcSrc), nameof(ConcSrc.TierGenW));
        private static MethodInfo ReplaceTgt => GetMethod(typeof(GenericHookConcurrencyTest), nameof(Replace));
        private static MethodInfo WrapTgt => GetMethod(typeof(GenericHookConcurrencyTest), nameof(Wrap));

        public static class ConcSrc
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string Gen<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string TierGen<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string TierGenW<T>(T value) => $"orig<{typeof(T).Name}>:{value}";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Replace<T>(T value) => $"hook<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Wrap<T>(Func<T, string> orig, T value) => $"wrap[{orig(value)}]";

        public sealed class R0
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => "r0";
        }
        public sealed class R1
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => "r1";
        }
        public sealed class R2
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => "r2";
        }
        public sealed class R3
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => "r3";
        }
        public sealed class R4
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => "r4";
        }
        public sealed class R5
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => "r5";
        }

        private static readonly object s_obj = new();
        private static readonly R0 s_r0 = new();
        private static readonly R1 s_r1 = new();
        private static readonly R2 s_r2 = new();
        private static readonly R3 s_r3 = new();
        private static readonly R4 s_r4 = new();
        private static readonly R5 s_r5 = new();
        private static void AssertWellFormed(string typeName, string value, string result)
            => Assert.True(
                result == $"hook<{typeName}>:{value}" || result == $"orig<{typeName}>:{value}",
                $"Unexpected dispatch result for <{typeName}>: '{result}'");

        private static void CheckCall(int k)
        {
            switch (k % 8)
            {
                case 0: AssertWellFormed("String", "s", ConcSrc.Gen<string>("s")); break;
                case 1: AssertWellFormed("Object", s_obj.ToString()!, ConcSrc.Gen<object>(s_obj)); break;
                case 2: AssertWellFormed("R0", "r0", ConcSrc.Gen<R0>(s_r0)); break;
                case 3: AssertWellFormed("R1", "r1", ConcSrc.Gen<R1>(s_r1)); break;
                case 4: AssertWellFormed("R2", "r2", ConcSrc.Gen<R2>(s_r2)); break;
                case 5: AssertWellFormed("R3", "r3", ConcSrc.Gen<R3>(s_r3)); break;
                case 6: AssertWellFormed("R4", "r4", ConcSrc.Gen<R4>(s_r4)); break;
                default: AssertWellFormed("R5", "r5", ConcSrc.Gen<R5>(s_r5)); break;
            }
        }

        private static void Stress(int threads, int iters, Action<int, int> body)
        {
            using var gate = new Barrier(threads + 1);
            Exception? failure = null;
            var workers = new Thread[threads];
            for (var t = 0; t < threads; t++)
            {
                var tid = t;
                workers[t] = new Thread(() =>
                {
                    gate.SignalAndWait();
                    try
                    {
                        for (var i = 0; i < iters && Volatile.Read(ref failure) is null; i++)
                        {
                            body(tid, i);
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref failure, ex, null);
                    }
                }) { IsBackground = true, Name = $"stress-{t}" };
                workers[t].Start();
            }
            gate.SignalAndWait();
            foreach (var w in workers)
            {
                w.Join();
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        [GenericHookFact]
        public void ConcurrentDispatchIsStable()
        {
            using var hook = new GenericHook(GenSrc, ReplaceTgt,
                [[typeof(string)], [typeof(object)]]);

            Stress(8, 20000, (tid, i) =>
            {
                AssertWellFormed("String", "s", ConcSrc.Gen<string>("s"));
                AssertWellFormed("Object", s_obj.ToString()!, ConcSrc.Gen<object>(s_obj));
            });
        }

        [GenericHookFact]
        public void ConcurrentReprimeAndDispatchDoesNotCorrupt()
        {
            using var baseHook = new GenericHook(GenSrc, WrapTgt, [[typeof(string)]]);

            var stop = 0;
            var churnFailure = null as Exception;
            var churner = new Thread(() =>
            {
                try
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        using var h = new GenericHook(GenSrc, WrapTgt, [[typeof(string)]]);
                        _ = ConcSrc.Gen<string>("y");
                    }
                }
                catch (Exception ex)
                {
                    churnFailure = ex;
                }
            })
            { IsBackground = true, Name = "churner" };
            churner.Start();
            try
            {
                Stress(6, 15000, (tid, i) =>
                {
                    var r = ConcSrc.Gen<string>("x");
                    Assert.Contains("orig<String>:x", r, StringComparison.InvariantCulture);
                });
            }
            finally
            {
                Volatile.Write(ref stop, 1);
                churner.Join();
            }
            Assert.Null(churnFailure);
        }

        [GenericHookFact]
        public void ConcurrentAutoDiscoveryIsSafe()
        {
            using var hook = new GenericHook(GenSrc, ReplaceTgt,
                new[] { new[] { typeof(string) } }, AutoPrimeMode.All);

            Stress(8, 8000, (tid, i) => CheckCall(i + tid));
        }

        [GenericHookFact]
        public void SharedBodyDetourSurvivesTieredRecompilation()
        {
            using var hook = new GenericHook(TierGenSrc, ReplaceTgt, [[typeof(string)]]);

            Assert.Equal("hook<String>:s", ConcSrc.TierGen<string>("s"));

            for (var i = 0; i < 60_000; i++)
            {
                _ = ConcSrc.TierGen<string>("s");
            }
            Thread.Sleep(500);
            for (var i = 0; i < 20_000; i++)
            {
                _ = ConcSrc.TierGen<string>("s");
            }

            for (var i = 0; i < 2_000; i++)
            {
                Assert.Equal("hook<String>:s", ConcSrc.TierGen<string>("s"));
            }

            hook.Dispose();
            Assert.Equal("orig<String>:s", ConcSrc.TierGen<string>("s"));
            using var rehook = new GenericHook(TierGenSrc, ReplaceTgt, [[typeof(string)]]);
            Assert.Equal("hook<String>:s", ConcSrc.TierGen<string>("s"));
        }

        [GenericHookFact]
        public void SharedBodyWithOrigSurvivesTieredRecompilation()
        {
            using var hook = new GenericHook(TierGenWSrc, WrapTgt, [[typeof(string)]]);

            Assert.Equal("wrap[orig<String>:s]", ConcSrc.TierGenW<string>("s"));

            for (var i = 0; i < 60_000; i++)
            {
                _ = ConcSrc.TierGenW<string>("s");
            }
            Thread.Sleep(500);
            for (var i = 0; i < 20_000; i++)
            {
                _ = ConcSrc.TierGenW<string>("s");
            }

            for (var i = 0; i < 2_000; i++)
            {
                Assert.Equal("wrap[orig<String>:s]", ConcSrc.TierGenW<string>("s"));
            }
        }
    }
}
