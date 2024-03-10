#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class DetourModifyTest : TestBase
    {

        private static readonly MethodInfo m_HookTarget = typeof(DetourModifyTest).GetMethod("HookTarget");

        public DetourModifyTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDetoursModify()
        {
            using (var h = new Hook(m_HookTarget, Hook, new DetourConfig("MainHook")))
                HookTarget(h, true);
        }

        private static Hook beforeHook;
        public static void Hook(Action<Hook, bool> orig, Hook hook, bool shouldInvoke)
        {
            // Check if we should have reached the hook method
            Assert.True(shouldInvoke, "Hook method should not have been called");

            // Sanity check
            orig(hook, true);

            // Test adding a hook *before* our own while already in an active call
            beforeHook = new Hook(m_HookTarget,
                new Action<Hook, bool>(static (h, _) =>
                {
                    if (h != null)
                    {
                        Assert.Fail("Newly added hooks before the active one mustn't be invoked for currently ongoing calls");
                    }
                }),
                new DetourConfig("BeforeHook", before: new string[] { hook.Config.Id })
            );
            HookTarget(null, false); // This should be caught by the newly created before-hook

            // Test adding a hook *after* our own while already in an active call
            using (var h = new Hook(m_HookTarget, new Action<Hook, bool>(static (_, _) => { }),
                new DetourConfig("AfterHook", after: new string[] { hook.Config.Id })
            ))
                orig(hook, false); // This should be caught by the newly created after-hook

            // Test removing our own hook while already in an active call
            hook.Dispose();
            Assert.Throws<InvalidOperationException>(() => orig(hook, true));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void HookTarget(Hook hook, bool shouldInvoke)
        {
            Assert.True(shouldInvoke, "Hook target should not have been called");
        }

    }
}
