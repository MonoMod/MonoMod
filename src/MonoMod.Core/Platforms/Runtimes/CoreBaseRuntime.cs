using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.IO;
using System.Linq;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal abstract class CoreBaseRuntime : FxCoreBaseRuntime, IInitialize
    {

        public static CoreBaseRuntime CreateForVersion(Version version, ISystem system, IArchitecture arch)
        {

            switch (version.Major)
            {
                case 2:
                case 4:
                    // .NET Core 2.x
                    // Note that .NET Core 2.x does not return a reasonable number for its version like 2.1, instead it gives 4.6.xxxxxx, like Framework.
                    return new Core21Runtime(system);

                case 3:
                    // .NET Core 3.x
                    return version.Minor switch
                    {
                        0 => new Core30Runtime(system),
                        1 => new Core31Runtime(system),
                        _ => throw new PlatformNotSupportedException($"Unknown .NET Core 3.x minor version {version.Minor}"),
                    };

                case 5:
                    // .NET 5.0.x
                    return new Core50Runtime(system);

                case 6:
                    // .NET 6.0.x
                    return new Core60Runtime(system);

                case 7:
                    // .NET 7.0.x
                    return new Core70Runtime(system, arch);
                case 8:
                    // .NET 8.0.x
                    return new Core80Runtime(system, arch);

                // currently, we need to manually add support for new versions.
                // TODO: possibly fall back to a JIT GUID check if we can?

                default:
                    throw new PlatformNotSupportedException($"CoreCLR version {version} is not supported");
            }

            throw new NotImplementedException();
        }

        public override RuntimeKind Target => RuntimeKind.CoreCLR;

        protected ISystem System { get; }

        protected CoreBaseRuntime(ISystem system)
        {
            System = system;

            if (PlatformDetection.Architecture == ArchitectureKind.x86_64 &&
                system.DefaultAbi is { } abi)
            {
                AbiCore = AbiForCoreFx45X64(abi);
            }
        }

        void IInitialize.Initialize()
        {
            InstallJitHook(JitObject);
        }

        private static bool IsMaybeClrJitPath(string path)
            => Path.GetFileNameWithoutExtension(path).EndsWith("clrjit", StringComparison.Ordinal);

        protected virtual string GetClrJitPath()
        {
            string? clrjitFile = null;

            if (Switches.TryGetSwitchValue(Switches.JitPath, out var swValue) && swValue is string jitPath)
            {
                if (!IsMaybeClrJitPath(jitPath))
                {
                    MMDbgLog.Warning($"Provided value for MonoMod.JitPath switch '{jitPath}' does not look like a ClrJIT path");
                }
                else
                {
                    clrjitFile = System.EnumerateLoadedModuleFiles()
                        .FirstOrDefault(f => f is not null && f == jitPath);
                    if (clrjitFile is null)
                    {
                        MMDbgLog.Warning($"Provided path for MonoMod.JitPath switch was not loaded in this process. jitPath: {jitPath}");
                    }
                }
            }

            clrjitFile ??= System.EnumerateLoadedModuleFiles()
                .FirstOrDefault(f => f is not null && IsMaybeClrJitPath(f));

            if (clrjitFile is null)
                throw new PlatformNotSupportedException("Could not locate clrjit library");

            MMDbgLog.Trace($"Got jit path: {clrjitFile}");
            return clrjitFile;
        }

        private IntPtr? lazyJitObject;
        protected IntPtr JitObject => lazyJitObject ??= GetJitObject();

        private unsafe IntPtr GetJitObject()
        {
            var path = GetClrJitPath();

            if (!DynDll.TryOpenLibrary(path, out var clrjit))
                throw new PlatformNotSupportedException("Could not open clrjit library");

            try
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr>)DynDll.GetExport(clrjit, "getJit"))();
            }
            catch
            {
                DynDll.CloseLibrary(clrjit);
                throw;
            }
        }

        protected abstract void InstallJitHook(IntPtr jit);

        private INativeExceptionHelper? lazyNativeExceptionHelper;
        protected INativeExceptionHelper? NativeExceptionHelper => lazyNativeExceptionHelper ??= System.NativeExceptionHelper;
        protected IntPtr EHNativeToManaged(IntPtr target, out IDisposable? handle)
        {
            if (NativeExceptionHelper is not null)
            {
                return NativeExceptionHelper.CreateNativeToManagedHelper(target, out handle);
            }
            handle = null;
            return target;
        }
        protected IntPtr EHManagedToNative(IntPtr target, out IDisposable? handle)
        {
            if (NativeExceptionHelper is not null)
            {
                return NativeExceptionHelper.CreateManagedToNativeHelper(target, out handle);
            }
            handle = null;
            return target;
        }
    }
}
