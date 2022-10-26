using System;

namespace MonoMod.Logs {
    public enum LogLevel {
        Trace,
        Info,
        Warning,
        Error,
        Assert
    }

    public static class LogLevelExtensions {
        public static string FastToString(this LogLevel level, IFormatProvider? provider = null)
            => level switch {
                LogLevel.Trace => nameof(LogLevel.Trace),
                LogLevel.Info => nameof(LogLevel.Info),
                LogLevel.Warning => nameof(LogLevel.Warning),
                LogLevel.Error => nameof(LogLevel.Error),
                LogLevel.Assert => nameof(LogLevel.Assert),
                var x => ((int) x).ToString(provider)
            };
    }
}

