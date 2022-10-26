using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;

namespace MonoMod.Logs {
    public readonly struct MessageHole {
        public int Start { get; }
        public int End { get; }
        public object? Value { get; }
        public bool IsValueUnrepresentable { get; }

        public MessageHole(int start, int end) {
            Value = null;
            IsValueUnrepresentable = true;
            Start = start;
            End = end;
        }

        public MessageHole(int start, int end, object? value) {
            Value = value;
            IsValueUnrepresentable = false;
            Start = start;
            End = end;
        }
    }

    public sealed class DebugLog {

        public delegate void OnLogMessage(string source, DateTime time, LogLevel level, string message);
        public delegate void OnLogMessageDetailed(string source, DateTime time, LogLevel level, string formattedMessage, ReadOnlyMemory<MessageHole> holes);

        internal static readonly DebugLog Instance = new();

        private sealed class LogMessage {
            public string Source { get; private set; }
            public DateTime Time { get; private set; }
            public LogLevel Level { get; private set; }
            public string FormattedMessage { get; private set; }
            public ReadOnlyMemory<MessageHole> FormatHoles { get; private set; }

            public LogMessage(string source, DateTime time, LogLevel level, string formatted, ReadOnlyMemory<MessageHole> holes) {
                Source = source;
                Time = time;
                Level = level;
                FormattedMessage = formatted;
                FormatHoles = holes;
            }

            public void Clear() {
                Source = "";
                Time = default;
                Level = default;
                FormattedMessage = "";
                FormatHoles = default;
            }

            public void Init(string source, DateTime time, LogLevel level, string formatted, ReadOnlyMemory<MessageHole> holes) {
                Source = source;
                Time = time;
                Level = level;
                FormattedMessage = formatted;
                FormatHoles = holes;
            }

            // TODO: what can we do about exceptions thrown in log handlers?
            public void ReportTo(OnLogMessage del) {
                try {
                    del(Source, Time, Level, FormattedMessage);
                } catch { }
            }

            public void ReportTo(OnLogMessageDetailed del) {
                try {
                    del(Source, Time, Level, FormattedMessage, FormatHoles);
                } catch { }
            }
        }

        // this is a cache for our WeakReference objects
        private static readonly ConcurrentBag<WeakReference<LogMessage>> weakRefCache = new();
        // and this is a cache for our LogMessage objects
        private static readonly ConcurrentBag<WeakReference<LogMessage>> messageObjectCache = new();

        private static LogMessage MakeMessage(string source, DateTime time, LogLevel level, string formatted, ReadOnlyMemory<MessageHole> holes) {
            while (messageObjectCache.TryTake(out var weakRef)) {
                if (weakRef.TryGetTarget(out var message)) {
                    message.Init(source, time, level, formatted, holes);
                    weakRefCache.Add(weakRef);
                    return message;
                }
            }

            // we weren't able to get an existing LogMessage object
            return new(source, time, level, formatted, holes);
        }

        private static void ReturnMessage(LogMessage message) {
            message.Clear();

            if (weakRefCache.TryTake(out var weakRef)) {
                weakRef.SetTarget(message);
                messageObjectCache.Add(weakRef);
            } else {
                messageObjectCache.Add(new(message));
            }
        }

        #region Log functions
        public void Write(string source, DateTime time, LogLevel level, string message) {
            if (!ShouldLog) return;
            PostMessage(MakeMessage(source, time, level, message, default));
        }

        public void Write(string source, DateTime time, LogLevel level, ref DebugLogInterpolatedStringHandler message) {
            // we check the handler's enabled field instead of our own HasHandlers because the handler may not have been recording anything in the first place
            if (!message.enabled || !ShouldLog)
                return;
            var formatted = message.ToStringAndClear(out var holes);
            PostMessage(MakeMessage(source, time, level, formatted, holes));
        }

        
        internal void LogCore(string source, LogLevel level, string message) {
            if (!ShouldLog)
                return;
            if ((globalFilter & (LogLevelFilter) (1 << (int) level)) == 0)
                return;
            Write(source, DateTime.UtcNow, level, message);
        }
        internal void LogCore(string source, LogLevel level, ref DebugLogInterpolatedStringHandler message) {
            if (!message.enabled || !ShouldLog)
                return;
            if ((globalFilter & (LogLevelFilter) (1 << (int) level)) == 0)
                return;
            Write(source, DateTime.UtcNow, level, ref message);
        }
        

