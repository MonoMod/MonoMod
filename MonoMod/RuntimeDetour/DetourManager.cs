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

        #region Native helpers

        public static unsafe void Write(this IntPtr to, ref int offs, byte value) {
            *((byte*) ((long) to + offs)) = value;
            offs += 1;
        }
        public static unsafe void Write(this IntPtr to, ref int offs, ushort value) {
            *((ushort*) ((long) to + offs)) = value;
            offs += 2;
        }
        public static unsafe void Write(this IntPtr to, ref int offs, uint value) {
            *((uint*) ((long) to + offs)) = value;
            offs += 4;
        }
        public static unsafe void Write(this IntPtr to, ref int offs, ulong value) {
            *((ulong*) ((long) to + offs)) = value;
            offs += 8;
        }

        #endregion

        #region Method-related helpers

        public static IntPtr GetJITStart(this MethodBase method)
            => Runtime.GetExecutableStart(method);
        public static IntPtr GetJITStart(this Delegate method)
            => method.Method.GetJITStart();
        public static IntPtr GetJITStart(this Expression method)
            => ((MethodCallExpression) method).Method.GetJITStart();

        public static DynamicMethod CreateILCopy(this MethodBase method)
            => Runtime.CreateCopy(method);

        #endregion

        #region DynamicMethod IL generation helpers

        private static NativeDetourData ToNativeDetourData(IntPtr method, IntPtr target, int size, IntPtr extra)
            => new NativeDetourData {
                Method = method,
                Target = target,
                Size = size,
                Extra = extra
            };

        private readonly static FieldInfo _Native = typeof(DetourManager).GetField("Native");
        private readonly static MethodInfo _ToNativeDetourData = typeof(DetourManager).GetMethod("ToNativeDetourData", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly static MethodInfo _Copy = typeof(IDetourNativePlatform).GetMethod("Copy");
        private readonly static MethodInfo _Apply = typeof(IDetourNativePlatform).GetMethod("Apply");

        public static void EmitDetourCopy(this ILGenerator il, IntPtr src, IntPtr dst, int size) {
            // Load NativePlatform instance.
            il.Emit(OpCodes.Ldsfld, _Native);

            // Fill stack with src, dst, size
            il.Emit(OpCodes.Ldc_I8, (long) src);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I8, (long) dst);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I4, size);

            // Copy.
            il.Emit(OpCodes.Callvirt, _Copy);
        }

        public static void EmitDetourApply(this ILGenerator il, NativeDetourData data) {
            // Load NativePlatform instance.
            il.Emit(OpCodes.Ldsfld, _Native);

            // Fill stack with data values.
            il.Emit(OpCodes.Ldc_I8, (long) data.Method);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I8, (long) data.Target);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I4, data.Size);
            il.Emit(OpCodes.Ldc_I8, (long) data.Extra);
            il.Emit(OpCodes.Conv_I);

            // Put values in stack into NativeDetourData.
            il.Emit(OpCodes.Call, _ToNativeDetourData);

            // Apply.
            il.Emit(OpCodes.Callvirt, _Apply);
        }

        #endregion

    }
}
