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

namespace MonoMod.RuntimeDetour {
    public static class DetourManager {

        public static IDetourRuntimePlatform RuntimePlatform;
        public static IDetourNativePlatform NativePlatform;

        static DetourManager() {
            if (Type.GetType("Mono.Runtime") != null) {
                RuntimePlatform = new DetourRuntimeMonoPlatform();
            } else {
                RuntimePlatform = new DetourRuntimeNETPlatform();
            }

            // TODO: Detect X86 vs ARM, implement NativeARMPlatform
            NativePlatform = new DetourNativeX86Platform();
            if ((PlatformHelper.Current & Platform.Windows) == Platform.Windows) {
                NativePlatform = new DetourNativeWindowsPlatform(NativePlatform);
            }
        }

        private readonly static unsafe LongDictionary<MethodBase> _TokenToMethod = new LongDictionary<MethodBase>();
        private readonly static unsafe HashSet<MethodBase> _Tokenized = new HashSet<MethodBase>();

        public static void CreateDetourToken(MethodBase method) {
            if (_Tokenized.Contains(method))
                return;
            _Tokenized.Add(method);

            // DynamicMethod can get disposed.
            if (method is DynamicMethod)
                return;
            long token = method.GetDetourToken();
            _TokenToMethod[token] = method;
        }

        public static IntPtr GetMethodStart(long token)
            => GetMethodStart(_TokenToMethod[token]);
        public static IntPtr GetMethodStart(this MethodBase method)
            => RuntimePlatform.GetMethodStart(method);

        public static unsafe long GetDetourToken(this MethodBase method)
            => (long) ((ulong) method.MetadataToken) << 32 | (
                (uint) ((method.Module.Name.GetHashCode() << 5) + method.Module.Name.GetHashCode()) ^
                (uint) method.Module.Assembly.FullName.GetHashCode()
            );

        public static long GetDetourToken(this Mono.Cecil.MethodReference method)
            => (long) ((ulong) method.MetadataToken.ToInt32()) << 32 | (
                (uint) ((method.Module.Name.GetHashCode() << 5) + method.Module.Name.GetHashCode()) ^
                (uint) method.Module.Assembly.FullName.GetHashCode()
            );

        

    }
}
