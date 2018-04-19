using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.BaseLoader {
    public static class Logger {

        /// <summary>
        /// The tag that appears in every logged message.
        /// </summary>
        public static string PreTag;

        /// <summary>
        /// Log a string to the console and to log.txt
        /// </summary>
        /// <param name="tag">The tag, preferably short enough to identify your mod, but not too long to clutter the log.</param>
        /// <param name="str">The string / message to log.</param>
        public static void Log(string tag, string str)
            => Log(LogLevel.Verbose, tag, str);
        /// <summary>
        /// Log a string to the console and to log.txt
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="tag">The tag, preferably short enough to identify your mod, but not too long to clutter the log.</param>
        /// <param name="str">The string / message to log.</param>
        public static void Log(LogLevel level, string tag, string str) {
            Console.Write("(");
            Console.Write(DateTime.Now);
            Console.Write(") [");
            if (!string.IsNullOrEmpty(PreTag)) {
                Console.Write(PreTag);
                Console.Write("] [");
            }
            Console.Write(level.ToString());
            Console.Write("] [");
            Console.Write(tag);
            Console.Write("] ");
            Console.WriteLine(str);
        }

    }
    public enum LogLevel {
        Verbose,
        Debug,
        Info,
        Warn,
        Error
    }
}
