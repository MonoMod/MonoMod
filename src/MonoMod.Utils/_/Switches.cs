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
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod {
    public static class Switches {
        private static readonly ConcurrentDictionary<string, StrongBox<object>> envSwitches = new();

        private const string Prefix = "MONOMOD_";

        static Switches() {
            foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables()) {
                var key = (string)envVar.Key;
                if (key.StartsWith(Prefix, StringComparison.Ordinal) && envVar.Value is not null) {
                    var sw = key.Substring(Prefix.Length);
                    _ = envSwitches.TryAdd(sw, new(envVar.Value));
                }
            }
        }

        private static bool TryParseEnvBool(string value,  out bool result) {
            var str = value?.Trim();
            if (string.IsNullOrEmpty(str)) {
                result = false;
                return false;
            }
            Helpers.DAssert(str is not null);
            if (bool.TryParse(str, out result))
                return true;
            if (int.TryParse(str, out var iresult)) {
                result = iresult != 0;
                return true;
            }
            if (str.Equals("yes", StringComparison.OrdinalIgnoreCase) || str.Equals("y", StringComparison.OrdinalIgnoreCase)) {
                result = true;
                return true;
            }
            if (str.Equals("no", StringComparison.OrdinalIgnoreCase) || str.Equals("n", StringComparison.OrdinalIgnoreCase)) {
                result = false;
                return true;
            }
            result = false;
            return false;
        }

        public static bool TryGetSwitchValue(string @switch, [NotNullWhen(true)] out object? value) {
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

            if (envSwitches.TryGetValue(@switch, out var boxValue)) {
                value = boxValue.Value!;
                return true;
            }

            value = null;
            return false;
        }
        
        // TODO: how do I want to handle setting and caching of this stuff?

        public static bool TryGetSwitchEnabled(string @switch, out bool isEnabled) {
#if HAS_APPCONTEXT_GETSWITCH
            if (AppContext.TryGetSwitch("MonoMod." + @switch, out isEnabled)) {
                return true;
            }
#endif

            if (envSwitches.TryGetValue(@switch, out var box)) {
                var orig = box.Value!;
                if (orig is bool bval) {
                    isEnabled = bval;
                    return true;
                }
                if (orig is string sval) {
                    if (TryParseEnvBool(sval, out var res)) {
                        isEnabled = res;
                        return true;
                    }
                }
                // don't konw what to do with the value, so simply fall out
            }

            isEnabled = false;
            return false;
        }
    }
}
