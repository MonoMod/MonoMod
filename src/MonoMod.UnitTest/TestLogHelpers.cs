using MonoMod.Logs;
using System;
using System.Threading;
using Xunit.Abstractions;

namespace MonoMod.UnitTest {
    internal static class TestLogHelpers {
        private static ITestOutputHelper? singleOutputHelper;
        private static readonly AsyncLocal<ITestOutputHelper?> currentOutputHelper = new();

        private static int isInitialized;

        private static void EnsureSubscribedToDebugLog() {
            if (Interlocked.CompareExchange(ref isInitialized, 1, 0) != 0) return;
            DebugLog.OnLog += static (source, time, level, message) => {
                if (currentOutputHelper.Value is not { } helper) {
                    helper = singleOutputHelper;
                    if (helper is null)
                        return;
                }
                var localTime = time.ToLocalTime();
                helper.WriteLine($"[{source}]({localTime}) {level.FastToString()}: {message}");
            };
        }

        public static void Startup(ITestOutputHelper helper, ref bool attachedSingleOutputHelper) {
            EnsureSubscribedToDebugLog();
            currentOutputHelper.Value = helper;
            attachedSingleOutputHelper = Interlocked.CompareExchange(ref singleOutputHelper, helper, null) == null;
        }

        public static void Shutdown(bool attachedSingleOutputHelper) {
            currentOutputHelper.Value = null;
            if (attachedSingleOutputHelper)
                singleOutputHelper = null;
        }
    }

    public class TestBase : IDisposable {
        private readonly bool attachedSingleOutputHelper;
        public TestBase(ITestOutputHelper helper) {
            TestLogHelpers.Startup(helper, ref attachedSingleOutputHelper);
        }
        public void Dispose() {
            TestLogHelpers.Shutdown(attachedSingleOutputHelper);
        }
    }
}
