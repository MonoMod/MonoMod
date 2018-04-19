using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;

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

        /// <summary>
        /// Write the given value at the address to + offs, afterwards advancing offs by sizeof(byte).
        /// </summary>
        public static unsafe void Write(this IntPtr to, ref int offs, byte value) {
            *((byte*) ((long) to + offs)) = value;
            offs += 1;
        }
        /// <summary>
        /// Write the given value at the address to + offs, afterwards advancing offs by sizeof(ushort).
        /// </summary>
        public static unsafe void Write(this IntPtr to, ref int offs, ushort value) {
            *((ushort*) ((long) to + offs)) = value;
            offs += 2;
        }
        /// <summary>
        /// Write the given value at the address to + offs, afterwards advancing offs by sizeof(ushort).
        /// </summary>
        public static unsafe void Write(this IntPtr to, ref int offs, uint value) {
            *((uint*) ((long) to + offs)) = value;
            offs += 4;
        }
        /// <summary>
        /// Write the given value at the address to + offs, afterwards advancing offs by sizeof(ulong).
        /// </summary>
        public static unsafe void Write(this IntPtr to, ref int offs, ulong value) {
            *((ulong*) ((long) to + offs)) = value;
            offs += 8;
        }

        #endregion

        #region Method-related helpers

        /// <summary>
        /// Get a pointer to the start of the executable section of the method. Normally, this is the JITed "native" function.
        /// Note: DetourManager.GetNativeStart silently pins your method. If you want to skip pinning, use DetourManager.Runtime.GetNativeStart directly.
        /// </summary>
        public static IntPtr GetNativeStart(this MethodBase method)
            => Runtime.GetNativeStart(method.Pin());
        public static IntPtr GetNativeStart(this Delegate method)
            => method.Method.GetNativeStart();
        public static IntPtr GetNativeStart(this Expression method)
            => ((MethodCallExpression) method).Method.GetNativeStart();

        public static DynamicMethod CreateILCopy(this MethodBase method)
            => Runtime.CreateCopy(method);

        public static T Pin<T>(this T method) where T : MethodBase {
            Runtime.Pin(method);
            return method;
        }

        #endregion

        #region DynamicMethod generation helpers

        /// <summary>
        /// Generate a DynamicMethod to easily call the given native function from another DynamicMethod.
        /// </summary>
        /// <param name="target">The pointer to the native function to call.</param>
        /// <param name="signature">A MethodBase with the target function's signature.</param>
        /// <returns>The detoured DynamicMethod.</returns>
        public static DynamicMethod GenerateNativeProxy(IntPtr target, MethodBase signature) {
            Type returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);

            ParameterInfo[] args = signature.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            DynamicMethod dm = new DynamicMethod(
                $"native_{((long) target).ToString("X16")}",
                returnType, argTypes,
                true
            ).StubCriticalDetour();

            // Detour the new DynamicMethod into the target.
            NativeDetourData detour = Native.Create(dm.GetNativeStart(), target);
            Native.MakeWritable(detour);
            Native.Apply(detour);
            Native.MakeExecutable(detour);
            Native.Free(detour);

            return dm.Pin();
        }

        // Used in EmitDetourApply.
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
        private readonly static ConstructorInfo _ctor_Exception = typeof(Exception).GetConstructor(new Type[] { typeof(string) });

        /// <summary>
        /// Fill the DynamicMethod with a throw.
        /// </summary>
        public static DynamicMethod StubCriticalDetour(this DynamicMethod dm) {
            ILGenerator il = dm.GetILGenerator();
            for (int i = 0; i < 10; i++) {
                // Prevent old Unity mono from inlining the DynamicMethod.
                il.Emit(OpCodes.Nop);
            }
            il.Emit(OpCodes.Ldstr, $"{dm.Name} should've been detoured!");
            il.Emit(OpCodes.Newobj, _ctor_Exception);
            il.Emit(OpCodes.Throw);
            return dm;
        }

        /// <summary>
        /// Emit a call to DetourManager.Native.Copy using the given parameters.
        /// </summary>
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

        /// <summary>
        /// Emit a call to DetourManager.Native.Apply using a copy of the given data.
        /// </summary>
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
