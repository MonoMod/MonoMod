#nullable enable
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

// This class is included in every MonoMod assembly.
namespace MonoMod {
    internal static class MMDbgLog {

        public static readonly string Tag = typeof(MMDbgLog).Assembly.GetName().Name ?? "MonoMod";

        public static TextWriter? Writer;

        public static bool Debugging;

        static MMDbgLog() {
            var enabled =
#if MONOMOD_DBGLOG
                true;
#else
                Environment.GetEnvironmentVariable("MONOMOD_DBGLOG") == "1" ||
                (Environment.GetEnvironmentVariable("MONOMOD_DBGLOG")?.ToUpperInvariant()?.Contains(Tag.ToUpperInvariant(), StringComparison.Ordinal) ?? false);
#endif

            if (enabled)
                Start();
        }

        public static void WaitForDebugger() {
            // When in doubt, enable this debugging helper block, add Debugger.Break() where needed and attach WinDbg quickly.
            if (!Debugging) {
                Debugging = true;
                // WinDbg doesn't trigger Debugger.IsAttached
                _ = Debugger.Launch();
                Thread.Sleep(6000);
                Debugger.Break();
            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "If the IO in the try blocks in this method fails, we don't want everything to break. We just continue without logging.")]
        public static void Start() {
            if (Writer != null)
                return;

            var path = Environment.GetEnvironmentVariable("MONOMOD_DBGLOG_PATH");
            if (path == "-") {
                Writer = Console.Out;
                return;
            }

            if (string.IsNullOrEmpty(path))
                path = "mmdbglog.txt";
            path = Path.GetFullPath($"{Path.GetFileNameWithoutExtension(path)}-{Tag}{Path.GetExtension(path)}");

            try {
                if (File.Exists(path))
                    File.Delete(path);
            } catch { }
            try {
                var dir = Path.GetDirectoryName(path);
                if (dir is not null && !Directory.Exists(dir))
                    _ = Directory.CreateDirectory(dir);
                Writer = new StreamWriter(new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8) {
                    AutoFlush = true
                };
            } catch { }
        }

        public static void Log(string str) {
            if (Writer is not { } w)
                return;

            w.WriteLine(str);
            w.Flush();
        }

        public static T Log<T>(string str, T value) {
            if (Writer is not { } w)
                return value;

            w.WriteLine(string.Format(CultureInfo.InvariantCulture, str, value));
            w.Flush();
            return value;
        }

    }
}
