using MonoMod.Core.Interop;
using MonoMod.Core.Platforms;
using MonoMod.Core.Platforms.Runtimes;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.RuntimeDetour.Generics
{
    /// <summary>
    /// Reconstructs the full type-argument vector (declaring-type args ++ method args) of a shared
    /// generic call from its runtime generic context.
    /// </summary>
    internal static unsafe class GenericContextReader
    {
        // Ordered list of candidate MethodTable*/TypeHandle -> Type resolvers.
        // Runtime versions expose different internal helpers, so pick whichever actually works
        private static readonly List<Func<IntPtr, Type?>> handleToTypeResolvers = BuildHandleToTypeResolvers();
        private static readonly HandleToMethod? HandleToMethodImpl = TryBuildHandleToMethod();

        public static Type[] FromMethodDesc(IntPtr methodDesc)
        {
            if (methodDesc == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(methodDesc));
            }

            if (!IsPlausibleRuntimePointer(methodDesc))
            {
                throw new ArgumentException($"Generic context 0x{methodDesc.ToInt64():x} is not a plausible MethodDesc pointer.", nameof(methodDesc));
            }

            var declaringMt = ReadDeclaringMethodTable(methodDesc);

            if (HandleToMethodImpl is not null)
            {
                var mb = HandleToMethodImpl(methodDesc, declaringMt) ?? throw new InvalidOperationException("Could not resolve MethodDesc to a MethodBase.");
                var declA = mb.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : Type.EmptyTypes;
                var methA = mb.IsGenericMethod ? mb.GetGenericArguments() : Type.EmptyTypes;
                return [.. declA, .. methA];
            }
            // .NET 5, 6, .NET Framework, .NET Core
            var md = (Fx.V48.MethodDesc*)methodDesc;
            var declaringType = RequireType(declaringMt);
            var methodArgs = md->TryAsInstantiated(out var imd) && imd->IMD_HasMethodInstantiation
                ? ReadHandles((IntPtr*)imd->m_pPerInstInfo, imd->m_wNumGenericArgs)
                : Type.EmptyTypes;
            var declArgs = declaringType.IsGenericType ? declaringType.GetGenericArguments() : Type.EmptyTypes;
            return [.. declArgs, .. methodArgs];
        }

        // Only reachable on Framework/CoreCLR
        private static IntPtr ReadDeclaringMethodTable(IntPtr methodDesc) => PlatformDetection.Runtime switch
        {
            RuntimeKind.Framework => (IntPtr)(void*)((Fx.V48.MethodDesc*)methodDesc)->MethodTable,
            RuntimeKind.CoreCLR => (IntPtr)(void*)((CoreCLR.V60.MethodDesc*)methodDesc)->MethodTable,
            _ => throw new NotImplementedException($"Reading Generic Context not implemented on {PlatformDetection.Runtime}."),
        };

        public static Type[] FromMethodTable(IntPtr methodTable)
        {
            if (methodTable == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(methodTable));
            }
            if (!IsPlausibleRuntimePointer(methodTable))
            {
                throw new ArgumentException($"Generic context 0x{methodTable.ToInt64():x} is not a plausible MethodTable pointer.", nameof(methodTable));
            }
            var t = RequireType(methodTable);
            return t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes;
        }

        public static Type[] FromThisObject(object self, MethodBase openSource)
        {
            Helpers.ThrowIfArgumentNull(self);
            Helpers.ThrowIfArgumentNull(openSource);
            return TypeArgsForDeclaringType(self.GetType(), openSource);
        }

        private static Type[] TypeArgsForDeclaringType(Type runtimeType, MethodBase openSource)
        {
            var openDecl = openSource.DeclaringType;
            Helpers.Assert(openDecl != null, "Open source has no declaring type.");
            var def = openDecl.IsGenericType ? openDecl.GetGenericTypeDefinition() : openDecl;

            for (var t = runtimeType; t is not null; t = t.BaseType)
            {
                if ((t.IsGenericType && t.GetGenericTypeDefinition() == def) || t == def)
                {
                    return t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes;
                }
            }

            throw new InvalidOperationException($"Could not find {def} in the hierarchy of {runtimeType}.");
        }


        private static Type[] ReadHandles(IntPtr* handles, int n)
        {
            if (n == 0 || handles is null)
            {
                return Type.EmptyTypes;
            }

            var result = new Type[n];
            for (var i = 0; i < n; i++)
            {
                result[i] = RequireType(handles[i]);
            }

            return result;
        }

        private static Type RequireType(IntPtr handle)
            => TryResolveType(handle) ?? throw new InvalidOperationException($"Unresolvable type handle 0x{handle:x}.");

        // Trying to dereference a wrong pointer can cause a host crash; so validate here
        private static bool IsPlausibleRuntimePointer(IntPtr p)
            => (p.ToInt64() & (IntPtr.Size - 1)) == 0 && (ulong)p.ToInt64() >= 0x10000UL;

        // Tries each candidate resolver until one returns a Type without faulting
        private static Func<IntPtr, Type?>? winningResolver;

        private static Type? TryResolveType(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            if (winningResolver is { } winner)
            {
                try
                {
                    var t = winner(handle);
                    if (t is not null)
                    {
                        return t;
                    }
                }
                catch
                {
                    // The previously-working resolver faulted on this handle?
                }
            }

            Exception? last = null;
            foreach (var resolver in handleToTypeResolvers)
            {
                try
                {
                    var t = resolver(handle);
                    if (t is not null)
                    {
                        winningResolver = resolver;
                        return t;
                    }
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            if (last is not null)
            {
                MMDbgLog.Trace($"All type-handle resolvers failed for 0x{handle:x}: {last.Message}");
            }
            return null;
        }

        private static List<Func<IntPtr, Type?>> BuildHandleToTypeResolvers()
        {
            List<Func<IntPtr, Type?>> resolvers = [];

            // 1. CoreCLR .NET 7+: the supported public RuntimeTypeHandle.FromIntPtr + Type.GetTypeFromHandle.
            var fromIntPtr = typeof(RuntimeTypeHandle).GetMethod("FromIntPtr", StaticFlags, null, [typeof(IntPtr)], null);
            if (fromIntPtr is not null)
            {
                var conv = (Func<IntPtr, RuntimeTypeHandle>)Delegate.CreateDelegate(typeof(Func<IntPtr, RuntimeTypeHandle>), fromIntPtr);
                resolvers.Add(h => Type.GetTypeFromHandle(conv(h)));
            }

            // 2. Mono: Type.internal_from_handle (or a reinterpret of the handle as a RuntimeTypeHandle).
            if (PlatformDetection.Runtime == RuntimeKind.Mono)
            {
                var monoFromHandle = typeof(Type).GetMethod("internal_from_handle", StaticFlags, null, [typeof(IntPtr)], null);
                if (monoFromHandle is not null)
                {
                    resolvers.Add((Func<IntPtr, Type?>)Delegate.CreateDelegate(typeof(Func<IntPtr, Type?>), monoFromHandle));
                }
                else
                {
                    resolvers.Add(static h => Type.GetTypeFromHandle(Unsafe.As<IntPtr, RuntimeTypeHandle>(ref h)));
                }
            }

            // 3 & 4. CoreCLR .NET 5/6 (and any version where FromIntPtr is unavailable): the internal
            // Type.GetTypeFromHandleUnsafe FCall, reached through a wrapper. It can fault; try CoreLib and this module as owners
            if (PlatformTriple.Current.Runtime is Core21Runtime core21 && core21.GetOrCreateGetTypeFromHandleUnsafe() is MethodInfo unsafeFromHandle)
            {
                Type?[] owners = [typeof(object), null];
                foreach (var owner in owners)
                {
                    try
                    {
                        resolvers.Add(BuildGetTypeFromHandleUnsafeWrapper(unsafeFromHandle, owner));
                    }
                    catch { }
                }
            }

            // 5. .NET Framework (and anything exposing Type.GetTypeFromHandleUnsafe directly as a delegate target).
            var unsafeM = typeof(Type).GetMethod("GetTypeFromHandleUnsafe", StaticFlags, null, [typeof(IntPtr)], null);
            if (unsafeM is not null)
            {
                try
                {
                    resolvers.Add((Func<IntPtr, Type?>)Delegate.CreateDelegate(typeof(Func<IntPtr, Type?>), unsafeM));
                }
                catch { }
            }

            if (resolvers.Count == 0)
            {
                throw new PlatformNotSupportedException($"{PlatformDetection.OS}, {PlatformDetection.Architecture}, {PlatformDetection.Runtime}: No way to turn handle to type.");
            }

            return resolvers;
        }

        private static Func<IntPtr, Type?> BuildGetTypeFromHandleUnsafeWrapper(MethodInfo target, Type? owner)
        {
            // Host the wrapper either in CoreLib (owner != null) or this module (owner == null).
            var dm = owner is not null
                ? new DynamicMethod("GetTypeFromHandleUnsafe_Wrapper", typeof(Type), [typeof(IntPtr)], owner, true)
                : new DynamicMethod("GetTypeFromHandleUnsafe_Wrapper", typeof(Type), [typeof(IntPtr)], typeof(GenericContextReader).Module, true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            return (Func<IntPtr, Type?>)dm.CreateDelegate(typeof(Func<IntPtr, Type?>));
        }

        private delegate MethodBase? HandleToMethod(IntPtr md, IntPtr declaringMt);

        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static HandleToMethod? TryBuildHandleToMethod()
        {
            var mhFromIntPtr = typeof(RuntimeMethodHandle).GetMethod("FromIntPtr", StaticFlags, null, [typeof(IntPtr)], null);
            var thFromIntPtr = typeof(RuntimeTypeHandle).GetMethod("FromIntPtr", StaticFlags, null, [typeof(IntPtr)], null);

            var getFromHandle = typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), StaticFlags, null, [typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle)], null);
            Helpers.Assert(getFromHandle != null);
            var getter = (Func<RuntimeMethodHandle, RuntimeTypeHandle, MethodBase?>)Delegate.CreateDelegate(typeof(Func<RuntimeMethodHandle, RuntimeTypeHandle, MethodBase?>), getFromHandle);

            var getFromHandle1 = typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), StaticFlags, null, [typeof(RuntimeMethodHandle)], null);
            var getter1 = getFromHandle1 is null
                ? null
                : (Func<RuntimeMethodHandle, MethodBase?>)Delegate.CreateDelegate(typeof(Func<RuntimeMethodHandle, MethodBase?>), getFromHandle1);

            if (mhFromIntPtr is null || thFromIntPtr is null)
            {
                if (PlatformDetection.Runtime == RuntimeKind.Mono)
                {
                    return BuildMonoHandleToMethod();
                }
                if (PlatformDetection.Runtime == RuntimeKind.Framework)
                {
                    return null;
                }

                // https://github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs#L1018
                // IntPtr value => new RuntimeMethodHandle(new RuntimeMethodInfoStub(new RuntimeMethodHandleInternal(value), keepAlive))
                createHandle = BuildCustomHandleToMethod();
                return (md, declMt) =>
                {
                    // Prefer resolving from the method handle alone: it avoids converting the declaring MethodTable*
                    // into a Type via GetTypeFromHandleUnsafe, which faults on some .NET 5/6 builds.
                    // It can throw generic declaring types
                    if (getter1 is not null)
                    {
                        try
                        {
                            var resolved = getter1(createHandle!(md, handleKeepAlive));
                            if (resolved is not null)
                            {
                                return resolved;
                            }
                        }
                        catch (Exception e)
                        {
                            MMDbgLog.Trace($"Handle-only resolve failed, falling back to declaring-type path: {e.Message}");
                        }
                    }

                    var t = RequireType(declMt);
                    return getter(createHandle!(md, t), t.TypeHandle);
                };
            }

            var mhConv = (Func<IntPtr, RuntimeMethodHandle>)Delegate.CreateDelegate(typeof(Func<IntPtr, RuntimeMethodHandle>), mhFromIntPtr);
            var thConv = (Func<IntPtr, RuntimeTypeHandle>)Delegate.CreateDelegate(typeof(Func<IntPtr, RuntimeTypeHandle>), thFromIntPtr);

            return (md, declMt) => getter(mhConv(md), thConv(declMt));
        }
        private delegate RuntimeMethodHandle CreateHandleDelegate(IntPtr mD, object keepAlive);
        private static CreateHandleDelegate? createHandle;
        private static readonly object handleKeepAlive = new();
        private static CreateHandleDelegate? BuildCustomHandleToMethod()
        {
            var t_RuntimeMethodHandleInternal = Type.GetType("System.RuntimeMethodHandleInternal");
            var t_IRuntimeMethodInfo = Type.GetType("System.IRuntimeMethodInfo");
            var t_RuntimeMethodInfoStub = Type.GetType("System.RuntimeMethodInfoStub");

            if (t_RuntimeMethodHandleInternal == null || t_IRuntimeMethodInfo == null || t_RuntimeMethodInfoStub == null)
                throw new InvalidOperationException("Required internal runtime types not found.");

            var ctor_mdHandle = t_RuntimeMethodHandleInternal.GetConstructor(InstanceFlags, null, [typeof(IntPtr)], null);
            var ctor_stub = t_RuntimeMethodInfoStub.GetConstructor(InstanceFlags, null, [t_RuntimeMethodHandleInternal, typeof(object)], null);
            var ctor_rmh = typeof(RuntimeMethodHandle).GetConstructor(InstanceFlags, null, [t_IRuntimeMethodInfo], null);

            if (ctor_mdHandle == null || ctor_stub == null || ctor_rmh == null)
                throw new InvalidOperationException("Required internal constructors not found.");

            var dm = new DynamicMethod("CreateRuntimeMethodHandle", typeof(RuntimeMethodHandle), [typeof(IntPtr), typeof(object)], typeof(GenericContextReader).Module, skipVisibility: true);

            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);               // IntPtr methodDesc
            il.Emit(OpCodes.Newobj, ctor_mdHandle); // new RuntimeMethodHandleInternal(methodDesc)
            il.Emit(OpCodes.Ldarg_1);               // object keepAlive
            il.Emit(OpCodes.Newobj, ctor_stub);     // new RuntimeMethodInfoStub(handle, keepAlive)
            il.Emit(OpCodes.Newobj, ctor_rmh);      // new RuntimeMethodHandle(stub)
            il.Emit(OpCodes.Ret);

            return (CreateHandleDelegate)dm.CreateDelegate(typeof(CreateHandleDelegate));
        }

        private static HandleToMethod? BuildMonoHandleToMethod()
        {
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            var noCheck = typeof(MethodBase).GetMethod("GetMethodFromHandleNoGenericCheck", F, null, [typeof(RuntimeMethodHandle)], null);

            if (noCheck is not null)
            {
                var f = (Func<RuntimeMethodHandle, MethodBase?>)Delegate.CreateDelegate(typeof(Func<RuntimeMethodHandle, MethodBase?>), noCheck);
                return (md, _) => f(Unsafe.As<IntPtr, RuntimeMethodHandle>(ref md));
            }

            return (md, _) => MethodBase.GetMethodFromHandle(Unsafe.As<IntPtr, RuntimeMethodHandle>(ref md));
        }

        public enum GenericContextKind
        {
            MethodDesc,
            MethodTable,
            ThisObject
        }

        public static GenericContextKind ClassifyContext(MethodBase m)
        {
            Helpers.ThrowIfArgumentNull(m);

            // https://www.mono-project.com/docs/advanced/runtime/docs/generic-sharing/

            // Generic methods get a MethodDesc encoding both class and method args
            if (m.IsGenericMethod)
            {
                return GenericContextKind.MethodDesc;
            }

            // non-generic non-static method on a generic reference type -> context is the table behind `this`
            if (!m.IsStatic && m.DeclaringType is { IsValueType: false })
            {
                return GenericContextKind.ThisObject;
            }

            return GenericContextKind.MethodTable;
        }

        // The ThisObject kind never reaches Read: those bodies dispatch through FromThisObject with a real,
        // GC-tracked object reference.
        public static Type[] Read(IntPtr context, GenericContextKind kind, MethodBase openSource)
        {
            Helpers.ThrowIfArgumentNull(openSource);

            if (PlatformDetection.Runtime == RuntimeKind.Mono)
            {
                return FromMonoRgctx(context, kind, openSource);
            }

            return kind switch
            {
                GenericContextKind.MethodDesc => FromMethodDesc(context),
                GenericContextKind.MethodTable => FromMethodTable(context),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static Type[] FromMonoRgctx(IntPtr context, GenericContextKind kind, MethodBase openSource)
        {
            if (context == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var declType = openSource.DeclaringType;

            if (kind == GenericContextKind.MethodDesc)
            {
                // MRGCTX layout begins: { MonoVTable* class_vtable; MonoGenericInst* method_inst; ... }
                // https://github.com/mono/mono/blob/main/mono/mini/mini.h#L1078
                var classVtable = *(IntPtr*)context;
                var methodInst = *(IntPtr*)((nint)context + IntPtr.Size);
                var methodArgs = ReadMonoGenericInst(methodInst);

                if (declType?.IsGenericType ?? false)
                {
                    var classArgs = ClassArgsFromVTable(classVtable, declType!);
                    return [.. classArgs, .. methodArgs];
                }
                else
                {
                    return methodArgs;
                }
            }

            return ClassArgsFromVTable(context, declType ?? throw new InvalidOperationException("No declaring type."));
        }

        private static Type[] ClassArgsFromVTable(IntPtr vtable, Type declType)
        {
            if (vtable == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(vtable));
            }

            var t = MonoTypeFromVTable(vtable);

            var def = declType.IsGenericType ? declType.GetGenericTypeDefinition() : declType;
            for (var cur = t; cur is not null; cur = cur.BaseType)
            {
                if ((cur.IsGenericType && cur.GetGenericTypeDefinition() == def) || cur == def)
                {
                    return cur.IsGenericType ? cur.GetGenericArguments() : Type.EmptyTypes;
                }
            }

            throw new InvalidOperationException($"Could not find {def} in hierarchy of {t}.");
        }

        private static Type[] ReadMonoGenericInst(IntPtr inst)
        {
            if (inst == IntPtr.Zero)
            {
                return Type.EmptyTypes;
            }
            // Assumes MONO_SMALL_CONFIG is not defined
            // TODO: handle other layouts? https://github.com/mono/mono/blob/main/mono/metadata/class-internals.h#L406
            var argc = (*(uint*)((nint)inst + 4) & 0x3FFFFFu); // Reads the first 22 bits representing argc
            if (argc == 0)
            {
                return Type.EmptyTypes;
            }

            var argv = (IntPtr*)((nint)inst + 8);
            var result = new Type[argc];
            for (var i = 0; i < argc; i++)
            {
                result[i] = RequireType(argv[i]);
            }

            return result;
        }
        // MonoVTable.klass is the first field (offset 0)
        // https://github.com/mono/mono/blob/main/mono/metadata/class-internals.h#L360
        private static Type? MonoTypeFromVTable(IntPtr vtable)
        {
            var klass = *(IntPtr*)vtable;
            Helpers.Assert(klass != IntPtr.Zero);
            return MonoClassToType(klass);
        }
        // mono_class_get_type
        private static IntPtr monoClassGetType;
        private static bool monoResolveAttempted;

        private static IntPtr MonoClassGetTypePtr()
        {
            if (monoResolveAttempted)
            {
                return monoClassGetType;
            }

            monoResolveAttempted = true;

            // Globally visible symbol?
            if (DynDll.TryOpenLibrary(null, out var main) && main.TryGetExport("mono_class_get_type", out var p) && p != IntPtr.Zero)
            {
                return monoClassGetType = p;
            }

            // In its own module
            string[] libs = ["mono-2.0-bdwgc", "mono-2.0-sgen", "mono-2.0-boehm", "monobdwgc-2.0", "monosgen-2.0", "mono-2.0", "mono"];
            foreach (var lib in libs)
            {
                if (DynDll.TryOpenLibrary(lib, out var h) &&
                    h.TryGetExport("mono_class_get_type", out var fp) && fp != IntPtr.Zero)
                {
                    return monoClassGetType = fp;
                }
            }

            throw new PlatformNotSupportedException($"{PlatformDetection.OS}, {PlatformDetection.Architecture}, {PlatformDetection.Runtime}: mono_class_get_type not found");
        }

        private static Type? MonoClassToType(IntPtr klass)
        {
            var fnptr = MonoClassGetTypePtr();

            // MonoType*
            var monoType = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)fnptr)(klass);
            Helpers.Assert(monoType != IntPtr.Zero);

            // Same MonoType* -> internal_from_handle
            return TryResolveType(monoType);
        }
    }
}
