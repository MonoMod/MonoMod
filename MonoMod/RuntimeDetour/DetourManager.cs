using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;
using System.Linq.Expressions;

namespace MonoMod.RuntimeDetour {
    public static class DetourManager {

        public static IDetourRuntimePlatform Runtime;
        public static IDetourNativePlatform Native;

        private static LongDictionary<DynamicMethod> Things;

        static DetourManager() {
            if (Type.GetType("Mono.Runtime") != null) {
                Runtime = new DetourRuntimeMonoPlatform();
            } else {
                Runtime = new DetourRuntimeNETPlatform();
            }

            // TODO: Detect X86 vs ARM, implement NativeARMPlatform
            Native = new DetourNativeX86Platform();
            if ((PlatformHelper.Current & Platform.Windows) == Platform.Windows) {
                Native = new DetourNativeWindowsPlatform(Native);
            }
            // TODO: Do Linux, macOS and other systems require protection lifting?
        }

        public static IntPtr GetMethodStart(this MethodBase method)
            => Runtime.GetMethodStart(method);
        public static IntPtr GetMethodStart(this Delegate method)
            => method.Method.GetMethodStart();
        public static IntPtr GetMethodStart(this Expression method)
            => (method as MethodCallExpression).Method.GetMethodStart();

    }
}
