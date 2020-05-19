using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

// This class is included in every MonoMod assembly.
namespace MonoMod {
    internal static class MMDbgLog {

        public static readonly string Tag = typeof(MMDbgLog).Assembly.GetName().Name;

        public static TextWriter Writer;

        static MMDbgLog() {
            bool enabled =
#if MONOMOD_DBGLOG
                true;
#else
                Environment.GetEnvironmentVariable("MONOMOD_DBGLOG") == "1" ||
                (Environment.GetEnvironmentVariable("MONOMOD_DBGLOG")?.ToLowerInvariant()?.Contains(Tag.ToLowerInvariant()) ?? false);
#endif

            if (enabled)
                Start();
        }

        public static void Start() {
            if (Writer != null)
                return;

            string path = Environment.GetEnvironmentVariable("MONOMOD_DBGLOG_PATH");
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
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                Writer = new StreamWriter(new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8);
            } catch { }
        }

        public static void Log(string str) {
            Writer?.WriteLine(str);
        }

    }
}
