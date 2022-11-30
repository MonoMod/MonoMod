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
#if !HAS_APPCONTEXT_GETDATA || !HAS_APPCONTEXT_GETSWITCH
using System.Reflection;
using MonoMod.Utils;
#endif

namespace MonoMod {
    public static class Switches {
        private static readonly ConcurrentDictionary<string, object?> switchValues = new();

        private const string Prefix = "MONOMOD_";

        static Switches() {
            foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables()) {
                var key = (string)envVar.Key;
                if (key.StartsWith(Prefix, StringComparison.Ordinal) && envVar.Value is not null) {
                    var sw = key.Substring(Prefix.Length);
                    _ = switchValues.TryAdd(sw, BestEffortParseEnvVar((string) envVar.Value));
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

        /// <summary>
        /// Sets the value associated with a switch.
        /// </summary>
        /// <param name="switch">The switch to set the value of.</param>
        /// <param name="value">The value of the switch.</param>
        public static void SetSwitchValue(string @switch, object? value) {
            switchValues[@switch] = value;
        }

        /// <summary>
        /// Clears the specified switch.
        /// </summary>
        /// <remarks>
        /// The primary use of this method is to enable switch lookups to fall back to reading <see cref="AppContext"/>, if
        /// that is available on the current platform.
        /// </remarks>
        /// <param name="switch">The switch to clear.</param>
        public static void ClearSwitchValue(string @switch) {
            _ = switchValues.TryRemove(@switch, out _);
        }

#if !HAS_APPCONTEXT_GETDATA || !HAS_APPCONTEXT_GETSWITCH
        private static readonly Type? tAppContext =
#if HAS_APPCONTEXT
            typeof(AppContext);
#else
            typeof(AppDomain).Assembly.GetType("System.AppContext");
#endif

        private static T? TryCreateDelegate<T>(MethodInfo? mi) where T : Delegate
        {
            try
            {
                return mi?.CreateDelegate<T>();
            }
            catch
            {
                // ignore
                return null;
            }
        }
#endif
#if !HAS_APPCONTEXT_GETDATA
        private static readonly MethodInfo? miGetData = tAppContext?.GetMethod("GetData",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(string) }, null);
        private static readonly Func<string, object?>? dGetData = TryCreateDelegate<Func<string, object?>>(miGetData);
#endif
#if !HAS_APPCONTEXT_GETSWITCH
        private delegate bool TryGetSwitchFunc(string @switch, out bool isEnabled);
        private static readonly MethodInfo? miTryGetSwitch = tAppContext?.GetMethod("TryGetSwitch",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(string), typeof(bool).MakeByRefType() }, null);
        private static readonly TryGetSwitchFunc? dTryGetSwitch = TryCreateDelegate<TryGetSwitchFunc>(miTryGetSwitch);
#endif

        public static bool TryGetSwitchValue(string @switch, out object? value) {
            // always check our stuff first
            if (switchValues.TryGetValue(@switch, out value)) {
                return true;
            }

#if !HAS_APPCONTEXT
            if (dGetData is not null || dTryGetSwitch is not null)
#endif
            {
                var appCtxSwitchName = "MonoMod." + @switch;

                object? res;
#if HAS_APPCONTEXT_GETDATA
                res = AppContext.GetData(appCtxSwitchName);
#else
                res = dGetData?.Invoke(appCtxSwitchName);
#endif
                if (res is not null) {
                    value = res;
                    return true;
                }

#if HAS_APPCONTEXT_GETSWITCH
                if (AppContext.TryGetSwitch(appCtxSwitchName, out var switchEnabled))
#else
                if (dTryGetSwitch is { } tryGetSwitch && tryGetSwitch(appCtxSwitchName, out var switchEnabled))
#endif
                {
                    value = switchEnabled;
                    return true;
                }
            }

            value = null;
            return false;
        }
        
        // TODO: how do I want to handle setting and caching of this stuff?

        public static bool TryGetSwitchEnabled(string @switch, out bool isEnabled) {
            // always check our stuff first
            if (switchValues.TryGetValue(@switch, out var orig)) {
                if (orig is not null && TryProcessBoolData(orig, out isEnabled)) {
                    return true;
                }
                // don't konw what to do with the value, so simply fall out
            }

#if !HAS_APPCONTEXT
            if (dGetData is not null || dTryGetSwitch is not null)
#endif
            {
                var appCtxSwitchName = "MonoMod." + @switch;

#if HAS_APPCONTEXT_GETSWITCH
                if (AppContext.TryGetSwitch(appCtxSwitchName, out isEnabled))
#else
                if (dTryGetSwitch is { } tryGetSwitch && tryGetSwitch(appCtxSwitchName, out isEnabled))
#endif
                {
                    return true;
                }

                object? res;
#if HAS_APPCONTEXT_GETDATA
                res = AppContext.GetData(appCtxSwitchName);
#else
                res = dGetData?.Invoke(appCtxSwitchName);
#endif
                if (res is not null && TryProcessBoolData(res, out isEnabled)) {
                    return true;
                }
            }

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