        public static void Log(string source, LogLevel level, string message) {
            var instance = Instance;
            if (!instance.ShouldLog)
                return;
            if ((instance.globalFilter & (LogLevelFilter) (1 << (int) level)) == 0)
                return;
            instance.Write(source, DateTime.UtcNow, level, message);
        }
        public static void Log(string source, LogLevel level, ref DebugLogInterpolatedStringHandler message) {
            var instance = Instance;
            if (!message.enabled || !instance.ShouldLog)
                return;
            if ((instance.globalFilter & (LogLevelFilter) (1 << (int) level)) == 0)
                return;
            instance.Write(source, DateTime.UtcNow, level, ref message);
        }
        #endregion

        private static string? GetStringEnvVar(string varName) {
            var str = Environment.GetEnvironmentVariable(varName)?.Trim();
            if (string.IsNullOrEmpty(str))
                return null;
            return str;
        }

        private static int? GetNumericEnvVar(string varName) {
            var str = Environment.GetEnvironmentVariable(varName)?.Trim();
            if (string.IsNullOrEmpty(str))
                return null;
            if (int.TryParse(str, out var result))
                return result;
            return null;
        }

        private static bool? GetBoolEnvVar(string varName) {
            var str = Environment.GetEnvironmentVariable(varName)?.Trim();
            if (string.IsNullOrEmpty(str))
                return null;
            Helpers.DAssert(str is not null);
            if (bool.TryParse(str, out var result))
                return result;
            if (int.TryParse(str, out var iresult))
                return iresult != 0;
            if (str.Equals("yes", StringComparison.OrdinalIgnoreCase) || str.Equals("y", StringComparison.OrdinalIgnoreCase))
                return true;
            if (str.Equals("no", StringComparison.OrdinalIgnoreCase) || str.Equals("n", StringComparison.OrdinalIgnoreCase))
                return false;
            return null;
        }

        private static string[]? GetListEnvVar(string varName) {
            var str = Environment.GetEnvironmentVariable(varName)?.Trim();
            if (string.IsNullOrEmpty(str))
                return null;
            Helpers.DAssert(str is not null);
            var list = str.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < list.Length; i++)
                list[i] = list[i].Trim();
            return list;
        }

        internal volatile bool recordHoles;
        private readonly int replayQueueLength;

        private readonly ConcurrentQueue<LogMessage>? replayQueue;

        private LogLevelFilter globalFilter = LogLevelFilter.DefaultFilter;

        private void PostMessage(LogMessage message) {
            if (onLogSimple is { } simple)
                message.ReportTo(simple);
            if (onLogDetailed is { } detailed)
                message.ReportTo(detailed);
            if (replayQueue is { } queue) {
                // enqueue the message, then dequeue old messages until we're back below the replay length
                queue.Enqueue(message);
                while (queue.Count > replayQueueLength && queue.TryDequeue(out _)) { }
            } else {
                // we only reuse message objects if we're not recording a replay queue to avoid race conditions
                ReturnMessage(message);
            }
        }

        private DebugLog() {
            recordHoles = GetBoolEnvVar("MMLOG_RECORD_HOLES") ?? false;
            replayQueueLength = GetNumericEnvVar("MMLOG_REPLAY_QUEUE_LENGTH") ?? 0;
            var showSpamLogs = GetBoolEnvVar("MMLOG_SPAM") ?? false;
            if (showSpamLogs)
                globalFilter |= LogLevelFilter.Spam;

            if (replayQueueLength > 0) {
                replayQueue = new();
            }

        }

