using MonoMod.Backports;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal sealed class MonoRuntime : IRuntime
    {
        public RuntimeKind Target => RuntimeKind.Mono;

        public RuntimeFeature Features =>
            RuntimeFeature.DisableInlining |
            RuntimeFeature.RequiresMethodPinning |
            RuntimeFeature.RequiresMethodIdentification |
            RuntimeFeature.RequiresCustomMethodCompile | // PrepareMethod doesn't actually compile the method...
            RuntimeFeature.PreciseGC | // some builds use SGen, which is a precise GC, while
                                       // others, such as Unity, use Boehm, which is a conservative GC.
            RuntimeFeature.GenericSharing;

        public Abi Abi { get; }

        private readonly ISystem system;

        private static TypeClassification LinuxAmd64Classifier(Type type, bool isReturn)
        {
            // this is implemented by mini-amd64.c get_call_info

            // first, always get the underlying type
            if (type.IsEnum)
                type = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).First().FieldType;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Empty:
                    // size == 0???
                    return TypeClassification.InRegister;
                case TypeCode.Object:
                case TypeCode.DBNull:
                case TypeCode.String:
                    // reference types
                    return TypeClassification.InRegister;

                case TypeCode.Boolean:
                case TypeCode.Char:
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    // integer types
                    return TypeClassification.InRegister;

                case TypeCode.Single:
                case TypeCode.Double:
                    // floating point types (via SSE)
                    return TypeClassification.InRegister;
            }

            // pointer types
            if (type.IsPointer)
                return TypeClassification.InRegister;
            if (type.IsByRef)
                return TypeClassification.InRegister;

            // native integer types
            if (type == typeof(IntPtr) || type == typeof(UIntPtr))
                return TypeClassification.InRegister;

            if (type == typeof(void))
                return TypeClassification.InRegister;

            Helpers.Assert(type.IsValueType);

            // valuetype handling is implemented by add_valuetype 
            return ClassifyValueType(type, true);
        }

        private static TypeClassification ClassifyValueType(Type type, bool isReturn)
        {
            // this is implemented by mini-amd64.c add_valuetype
            var size = type.GetManagedSize();

            var passOnStack = (!isReturn || size is not 8) && (isReturn || size > 16);

            /*
            foreach (var field in NestedValutypeFields(type)) {
                if ((fields [i].offset < 8) && (fields [i].offset + fields [i].size) > 8) {
                    pass_on_stack = TRUE;
                    break;
                }
                // TODO: how?
            }
            */

            if (size == 0)
                return TypeClassification.InRegister;

            if (passOnStack)
            {
                return isReturn ? TypeClassification.ByReference : TypeClassification.OnStack;
            }

            var nquads = size > 8 ? 2 : 1;

            // mono_class_value_size???
            //var n = /*mono_class_value_size*/size;
            //var quadsize0 = n >= 8 ? 8 : n;
            //var quadsize1 = n >= 8 ? Math.Max(n - 8, 8) : 0;

            const int ClassInteger = 1;
            const int ClassMemory = 2;

            var args0 = ClassInteger;
            var args1 = ClassInteger;

            if (isReturn && nquads != 1)
            {
                args0 = args1 = ClassMemory;
            }

            if (args0 is ClassMemory || args1 is ClassMemory)
            {
                args0 = /*args1 =*/ ClassMemory;
            }

            // it then goes on to try to allocate regs, but we don't need to do that here
            return args0 switch
            {
                ClassInteger => TypeClassification.InRegister,
                ClassMemory => TypeClassification.OnStack,
                _ => throw new InvalidOperationException()
            };
        }

        private static IEnumerable<FieldInfo> NestedValutypeFields(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType.IsValueType)
                {
                    foreach (var f in NestedValutypeFields(field.FieldType))
                    {
                        yield return f;
                    }
                }
                else
                {
                    yield return field;
                }
            }
        }

        public MonoRuntime(ISystem system)
        {
            this.system = system;

            // see https://github.com/dotnet/runtime/blob/v6.0.5/src/mono/mono/mini/mini-amd64.c line 472, 847, 1735
            if (system.DefaultAbi is { } abi)
            {
                if (PlatformDetection.OS.GetKernel() is OSKind.Linux or OSKind.OSX && PlatformDetection.Architecture is ArchitectureKind.x86_64)
                {
                    // Linux on AMD64 doesn't actually use SystemV for managed calls.
                    abi = abi with
                    {
                        Classifier = LinuxAmd64Classifier
                    };
                }
                // notably, in Mono, the generic context pointer is not an argument in the normal calling convention, but an argument elsewhere (r10 on x64)
                if (PlatformDetection.OS is OSKind.Windows or OSKind.Wine && PlatformDetection.Architecture is ArchitectureKind.x86_64 or ArchitectureKind.x86)
                {
                    // on x86_64, it seems like Mono always uses this, ret, args order
                    // TODO: there are probably other platforms that have this same argument order, 
                    abi = abi with
                    {
                        ArgumentOrder = new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.UserArguments }
                    };
                }
                Abi = abi;

            }
            else
            {
                throw new InvalidOperationException("Cannot use Mono system, because the underlying system doesn't provide a default ABI!");
            }
        }

