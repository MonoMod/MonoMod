#if !CECIL0_9
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonoMod.DebugIL {
    public class DebugILWriter : IDisposable {

        public static readonly Regex PathVerifyRegex =
            new Regex("[" + Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars())) + "]", RegexOptions.Compiled);

        public string FullPath;
        public StreamWriter Writer;

        // SequencePoint indices start at 1.
        public int Line = 1;

        public DebugILWriter(string parent, string name, int index = 0, string ext = "il") {
            name = PathVerifyRegex.Replace(name, "_");
            FullPath = Path.Combine(parent, index <= 0 ? $"{name}.{ext}" : $"{name}.{index}.{ext}");

            Directory.CreateDirectory(Path.GetDirectoryName(FullPath));
            Writer = new StreamWriter(File.OpenWrite(FullPath));
        }

        public void Dispose() {
            Writer?.Dispose();
            Writer = null;
        }

        public void Write(string value) {
            Writer.Write(value);
        }

        public void WriteLine() {
            Writer.WriteLine();
            Line++;
        }

        public void WriteLine(string value) {
            Writer.WriteLine(value);
        }

    }
}
#endif
