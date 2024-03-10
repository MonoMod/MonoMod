using MonoMod.Logs;
using System;
using System.Threading;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    internal static class TestLogHelpers
    {
        private static ITestOutputHelper? singleOutputHelper;
        private static readonly AsyncLocal<ITestOutputHelper?> currentOutputHelper = new();

        private static int isInitialized;

        private static void EnsureSubscribedToDebugLog()
        {
            if (Interlocked.CompareExchange(ref isInitialized, 1, 0) != 0)
                return;
            DebugLog.OnLog += static (source, time, level, message) =>
            {
                if (currentOutputHelper.Value is not { } helper)
                {
                    helper = singleOutputHelper;
                    if (helper is null)
                        return;
                }
                var localTime = time.ToLocalTime();
                helper.WriteLine($"[{source}]({localTime}) {level.FastToString(null)}: {message}");
            };
        }

        public static void Startup(ITestOutputHelper helper, ref bool attachedSingleOutputHelper)
        {
            EnsureSubscribedToDebugLog();
            currentOutputHelper.Value = helper;
            attachedSingleOutputHelper = Interlocked.CompareExchange(ref singleOutputHelper, helper, null) == null;

            if (attachedSingleOutputHelper)
                MMDbgLog.Info("------------- TEST BEGIN -------------");
        }

        public static void Shutdown(bool attachedSingleOutputHelper)
        {
            if (attachedSingleOutputHelper)
                MMDbgLog.Info("-------------- TEST END --------------");

            currentOutputHelper.Value = null;
            if (attachedSingleOutputHelper)
                singleOutputHelper = null;
        }
    }

    public class TestBase : IDisposable
    {
        private readonly bool attachedSingleOutputHelper;

        public TestBase(ITestOutputHelper helper)
        {
            TestLogHelpers.Startup(helper, ref attachedSingleOutputHelper);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    TestLogHelpers.Shutdown(attachedSingleOutputHelper);
                }

                disposedValue = true;
            }
        }

        private bool disposedValue;
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
