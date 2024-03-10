using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using DynamicMethod = System.Reflection.Emit.DynamicMethod;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal abstract class FxCoreBaseRuntime : IRuntime
    {

        public abstract RuntimeKind Target { get; }

        public virtual RuntimeFeature Features =>
            RuntimeFeature.RequiresMethodIdentification |
            RuntimeFeature.DisableInlining |
            RuntimeFeature.PreciseGC |
            RuntimeFeature.RequiresBodyThunkWalking |
            RuntimeFeature.GenericSharing |
            RuntimeFeature.HasKnownABI |
            RuntimeFeature.RequiresCustomMethodCompile;

        protected Abi? AbiCore;

        public Abi Abi => AbiCore ?? throw new PlatformNotSupportedException($"The runtime's Abi field is not set, and is unusable ({GetType()})");

        private static TypeClassification ClassifyRyuJitX86(Type type, bool isReturn)
        {

            while (!type.IsPrimitive || type.IsEnum)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fields == null || fields.Length != 1)
                {
                    // zero-size empty or too large struct, passed byref
                    break;
                }
                type = fields[0].FieldType;
            }

            // we now either have a primitive or a multi-valued struct

            var typeCode = Type.GetTypeCode(type);
            if (typeCode is
                TypeCode.Boolean or
                TypeCode.Byte or TypeCode.SByte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 ||
                type == typeof(IntPtr) || type == typeof(UIntPtr))
            {
                // if it's one of these primitives, it's always passed in register
                return TypeClassification.InRegister;
            }

            // if the type is a 64-bit integer and we're checking return, it's passed in register
            if (isReturn && typeCode is TypeCode.Int64 or TypeCode.UInt64)
            {
                return TypeClassification.InRegister;
            }

            // all others are passed on stack, or in a return buffer
            if (isReturn)
            {
                return TypeClassification.ByReference;
            }
            else
            {
                return TypeClassification.OnStack;
            }
        }

        protected FxCoreBaseRuntime()
        {
            if (PlatformDetection.Architecture == ArchitectureKind.x86)
            {
                // On x86/RyuJIT, the runtime uses its own really funky ABI
                // TODO: is this the ABI used on CLR 2?
                AbiCore = new Abi(
                    new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.UserArguments, SpecialArgumentKind.GenericContext },
                    ClassifyRyuJitX86,
                    ReturnsReturnBuffer: true
                    );
            }
        }

        protected static Abi AbiForCoreFx45X64(Abi baseAbi)
        {
            return baseAbi with
            {
                ArgumentOrder = new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.GenericContext, SpecialArgumentKind.UserArguments },
            };
        }

        private static readonly Type? RTDynamicMethod =
            typeof(DynamicMethod).GetNestedType("RTDynamicMethod", BindingFlags.NonPublic);
        private static readonly FieldInfo? RTDynamicMethod_m_owner =
            RTDynamicMethod?.GetField("m_owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? _DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo? _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _RuntimeMethodHandle_get_Value =
            typeof(RuntimeMethodHandle).GetMethod("get_Value", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo? _RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo? _IRuntimeMethodInfo_get_Value =
            typeof(RuntimeMethodHandle).Assembly.GetType("System.IRuntimeMethodInfo")?.GetMethod("get_Value");

        private static readonly MethodInfo? _RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static) ??
            typeof(RuntimeHelpers).GetMethod("CompileMethod", BindingFlags.NonPublic | BindingFlags.Static); // the underscore is not present in .NET 6
        private static readonly Type? RtH_CM_FirstArg = _RuntimeHelpers__CompileMethod?.GetParameters()[0].ParameterType;
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIntPtr = RtH_CM_FirstArg?.FullName == "System.IntPtr";
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo = RtH_CM_FirstArg?.FullName == "System.IRuntimeMethodInfo";
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesRuntimeMethodHandleInternal = RtH_CM_FirstArg?.FullName == "System.RuntimeMethodHandleInternal";

        public virtual MethodBase GetIdentifiable(MethodBase method)
        {
            if (RTDynamicMethod_m_owner != null && method.GetType() == RTDynamicMethod)
                return (MethodBase)RTDynamicMethod_m_owner.GetValue(method)!;
            return method;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "CreateDelegate throwing is expected and wanted, as it forces the method to be compiled. " +
            "We don't want those exceptions propagating higher up the stack though.")]
        public virtual RuntimeMethodHandle GetMethodHandle(MethodBase method)
        {
            // Compile the method handle before getting our hands on the final method handle.
            if (method is DynamicMethod dm)
            {
                if (TryGetDMHandle(dm, out var handle) && TryInvokeBclCompileMethod(handle))
                {
                    return handle;
                }
                else
                {
                    // This should work just fine.
                    // It abuses the fact that CreateDelegate first compiles the DynamicMethod, before creating the delegate and failing.
                    // Only side effect: It introduces a possible deadlock in f.e. tModLoader, which adds a FirstChanceException handler.
                    try
                    {
                        dm.CreateDelegate(typeof(MulticastDelegate));
                    }
                    catch
                    {
                    }
                }

                if (TryGetDMHandle(dm, out handle))
                    return handle;
                if (_DynamicMethod_m_method != null) // TODO: is this for Mono? Is there *any* .NET Framework/Core where this is the case?
                    return (RuntimeMethodHandle)_DynamicMethod_m_method.GetValue(method)!;
            }

            return method.MethodHandle;
        }

        private Func<DynamicMethod, RuntimeMethodHandle>? lazyGetDmHandleHelper;
        private Func<DynamicMethod, RuntimeMethodHandle> GetDMHandleHelper => lazyGetDmHandleHelper ??= CreateGetDMHandleHelper();
        private static bool CanCreateGetDMHandleHelper => _DynamicMethod_GetMethodDescriptor is not null;

        private static Func<DynamicMethod, RuntimeMethodHandle> CreateGetDMHandleHelper()
        {
            Helpers.Assert(CanCreateGetDMHandleHelper);

            using var dmd = new DynamicMethodDefinition("get DynamicMethod RuntimeMethodHandle", typeof(RuntimeMethodHandle), new[] { typeof(DynamicMethod) });
            var module = dmd.Module!;
            var il = dmd.GetILProcessor();

            Helpers.Assert(_DynamicMethod_GetMethodDescriptor is not null);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _DynamicMethod_GetMethodDescriptor);
            il.Emit(OpCodes.Ret);

            return dmd.Generate().CreateDelegate<Func<DynamicMethod, RuntimeMethodHandle>>();
        }

        private Action<RuntimeMethodHandle>? lazyBclCompileMethod;
        private Action<RuntimeMethodHandle> BclCompileMethodHelper => lazyBclCompileMethod ??= CreateBclCompileMethodHelper();
        private static bool CanCreateBclCompileMethodHelper
            => _RuntimeHelpers__CompileMethod is not null
            && (_RuntimeHelpers__CompileMethod_TakesIntPtr
            || (_RuntimeMethodHandle_m_value is not null
            && (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo
            || (_IRuntimeMethodInfo_get_Value is not null
            && _RuntimeHelpers__CompileMethod_TakesRuntimeMethodHandleInternal
                ))));

        private static Action<RuntimeMethodHandle> CreateBclCompileMethodHelper()
        {
            Helpers.Assert(CanCreateBclCompileMethodHelper);

            using var dmd = new DynamicMethodDefinition("invoke RuntimeHelpers.CompileMethod", null, new[] { typeof(RuntimeMethodHandle) });
            var module = dmd.Module!;
            var il = dmd.GetILProcessor();

            il.Emit(OpCodes.Ldarga_S, (byte)0);
            if (_RuntimeHelpers__CompileMethod_TakesIntPtr)
            {
                il.Emit(OpCodes.Call, module.ImportReference(_RuntimeMethodHandle_get_Value));
                il.Emit(OpCodes.Call, module.ImportReference(_RuntimeHelpers__CompileMethod));
                il.Emit(OpCodes.Ret);
                return dmd.Generate().CreateDelegate<Action<RuntimeMethodHandle>>();
            }

            Helpers.Assert(_RuntimeMethodHandle_m_value is not null);
            il.Emit(OpCodes.Ldfld, module.ImportReference(_RuntimeMethodHandle_m_value));
            if (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo)
            {
                il.Emit(OpCodes.Call, module.ImportReference(_RuntimeHelpers__CompileMethod));
                il.Emit(OpCodes.Ret);
                return dmd.Generate().CreateDelegate<Action<RuntimeMethodHandle>>();
            }

            Helpers.Assert(_IRuntimeMethodInfo_get_Value is not null);
            il.Emit(OpCodes.Callvirt, module.ImportReference(_IRuntimeMethodInfo_get_Value));
            if (_RuntimeHelpers__CompileMethod_TakesRuntimeMethodHandleInternal)
            {
                il.Emit(OpCodes.Call, module.ImportReference(_RuntimeHelpers__CompileMethod));
                il.Emit(OpCodes.Ret);
                return dmd.Generate().CreateDelegate<Action<RuntimeMethodHandle>>();
            }

            Helpers.Assert(false, "Tried to generate BCL CompileMethod helper when it's not possible? (This should never happen if CanCreateBclCompileMethodHelper is correct)");
            throw new InvalidOperationException("UNREACHABLE");
        }

        private bool TryGetDMHandle(DynamicMethod dm, out RuntimeMethodHandle handle)
        {
            if (CanCreateGetDMHandleHelper)
            {
                handle = GetDMHandleHelper(dm);
                return true;
            }
            return TryGetDMHandleRefl(dm, out handle);
        }

        protected bool TryInvokeBclCompileMethod(RuntimeMethodHandle handle)
        {
            if (CanCreateBclCompileMethodHelper)
            {
                BclCompileMethodHelper(handle);
                return true;
            }
            return TryInvokeBclCompileMethodRefl(handle);
        }

        private static bool TryGetDMHandleRefl(DynamicMethod dm, out RuntimeMethodHandle handle)
        {
            handle = default;
            if (_DynamicMethod_GetMethodDescriptor is null)
                return false;
            handle = (RuntimeMethodHandle)_DynamicMethod_GetMethodDescriptor.Invoke(dm, null)!;
            return true;
        }

        private static bool TryInvokeBclCompileMethodRefl(RuntimeMethodHandle handle)
        {
            if (_RuntimeHelpers__CompileMethod is null)
                return false;
            if (_RuntimeHelpers__CompileMethod_TakesIntPtr)
            {
                // mscorlib 2.0.0.0
                _RuntimeHelpers__CompileMethod.Invoke(null, new object?[] { handle.Value });
                return true;
            }
            if (_RuntimeMethodHandle_m_value is null)
                return false;
            var rtMethodInfo = _RuntimeMethodHandle_m_value.GetValue(handle);
            if (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo)
            {
                // mscorlib 4.0.0.0, System.Private.CoreLib 2.1.0
                _RuntimeHelpers__CompileMethod.Invoke(null, new object?[] { rtMethodInfo });
                return true;
            }
            if (_IRuntimeMethodInfo_get_Value is null)
                return false;
            var rtMethodHandleInternal = _IRuntimeMethodInfo_get_Value.Invoke(rtMethodInfo, null);
            if (_RuntimeHelpers__CompileMethod_TakesRuntimeMethodHandleInternal)
            {
                _RuntimeHelpers__CompileMethod.Invoke(null, new object?[] { rtMethodHandleInternal });
                return true;
            }

            // something funky is going on if we make it here
            MMDbgLog.Error($"Could not compile DynamicMethod using BCL reflection (_CompileMethod first arg: {RtH_CM_FirstArg})");
            return false;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "We want to catch and swallow exceptions from CreateDelegate.")]
        public virtual void Compile(MethodBase method)
        {
            var handle = GetMethodHandle(method);
            RuntimeHelpers.PrepareMethod(handle);
            Helpers.Assert(TryInvokeBclCompileMethod(handle));

            if (method.IsVirtual && (method.DeclaringType?.IsValueType ?? false))
            {
                // if the method is a virtual method on a value type, we want to do something to compile the real
                // method instead of just the unboxing stub. The RuntimeMethodHandle we got from the MethodBase
                // points to the unboxing stub. We could use knowlege of the runtime to get the non-unboxing stub
                // MethodDesc, then create a RuntimeMethodHandle for it, however that may change per-runtime.
                // If we want to implement that, we can implement TryGetCacnonicalMethodHandle.

                if (TryGetCanonicalMethodHandle(ref handle))
                {
                    Helpers.Assert(TryInvokeBclCompileMethod(handle));
                }
                else
                {
                    try
                    {
                        // Just like in GetMethodHandle, CreateDelegate will likely compile the right method.
                        _ = method.CreateDelegate<Action>();
                    }
                    catch (Exception e)
                    {
                        MMDbgLog.Spam($"Caught exception while attempting to compile real entry point of virtual method on struct: {e}");
                    }
                }
            }
        }

        protected virtual bool TryGetCanonicalMethodHandle(ref RuntimeMethodHandle handle) => false;

        // pinning isn't usually needed in fx/core
        public virtual IDisposable? PinMethodIfNeeded(MethodBase method)
        {
            return null;
        }

        protected static readonly bool IsDebugClr = Switches.TryGetSwitchEnabled(Switches.DebugClr, out var isEnabled) && isEnabled;

        // It seems that across all versions of Framework and Core, the layout of the start of a MethodDesc is quite consistent
        public unsafe virtual void DisableInlining(MethodBase method)
        {
            // https://github.com/dotnet/runtime/blob/89965be3ad2be404dc82bd9e688d5dd2a04bcb5f/src/coreclr/src/vm/method.hpp#L178
            // mdcNotInline = 0x2000
            // References to RuntimeMethodHandle (CORINFO_METHOD_HANDLE) pointing to MethodDesc
            // can be traced as far back as https://ntcore.com/files/netint_injection.htm

            var handle = GetMethodHandle(method);

            var offset = 0
              + (IsDebugClr ? 0 // #ifdef _DEBUG
                + IntPtr.Size   // LPCUTF8 m_pszDebugMethodName;
                + IntPtr.Size   // LPCUTF8 m_pszDebugClassName;
                + IntPtr.Size   // LPCUTF8 m_pszDebugMethodSignature;
                + IntPtr.Size   // FixupPointer<PTR_MethodTable> m_pDebugMethodTable;
                + IntPtr.Size   // PTR_GCCoverageInfo m_GcCover;
              : 0)              // #endif
              + 2 // UINT16 m_wFlags3AndTokenRemainder
              + 1 // BYTE m_chunkIndex
              + 1 // BYTE m_chunkIndex
              + 2 // WORD m_wSlotNumber
              ;

            var m_wFlags = (ushort*)(((byte*)handle.Value) + offset);
            *m_wFlags |= 0x2000;
        }

        public virtual IntPtr GetMethodEntryPoint(MethodBase method)
        {
            method = GetIdentifiable(method);

            if (method.IsVirtual && (method.DeclaringType?.IsValueType ?? false))
            {
                /* .NET has got TWO MethodDescs and thus TWO ENTRY POINTS for virtual struct methods (f.e. override ToString).
                 * More info: https://mattwarren.org/2017/08/02/A-look-at-the-internals-of-boxing-in-the-CLR/#unboxing-stub-creation
                 *
                 * Observations made so far:
                 * - GetFunctionPointer ALWAYS returns a pointer to the unboxing stub handle.
                 * - On x86, the "real" entry point is often found 8 bytes after the unboxing stub entry point.
                 * - Methods WILL be called INDIRECTLY using the pointer found in the "real" MethodDesc.
                 * - The "real" MethodDesc will be updated, which isn't an issue except that we can't patch the stub in time.
                 * - The "real" stub will stay untouched.
                 * - LDFTN RETURNS A POINTER TO THE "REAL" ENTRY POINT.
                 *
                 * Exceptions so far:
                 * - SOME interface methods seem to follow similar rules, but ldftn isn't enough.
                 * - Can't use GetBaseDefinition to check for interface methods as that holds up ALC unloading. (Mapping info is fine though...)
                 */
                /*foreach (Type intf in method.DeclaringType.GetInterfaces()) {
                    if (method.DeclaringType.GetInterfaceMap(intf).TargetMethods.Contains(method)) {
                        break;
                    }
                }*/

                return method.GetLdftnPointer();
            }
            else
            {
                // Your typical method.
                var handle = GetMethodHandle(method);
                return handle.GetFunctionPointer();
            }
        }

        public event OnMethodCompiledCallback? OnMethodCompiled;

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "We catch exceptions here to log them and not ignore them, as this method's caller would.")]
        protected virtual void OnMethodCompiledCore(
            RuntimeTypeHandle declaringType,
            RuntimeMethodHandle methodHandle,
            ReadOnlyMemory<RuntimeTypeHandle>? genericTypeArguments,
            ReadOnlyMemory<RuntimeTypeHandle>? genericMethodArguments,
            IntPtr methodBodyStart,
            IntPtr methodBodyRw,
            ulong methodBodySize
        )
        {
            try
            {
                var declType = Type.GetTypeFromHandle(declaringType);
                if (genericTypeArguments is { } gte && declType!.IsGenericTypeDefinition)
                {
                    var typeArr = new Type[gte.Length];
                    for (var i = 0; i < gte.Length; i++)
                    {
                        typeArr[i] = Type.GetTypeFromHandle(gte.Span[i])!;
                    }
                    declType = declType.MakeGenericType(typeArr);
                }

                var method = MethodBase.GetMethodFromHandle(methodHandle, declType!.TypeHandle);

                // When method is null, there are several possibilities
                // One of them is that it's a P/Invoke, and declType is the P/Invoke IL stub helper class
                // In that case, we can sometimes iterate through the methods of that class to find the one wich a matching MethodHandle
                // It is worth noting, though, that the P/Invoke stubs that are compiled are reused based on signature, and so are not at all
                // means of uniquely identifying P/Invoke targets.
                // https://github.com/dotnet/runtime/blob/c7f926c69725369545671305a3b1c4d4391d80f4/docs/design/coreclr/botr/clr-abi.md#hidden-parameters
                if (method is null)
                {
                    foreach (var meth in declType.GetMethods((BindingFlags)(-1)))
                    {
                        if (meth.MethodHandle.Value == methodHandle.Value)
                        {
                            method = meth;
                            break;
                        }
                    }
                }

                MMDbgLog.Spam($"JIT compiled {method} to 0x{methodBodyStart:x16} (rw: 0x{methodBodyRw:x16})");

                try
                {
                    OnMethodCompiled?.Invoke(methodHandle, method, methodBodyStart, methodBodyRw, methodBodySize);
                }
                catch (Exception e)
                {
                    MMDbgLog.Error($"Error executing OnMethodCompiled event: {e}");
                }
            }
            catch (Exception e)
            {
                MMDbgLog.Error($"Error in OnMethodCompiledCore: {e}");
            }
        }
    }
}
