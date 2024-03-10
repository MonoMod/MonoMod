using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#pragma warning disable CA1031 // Do not catch general exception types
// Whenever this is done, it is done to prevent logger exceptions from killing the caller

namespace MonoMod.Logs
{
    public readonly record struct MessageHole
    {
        public int Start { get; }
        public int End { get; }
        public object? Value { get; }
        public bool IsValueUnrepresentable { get; }

        public MessageHole(int start, int end)
        {
            Value = null;
            IsValueUnrepresentable = true;
            Start = start;
            End = end;
        }

        public MessageHole(int start, int end, object? value)
        {
            Value = value;
            IsValueUnrepresentable = false;
            Start = start;
            End = end;
        }
    }

    public sealed class DebugLog
    {

        public delegate void OnLogMessage(string source, DateTime time, LogLevel level, string message);
        public delegate void OnLogMessageDetailed(string source, DateTime time, LogLevel level, string formattedMessage, ReadOnlyMemory<MessageHole> holes);

        internal static readonly DebugLog Instance = new();

        private sealed class LogMessage
        {
            public string Source { get; private set; }
            public DateTime Time { get; private set; }
            public LogLevel Level { get; private set; }
            public string FormattedMessage { get; private set; }
            public ReadOnlyMemory<MessageHole> FormatHoles { get; private set; }

            public LogMessage(string source, DateTime time, LogLevel level, string formatted, ReadOnlyMemory<MessageHole> holes)
            {
                Source = source;
                Time = time;
                Level = level;
                FormattedMessage = formatted;
                FormatHoles = holes;
            }

            public void Clear()
            {
                Source = "";
                Time = default;
                Level = default;
                FormattedMessage = "";
                FormatHoles = default;
            }

            public void Init(string source, DateTime time, LogLevel level, string formatted, ReadOnlyMemory<MessageHole> holes)
            {
                Source = source;
                Time = time;
                Level = level;
                FormattedMessage = formatted;
                FormatHoles = holes;
            }

            public void ReportTo(OnLogMessage del)
            {
                try
                {
                    del(Source, Time, Level, FormattedMessage);
                }
                catch (Exception e)
                {
                    // this is done in two separate calls to avoid calling string.Concat, because that may cause issues
                    Debugger.Log(int.MaxValue, "MonoMod.DebugLog", "Exception caught while reporting to message handler");
                    Debugger.Log(int.MaxValue, "MonoMod.DebugLog", e.ToString());
                }
            }

            public void ReportTo(OnLogMessageDetailed del)
            {
                try
                {
                    del(Source, Time, Level, FormattedMessage, FormatHoles);
                }
                catch (Exception e)
                {
                    // this is done in two separate calls to avoid calling string.Concat, because that may cause issues
                    Debugger.Log(int.MaxValue, "MonoMod.DebugLog", "Exception caught while reporting to message handler");
                    Debugger.Log(int.MaxValue, "MonoMod.DebugLog", e.ToString());
                }
            }
        }

        public static bool IsFinalizing => Environment.HasShutdownStarted || AppDomain.CurrentDomain.IsFinalizingForUnload();

        // this is a cache for our WeakReference objects
        private static readonly ConcurrentBag<WeakReference<LogMessage>> weakRefCache = new();
        // and this is a cache for our LogMessage objects
        private static readonly ConcurrentBag<WeakReference<LogMessage>> messageObjectCache = new();

        private LogMessage MakeMessage(string source, DateTime time, LogLevel level, string formatted, ReadOnlyMemory<MessageHole> holes)
        {
            try
            {
                if (replayQueue is null && !IsFinalizing)
                {
                    while (messageObjectCache.TryTake(out var weakRef))
                    {
                        if (weakRef.TryGetTarget(out var message))
                        {
                            message.Init(source, time, level, formatted, holes);
                            weakRefCache.Add(weakRef);
                            return message;
                        }
                        else
                        {
                            weakRefCache.Add(weakRef);
                        }
                    }
                }
            }
            catch
            {
                // Message creation should be mostly infallible. If something fails in here, it shouldn't bring down the caller of the logger.
                // TODO: somehow, somewhere, record this exception
            }

            // we weren't able to get an existing LogMessage object
            return new(source, time, level, formatted, holes);
        }

