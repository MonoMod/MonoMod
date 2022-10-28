using System;

namespace MonoMod.Logs {
    public enum LogLevel {
        Spam,
        Trace,
        Info,
        Warning,
        Error,
        Assert
    }

    [Flags]
    public enum LogLevelFilter {
        None = 0,
        Spam = 1 << (int)LogLevel.Spam,
        Trace = 1 << (int)LogLevel.Trace,
        Info = 1 << (int)LogLevel.Info,
        Warning = 1 << (int)LogLevel.Warning,
        Error = 1 << (int)LogLevel.Error,
        Assert = 1 << (int)LogLevel.Assert,

        DefaultFilter = (-1) & ~Spam,
    }

    public static class LogLevelExtensions {
        public const LogLevel MaxLevel = LogLevel.Assert;
        public static string FastToString(this LogLevel level, IFormatProvider? provider = null)
            => level switch {
                LogLevel.Spam => nameof(LogLevel.Spam),
                LogLevel.Trace => nameof(LogLevel.Trace),
                LogLevel.Info => nameof(LogLevel.Info),
                LogLevel.Warning => nameof(LogLevel.Warning),
                LogLevel.Error => nameof(LogLevel.Error),
                LogLevel.Assert => nameof(LogLevel.Assert),
                var x => ((int) x).ToString(provider)
            };
    }
}

