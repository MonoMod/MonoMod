using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MonoMod.UnitTest {
    public class MultiHookUnitTestAutomaticRegistration {
        [Collection("RuntimeDetour")]
        public class OnIL {
            Hook h1;
            ILHook hIL;

            private bool h1Run;
            private bool hILRun;

            [Fact]
            public void OnThenIL() {
                h1 = new Hook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    new Action<Action<OnIL>, OnIL>((orig, self) => {
                        orig(self);
                        h1Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );
                hIL = new ILHook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    il => {
                        ILCursor c = new ILCursor(il);
                        c.EmitDelegate<Action>(() => {
                            hILRun = true;
                        });
                    },
                    new ILHookConfig {
                        ManualApply = false
                    }
                );
                h1Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(hILRun);
                h1.Dispose();
                hIL.Dispose();
            }

            [Fact]
            public void ILThenOn() {
                hIL = new ILHook(
                    typeof(OnIL).GetMethod("DoNothing"),
                    il => {
                        ILCursor c = new ILCursor(il);
                        c.EmitDelegate<Action>(() => {
                            hILRun = true;
                        });
                    },
                    new ILHookConfig {
                        ManualApply = false
                    }
                );
                h1 = new Hook(
                   typeof(OnIL).GetMethod("DoNothing"),
                   new Action<Action<OnIL>, OnIL>((orig, self) => {
                       orig(self);
                       h1Run = true;
                   }),
                   new HookConfig {
                       ManualApply = false
                   }
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
            public void DoNothing() {
            }
        }
        [Collection("RuntimeDetour")]
        public class OnOnIL {
            Hook h1;
            Hook h2;
            ILHook hIL;

            private bool h1Run;
            private bool h2Run;
            private bool hILRun;

            [Fact]
            public void OnThenOnThenIL() {
                h1 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) => {
                        orig(self);
                        h1Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );
                h2 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) => {
                        orig(self);
                        h2Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );
                hIL = new ILHook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    il => {
                        ILCursor c = new ILCursor(il);
                        c.EmitDelegate<Action>(() => {
                            hILRun = true;
                        });
                    },
                    new ILHookConfig {
                        ManualApply = false
                    }
                );
                h1Run = false;
                h2Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(h2Run);
                Assert.True(hILRun);
                h1.Dispose();
                hIL.Dispose();
            }

            [Fact]
            public void OnThenILThenOn() {
                h1 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) => {
                        orig(self);
                        h1Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );
                hIL = new ILHook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    il => {
                        ILCursor c = new ILCursor(il);
                        c.EmitDelegate<Action>(() => {
                            hILRun = true;
                        });
                    },
                    new ILHookConfig {
                        ManualApply = false
                    }
                );
                h2 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) => {
                        orig(self);
                        h2Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );
                h1Run = false;
                h2Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(h2Run);
                Assert.True(hILRun);
                h1.Dispose();
                hIL.Dispose();
            }

            [Fact]
            public void ILThenOnThenOn() {
                hIL = new ILHook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    il => {
                        ILCursor c = new ILCursor(il);
                        c.EmitDelegate<Action>(() => {
                            hILRun = true;
                        });
                    },
                    new ILHookConfig {
                        ManualApply = false
                    }
                );
                h1 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) => {
                        orig(self);
                        h1Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );
                h2 = new Hook(
                    typeof(OnOnIL).GetMethod("DoNothing"),
                    new Action<Action<OnOnIL>, OnOnIL>((orig, self) => {
                        orig(self);
                        h2Run = true;
                    }),
                    new HookConfig {
                        ManualApply = false
                    }
                );                
                h1Run = false;
                h2Run = false;
                hILRun = false;
                DoNothing();
                Assert.True(h1Run);
                Assert.True(h2Run);
                Assert.True(hILRun);
                h1.Dispose();
                hIL.Dispose();
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void DoNothing() {
            }
        }
    }
}
