using MonoMod.Utils;
using System.Reflection;

namespace MonoMod.RuntimeDetour.Utils {
    internal static class Extensions {
        public static MethodBase CreateILCopy(this MethodBase method) {
            using var dmd = new DynamicMethodDefinition(method);
            return dmd.Generate();
        }
    }
}
