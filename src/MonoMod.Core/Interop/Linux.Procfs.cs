// Based on https://github.com/dotnet/runtime/blob/48a162cff4bdfc77d7d7767497b073481febf1d8/src/libraries/Common/src/Interop/Linux/procfs/Interop.ProcFsStat.ParseMapModules.cs

using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace MonoMod.Core.Interop;

internal static class Linux
{
    internal static class Procfs
    {
        private const string RootPath = "/proc/";
        private const string Self = "self";

        private const string MapsFileName = "/maps";

        internal enum ProcPid
        {
            Invalid = -1,
            Self = 0,
        }

        private static string GetMapsFilePathForProcess(ProcPid pid) =>
            pid == ProcPid.Self ? $"{RootPath}{Self}{MapsFileName}" : $"{RootPath}{(uint)pid}{MapsFileName}";

        internal struct Module(ulong startAddress, ulong size, string path)
        {
            public ulong StartAddress { get; } = startAddress;
            public ulong Size { get; internal set; } = size;
            public string Path { get; } = path;
        }

        internal static IEnumerable<Module>? ParseMapsModules(ProcPid pid)
        {
            try
            {
                return ParseMapsModulesCore(File.ReadAllLines(GetMapsFilePathForProcess(pid)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return null;
        }

        private static List<Module> ParseMapsModulesCore(IEnumerable<string> lines)
        {
            Debug.Assert(lines != null);

            Module module = default;
            var modules = new List<Module>();
            var moduleHasReadAndExecFlags = false;

            foreach (var line in lines!)
            {
                if (!TryParseMapsEntry(line, out var parsedLine))
                {
                    // Invalid entry for the purposes of ProcessModule parsing,
                    // discard flushing the current module if it exists.
                    CommitCurrentModule();
                    continue;
                }

                // Check if entry is a continuation of the current module.
                if (module.Size > 0 &&
                    module.Path == parsedLine.Path &&
                    module.StartAddress + module.Size == parsedLine.StartAddress)
                {
                    // Is continuation, update the current module.
                    module.Size += parsedLine.Size;
                    moduleHasReadAndExecFlags |= parsedLine.HasReadAndExecFlags;
                    continue;
                }

                // Not a continuation, commit any current modules and create a new one.
                CommitCurrentModule();

                module = new Module(parsedLine.StartAddress, parsedLine.Size, parsedLine.Path);
                moduleHasReadAndExecFlags = parsedLine.HasReadAndExecFlags;
            }

            // Commit any pending modules.
            CommitCurrentModule();

            return modules;

            void CommitCurrentModule()
            {
                // we only add module to collection, if at least one row had 'r' and 'x' set.
                if (moduleHasReadAndExecFlags && module.Size > 0)
                {
                    modules.Add(module);
                    module = default;
                }
            }
        }

        private static bool TryParseMapsEntry(string line, out (ulong StartAddress, ulong Size, bool HasReadAndExecFlags, string Path) parsedLine)
        {
            // Use a StringParser to avoid string.Split costs
            var parser = new StringParser(line, separator: ' ', skipEmpty: true);

            // Parse the address start and size
            var (start, size) = parser.ParseRaw(TryParseAddressRange);

            if (size <= 0)
            {
                parsedLine = default;
                return false;
            }

            // Parse the permissions
            var lineHasReadAndExecFlags = parser.ParseRaw(HasReadAndExecFlags);

            // Skip past the offset, dev, and inode fields
            parser.MoveNext();
            parser.MoveNext();
            parser.MoveNext();

            // we only care about the named modules
            if (!parser.MoveNext())
            {
                parsedLine = default;
                return false;
            }

            // Parse the pathname
            var pathname = parser.ExtractCurrentToEnd();
            parsedLine = (start, size, lineHasReadAndExecFlags, pathname);
            return true;

            static (ulong Start, ulong Size) TryParseAddressRange(string s, ref int start, ref int end)
            {
                var pos = s.IndexOf('-', start, end - start);
                if (pos > 0)
                {
                    if (ulong.TryParse(s.AsSpan(start, pos).ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var startingAddress) &&
                        ulong.TryParse(s.AsSpan(pos + 1, end - (pos + 1)).ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var endingAddress))
                    {
                        return (startingAddress, endingAddress - startingAddress);
                    }
                }

                return (0, 0);
            }

            static bool HasReadAndExecFlags(string s, ref int start, ref int end)
            {
                var span = s.AsSpan(start, end - start);
                return span.IndexOf('r') > -1 && span.IndexOf('x') > -1;
            }
        }
    }
}
