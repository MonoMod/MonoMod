using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface INeedsHostTripleInit {
        void Initialize(HostTripleDetourFactory factory);
        void PostInit();
    }

    public class HostTripleDetourFactory : IDetourFactory {

        public IArchitecture Architecture { get; }
        public ISystem System { get; }
        public IRuntime Runtime { get; }

        public static HostTripleDetourFactory CreateCurrent()
            => new(Platform.CreateCurrentArchitecture(), Platform.CreateCurrentSystem(), Platform.CreateCurrentRuntime());

        public HostTripleDetourFactory(IArchitecture architecture, ISystem system, IRuntime runtime) {
            Helpers.ThrowIfNull(architecture);
            Helpers.ThrowIfNull(system);
            Helpers.ThrowIfNull(runtime);

            Architecture = architecture;
            System = system;
            Runtime = runtime;

            // init the interface implementations
            InitIfNeeded(architecture, out var archIniter);
            InitIfNeeded(system, out var sysIniter);
            InitIfNeeded(runtime, out var rtIniter);
            archIniter?.PostInit();
            sysIniter?.PostInit();
            rtIniter?.PostInit();
        }

        private void InitIfNeeded(object obj, out INeedsHostTripleInit? initer) {
            if (obj is INeedsHostTripleInit init) {
                initer = init;
                init.Initialize(this);
            } else {
                initer = null;
            }
        }

        public (ArchitectureKind Arch, OSKind OS, RuntimeKind Runtime) HostTriple => (Architecture.Target, System.Target, Runtime.Target);

        private FeatureFlags? lazySupportedFeatures;
        public FeatureFlags SupportedFeatures => lazySupportedFeatures ??= new(Architecture.Features, System.Features, Runtime.Features);

        /// <summary>
        /// Prepares <paramref name="method"/> by calling <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>, this method handles generic instantiations.
        /// In order to do this, however, it has to perform a fair bit of reflection on invocation. Avoid calling it multiple times
        /// for the same method, if possible.
        /// </remarks>
        /// <param name="method">The method to prepare.</param>
        public void Prepare(MethodBase method) {
            Helpers.ThrowIfNull(method);

            if (method.IsGenericMethodDefinition) {
                throw new ArgumentException("Cannot prepare generic method definition", nameof(method));
            }

            method = GetIdentifiable(method);
            var handle = Runtime.GetMethodHandle(method);

            if (method.IsGenericMethod) {
                // we need to get the handles of the type args too
                var typeArgs = method.GetGenericArguments();
                var argHandles = new RuntimeTypeHandle[typeArgs.Length];
                for (int i = 0; i < typeArgs.Length; i++)
                    argHandles[i] = typeArgs[i].TypeHandle;

                RuntimeHelpers.PrepareMethod(handle, argHandles);
            } else {
                // or we can just call the normal PrepareMethod
                RuntimeHelpers.PrepareMethod(handle);
            }
        }

        public MethodBase GetIdentifiable(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodIdentification)) {
                // see the comment in PinMethodIfNeeded
                return Runtime.GetIdentifiable(method);
            }

            // if the runtime doesn't require method identification, we just return the provided method implementation.
            return method;
        }

        public IDisposable? PinMethodIfNeeded(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodPinning)) {
                // only make the interface call if it's needed, because interface dispatches are slow
                return Runtime.PinMethodIfNeeded(method);
            }

            // otherwise, always return
            return null;
        }

        public bool DisableInliningIfPossible(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.DisableInlining)) {
                Runtime.DisableInlining(method);
                return true;
            }

            return false;
        }

        public ICoreDetour CreateDetour(MethodBase source, MethodBase dest) {
            throw new NotImplementedException();
        }
    }
}
