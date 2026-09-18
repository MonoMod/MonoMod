using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Core.Utils;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A triple of <see cref="IArchitecture"/>, <see cref="ISystem"/>, and <see cref="IRuntime"/> which provides higher-level operations
    /// based on the underlying implementations.
    /// </summary>
    public sealed class PlatformTriple
    {
        /// <summary>
        /// Creates an <see cref="IRuntime"/> implementation using the provided <see cref="ISystem"/> and <see cref="IArchitecture"/> according
        /// to the runtime detected by <see cref="PlatformDetection.Runtime"/>.
        /// </summary>
        /// <remarks>
        /// The runtime may utilize the values of <see cref="PlatformDetection"/> to make decisions about its behaviour. As such, the provided
        /// implementations must be for the current process.
        /// </remarks>
        /// <param name="system">The <see cref="ISystem"/> implementation for the runtime to use.</param>
        /// <param name="arch">The <see cref="IArchitecture"/> implementation for the runtime to use.</param>
        /// <returns>An <see cref="IRuntime"/> implementation for the currently running runtime.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown if <see cref="PlatformDetection.Runtime"/> returns an unsupported
        /// runtime kind.</exception>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static IRuntime CreateCurrentRuntime(ISystem system, IArchitecture arch)
        {
            Helpers.ThrowIfArgumentNull(system);
            Helpers.ThrowIfArgumentNull(arch);
            return PlatformDetection.Runtime switch
            {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion, system),
                RuntimeKind.CoreCLR => Runtimes.CoreBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion, system, arch),
                RuntimeKind.Mono => new Runtimes.MonoRuntime(system),
                var kind => throw new PlatformNotSupportedException($"Runtime kind {kind} not supported"),
            };
        }

        /// <summary>
        /// Creates an <see cref="IArchitecture"/> implementation using the provided <see cref="ISystem"/> according
        /// to the architecture detected by <see cref="PlatformDetection.Architecture"/>.
        /// </summary>
        /// <remarks>
        /// The architecture may utilize the values of <see cref="PlatformDetection"/> to make decisions about its behaviour. As such, the provided
        /// implementations must be for the current process.
        /// </remarks>
        /// <param name="system">The <see cref="ISystem"/> implementation for the architecture to use.</param>
        /// <returns>An <see cref="IArchitecture"/> implementation for the current architecture.</returns>
        /// <exception cref="NotImplementedException">Thrown if <see cref="PlatformDetection.Architecture"/> returns an architecture
        /// which will be supported in the future, but is not currently.</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown if <see cref="PlatformDetection.Architecture"/> returns an unsupported
        /// architecture kind.</exception>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static IArchitecture CreateCurrentArchitecture(ISystem system)
        {
            Helpers.ThrowIfArgumentNull(system);
            return PlatformDetection.Architecture switch
            {
                ArchitectureKind.x86 => new Architectures.x86Arch(system),
                ArchitectureKind.x86_64 => new Architectures.x86_64Arch(system),
                ArchitectureKind.Arm => throw new NotImplementedException(),
                ArchitectureKind.Arm64 => new Architectures.Arm64Arch(system),
                var kind => throw new PlatformNotSupportedException($"Architecture kind {kind} not supported"),
            };
        }

        /// <summary>
        /// Creates an <see cref="ISystem"/> implementation according to the operating system detected by <see cref="PlatformDetection.OS"/>.
        /// </summary>
        /// <returns>The <see cref="ISystem"/> implementation for the current operating system.</returns>
        /// <exception cref="NotImplementedException">Thrown if <see cref="PlatformDetection.OS"/> returns an operating system
        /// which will be supported in the future, but is not currently.</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown if <see cref="PlatformDetection.OS"/> returns an unsupported
        /// operating system.</exception>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static ISystem CreateCurrentSystem()
            => PlatformDetection.OS switch
            {
                OSKind.Posix => throw new NotImplementedException(),
                OSKind.Linux => new Systems.LinuxSystem(),
                OSKind.Android => throw new NotImplementedException(),
                OSKind.OSX => new Systems.MacOSSystem(),
                OSKind.IOS => throw new NotImplementedException(),
                OSKind.BSD => throw new NotImplementedException(),
                OSKind.Windows or OSKind.Wine => new Systems.WindowsSystem(),
                var kind => throw new PlatformNotSupportedException($"OS kind {kind} not supported"),
            };

        /// <summary>
        /// Gets the <see cref="IArchitecture"/> for this <see cref="PlatformTriple"/>.
        /// </summary>
        public IArchitecture Architecture { get; }
        /// <summary>
        /// Gets the <see cref="ISystem"/> for this <see cref="PlatformTriple"/>.
        /// </summary>
        /// <remarks>
        /// The <see cref="System"/> object may also implement <see cref="IControlFlowGuard"/>, if its operating system has such a feature.
        /// </remarks>
        public ISystem System { get; }
        /// <summary>
        /// Gets the <see cref="IRuntime"/> for this <see cref="PlatformTriple"/>.
        /// </summary>
        public IRuntime Runtime { get; }

        private static object lazyCurrentLock = new();
        private static PlatformTriple? lazyCurrent;
        /// <summary>
        /// Gets the current <see cref="PlatformTriple"/>.
        /// </summary>
        /// <remarks>
        /// This <see cref="PlatformTriple"/> is automatically constructed on first access, according to the values returned by <see cref="PlatformDetection"/>.
        /// </remarks>
        public static unsafe PlatformTriple Current => Helpers.GetOrInitWithLock(ref lazyCurrent, lazyCurrentLock, createCurrentFunc);

        private static readonly Func<PlatformTriple> createCurrentFunc = CreateCurrent;
        private static PlatformTriple CreateCurrent()
        {
            var sys = CreateCurrentSystem();
            var arch = CreateCurrentArchitecture(sys);
            var runtime = CreateCurrentRuntime(sys, arch);
            return new(arch, sys, runtime);
        }

        /// <summary>
        /// Sets the current <see cref="PlatformTriple"/>.
        /// </summary>
        /// <remarks>
        /// This must be called before the first invocation of <see cref="Current"/>.
        /// </remarks>
        /// <param name="triple">The <see cref="PlatformTriple"/> to set.</param>
        /// <exception cref="InvalidOperationException">Thrown if a platform triple was previously set, or <see cref="Current"/>
        /// was invoked before calling this method.</exception>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static void SetPlatformTriple(PlatformTriple triple)
        {
            Helpers.ThrowIfArgumentNull(triple);
            if (lazyCurrent is not null)
                ThrowTripleAlreadyExists();

            lock (lazyCurrentLock)
            {
                if (lazyCurrent is not null)
                    ThrowTripleAlreadyExists();

                lazyCurrent = triple;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTripleAlreadyExists()
        {
            throw new InvalidOperationException("The platform triple has already been initialized; cannot set a new one");
        }

        /// <summary>
        /// Constructs a <see cref="PlatformTriple"/> with the provided <see cref="IArchitecture"/>, <see cref="ISystem"/>, and <see cref="IRuntime"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each of the provided objects will be initialized, if they support it. The following interfaces are checked, and their initialize methods called:
        /// <list type="bullet">
        ///     <item><see cref="IInitialize{T}"/> of <see cref="ISystem"/></item>
        ///     <item><see cref="IInitialize{T}"/> of <see cref="IArchitecture"/></item>
        ///     <item><see cref="IInitialize{T}"/> of <see cref="IRuntime"/></item>
        ///     <item><see cref="IInitialize{T}"/> of <see cref="PlatformTriple"/></item>
        ///     <item><see cref="IInitialize"/></item>
        /// </list>
        /// After being initialized, <see cref="IRuntime.Abi"/> is read from the <see cref="IRuntime"/> instance.
        /// </para>
        /// </remarks>
        /// <param name="architecture">The <see cref="IArchitecture"/> to use.</param>
        /// <param name="system">The <see cref="ISystem"/> to use.</param>
        /// <param name="runtime">The <see cref="IRuntime"/> to use.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public PlatformTriple(IArchitecture architecture, ISystem system, IRuntime runtime)
        {
            Helpers.ThrowIfArgumentNull(architecture);
            Helpers.ThrowIfArgumentNull(system);
            Helpers.ThrowIfArgumentNull(runtime);

            Architecture = architecture;
            System = system;
            Runtime = runtime;

            // eagerly initialize this so that the check functions get as much inlined as possible
            SupportedFeatures = new(Architecture.Features, System.Features, Runtime.Features);

            InitIfNeeded(Architecture);
            InitIfNeeded(System);
            InitIfNeeded(Runtime);

            Abi = Runtime.Abi;
        }

        private void InitIfNeeded(object obj)
        {
            (obj as IInitialize<ISystem>)?.Initialize(System);
            (obj as IInitialize<IArchitecture>)?.Initialize(Architecture);
            (obj as IInitialize<IRuntime>)?.Initialize(Runtime);
            (obj as IInitialize<PlatformTriple>)?.Initialize(this);
            (obj as IInitialize)?.Initialize();
        }

        /// <summary>
        /// Gets the triple of <see cref="ArchitectureKind"/>, <see cref="OSKind"/>, and <see cref="RuntimeKind"/> represented by this <see cref="PlatformTriple"/>.
        /// </summary>
        public (ArchitectureKind Arch, OSKind OS, RuntimeKind Runtime) HostTriple => (Architecture.Target, System.Target, Runtime.Target);

        /// <summary>
        /// Gets the supported features of this <see cref="PlatformTriple"/>.
        /// </summary>
        public FeatureFlags SupportedFeatures { get; }

        /// <summary>
        /// Gets the ABI for this <see cref="PlatformTriple"/>.
        /// </summary>
        public Abi Abi { get; }

        /// <summary>
        /// Prepares <paramref name="method"/> by calling <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>, this method handles generic instantiations.
        /// In order to do this, however, it has to perform a fair bit of reflection on invocation. Avoid calling it multiple times
        /// for the same method, if possible.
        /// </remarks>
        /// <param name="method">The method to prepare.</param>
        public void Compile(MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(method);

            if (method.IsGenericMethodDefinition)
            {
                throw new ArgumentException("Cannot prepare generic method definition", nameof(method));
            }

            method = GetIdentifiable(method);

            // if this flag is set, then the runtime implementation is solely responsible for ensuring that the method gets compiled
            if (SupportedFeatures.Has(RuntimeFeature.RequiresCustomMethodCompile))
            {
                Runtime.Compile(method);
            }
            else
            {
                var handle = Runtime.GetMethodHandle(method);

                if (method.IsGenericMethod)
                {
                    // we need to get the handles of the type args too
                    var typeArgs = method.GetGenericArguments();
                    var argHandles = new RuntimeTypeHandle[typeArgs.Length];
                    for (var i = 0; i < typeArgs.Length; i++)
                        argHandles[i] = typeArgs[i].TypeHandle;

                    RuntimeHelpers.PrepareMethod(handle, argHandles);
                }
                else
                {
                    // or we can just call the normal PrepareMethod
                    RuntimeHelpers.PrepareMethod(handle);
                }
            }
        }

        /// <summary>
        /// Gets an "identifiable" <see cref="MethodBase"/> for a method, which has object identity.
        /// </summary>
        /// <param name="method">The method to identify.</param>
        /// <returns>The identifiable <see cref="MethodBase"/>.</returns>
        /// <seealso cref="IRuntime.GetIdentifiable(MethodBase)"/>
        public MethodBase GetIdentifiable(MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(method);

            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodIdentification))
            {
                // see the comment in PinMethodIfNeeded
                method = Runtime.GetIdentifiable(method);
            }

            // because the .NET reflection APIs are really bad, two MethodBases may not compare equal if they represent the same method
            // *but were gotten through different means*. Because MemberInfo.ReflectedType exists.
            // In order to fix this, when getting an identifiable method, we make sure to correct it, by retrieving it directly from its declaring type (or module, as it may be)
            if (method.ReflectedType != method.DeclaringType)
            {
                var parameters = method.GetParameters();
                var paramTypes = new Type[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    paramTypes[i] = parameters[i].ParameterType;
                }

                if (method.DeclaringType is null)
                {
                    // the method lives on the module, get it from there
                    var got = method.Module.GetMethod(method.Name, (BindingFlags)(-1), null, method.CallingConvention, paramTypes, null);
                    Helpers.Assert(got is not null, $"orig: {method}, module: {method.Module}");
                    method = got;
                }
                else
                {
                    // the method has a declaring type, get it there
                    if (method.IsConstructor)
                    {
                        var got = method.DeclaringType.GetConstructor((BindingFlags)(-1), null, method.CallingConvention, paramTypes, null);
                        Helpers.Assert(got is not null, $"orig: {method}");
                        method = got;
                    }
                    else
                    {
                        var got = method.DeclaringType.GetMethod(method.Name, (BindingFlags)(-1), null, method.CallingConvention, paramTypes, null);
                        Helpers.Assert(got is not null, $"orig: {method}");
                        method = got;
                    }
                }
            }

            return method;
        }

        /// <summary>
        /// Pins a method if that is required by the runtime.
        /// </summary>
        /// <param name="method">The method to pin.</param>
        /// <returns>An <see cref="IDisposable"/> which managed the lifetime of the pin.</returns>
        /// <seealso cref="IRuntime.PinMethodIfNeeded(MethodBase)"/>
        public IDisposable? PinMethodIfNeeded(MethodBase method)
        {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodPinning))
            {
                // only make the interface call if it's needed, because interface dispatches are slow
                return Runtime.PinMethodIfNeeded(method);
            }

            // otherwise, always return
            return null;
        }

        /// <summary>
        /// Tries to disable inlining of the provided method, if the underlying runtime supports it.
        /// </summary>
        /// <param name="method">The method to disable inlining of.</param>
        /// <returns><see langword="true"/> if inlining could be disabled; <see langword="false"/> otherwise.</returns>
        public bool TryDisableInlining(MethodBase method)
        {
            if (SupportedFeatures.Has(RuntimeFeature.DisableInlining))
            {
                Runtime.DisableInlining(method);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates and applies a <see cref="SimpleNativeDetour"/> from one address to another.
        /// </summary>
        /// <param name="from">The address to detour.</param>
        /// <param name="to">The target of the detour.</param>
        /// <param name="detourMaxSize">The maximum size available for the detour.</param>
        /// <param name="fromRw">The address to write the detour to, if that is different from <paramref name="from"/>. Otherwise, the default value of <see langword="default"/> should be passed.
        /// Refer to <see cref="OnMethodCompiledCallback"/> for more information.</param>
        /// <returns>A <see cref="SimpleNativeDetour"/> instance managing the generated detour.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "allocHandle is correctly transferred around, as needed")]
        public SimpleNativeDetour CreateSimpleDetour(IntPtr from, IntPtr to, int detourMaxSize = -1, IntPtr fromRw = default)
        {
            if (fromRw == default)
            {
                fromRw = from;
            }
            Helpers.Assert(from != to, $"Cannot detour a method to itself! (from: {from}, to: {to})");

            MMDbgLog.Trace($"Creating simple detour 0x{from:x16} => 0x{to:x16}");

            var detourInfo = Architecture.ComputeDetourInfo(from, to, detourMaxSize);

            // detours are usually fairly small, so we'll stackalloc it
            Span<byte> detourData = stackalloc byte[detourInfo.Size];

            // get the detour bytes from the architecture
            var size = Architecture.GetDetourBytes(detourInfo, detourData, out var allocHandle);

            // these should be the same
            Helpers.DAssert(size == detourInfo.Size);

            // allocate a backup
            var backup = new byte[detourInfo.Size];

            // now we can apply the detour through the system
            System.PatchData(PatchTargetKind.Executable, fromRw, detourData, backup);

            // and now we just create the NativeDetour object
            return new SimpleNativeDetour(this, detourInfo, backup, allocHandle);
        }

        /// <summary>
        /// A wrapper struct containing a <see cref="SimpleNativeDetour"/> and alternate entrypoint handle.
        /// </summary>
        /// <param name="Simple">The <see cref="SimpleNativeDetour"/> instance backing this detour.</param>
        /// <param name="AltEntry">A pointer to the generated alternate entrypoint, if one was generated.</param>
        /// <param name="AltHandle">The memory handle for the alternate entry point, if one is present.</param>
        /// <seealso cref="CreateNativeDetour(IntPtr, IntPtr, int, IntPtr)"/>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible",
            Justification = "This type should rarely be used, and should not exist in the above namespace to avoid people trying to use it when they shouldn't.")]
        public record struct NativeDetour(SimpleNativeDetour Simple, IntPtr AltEntry, IDisposable? AltHandle)
        {
            /// <summary>
            /// Gets whether or not this instance is holding an alternate entrypoint in <see cref="AltEntry"/>.
            /// </summary>
            public bool HasAltEntry => AltEntry != IntPtr.Zero;
        }

        /// <summary>
        /// Creates a <see cref="NativeDetour"/>. This is basically identical to <see cref="CreateSimpleDetour(IntPtr, IntPtr, int, IntPtr)"/>,
        /// except that it generates an alternate entrypoint for <paramref name="from"/>.
        /// </summary>
        /// <remarks>
        /// This method will always create an alternate entrypoint in <see cref="NativeDetour.AltEntry"/> when <see cref="ArchitectureFeature.CreateAltEntryPoint"/>
        /// is available.
        /// </remarks>
        /// <param name="from">The address to detour.</param>
        /// <param name="to">The target of the detour.</param>
        /// <param name="detourMaxSize">The maximum size available for the detour.</param>
        /// <param name="fromRw">The address to write the detour to, if that is different from <paramref name="from"/>. Otherwise, the default value of <see langword="default"/> should be passed.
        /// Refer to <see cref="OnMethodCompiledCallback"/> for more information.</param>
        /// <returns>A <see cref="NativeDetour"/> instance managing the generated detour.</returns>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "allocHandle is correctly transferred around, as needed")]
        public NativeDetour CreateNativeDetour(IntPtr from, IntPtr to, int detourMaxSize = -1, IntPtr fromRw = default)
        {
            if (fromRw == default)
            {
                fromRw = from;
            }
            Helpers.Assert(from != to, $"Cannot detour a method to itself! (from: {from}, to: {to})");

            MMDbgLog.Trace($"Creating simple detour 0x{from:x16} => 0x{to:x16}");

            var detourInfo = Architecture.ComputeDetourInfo(from, to, detourMaxSize);

            // detours are usually fairly small, so we'll stackalloc it
            Span<byte> detourData = stackalloc byte[detourInfo.Size];

            // get the detour bytes from the architecture
            var size = Architecture.GetDetourBytes(detourInfo, detourData, out var allocHandle);

            // these should be the same
            Helpers.DAssert(size == detourInfo.Size);

            // now that we have the detour size, we'll try to allocate an alternate entry point
            var altEntry = IntPtr.Zero;
            IDisposable? altHandle = null;
            if (SupportedFeatures.Has(ArchitectureFeature.CreateAltEntryPoint))
            {
                altEntry = Architecture.AltEntryFactory.CreateAlternateEntrypoint(from, size, out altHandle);
            }
            else
            {
                MMDbgLog.Warning($"Cannot create alternate entry point for native detour (from: {from:x16}, to: {to:x16}");
            }

            // allocate a backup
            var backup = new byte[detourInfo.Size];

            // now we can apply the detour through the system
            System.PatchData(PatchTargetKind.Executable, fromRw, detourData, backup);

            // and now we just create the NativeDetour object
            return new NativeDetour(new(this, detourInfo, backup, allocHandle), altEntry, altHandle);
        }

        /// <summary>
        /// Gets the native method body for a method.
        /// </summary>
        /// <param name="method">The method to get the body of.</param>
        /// <returns>A pointer to the native method body of the method.</returns>
        public IntPtr GetNativeMethodBody(MethodBase method)
        {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresBodyThunkWalking))
            {
                return GetNativeMethodBodyWalk(method, reloadPtr: true);
            }
            else
            {
                return GetNativeMethodBodyDirect(method);
            }
        }

        private unsafe IntPtr GetNativeMethodBodyWalk(MethodBase method, bool reloadPtr)
        {
            var regenerated = false;
            var didPrepareLastIter = false;
            var iters = 0;

            var archMatchCollection = Architecture.KnownMethodThunks;

            MMDbgLog.Trace($"Performing method body walk for {method}");

            nint prevEntry = -1;

            ReloadFuncPtr:
            var entry = (nint)Runtime.GetMethodEntryPoint(method);
            MMDbgLog.Trace($"Starting entry point = 0x{entry:x16}");
            do
            {
                if (iters++ > 20)
                {
                    MMDbgLog.Error($"Could not get entry point for {method}! (tried {iters} times) entry: 0x{entry:x16} prevEntry: 0x{prevEntry:x16}");
                    throw new NotSupportedException(DebugFormatter.Format($"Could not get entrypoint for {method} (stuck in a loop)"));
                }

                if (!didPrepareLastIter && prevEntry == entry)
                {
                    // we're in a loop, break out
                    break;
                }
                prevEntry = entry;

                var readableLen = System.GetSizeOfReadableMemory(entry, archMatchCollection.MaxMinLength);
                if (readableLen <= 0)
                {
                    MMDbgLog.Warning($"Got zero or negative readable length {readableLen} at 0x{entry:x16}");
                }

                // we still have to limit it like this because otherwise it'll scan and find *other* stubs
                // if we want to, we could scan for an arch-specific padding pattern and use that to limit instead
                var span = new ReadOnlySpan<byte>((void*)entry, Math.Min((int)readableLen, archMatchCollection.MaxMinLength));

                // TODO: be more limiting with which patterns can be scanned forward and which cannot
                if (!archMatchCollection.TryFindMatch(span, out var addr, out var match, out var offset, out _))
                    break;

                var lastEntry = entry;

                didPrepareLastIter = false;

                var meaning = match.AddressMeaning;
                MMDbgLog.Trace($"Matched thunk with {meaning} at 0x{entry:x16} (addr: 0x{addr:x8}, offset: {offset})");
                if (meaning.Kind.IsPrecodeFixup() && !regenerated)
                {
                    var precode = meaning.ProcessAddress(entry, offset, addr);
                    if (reloadPtr)
                    {
                        MMDbgLog.Trace($"Method thunk reset; regenerating (PrecodeFixupThunk: 0x{precode:X16})");
                        Compile(method);
                        didPrepareLastIter = true;
                        //regenerated = true;
                        goto ReloadFuncPtr;
                    }
                    else
                    {
                        entry = precode;
                    }
                }
                else
                {
                    entry = meaning.ProcessAddress(entry, offset, addr);
                }
                MMDbgLog.Trace($"Got next entry point 0x{entry:x16}");

                entry = NotThePreStub(lastEntry, entry, out var wasPreStub);
                if (wasPreStub && reloadPtr)
                {
                    MMDbgLog.Trace("Matched ThePreStub");
                    Compile(method);
                    //regenerated = true;
                    goto ReloadFuncPtr;
                }
            } while (true);

            return entry;
        }

        private unsafe IntPtr GetNativeMethodBodyDirect(MethodBase method)
        {
            return Runtime.GetMethodEntryPoint(method);
        }

        private IntPtr ThePreStub = IntPtr.Zero;

        // TODO: make this something actually runtime-dependent
        private IntPtr NotThePreStub(IntPtr ptrGot, IntPtr ptrParsed, out bool wasPreStub)
        {
            if (ThePreStub == IntPtr.Zero)
            {
                ThePreStub = (IntPtr)(-2);

                // FIXME: Find a better less likely called NGEN'd candidate that points to ThePreStub.
                // This was "found" by tModLoader.
                // Can be missing in .NET 5.0 outside of Windows for some reason.

                // Instead of using any specific method on System.Net.Connection, we just check all of them, as (hopefully) most aren't called by this point
                var pre = typeof(System.Net.HttpWebRequest).Assembly
                    .GetType("System.Net.Connection")
                    ?.GetMethods()
                    .GroupBy(m => GetNativeMethodBodyWalk(m, reloadPtr: false))
                    .First(g => g.Count() > 1)
                    .Key ?? (nint)(-1);

                ThePreStub = pre;
                MMDbgLog.Trace($"ThePreStub: 0x{ThePreStub:X16}");
            }

            wasPreStub = ptrParsed == ThePreStub /*|| ThePreStub == (IntPtr) (-1)*/;

            return wasPreStub ? ptrGot : ptrParsed;
        }

        /// <summary>
        /// Gets the 'real detour target' when detouring <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <remarks>
        /// This will return an ABI fixup method instead of <paramref name="to"/> when needed, automatically
        /// generating them when needed. When this is needed depends on the argument order in the ABI.
        /// </remarks>
        /// <param name="from">The method being detoured from.</param>
        /// <param name="to">The method being detoured to.</param>
        /// <returns>The method to detour to instead of <paramref name="to"/>.</returns>
        public MethodBase GetRealDetourTarget(MethodBase from, MethodBase to)
        {
            Helpers.ThrowIfArgumentNull(from);
            Helpers.ThrowIfArgumentNull(to);

            // TODO: check that `from` and `to` are actually argument- and return-compatible.
            // When we use this method internally, all the necessary checks are already performed
            // elsewhere, so we do not need to repeat them here. However, this method is part of
            // the public API, and currently, it produces faulty results for unsanitized inputs.
            // So, maybe we should do something about that.

            to = GetIdentifiable(to);
            if (from is not MethodInfo fromInfo || to is not MethodInfo toInfo)
                return to;

            var returnBufferIsArgument = false;
            foreach (var arg in Abi.ArgumentOrder.Span)
            {
                if (arg is SpecialArgumentKind.ReturnBuffer)
                {
                    returnBufferIsArgument = true;
                    break;
                }
            }

            // Whenever we detour a call from an instance method to a static one, `this` needs to
            // change its position to morph into a simple argument. Otherwise, it will be swapped
            // with the return buffer, causing a spectacular failure on the callee side:
            //
            // Argument order for the instance method: [ThisPointer,  ReturnBuffer, UserArguments]
            // Argument order for the static method:   [ReturnBuffer, ThisPointer,  UserArguments]
            //
            // Note that this fixup is only relevant when there is a return buffer between `this`
            // and the user arguments to begin with. Otherwise, both instance and static methods
            // will have the exact same effective argument order: [ThisPointer, UserArguments].
            // A return buffer is only present in scenarios where a value is returned by reference.
            //
            // Consequently, there is no need for a fixup for ABIs that declare argument order
            // like this: [ReturnBuffer, ThisPointer, UserArguments] (e.g., Mono on Linux).
            // While there is no need, there is also no harm in applying it anyways, as it will
            // act as a simple passthrough.
            // However, TODO: this scenario can be optimized out of existence.
            var returnType = fromInfo.ReturnType;
            var hasReturnBuffer = Abi.Classify(returnType, true) is TypeClassification.ByReference;
            var hasThis = !fromInfo.IsStatic;
            var requiresReturnBufferFixup = hasThis && toInfo.IsStatic && hasReturnBuffer && returnBufferIsArgument;

            // Whenever we detour a call from a generic method, depending on the ABI, we may
            // receive a generic context as an argument, which a callee never needs, at least
            // in the form of an explicitly passed argument, even if it is also a generic method.
            // The reason here is simple: we do NOT allow specifying a generic method definition
            // as a detour target (while that would make sense from a user's perspective, it
            // requires much more work to function properly), thus, `to` can only be
            // a constructed generic method with its generic context baked in.
            // This leads to a pretty bad discrepancy:
            //
            // Caller's argument order: [ThisPointer, ReturnBuffer, GenericContext, UserArguments]
            // Callee's argument order: [ThisPointer, ReturnBuffer, UserArguments]
            //
            // Because of this, the callee will receive a pointer to the generic context information
            // instead of its first argument, causing all subsequent arguments to be shifted by one,
            // resulting in all types of phantasmagorical problems that will immediately blow up in
            // the user's face. Moreover, to add insult to injury, there is no way to access
            // the last argument, although this is the least of our concerns here.
            //
            // Note that we only need this fixup if the current ABI explicitly defines the generic
            // context as an argument. For example, Mono on Windows/Wine x86_64 stores a pointer to
            // the generic context struct in r10, so generic injections somewhat work there already.
            //
            // Consequently, there is no need for a fixup for ABIs that declare `GenericContext`
            // **after** `UserArguments`. However, as far as I know, this is only relevant for
            // RyuJITx86, so there is no value in optimizing for that scenario.
            var requiresGenericContextFixup = HasGenericContext(Abi) && Runtime.RequiresGenericContext(fromInfo);

            // Check if a fixup is needed for the current scenario.
            if (!requiresReturnBufferFixup && !requiresGenericContextFixup)
            {
                // `from` and `to` are considered compatible at this point.
                // Thus, no ABI fixups are needed, return `to` as is.
                return to;
            }

            var returnBufferType = hasReturnBuffer ? returnType.MakeByRefType() : returnType;
            var newReturnType = hasReturnBuffer && !Abi.ReturnsReturnBuffer ? typeof(void) : returnBufferType;

            var thisPos = -1;
            var returnBufferPos = -1;
            var userArgumentsOffset = -1;
            var parameters = from.GetParameters();
            var argumentTypes = new List<Type>(parameters.Length + 3);
            var argumentKinds = Abi.ArgumentOrder.Span;
            for (var i = 0; i < argumentKinds.Length; i++)
            {
                switch (argumentKinds[i])
                {
                    case SpecialArgumentKind.ThisPointer when hasThis:
                        thisPos = argumentTypes.Count;
                        argumentTypes.Add(from.GetThisParamType());
                        break;

                    case SpecialArgumentKind.ReturnBuffer when hasReturnBuffer:
                        returnBufferPos = argumentTypes.Count;
                        argumentTypes.Add(returnBufferType);
                        break;

                    case SpecialArgumentKind.GenericContext when requiresGenericContextFixup:
                        // Currently, we do the bare minimum: we acknowledge that
                        // the generic context exists. That's all. After that,
                        // we simply throw it out of the window and rely on
                        // the generic context (if any) baked into in the callee.
                        //
                        // While this does work fine, it introduces funny little
                        // holes in the type-safety premise of .NET:
                        //
                        // // This may return `false`!
                        // bool Hook<T>(T it)
                        //     => typeof(T).IsAssignableFrom(it.GetType());
                        //
                        // This behavior is caused by the fact that constructed
                        // generics for reference types are Java'd into a single
                        // definition at runtime (and rightfully so).
                        // So, even if you try to detour a specific generic
                        // implementation using something like
                        // `to.MakeGenericMethod(typeof(string))`,
                        // your hook will receive calls for all
                        // reference type-based implementations out there.
                        // However, since we do not patch the generic context
                        // of the provided hook, it remains unchanged,
                        // causing `typeof(T)` to defy users' expectations.
                        //
                        // So, TODO: patch the generic context of the detour target
                        // and/or introduce a call filter based on the current context.

                        // The generic context is passed as a pointer to a struct that
                        // contains all the needed (?) information.
                        // We can treat it as a simple `IntPtr`.
                        argumentTypes.Add(typeof(nint));
                        break;

                    case SpecialArgumentKind.UserArguments:
                        userArgumentsOffset = argumentTypes.Count;
                        argumentTypes.AddRange(parameters.Select(static p => p.ParameterType));
                        break;
                }
            }

            Helpers.DAssert(thisPos >= 0 || !hasThis);
            // note: ARM64 (sysv) uses a dedicated register for the return buffer, so we don't record it as an argument
            Helpers.DAssert(!Abi.ReturnsReturnBuffer || !hasReturnBuffer || returnBufferPos >= 0);
            Helpers.DAssert(userArgumentsOffset >= 0);

            using var dmd = new DynamicMethodDefinition(
                DebugFormatter.Format($"Glue:AbiFixup<{from},{to}>"),
                newReturnType, argumentTypes.ToArray()
            );
            // TODO: make DMD apply attributes to the generated DynamicMethod, when possible
            dmd.Definition!.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining |
                (Mono.Cecil.MethodImplAttributes)(int)MethodImplOptionsEx.AggressiveOptimization;

            var il = dmd.GetILProcessor();

            // load return buffer
            if (hasReturnBuffer && returnBufferPos >= 0)
                il.Emit(OpCodes.Ldarg, returnBufferPos);

            // load thisptr
            if (hasThis)
                il.Emit(OpCodes.Ldarg, thisPos);

            // load user arguments
            for (var i = 0; i < parameters.Length; i++)
                il.Emit(OpCodes.Ldarg, i + userArgumentsOffset);

            // call the target method
            il.Emit(OpCodes.Call, il.Body.Method.Module.ImportReference(to));

            // store the returned object
            if (hasReturnBuffer && returnBufferPos >= 0)
                il.Emit(OpCodes.Stobj, il.Body.Method.Module.ImportReference(returnType));

            // if we need to return the pointer, do that
            if (hasReturnBuffer && Abi.ReturnsReturnBuffer)
                il.Emit(OpCodes.Ldarg, returnBufferPos);

            // then we're done
            il.Emit(OpCodes.Ret);

            return dmd.Generate();
        }

        private static bool HasGenericContext(Abi abi)
        {
            var arguments = abi.ArgumentOrder.Span;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is SpecialArgumentKind.GenericContext)
                    return true;
            }
            return false;
        }
    }
}