        private void ReturnMessage(LogMessage message)
        {
            message.Clear();

            try
            {
                if (replayQueue is null && !IsFinalizing)
                {
                    if (weakRefCache.TryTake(out var weakRef))
                    {
                        weakRef.SetTarget(message);
                        messageObjectCache.Add(weakRef);
                    }
                    else
                    {
                        messageObjectCache.Add(new(message));
                    }
                }
            }
            catch
            {
                // Message creation should be mostly infallible. If something fails in here, it shouldn't bring down the caller of the logger.
                // TODO: somehow, somewhere, record this exception
            }
        }

        public static bool IsWritingLog => Instance.ShouldLog;
        internal bool AlwaysLog => replayQueue is not null || Debugger.IsAttached;
        internal bool ShouldLog => subscriptions.ActiveLevels is not LogLevelFilter.None || AlwaysLog;
        internal bool RecordHoles => recordHoles || subscriptions.DetailLevels is not LogLevelFilter.None;

        private void PostMessage(LogMessage message)
        {
            // we do this log here because we want to always log to the debugger when its attached, instead of just if its attached at startup
            if (Debugger.IsAttached)
            {
                try
                {
                    // Even though Debugger.Log won't do anything when no debugger is attached, it's still worth guarding it with a check of Debugger.IsAttached for a few reasons:
                    //  1. It avoids the allocation of the message string when it wouldn't be used
                    //  2. Debugger.Log is implemented as a QCall (on CoreCLR, and probably Framework) which pulls in all of the P/Invoke machinery, and necessitates a GC transition.
                    //     Debugger.IsAttached, on the other hand, is an FCall (MethodImplOptions.InternalCall) and likely elides the helper frames entirely, making it much faster.
                    Debugger.Log((int)message.Level, message.Source,
                        DebugFormatter.Format($"[{message.Source}] {message.Level.FastToString(null)}: {message.FormattedMessage}\n")); // the VS output window doesn't automatically add a newline
                }
                catch
                {
                    // We want to completely swallow exceptions that happen here, because logging errors shouldn't cause problems for the callers.
                }
            }

            try
            {
                var sub = subscriptions;
                var idx = (int)message.Level;
                if (sub.SimpleRegs[idx] is { } simple)
                    message.ReportTo(simple);
                if (sub.DetailedRegs[idx] is { } detailed)
                    message.ReportTo(detailed);

                if (!IsFinalizing)
                {
                    if (replayQueue is { } queue)
                    {
                        // enqueue the message, then dequeue old messages until we're back below the replay length
                        queue.Enqueue(message);
                        while (queue.Count > replayQueueLength && queue.TryDequeue(out _)) { }
                    }
                    else
                    {
                        // we only reuse message objects if we're not recording a replay queue to avoid race conditions
                        ReturnMessage(message);
                    }
                }
            }
            catch
            {
                // Same deal here as above. Exceptions in the logger shouldn't bubble up to callers.
            }
        }

        #region Log functions
        internal bool ShouldLogLevel(LogLevel level) // check AlwaysLog last because it's more complex
            => ((1 << (int)level) & (int)subscriptions.ActiveLevels) is not 0
            // if we're falling through to AlwaysLog, we only want to always log stuff in the global filter
            || (((1 << (int)level) & (int)globalFilter) is not 0 && AlwaysLog);
        internal bool ShouldLevelRecordHoles(LogLevel level)
            => recordHoles || ((1 << (int)level) & (int)subscriptions.DetailLevels) is not 0;

        public void Write(string source, DateTime time, LogLevel level, string message)
        {
            if (!ShouldLogLevel(level))
            {
                return;
            }
            PostMessage(MakeMessage(source, time, level, message, default));
        }