        static DebugLog() {
            var diskLogFile = GetStringEnvVar("MMLOG_OUT_FILE");
            var diskSourceFilter = GetListEnvVar("MMLOG_FILE_SOURCE_FILTER");

            if (diskLogFile is not null) {
                TryInitializeLogToFile(diskLogFile, diskSourceFilter);
            }

            if (Debugger.IsAttached) {
                AttachDebuggerLogSink();
            }
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "They need to stay alive for the life of the application.")]
        private static void TryInitializeLogToFile(string file, string[]? sourceFilter) {
            try {
                var comparer = StringComparerEx.FromComparison(StringComparison.OrdinalIgnoreCase);
                if (sourceFilter is not null)
                    Array.Sort(sourceFilter, comparer);

                object sync = new();
                TextWriter writer;
                if (file == "-") {
                    writer = Console.Out;
                } else {
                    var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Write);
                    writer = new StreamWriter(fs, Encoding.UTF8) {
                        AutoFlush = true
                    };
                }
                OnLog += (source, time, level, msg) => {
                    if (sourceFilter is not null) {
                        var idx = sourceFilter.AsSpan().BinarySearch(source, comparer);
                        if (idx < 0) // we didn't find the source in the filter list
                            return;
                    }

                    var realTime = time.ToLocalTime();
                    var outMsg = $"[{source}]({realTime}) {level.FastToString()}: {msg}";
                    // if we don't do this, on .NET 6, we'll sometimes get a corrupt buffer out
                    lock (sync) {
                        writer.WriteLine(outMsg);
                    }
                };
            } catch (Exception e) {
                Instance.LogCore("DebugLog", LogLevel.Error, $"Exception while trying to initialize writing logs to a file: {e}");
            }
        }

        private static void AttachDebuggerLogSink() {
            OnLog += static (source, time, level, msg) => {
                var realTime = time.ToLocalTime();
                Debugger.Log((int) level, source, $"[{source}] {level.FastToString()}: {msg}\n"); // the VS output window doesn't automatically add a newline
            };
        }

        #region Message Events
        private void MaybeReplayTo(OnLogMessage del) {
            if (replayQueue is null) {
                return;
            }

            var msgs = replayQueue.ToArray();
            // if we're recording message replays, we don't bother reusing message objects, so this is safe
            foreach (var msg in msgs) {
                msg.ReportTo(del);
            }
        }
        private void MaybeReplayTo(OnLogMessageDetailed del) {
            if (replayQueue is null) {
                return;
            }

            var msgs = replayQueue.ToArray();
            // if we're recording message replays, we don't bother reusing message objects, so this is safe
            foreach (var msg in msgs) {
                msg.ReportTo(del);
            }
        }

        internal bool ShouldLog => replayQueue is not null || Volatile.Read(ref onLogSimple) is not null || Volatile.Read(ref onLogDetailed) is not null;
        public static bool IsWritingLog => Instance.ShouldLog;

        private OnLogMessage? onLogSimple;
        private OnLogMessageDetailed? onLogDetailed;

        private event OnLogMessage OnLogSimple {
            add {
                var orig = onLogSimple;
                var del = orig;
                do {
                    del = (OnLogMessage) Delegate.Combine(del, value);
                } while (Interlocked.CompareExchange(ref onLogSimple, del, orig) != orig);
                MaybeReplayTo(value);
            }
            remove {
                var orig = onLogSimple;
                var del = orig;
                do {
                    del = (OnLogMessage?) Delegate.Remove(del, value);
                } while (Interlocked.CompareExchange(ref onLogSimple, del, orig) != orig);
            }
        }

        public static event OnLogMessage OnLog {
            add => Instance.OnLogSimple += value;
            remove => Instance.OnLogSimple -= value;
        }

        private event OnLogMessageDetailed OnLogDetailed {
            add {
                // if a detailed handler is ever subscribed, we need to start recording holes
                recordHoles = true;
                var orig = onLogDetailed;
                var del = orig;
                do {
                    del = (OnLogMessageDetailed) Delegate.Combine(del, value);
                } while (Interlocked.CompareExchange(ref onLogDetailed, del, orig) != orig);
                MaybeReplayTo(value);
            }
            remove {
                var orig = onLogDetailed;
                var del = orig;
                do {
                    del = (OnLogMessageDetailed?) Delegate.Remove(del, value);
                } while (Interlocked.CompareExchange(ref onLogDetailed, del, orig) != orig);
            }
        }

        public static event OnLogMessageDetailed OnDetailedLog {
            add => Instance.OnLogDetailed += value;
            remove => Instance.OnLogDetailed -= value;
        }
        #endregion
    }
}
