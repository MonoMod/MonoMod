using MonoMod.Logs;
using System;
using System.Threading;
using Xunit.Abstractions;

namespace MonoMod.UnitTest {
    internal static class TestLogHelpers {
        private static readonly AsyncLocal<ITestOutputHelper?> currentOutputHelper = new();

        private static int isInitialized;

        private static void EnsureSubscribedToDebugLog() {
            if (Interlocked.CompareExchange(ref isInitialized, 1, 0) != 0) return;
            DebugLog.OnLog += static (source, time, level, message) => {
                if (currentOutputHelper.Value is not { } helper)
                    return;
                var localTime = time.ToLocalTime();
                helper.WriteLine($"[{source}]({localTime}) {level.FastToString()}: {message}");
            };
        }

        public static void Startup(ITestOutputHelper helper) {
            EnsureSubscribedToDebugLog();
            currentOutputHelper.Value = helper;
        }

        public static void Shutdown() {
            currentOutputHelper.Value = null;
        }
    }

    public class TestBase : IDisposable {
        public TestBase(ITestOutputHelper helper) {
            TestLogHelpers.Startup(helper);
        }
        public void Dispose() {
            TestLogHelpers.Shutdown();
        }
    }
}
