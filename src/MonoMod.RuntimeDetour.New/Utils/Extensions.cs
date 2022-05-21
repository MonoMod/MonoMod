using MonoMod.Backports;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.RuntimeDetour.Utils {
    internal static class Extensions {
        public static MethodBase CreateILCopy(this MethodBase method) {
            using var dmd = new DynamicMethodDefinition(method);
            return dmd.Generate();
        }
    }
}
