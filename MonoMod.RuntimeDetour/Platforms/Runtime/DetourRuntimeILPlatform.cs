using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Linq;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour.Platforms {
    public abstract class DetourRuntimeILPlatform : IDetourRuntimePlatform {
        protected abstract RuntimeMethodHandle GetMethodHandle(MethodBase method);

        // Prevent the GC from collecting those.
        protected HashSet<MethodBase> PinnedMethods = new HashSet<MethodBase>();

        private bool GlueThiscallStructRetPtr;

        public DetourRuntimeILPlatform() {
            // Perform a selftest if this runtime requires special handling for instance methods returning structs.
            // This is documented behavior for coreclr, but can implicitly affect all other runtimes (including mono!) as well.
            // Specifically, this should affect all __thiscalls

            MethodInfo selftest = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetStruct", BindingFlags.NonPublic | BindingFlags.Instance);
            Pin(selftest);
            MethodInfo selftestHook = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetStructHook", BindingFlags.NonPublic | BindingFlags.Static);
            Pin(selftestHook);
            NativeDetourData detour = DetourHelper.Native.Create(
                GetNativeStart(selftest),
                GetNativeStart(selftestHook),
                null
            );
            DetourHelper.Native.MakeWritable(detour);
            DetourHelper.Native.Apply(detour);
            DetourHelper.Native.MakeExecutable(detour);
            DetourHelper.Native.Free(detour);
            // No need to undo the detour.

            _SelftestStruct s;
            // Make sure that the selftest isn't optimized away.
            try {
            } finally {
                s = _SelftestGetStruct(IntPtr.Zero, IntPtr.Zero);
                unsafe {
                    *&s = s;
                }
            }
        }

        #region Selftest: Struct

        // Struct must be 3, 5, 6, 7 or 9+ bytes big.
        private struct _SelftestStruct {
            private readonly byte A, B, C;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private _SelftestStruct _SelftestGetStruct(IntPtr x, IntPtr y) {
            Console.Error.WriteLine("If you're reading this, the MonoMod.RuntimeDetour selftest failed.");
            throw new Exception("This method should've been detoured!");
        }

        private static unsafe void _SelftestGetStructHook(DetourRuntimeILPlatform self, IntPtr a, IntPtr b, IntPtr c) {
            // Normally, self = this, a = x, b = y
            // For coreclr x64 __thiscall, self = this, a = __ret, b = x, c = y

            // For the selftest, x must be equal to y.
            // If a != b, a is probably pointing to the return buffer.
            self.GlueThiscallStructRetPtr = a != b;
        }

        #endregion

        protected virtual IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
#if NETSTANDARD1_X
            => throw new NotSupportedException();
#else
            => handle.GetFunctionPointer();
#endif

        protected virtual void PrepareMethod(RuntimeMethodHandle handle)
#if NETSTANDARD1_X
            => throw new NotSupportedException();
#else
            => RuntimeHelpers.PrepareMethod(handle);
#endif

        public IntPtr GetNativeStart(MethodBase method)
            => GetFunctionPointer(GetMethodHandle(method));

        public void Pin(MethodBase method) {
            PinnedMethods.Add(method);
            RuntimeMethodHandle handle = GetMethodHandle(method);
            PrepareMethod(handle);
        }

        public MethodInfo CreateCopy(MethodBase method) {
            if (method == null || (method.GetMethodImplementationFlags() & (MethodImplAttributes.OPTIL | MethodImplAttributes.Native | MethodImplAttributes.Runtime)) != 0) {
                throw new InvalidOperationException($"Uncopyable method: {method?.ToString() ?? "NULL"}");
            }

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(method))
                return dmd.Generate();
        }

        public bool TryCreateCopy(MethodBase method, out MethodInfo dm) {
            if (method == null || (method.GetMethodImplementationFlags() & (MethodImplAttributes.OPTIL | MethodImplAttributes.Native | MethodImplAttributes.Runtime)) != 0) {
                dm = null;
                return false;
            }

            try {
                dm = CreateCopy(method);
                return true;
            } catch {
                dm = null;
                return false;
            }
        }

        public MethodBase GetDetourTarget(MethodBase from, MethodBase to) {
            MethodInfo fromInfo = from as MethodInfo;
            MethodInfo toInfo = to as MethodInfo;
            Type context = to.DeclaringType;

            MethodInfo dm = null;

            if (GlueThiscallStructRetPtr &&
                fromInfo != null && !from.IsStatic &&
                toInfo != null && to.IsStatic &&
                fromInfo.ReturnType == toInfo.ReturnType &&
                fromInfo.ReturnType.GetTypeInfo().IsValueType) {

                int size = fromInfo.ReturnType.GetManagedSize();
                if (size == 3 || size == 5 || size == 6 || size == 7 || size >= 9) {
                    List<Type> argTypes = new List<Type> {
                        from.GetThisParamType(), // this
                        fromInfo.ReturnType.MakeByRefType() // __ret - Refs are shiny pointers.
                    };
                    argTypes.AddRange(from.GetParameters().Select(p => p.ParameterType));

                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                        $"Glue:ThiscallStructRetPtr<{from.GetFindableID(simple: true)},{to.GetFindableID(simple: true)}>",
                        typeof(void), argTypes.ToArray()
                    )) {
                        ILProcessor il = dmd.GetILProcessor();

                        // Load the return buffer address.
                        il.Emit(OpCodes.Ldarg, 1);

                        // Invoke the target method with all remaining arguments.
                        {
                            il.Emit(OpCodes.Ldarg, 0);
                            for (int i = 2; i < argTypes.Count; i++)
                                il.Emit(OpCodes.Ldarg, i);
                            il.Emit(OpCodes.Call, (MethodInfo) to);
                        }

                        // Store the returned object to the return buffer.
                        il.Emit(OpCodes.Stobj, fromInfo.ReturnType);
                        il.Emit(OpCodes.Ret);

                        dm = dmd.Generate();
                    }
                }
            }

            return dm ?? to;
        }
    }
}