        public void Write(string source, DateTime time, LogLevel level,
            [InterpolatedStringHandlerArgument("level")] ref DebugLogInterpolatedStringHandler message)
        {
            // we check the handler's enabled field instead of our own HasHandlers because the handler may not have been recording anything in the first place
            if (!message.enabled)
                return;
            if (!ShouldLogLevel(level))
            {
                return;
            }
            var formatted = message.ToStringAndClear(out var holes);
            PostMessage(MakeMessage(source, time, level, formatted, holes));
        }

        internal void LogCore(string source, LogLevel level, string message)
        {
            if (!ShouldLogLevel(level))
            {
                return;
            }
            Write(source, DateTime.UtcNow, level, message);
        }

        internal void LogCore(string source, LogLevel level,
            [InterpolatedStringHandlerArgument("level")] ref DebugLogInterpolatedStringHandler message)
        {
            if (!message.enabled)
                return;
            if (!ShouldLogLevel(level))
            {
                return;
            }
            Write(source, DateTime.UtcNow, level, ref message);
        }

        public static void Log(string source, LogLevel level, string message)
        {
            var instance = Instance;
            if (!instance.ShouldLogLevel(level))
            {
                return;
            }
            instance.Write(source, DateTime.UtcNow, level, message);
        }

        public static void Log(string source, LogLevel level,
            [InterpolatedStringHandlerArgument("level")] ref DebugLogInterpolatedStringHandler message)
        {
            var instance = Instance;
            if (!message.enabled)
                return;
            if (!instance.ShouldLogLevel(level))
            {
                return;
            }
            instance.Write(source, DateTime.UtcNow, level, ref message);
        }
        #endregion


        private static readonly char[] listEnvSeparator = [' ', ';', ','];
        private static string[]? GetListEnvVar(string text)
        {
            var str = text.Trim();
            if (string.IsNullOrEmpty(str))
                return null;
            var list = str.Split(listEnvSeparator, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < list.Length; i++)
                list[i] = list[i].Trim();
            return list;
        }

        private readonly bool recordHoles;
        private readonly int replayQueueLength;

        private readonly ConcurrentQueue<LogMessage>? replayQueue;

        private LogLevelFilter globalFilter = LogLevelFilter.DefaultFilter;

