#pragma warning disable xUnit1013 // Public method should be marked as test
#pragma warning disable CA1825 // Avoid zero-length array allocations

extern alias New;

using MonoMod.Cil;
using New::MonoMod.RuntimeDetour;
using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public class MultiHookUnitTestAutomaticRegistration : TestBase
    {
        public MultiHookUnitTestAutomaticRegistration(ITestOutputHelper helper) : base(helper)
        {
        }

        [Collection("RuntimeDetour")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
            Justification = "Sucks.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible",
            Justification = "Sucks.")]
        public class OnIL
        {
            Hook h1;
            ILHook hIL;

            private bool h1Run;
            private bool hILRun;

            [SkipRemoteLinuxMonoFact]
            public void OnThenIL()
            {
                h1 = new Hook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    new Action<Action<OnIL>, OnIL>((orig, self) =>
                    {
                        orig(self);
                        h1Run = true;
                    }),
                    applyByDefault: true
                );
                hIL = new ILHook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    il =>
                    {
                        var c = new ILCursor(il);
                        c.EmitDelegate<Action>(() =>
                        {
                            hILRun = true;
                        });
                    },
                    applyByDefault: true
                );
                h1Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(hILRun);
                h1.Dispose();
                hIL.Dispose();
            }

            [SkipRemoteLinuxMonoFact]
            public void ILThenOn()
            {
                hIL = new ILHook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    il =>
                    {
                        var c = new ILCursor(il);
                        c.EmitDelegate<Action>(() =>
                        {
                            hILRun = true;
                        });
                    },
                    applyByDefault: true
                );
                h1 = new Hook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    new Action<Action<OnIL>, OnIL>((orig, self) =>
                    {
                        orig(self);
                        h1Run = true;
                    }),
                    applyByDefault: true
               );
                h1Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(hILRun);
                h1.Dispose();
                hIL.Dispose();
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void DoNothing()
            {
            }
        }

        [Collection("RuntimeDetour")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
            Justification = "Sucks.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible",
            Justification = "Sucks.")]
        public class OnOnIL
        {
            Hook h1;
            Hook h2;
            ILHook hIL;

            private bool h1Run;
            private bool h2Run;
            private bool hILRun;

            [SkipRemoteLinuxMonoFact]
            public void OnThenOnThenIL()
            {
                h1 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) =>
                    {
                        orig(self);
                        h1Run = true;
                    }),
                    applyByDefault: true
                );
                h2 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) =>
                    {
                        orig(self);
                        h2Run = true;
                    }),
                    applyByDefault: true
                );
                hIL = new ILHook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    il =>
                    {
                        var c = new ILCursor(il);
                        c.EmitDelegate<Action>(() =>
                        {
                            hILRun = true;
                        });
                    },
                    applyByDefault: true
                );
                h1Run = false;
                h2Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(h2Run);
                Assert.True(hILRun);
                h1.Dispose();
                h2.Dispose();
                hIL.Dispose();
            }

            [SkipRemoteLinuxMonoFact]
            public void OnThenILThenOn()
            {
                h1 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) =>
                    {
                        orig(self);
                        h1Run = true;
                    }),
                    applyByDefault: true
                );
                hIL = new ILHook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    il =>
                    {
                        var c = new ILCursor(il);
                        c.EmitDelegate<Action>(() =>
                        {
                            hILRun = true;
                        });
                    },
                    applyByDefault: true
                );
                h2 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) =>
                    {
                        orig(self);
                        h2Run = true;
                    }),
                    applyByDefault: true
                );
                h1Run = false;
                h2Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(h2Run);
                Assert.True(hILRun);
                h1.Dispose();
                h2.Dispose();
                hIL.Dispose();
            }

            [SkipRemoteLinuxMonoFact]
            public void ILThenOnThenOn()
            {
                hIL = new ILHook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    il =>
                    {
                        var c = new ILCursor(il);
                        c.EmitDelegate<Action>(() =>
                        {
                            hILRun = true;
                        });
                    },
                    applyByDefault: true
                );
                h1 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) =>
                    {
                        orig(self);
                        h1Run = true;
                    }),
                    applyByDefault: true
                );
                h2 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) =>
                    {
                        orig(self);
                        h2Run = true;
                    }),
                    applyByDefault: true
                );
                h1Run = false;
                h2Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(h2Run);
                Assert.True(hILRun);
                h1.Dispose();
                h2.Dispose();
                hIL.Dispose();
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void DoNothing()
            {
            }
        }
    }
}
