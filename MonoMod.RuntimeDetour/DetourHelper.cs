using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using MonoMod.RuntimeDetour.Platforms;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour {
    public static class DetourHelper {

        private static readonly object _RuntimeLock = new object();
        private static IDetourRuntimePlatform _Runtime;
        public static IDetourRuntimePlatform Runtime {
            get {
                lock (_RuntimeLock) {
                    if (_Runtime != null)
                        return _Runtime;

                    if (Type.GetType("Mono.Runtime") != null) {
                        _Runtime = new DetourRuntimeMonoPlatform();
                    } else {
                        _Runtime = new DetourRuntimeNETPlatform();
                    }

                    return _Runtime;
                }
            }
            set => _Runtime = value;
        }

        private static readonly object _NativeLock = new object();
        private static IDetourNativePlatform _Native;
        public static IDetourNativePlatform Native {
            get {
                lock (_NativeLock) {
                    if (_Native != null)
                        return _Native;

                    if (PlatformHelper.Is(Platform.ARM)) {
                        _Native = new DetourNativeARMPlatform();
                    } else {
                        _Native = new DetourNativeX86Platform();
                    }

                    if (Type.GetType("Mono.Runtime") != null) {
                        try {
                            // It's prefixed with lib on every platform.
                            _Native = new DetourNativeMonoPlatform(_Native, $"libmonosgen-2.0.{PlatformHelper.LibrarySuffix}");
                            return _Native;
                        } catch {
                            // Fall back to another native platform wrapper.
                        }
                    }

                    if (PlatformHelper.Is(Platform.Windows)) {
                        _Native = new DetourNativeWindowsPlatform(_Native);
                    } else if (PlatformHelper.Is(Platform.Unix)) {
                        if (Type.GetType("Mono.Runtime") != null) {
                            try {
                                _Native = new DetourNativeMonoPosixPlatform(_Native);
                            } catch {
                                // If you're reading this: Good job, your copy of Mono doesn't ship with MonoPosixHelper.
                                // https://www.youtube.com/watch?v=l60MnDJklnM
                            }
                        } else {
                            // TODO: .NET Core Posix native platform wrapper.
                        }
                    }

                    return _Native;
                }
            }
            set => _Native = value;
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
            => method.GetMethodInfo().GetNativeStart();
        public static IntPtr GetNativeStart(this Expression method)
            => ((MethodCallExpression) method).Method.GetNativeStart();

        public static MethodInfo CreateILCopy(this MethodBase method)
            => Runtime.CreateCopy(method);
        public static bool TryCreateILCopy(this MethodBase method, out MethodInfo dm)
            => Runtime.TryCreateCopy(method, out dm);

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
        public static MethodInfo GenerateNativeProxy(IntPtr target, MethodBase signature) {
            Type returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);

            ParameterInfo[] args = signature.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            MethodInfo dm;
            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"Native<{((long) target).ToString("X16")}>",
                returnType, argTypes
            ))
                dm = dmd.StubCriticalDetour().Generate().Pin();

            // Detour the new DynamicMethod into the target.
            NativeDetourData detour = Native.Create(dm.GetNativeStart(), target);
            Native.MakeWritable(detour);
            Native.Apply(detour);
            Native.MakeExecutable(detour);
            Native.Free(detour);

            return dm;
        }

        // Used in EmitDetourApply.
        private static NativeDetourData ToNativeDetourData(IntPtr method, IntPtr target, uint size, byte type, IntPtr extra)
            => new NativeDetourData {
                Method = method,
                Target = target,
                Size = size,
                Type = type,
                Extra = extra
            };

        private static readonly FieldInfo _f_Native = typeof(DetourHelper).GetField("_Native", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo _m_ToNativeDetourData = typeof(DetourHelper).GetMethod("ToNativeDetourData", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo _m_Copy = typeof(IDetourNativePlatform).GetMethod("Copy");
        private static readonly MethodInfo _m_Apply = typeof(IDetourNativePlatform).GetMethod("Apply");
        private static readonly ConstructorInfo _ctor_Exception = typeof(Exception).GetConstructor(new Type[] { typeof(string) });

        /// <summary>
        /// Fill the DynamicMethodDefinition with a throw.
        /// </summary>
        public static DynamicMethodDefinition StubCriticalDetour(this DynamicMethodDefinition dm) {
            ILProcessor il = dm.GetILProcessor();
            for (int i = 0; i < 10; i++) {
                // Prevent mono from inlining the DynamicMethod.
                il.Emit(OpCodes.Nop);
            }
            il.Emit(OpCodes.Ldstr, $"{dm.Definition.Name} should've been detoured!");
            il.Emit(OpCodes.Newobj, _ctor_Exception);
            il.Emit(OpCodes.Throw);
            return dm;
        }

        /// <summary>
        /// Emit a call to DetourManager.Native.Copy using the given parameters.
        /// </summary>
        public static void EmitDetourCopy(this ILProcessor il, IntPtr src, IntPtr dst, byte type) {
            // Load NativePlatform instance.
            il.Emit(OpCodes.Ldsfld, _f_Native);

            // Fill stack with src, dst, type
            il.Emit(OpCodes.Ldc_I8, (long) src);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I8, (long) dst);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I4, (int) type);
            il.Emit(OpCodes.Conv_U1);

            // Copy.
            il.Emit(OpCodes.Callvirt, _m_Copy);
        }

        /// <summary>
        /// Emit a call to DetourManager.Native.Apply using a copy of the given data.
        /// </summary>
        public static void EmitDetourApply(this ILProcessor il, NativeDetourData data) {
            // Load NativePlatform instance.
            il.Emit(OpCodes.Ldsfld, _f_Native);

            // Fill stack with data values.
            il.Emit(OpCodes.Ldc_I8, (long) data.Method);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I8, (long) data.Target);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldc_I4, (int) data.Size);
            il.Emit(OpCodes.Ldc_I4, (int) data.Type);
            il.Emit(OpCodes.Conv_U1);
            il.Emit(OpCodes.Ldc_I8, (long) data.Extra);
            il.Emit(OpCodes.Conv_I);

            // Put values in stack into NativeDetourData.
            il.Emit(OpCodes.Call, _m_ToNativeDetourData);

            // Apply.
            il.Emit(OpCodes.Callvirt, _m_Apply);
        }

        #endregion

    }
}
