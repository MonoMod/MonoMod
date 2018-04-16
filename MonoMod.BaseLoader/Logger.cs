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

        /// <summary>
        /// Print the exception to the console, including extended loading / reflection data useful for mods.
        /// </summary>
        public static void LogDetailed(this Exception e, string tag = null) {
            if (tag == null) {
                Console.WriteLine("--------------------------------");
                Console.WriteLine("Detailed exception log:");
            }
            for (Exception e_ = e; e_ != null; e_ = e_.InnerException) {
                Console.WriteLine("--------------------------------");
                Console.WriteLine(e_.GetType().FullName + ": " + e_.Message + "\n" + e_.StackTrace);
                if (e_ is ReflectionTypeLoadException) {
                    ReflectionTypeLoadException rtle = (ReflectionTypeLoadException) e_;
                    for (int i = 0; i < rtle.Types.Length; i++) {
                        Console.WriteLine("ReflectionTypeLoadException.Types[" + i + "]: " + rtle.Types[i]);
                    }
                    for (int i = 0; i < rtle.LoaderExceptions.Length; i++) {
                        LogDetailed(rtle.LoaderExceptions[i], tag + (tag == null ? "" : ", ") + "rtle:" + i);
                    }
                }
                if (e_ is TypeLoadException) {
                    Console.WriteLine("TypeLoadException.TypeName: " + ((TypeLoadException) e_).TypeName);
                }
                if (e_ is BadImageFormatException) {
                    Console.WriteLine("BadImageFormatException.FileName: " + ((BadImageFormatException) e_).FileName);
                }
            }
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