#pragma warning disable CS0067 // The event 'MonoRuntime.OnMethodCompiled' is never used
        // The Mono runtime doesn't implement a JIT hook at the moment.
        public event OnMethodCompiledCallback? OnMethodCompiled;
#pragma warning restore CS0067 // The event 'MonoRuntime.OnMethodCompiled' is never used

        public unsafe void DisableInlining(MethodBase method)
        {
            var handle = GetMethodHandle(method);
            // https://github.com/mono/mono/blob/34dee0ea4e969d6d5b37cb842fc3b9f73f2dc2ae/mono/metadata/class-internals.h#L64
            var iflags = (ushort*)((long)handle.Value + 2);
            *iflags |= (ushort)MethodImplOptionsEx.NoInlining;
        }

        private static readonly MethodInfo _DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo _DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance)!;

        public RuntimeMethodHandle GetMethodHandle(MethodBase method)
        {
            // Compile the method handle before getting our hands on the final method handle.
            // Note that Mono can return RuntimeMethodInfo instead of DynamicMethod in some places, thus bypassing this.
            // Let's assume that the method was already compiled ahead of this method call if that is the case.
            if (method is DynamicMethod)
            {
                _DynamicMethod_CreateDynMethod?.Invoke(method, ArrayEx.Empty<object?>());
                if (_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle)_DynamicMethod_mhandle.GetValue(method)!;
            }

            return method.MethodHandle;
        }

        private sealed class PrivateMethodPin
        {
            private readonly MonoRuntime runtime;
            public PrivateMethodPin(MonoRuntime runtime)
                => this.runtime = runtime;

            public MethodPinInfo Pin;

            public void UnpinOnce() => runtime.UnpinOnce(this);
        }

        private sealed class PinHandle : IDisposable
        {
            private readonly PrivateMethodPin pin;
            public PinHandle(PrivateMethodPin pin)
            {
                this.pin = pin;
            }

            private bool disposedValue;

            private void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // dispose managed state (managed objects)
                    }

                    pin.UnpinOnce();
                    disposedValue = true;
                }
            }

            ~PinHandle()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        private struct MethodPinInfo
        {
            public int Count;
            public MethodBase Method;
            public RuntimeMethodHandle Handle;

            public override string ToString()
            {
                return $"(MethodPinInfo: {Count}, {Method}, 0x{(long)Handle.Value:X})";
            }
        }

        private readonly ConcurrentDictionary<MethodBase, PrivateMethodPin> pinnedMethods = new();
        private readonly ConcurrentDictionary<RuntimeMethodHandle, PrivateMethodPin> pinnedHandles = new();

        public IDisposable? PinMethodIfNeeded(MethodBase method)
        {
            method = GetIdentifiable(method);

            var pin = pinnedMethods.GetOrAdd(method, m =>
            {
                var pin = new PrivateMethodPin(this);

                pin.Pin.Method = m;
                var handle = pin.Pin.Handle = GetMethodHandle(m);
                pinnedHandles[handle] = pin;

                DisableInlining(method);
                if (method.DeclaringType?.IsGenericType ?? false)
                {
                    // TODO: PrepareMethod
                    //PrepareMethod(method, handle, method.DeclaringType.GetGenericArguments().Select(type => type.TypeHandle).ToArray());
                }
                else
                {
                    //PrepareMethod(method, handle);
                }

                return pin;
            });
            Interlocked.Increment(ref pin.Pin.Count);

            return new PinHandle(pin);
        }

        private void UnpinOnce(PrivateMethodPin pin)
        {
            if (Interlocked.Decrement(ref pin.Pin.Count) <= 0)
            {
                pinnedMethods.TryRemove(pin.Pin.Method, out _);
                pinnedHandles.TryRemove(pin.Pin.Handle, out _);
            }
        }

        public MethodBase GetIdentifiable(MethodBase method)
        {
            return pinnedHandles.TryGetValue(GetMethodHandle(method), out var pin) ? pin.Pin.Method : method;
        }

        public IntPtr GetMethodEntryPoint(MethodBase method)
        {
            if (pinnedMethods.TryGetValue(method, out var pmp))
            {
                return pmp.Pin.Handle.GetFunctionPointer();
            }
            var handle = GetMethodHandle(method);
            return handle.GetFunctionPointer();
        }

        public void Compile(MethodBase method)
        {
            // GetFunctionPointer forces the method to be compiled on Mono
            _ = GetMethodHandle(method).GetFunctionPointer();
        }
    }
}
