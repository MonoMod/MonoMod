#if NETCOREAPP || NET6_0_OR_GREATER || NET46_OR_GREATER || NETSTANDARD1_3_OR_GREATER
#define HAS_APPCONTEXT_GETSWITCH
#define HAS_APPCONTEXT
#endif

#if NETCOREAPP || NET6_0_OR_GREATER || NET47_OR_GREATER
#define HAS_APPCONTEXT_GETDATA
#define HAS_APPCONTEXT
#endif

using System;
using System.Collections.Concurrent;
using System.Collections;
using System.Globalization;

namespace MonoMod {
    public static class Switches {
        private static readonly ConcurrentDictionary<string, object?> envSwitches = new();

        private const string Prefix = "MONOMOD_";

        static Switches() {
            foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables()) {
                var key = (string)envVar.Key;
                if (key.StartsWith(Prefix, StringComparison.Ordinal) && envVar.Value is not null) {
                    var sw = key.Substring(Prefix.Length);
                    _ = envSwitches.TryAdd(sw, BestEffortParseEnvVar((string) envVar.Value));
                }
            }
        }

        private static object? BestEffortParseEnvVar(string value) {
            if (value.Length is 0)
                return null;

            // try to parse as a number
            if (int.TryParse(value, NumberStyles.AllowHexSpecifier | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ires)) {
                return ires;
            }

            if (long.TryParse(value, NumberStyles.AllowHexSpecifier | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var lres)) {
                return lres;
            }

            // next, check for a possible boolean
            if (value[0] is 't' or 'T' or 'f' or 'F' or 'y' or 'Y' or 'n' or 'N') {
                if (bool.TryParse(value, out var bresult))
                    return bresult;
                if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("y", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
                if (value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("n", StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            // otherwise unknown, just return the string
            return value;
        }

        #region MonoMod Switches
        public const string LogRecordHoles = "LogRecordHoles";
        public const string LogInMemory = "LogInMemory";
        public const string LogSpam = "LogSpam";
        public const string LogReplayQueueLength = "LogReplayQueueLength";
        public const string LogToFile = "LogToFile";
        public const string LogToFileFilter = "LogToFileFilter";

        public const string DMDType = "DMDType";
        public const string DMDDebug = "DMDDebug";
        public const string DMDDumpTo = "DMDDumpTo";
        #endregion

        public static bool TryGetSwitchValue(string @switch, out object? value) {
            // always check our stuff first
            if (envSwitches.TryGetValue(@switch, out value)) {
                return true;
            }

#if HAS_APPCONTEXT
            var appCtxSwitchName = "MonoMod." + @switch;
#endif
#if HAS_APPCONTEXT_GETDATA
            var res = AppContext.GetData(appCtxSwitchName);
            if (res is not null) {
                value = res;
                return true;
            }
#endif
#if HAS_APPCONTEXT_GETSWITCH
            if (AppContext.TryGetSwitch(appCtxSwitchName, out var switchEnabled)) {
                value = switchEnabled;
                return true;
            }
#endif

            value = null;
            return false;
        }
        
        // TODO: how do I want to handle setting and caching of this stuff?

        public static bool TryGetSwitchEnabled(string @switch, out bool isEnabled) {
            // always check our stuff first
            if (envSwitches.TryGetValue(@switch, out var orig)) {
                if (orig is not null && TryProcessBoolData(orig, out isEnabled)) {
                    return true;
                }
                // don't konw what to do with the value, so simply fall out
            }

#if HAS_APPCONTEXT
            var appCtxSwitchName = "MonoMod." + @switch;
#endif
#if HAS_APPCONTEXT_GETSWITCH
            if (AppContext.TryGetSwitch(appCtxSwitchName, out isEnabled)) {
                return true;
            }
#endif
#if HAS_APPCONTEXT_GETDATA
            var res = AppContext.GetData(appCtxSwitchName);
            if (res is not null && TryProcessBoolData(res, out isEnabled)) {
                return true;
            }
#endif

            isEnabled = false;
            return false;
        }

        private static bool TryProcessBoolData(object data, out bool boolVal) {
            switch (data) {
                case bool b:
                    boolVal = b;
                    return true;

                case int i:
                    boolVal = i != 0;
                    return true;

                case long i:
                    boolVal = i != 0;
                    return true;

                case string s when bool.TryParse(s, out boolVal):
                    return true;

                case IConvertible conv:
                    boolVal = conv.ToBoolean(CultureInfo.CurrentCulture);
                    return true;

                default:
                    boolVal = false;
                    return false;
            }
        }
    }
}
