using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms.Runtime {
    internal abstract class FxCoreCLRBaseRuntime : IRuntime {

        public abstract RuntimeKind Target { get; }

        public virtual RuntimeFeature Features => 
            RuntimeFeature.RequiresMethodIdentification | 
            RuntimeFeature.PreciseGC |
            RuntimeFeature.RequiresBodyPointerWalking;


        private static readonly Type? RTDynamicMethod =
            typeof(DynamicMethod).GetNestedType("RTDynamicMethod", BindingFlags.NonPublic);
        private static readonly FieldInfo? RTDynamicMethod_m_owner =
            RTDynamicMethod?.GetField("m_owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? _DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo? _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? _RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo? _IRuntimeMethodInfo_get_Value =
            typeof(RuntimeMethodHandle).Assembly.GetType("System.IRuntimeMethodInfo")?.GetMethod("get_Value");

        private static readonly MethodInfo? _RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly Type? RtH_CM_FirstArg = _RuntimeHelpers__CompileMethod?.GetParameters()[0].ParameterType;
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIntPtr = RtH_CM_FirstArg?.FullName == "System.IntPtr";
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo = RtH_CM_FirstArg?.FullName == "System.IRuntimeMethodInfo";
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesRuntimeMethodHandleInternal = RtH_CM_FirstArg?.FullName == "System.RuntimeMethodHandleInternal";

        public virtual MethodBase GetIdentifiable(MethodBase method) {
            if (RTDynamicMethod_m_owner != null && method.GetType() == RTDynamicMethod)
                return (MethodBase) RTDynamicMethod_m_owner.GetValue(method)!;
            return method;
        }

        public virtual RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            // Compile the method handle before getting our hands on the final method handle.
            if (method is DynamicMethod dm) {
                if (TryInvokeBclCompileMethod(dm, out var handle)) {
                    return handle;
                } else {
                    // This should work just fine.
                    // It abuses the fact that CreateDelegate first compiles the DynamicMethod, before creating the delegate and failing.
                    // Only side effect: It introduces a possible deadlock in f.e. tModLoader, which adds a FirstChanceException handler.
                    try {
                        dm.CreateDelegate(typeof(MulticastDelegate));
                    } catch {
                    }
                }

                if (_DynamicMethod_m_method != null)
                    return (RuntimeMethodHandle) _DynamicMethod_m_method.GetValue(method)!;
                if (_DynamicMethod_GetMethodDescriptor != null)
                    return (RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(method, null)!;
            }

            return method.MethodHandle;
        }

        // TODO: maybe create helpers to make this not rediculously slow
        private bool TryInvokeBclCompileMethod(DynamicMethod dm, out RuntimeMethodHandle handle) {
            handle = default;
            if (_RuntimeHelpers__CompileMethod is null || _DynamicMethod_GetMethodDescriptor is null)
                return false;
            handle = (RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(dm, null)!;
            if (_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                // mscorlib 2.0.0.0
                _RuntimeHelpers__CompileMethod.Invoke(null, new object?[] { handle.Value });
                return true;
            }
            if (_RuntimeMethodHandle_m_value is null)
                return false;
            var rtMethodInfo = _RuntimeMethodHandle_m_value.GetValue(handle);
            if (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
                // mscorlib 4.0.0.0, System.Private.CoreLib 2.1.0
                _RuntimeHelpers__CompileMethod.Invoke(null, new object?[] { rtMethodInfo });
                return true;
            }
            if (_IRuntimeMethodInfo_get_Value is null)
                return false;
            var rtMethodHandleInternal = _IRuntimeMethodInfo_get_Value.Invoke(rtMethodInfo, null);
            if (_RuntimeHelpers__CompileMethod_TakesRuntimeMethodHandleInternal) {
                _RuntimeHelpers__CompileMethod.Invoke(null, new object?[] { rtMethodHandleInternal });
                return true;
            }

            // something funky is going on if we make it here
            MMDbgLog.Log($"Could not compile DynamicMethod using BCL reflection (_CompileMethod first arg: {RtH_CM_FirstArg})");
            return false;
        }

        // pinning isn't usually in fx/core
        public virtual IDisposable? PinMethodIfNeeded(MethodBase method) {
            return null;
        }

        // inlining disabling is up to each individual runtime
        public abstract void DisableInlining(MethodBase method);
    }
}
