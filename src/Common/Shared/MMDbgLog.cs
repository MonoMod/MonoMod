#nullable enable

// This class is included in every MonoMod assembly.
namespace MonoMod {
    internal static class MMDbgLog {

        public const string Tag = AssemblyInfo.AssemblyName;

        public static bool IsWritingLog => DebugLog.IsWritingLog;

        static MMDbgLog() {
            Log($"Version {AssemblyInfo.AssemblyVersion}");
        }

        public static void Log(string message) {
            DebugLog.Log(AssemblyInfo.AssemblyName, message);
        }
        public static void Log(ref DebugLogInterpolatedStringHandler message) {
            DebugLog.Log(AssemblyInfo.AssemblyName, ref message);
        }

    }
}
