using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MonoMod.DebugIL
{
    public sealed class DebugILWriter : IDisposable
    {

        public static readonly Regex PathVerifyRegex =
            new Regex("[" + Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars())) + "]", RegexOptions.Compiled);

        public string FullPath { get; set; }
        public StreamWriter Writer { get; set; }
        // Sequencee point lines start at 1
        public int Line { get; set; } = 1;

        public DebugILWriter(string parent, string name, int index = 0, string ext = "il")
        {
            name = PathVerifyRegex.Replace(name, "_");
            FullPath = Path.Combine(parent, index <= 0 ? $"{name}.{ext}" : $"{name}.{index}.{ext}");

            Directory.CreateDirectory(Path.GetDirectoryName(FullPath));
            Writer = new StreamWriter(File.OpenWrite(FullPath));
        }

        public void Dispose()
        {
            Writer?.Dispose();
            Writer = null;
        }

        public void Write(string value)
        {
            Writer.Write(value);
        }

        public void WriteLine()
        {
            Writer.WriteLine();
            Line++;
        }

        public void WriteLine(string value)
        {
            Writer.WriteLine(value);
        }

    }
}
