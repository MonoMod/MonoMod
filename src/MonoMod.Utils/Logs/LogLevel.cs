using System;

namespace MonoMod.Logs
{
    public enum LogLevel
    {
        Spam,
        Trace,
        Info,
        Warning,
        Error,
        Assert
    }

    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2217:Do not mark enums with FlagsAttribute",
        Justification = "This is a flags, just with one value which is all of them.")]
    public enum LogLevelFilter
    {
        None = 0,
        Spam = 1 << LogLevel.Spam,
        Trace = 1 << LogLevel.Trace,
        Info = 1 << LogLevel.Info,
        Warning = 1 << LogLevel.Warning,
        Error = 1 << LogLevel.Error,
        Assert = 1 << LogLevel.Assert,

        DefaultFilter = (-1) & ~Spam,
    }

    public static class LogLevelExtensions
    {
        public const LogLevel MaxLevel = LogLevel.Assert;
        public static string FastToString(this LogLevel level, IFormatProvider? provider = null)
            => level switch
            {
                LogLevel.Spam => nameof(LogLevel.Spam),
                LogLevel.Trace => nameof(LogLevel.Trace),
                LogLevel.Info => nameof(LogLevel.Info),
                LogLevel.Warning => nameof(LogLevel.Warning),
                LogLevel.Error => nameof(LogLevel.Error),
                LogLevel.Assert => nameof(LogLevel.Assert),
                var x => ((int)x).ToString(provider)
            };
    }
}