        private DebugLog()
        {
            // TODO: improve switches ergonomics
            recordHoles = Switches.TryGetSwitchEnabled(Switches.LogRecordHoles, out var switchEnabled) && switchEnabled;
            replayQueueLength = 0;
            if (Switches.TryGetSwitchValue(Switches.LogReplayQueueLength, out var switchValue))
            {
                replayQueueLength = switchValue as int? ?? 0;
            }

            var showSpamLogs = Switches.TryGetSwitchEnabled(Switches.LogSpam, out switchEnabled) && switchEnabled;
            if (showSpamLogs)
                globalFilter |= LogLevelFilter.Spam;

            if (replayQueueLength > 0)
            {
                replayQueue = new();
            }

            var diskLogFile = Switches.TryGetSwitchValue(Switches.LogToFile, out switchValue) ? switchValue as string : null;
            string[]? diskSourceFilter = null;
            if (Switches.TryGetSwitchValue(Switches.LogToFileFilter, out switchValue))
            {
                diskSourceFilter = switchValue switch
                {
                    string[] sa => sa,
                    string s => GetListEnvVar(s),
                    _ => null
                };
            }

            if (diskLogFile is not null)
            {
                TryInitializeLogToFile(diskLogFile, diskSourceFilter, globalFilter);
            }

            var useMemlog = Switches.TryGetSwitchEnabled(Switches.LogInMemory, out switchEnabled) && switchEnabled;
            if (useMemlog)
            {
                TryInitializeMemoryLog(globalFilter);
            }
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The subscription should live forever, so we don't want to dispose the subscription registration.")]
        private void TryInitializeLogToFile(string file, string[]? sourceFilter, LogLevelFilter filter)
        {
            try
            {
                var comparer = StringComparerEx.FromComparison(StringComparison.OrdinalIgnoreCase);
                if (sourceFilter is not null)
                    Array.Sort(sourceFilter, comparer);

                object sync = new();
                TextWriter writer;
                if (file == "-")
                {
                    writer = Console.Out;
                }
                else
                {
                    var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Write);
                    writer = new StreamWriter(fs, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                }
                _ = SubscribeCore(filter, (source, time, level, msg) =>
                {
                    if (sourceFilter is not null)
                    {
                        var idx = sourceFilter.AsSpan().BinarySearch(source, comparer);
                        if (idx < 0) // we didn't find the source in the filter list
                            return;
                    }

                    var realTime = time.ToLocalTime();
                    var outMsg = $"[{source}]({realTime}) {level.FastToString(null)}: {msg}";

                    // if we don't do this, on .NET 6, we'll sometimes get a corrupt buffer out
                    lock (sync)
                    {
                        writer.WriteLine(outMsg);
                    }
                });
            }
            catch (Exception e)
            {
                Instance.LogCore("DebugLog", LogLevel.Error, $"Exception while trying to initialize writing logs to a file: {e}");
            }
        }

        // TODO: thread-local memlog?
        private static byte[]? memlog;
        private static int memlogPos;

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The subscription should live forever, so we don't want to dispose the subscription registration.")]
        private void TryInitializeMemoryLog(LogLevelFilter filter)
        {
            try
            { // maxSize is in kb
                memlogPos = 0;
                memlog = new byte[0x1000]; // start with a fairly large buffer, because we log quite a bit

                var sync = new object();

                var encoding = Encoding.UTF8;
                _ = SubscribeCore(filter, (source, time, level, msg) =>
                {
                    var blevel = (byte)(int)level;
                    var ticks = time.Ticks;

                    if (source.Length > 255) // if your source is this long, you're doing something wrong
                        source = source.Substring(0, 255);
                    var bSourceLen = (byte)source.Length;
                    var msgLen = msg.Length;

                    var totalMsgLen = sizeof(byte) + sizeof(long) + sizeof(byte) + sizeof(int) + (bSourceLen * 2) + (msgLen * 2);

                    lock (sync)
                    {
                        if (memlog.Length - memlogPos < totalMsgLen)
                        {
                            // our message wouldn't fit at the end of the memlog, so resize it so it will
                            var newSize = memlog.Length * 4;
                            while (newSize - memlogPos < totalMsgLen)
                                newSize *= 4;
                            Array.Resize(ref memlog, newSize);
                        }
                        var span = memlog.AsSpan().Slice(memlogPos);
                        ref var msgBase = ref MemoryMarshal.GetReference(span);
                        var pos = 0;
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref msgBase, pos), blevel);
                        pos += sizeof(byte);
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref msgBase, pos), ticks);
                        pos += sizeof(long);
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref msgBase, pos), bSourceLen);
                        pos += sizeof(byte);
                        Unsafe.CopyBlock(ref Unsafe.Add(ref msgBase, pos), ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(source.AsSpan())), bSourceLen * 2u);
                        pos += bSourceLen * 2;
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref msgBase, pos), msgLen);
                        pos += sizeof(int);
                        Unsafe.CopyBlock(ref Unsafe.Add(ref msgBase, pos), ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(msg.AsSpan())), (uint)msgLen * 2u);
                        pos += msgLen * 2;
                        memlogPos += pos;
                    }
                });
            }
            catch (Exception e)
            {
                Instance.LogCore("DebugLog", LogLevel.Error, $"Exception while initializing the memory log: {e}");
            }
        }

        private sealed class LevelSubscriptions
        {
            public LogLevelFilter ActiveLevels;
            public LogLevelFilter DetailLevels;
            public readonly OnLogMessage?[] SimpleRegs;
            public readonly OnLogMessageDetailed?[] DetailedRegs;

            private const LogLevelFilter ValidFilter = (LogLevelFilter)((1 << ((int)LogLevelExtensions.MaxLevel + 1)) - 1);

            private LevelSubscriptions(LogLevelFilter active, LogLevelFilter detail, OnLogMessage?[] simple, OnLogMessageDetailed?[] detailed)
            {
                ActiveLevels = active | detail; // detail is by definition a subset of active
                DetailLevels = detail;
                SimpleRegs = simple;
                DetailedRegs = detailed;
            }

            private LevelSubscriptions()
            {
                ActiveLevels = LogLevelFilter.None;
                DetailLevels = LogLevelFilter.None;
                SimpleRegs = new OnLogMessage?[(int)LogLevelExtensions.MaxLevel + 1];
                DetailedRegs = new OnLogMessageDetailed?[SimpleRegs.Length];
            }

            public static readonly LevelSubscriptions None = new();

            private LevelSubscriptions Clone(bool changingDetail)
            {
                var simple = SimpleRegs;
                var detailed = DetailedRegs;

                if (!changingDetail)
                {
                    simple = new OnLogMessage?[SimpleRegs.Length];
                    Array.Copy(SimpleRegs, simple, simple.Length);
                }
                else
                {
                    detailed = new OnLogMessageDetailed?[DetailedRegs.Length];
                    Array.Copy(DetailedRegs, detailed, detailed.Length);
                }
                return new(ActiveLevels, DetailLevels, simple, detailed);
            }

            private void FixFilters()
            {
                ActiveLevels &= ValidFilter;
                DetailLevels &= ValidFilter;
            }

            public LevelSubscriptions AddSimple(LogLevelFilter filter, OnLogMessage del)
            {
                var clone = Clone(false);
                clone.ActiveLevels |= filter;
                var ifilter = (int)filter;
                for (var i = 0; i < clone.SimpleRegs.Length; i++)
                {
                    if ((ifilter & (1 << i)) == 0)
                        continue;
                    _ = Helpers.EventAdd(ref clone.SimpleRegs[i], del);
                }
                clone.FixFilters();
                return clone;
            }

            public LevelSubscriptions RemoveSimple(LogLevelFilter filter, OnLogMessage del)
            {
                var clone = Clone(false);
                var ifilter = (int)filter;
                for (var i = 0; i < clone.SimpleRegs.Length; i++)
                {
                    if ((ifilter & (1 << i)) == 0)
                        continue;
                    var result = Helpers.EventRemove(ref clone.SimpleRegs[i], del);
                    if (result is null)
                        clone.ActiveLevels &= (LogLevelFilter)~(1 << i);
                }
                clone.ActiveLevels |= clone.DetailLevels;
                clone.FixFilters();
                return clone;
            }

            public LevelSubscriptions AddDetailed(LogLevelFilter filter, OnLogMessageDetailed del)
            {
                var clone = Clone(true);
                clone.DetailLevels |= filter;
                var ifilter = (int)filter;
                for (var i = 0; i < clone.DetailedRegs.Length; i++)
                {
                    if ((ifilter & (1 << i)) == 0)
                        continue;
                    _ = Helpers.EventAdd(ref clone.DetailedRegs[i], del);
                }
                clone.ActiveLevels |= clone.DetailLevels;
                clone.FixFilters();
                return clone;
            }

            public LevelSubscriptions RemoveDetailed(LogLevelFilter filter, OnLogMessageDetailed del)
            {
                var clone = Clone(true);
                var ifilter = (int)filter;
                for (var i = 0; i < clone.DetailedRegs.Length; i++)
                {
                    if ((ifilter & (1 << i)) == 0)
                        continue;
                    var result = Helpers.EventRemove(ref clone.DetailedRegs[i], del);
                    if (result is null)
                        clone.DetailLevels &= (LogLevelFilter)~(1 << i);
                }
                clone.ActiveLevels |= clone.DetailLevels;
                clone.FixFilters();
                return clone;
            }
        }

        private LevelSubscriptions subscriptions = LevelSubscriptions.None;

        #region Message Events
        private void MaybeReplayTo(LogLevelFilter filter, OnLogMessage del)
        {
            if (replayQueue is null || filter is LogLevelFilter.None)
            {
                return;
            }

            var msgs = replayQueue.ToArray();
            // if we're recording message replays, we don't bother reusing message objects, so this is safe
            foreach (var msg in msgs)
            {
                if (((1 << (int)msg.Level) & (int)filter) is 0)
                    continue;
                msg.ReportTo(del);
            }
        }
        private void MaybeReplayTo(LogLevelFilter filter, OnLogMessageDetailed del)
        {
            if (replayQueue is null || filter is LogLevelFilter.None)
            {
                return;
            }

            var msgs = replayQueue.ToArray();
            // if we're recording message replays, we don't bother reusing message objects, so this is safe
            foreach (var msg in msgs)
            {
                if (((1 << (int)msg.Level) & (int)filter) is 0)
                    continue;
                msg.ReportTo(del);
            }
        }

        public static IDisposable Subscribe(LogLevelFilter filter, OnLogMessage value)
            => Instance.SubscribeCore(filter, value);
        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
            Justification = "The return type is intended to be opaque, and Dispose() is not expected to be in a hot path.")]
        private IDisposable SubscribeCore(LogLevelFilter filter, OnLogMessage value)
        {
            LevelSubscriptions o, n;
            do
            {
                o = subscriptions;
                n = o.AddSimple(filter, value);
            } while (Interlocked.CompareExchange(ref subscriptions, n, o) != o);
            MaybeReplayTo(filter, value);
            return new LogSubscriptionSimple(this, value, filter);
        }

        private sealed class LogSubscriptionSimple : IDisposable
        {
            private readonly DebugLog log;
            private readonly OnLogMessage del;
            private readonly LogLevelFilter filter;

            public LogSubscriptionSimple(DebugLog log, OnLogMessage del, LogLevelFilter filter)
            {
                this.log = log;
                this.del = del;
                this.filter = filter;
            }

            public void Dispose()
            {
                LevelSubscriptions o, n;
                do
                {
                    o = log.subscriptions;
                    n = o.RemoveSimple(filter, del);
                } while (Interlocked.CompareExchange(ref log.subscriptions, n, o) != o);
            }
        }

        public static IDisposable Subscribe(LogLevelFilter filter, OnLogMessageDetailed value)
            => Instance.SubscribeCore(filter, value);
        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
            Justification = "The return type is intended to be opaque, and Dispose() is not expected to be in a hot path.")]
        private IDisposable SubscribeCore(LogLevelFilter filter, OnLogMessageDetailed value)
        {
            LevelSubscriptions o, n;
            do
            {
                o = subscriptions;
                n = o.AddDetailed(filter, value);
            } while (Interlocked.CompareExchange(ref subscriptions, n, o) != o);
            MaybeReplayTo(filter, value);
            return new LogSubscriptionDetailed(this, value, filter);
        }

        private sealed class LogSubscriptionDetailed : IDisposable
        {
            private readonly DebugLog log;
            private readonly OnLogMessageDetailed del;
            private readonly LogLevelFilter filter;

            public LogSubscriptionDetailed(DebugLog log, OnLogMessageDetailed del, LogLevelFilter filter)
            {
                this.log = log;
                this.del = del;
                this.filter = filter;
            }

            public void Dispose()
            {
                LevelSubscriptions o, n;
                do
                {
                    o = log.subscriptions;
                    n = o.RemoveDetailed(filter, del);
                } while (Interlocked.CompareExchange(ref log.subscriptions, n, o) != o);
            }
        }

        private static readonly ConcurrentDictionary<OnLogMessage, IDisposable> simpleRegDict = new();

        [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "I am. I'm not sure why this warning is being issued.")]
        public static event OnLogMessage OnLog
        {
            add
            {
                var res = Subscribe(Instance.globalFilter, value);
                simpleRegDict.AddOrUpdate(value, res, (_, d) => { d.Dispose(); return res; });
            }
            remove
            {
                if (simpleRegDict.TryRemove(value, out var d))
                {
                    d.Dispose();
                }
            }
        }
        #endregion
    }
}
