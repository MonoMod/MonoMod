#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test
#pragma warning disable CA1825 // Avoid zero-length array allocations

extern alias New;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using New::MonoMod.RuntimeDetour;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Hooks are disposed by the test teardown method")]
    public class ManualMultiHookTest : TestBase
    {
        Hook h1;
        Hook h2;
        ILHook hIL;

        private bool h1Run;
        private bool h2Run;
        private bool hILRun;

        public ManualMultiHookTest(ITestOutputHelper helper) : base(helper)
        {
        }

        private void Setup()
        {
            h1 = new Hook(
                typeof(ManualMultiHookTest).GetMethod("DoNothing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                new Action<Action<ManualMultiHookTest>, ManualMultiHookTest>((orig, self) =>
                {
                    orig(self);
                    h1Run = true;
                }),
                applyByDefault: false
            );
            h2 = new Hook(
                typeof(ManualMultiHookTest).GetMethod("DoNothing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                new Action<Action<ManualMultiHookTest>, ManualMultiHookTest>((orig, self) =>
                {
                    orig(self);
                    h2Run = true;
                }),
                applyByDefault: false
            );
            hIL = new ILHook(
                typeof(ManualMultiHookTest).GetMethod("DoNothing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                il =>
                {
                    var c = new ILCursor(il);
                    c.Emit(OpCodes.Ldc_I4_1);
                    c.EmitDelegate<Action<bool>>(v =>
                    {
                        hILRun = v;
                    });
                },
                applyByDefault: false
            );
            h1Run = false;
            h2Run = false;
            hILRun = false;
        }

        [SkipRemoteLinuxMonoFact]
        public void DoNothingTest()
        {
            Setup();
            DoNothing();
            Assert.False(h1Run);
            Assert.False(h2Run);
            Assert.False(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H1()
        {
            Setup();
            h1.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.False(h2Run);
            Assert.False(hILRun);
            h1.Undo();
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H2()
        {
            Setup();
            h2.Apply();
            DoNothing();
            Assert.False(h1Run);
            Assert.True(h2Run);
            Assert.False(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void HIL()
        {
            Setup();
            hIL.Apply();
            DoNothing();
            Assert.False(h1Run);
            Assert.False(h2Run);
            Assert.True(hILRun);
            TearDown();
        }


        [SkipRemoteLinuxMonoFact]
        public void HILH1()
        {
            Setup();
            hIL.Apply();
            h1.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.False(h2Run);
            Assert.True(hILRun);
            TearDown();
        }


        [SkipRemoteLinuxMonoFact]
        public void HILH1H2()
        {
            Setup();
            hIL.Apply();
            h1.Apply();
            h2.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            TearDown();
        }


        [SkipRemoteLinuxMonoFact]
        public void HILH2H1()
        {
            Setup();
            hIL.Apply();
            h2.Apply();
            h1.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H1H2HIL()
        {
            Setup();
            h1.Apply();
            h2.Apply();
            hIL.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H2H1HIL()
        {
            Setup();
            h2.Apply();
            h1.Apply();
            hIL.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H2HIL()
        {
            Setup();
            h2.Apply();
            hIL.Apply();
            DoNothing();
            Assert.False(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            hIL.Undo();
            h2.Undo();
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H1HILH2()
        {
            Setup();
            h1.Apply();
            hIL.Apply();
            h2.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void H1HIL()
        {
            Setup();
            h1.Apply();
            hIL.Apply();
            DoNothing();
            Assert.True(h1Run);
            Assert.False(h2Run);
            Assert.True(hILRun);
            TearDown();
        }

        [SkipRemoteLinuxMonoFact]
        public void HILH2()
        {
            Setup();
            hIL.Apply();
            h2.Apply();
            DoNothing();
            Assert.False(h1Run);
            Assert.True(h2Run);
            Assert.True(hILRun);
            TearDown();
        }

        private void TearDown()
        {
            h1.Dispose();
            h2.Dispose();
            hIL.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoNothing()
        {
        }
    }
}