extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;
using MonoMod.Cil;

namespace MonoMod.UnitTest.Github
{
    public class Issue220 : TestBase
    {
        public Issue220(ITestOutputHelper helper) : base(helper)
        {
        }

        private class DrawTarget
        {
            public bool IsDrawn { get; private set; }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void DrawMenu(GameTime gameTime)
            {
                IsDrawn = true;
            }
        }

        private class GameTime
        {
            // Minimal mock to match the scenario
        }

        class GCCallback(Action callback)
        {
            ~GCCallback()
            {
                callback();
            }

            public static void Register(Action callback)
            {
                _ = new GCCallback(callback);
            }
        }


        [Fact]
        public void ConcurrentILHooksDoNotCauseRaceCondition()
        {
            var method = typeof(DrawTarget).GetMethod(nameof(DrawTarget.DrawMenu));
            var target = new DrawTarget();

            using ILHook hook = new(method, i =>
            {
                ILCursor ic = new(i);
                ic.EmitDelegate(() => { });
            });
            using ILHook whereRaceConditionHappens = new(method, _ =>
            {
                var done = false;
                GCCallback.Register(() => done = true);

                var retryCount = 0;
                while (!done)
                {
                    GC.Collect(2, GCCollectionMode.Forced);
                    Thread.Sleep(1);
                    retryCount++;
                    if (retryCount > 100)
                    {
                        throw new InvalidOperationException("if GCCallback is not triggered, this test case can't work.");
                    }
                }
                Task.Run(() => target.DrawMenu(new GameTime())).Wait();
            });
        }
    }
}