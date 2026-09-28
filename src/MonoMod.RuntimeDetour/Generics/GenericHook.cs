using MonoMod.Core;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.RuntimeDetour.Generics
{
    public class GenericHook : IDisposable
    {
        /// <summary>
        /// Whether value-type-containing instantiations (e.g. <c>&lt;TRef, int&gt;</c>) can be auto-discovered on the
        /// current runtime. <see langword="false"/> on Mono gsharedvt context is unreadable
        /// Explicit <see cref="Prime"/> works everywhere.
        /// </summary>
        public static bool SupportsValueTypeAutoPrime { get; } = PlatformDetection.Runtime != RuntimeKind.Mono;

        private readonly MethodBase openSource;
        private readonly MethodInfo openTarget;
        private readonly DetourConfig? config;
        private readonly IDetourFactory factory;
        private readonly SharedBodyDispatcher dispatcher;
        private readonly object _lock = new();

        // Enumerated lock-free on the dispatch path while Prime can add concurrently.
        internal readonly ConcurrentDictionary<InstantiationKey, byte> Primed = [];
        private readonly Dictionary<InstantiationKey, Hook> unshared = [];
        private bool disposed;

        /// <summary>
        /// Detours a generic source with the target.
        /// </summary>
        /// <param name="openSource">The open source method, e.g. A&lt;T&gt; (not a closed instantiation like A&lt;int&gt;!)</param>
        /// <param name="openTarget">The open target method, e.g. B&lt;T&gt; (not a closed instantiation like B&lt;int&gt;!)</param>
        /// <param name="primedTypes">The type-argument vectors to hook initially; more can be added via <see cref="Prime"/>.</param>
        /// <param name="autoPrime">Whether instantiations beyond <paramref name="primedTypes"/> are hooked automatically.</param>
        /// <param name="config">Only used for unshared instantiations</param>
        /// <param name="factory">Only used for unshared instantiations</param>
        /// <exception cref="ArgumentException"><paramref name="openSource"/> is not an open generic method.</exception>
        public GenericHook(MethodBase openSource, MethodInfo openTarget, IEnumerable<Type[]>? primedTypes = null, AutoPrimeMode autoPrime = AutoPrimeMode.None, DetourConfig? config = null, IDetourFactory? factory = null)
        {
            Helpers.ThrowIfArgumentNull(openSource);
            Helpers.ThrowIfArgumentNull(openTarget);
            if (!openSource.ContainsGenericParameters)
            {
                throw new ArgumentException("Source must be an open generic.", nameof(openSource));
            }

            this.openSource = openSource;
            this.openTarget = openTarget;
            this.config = config;
            this.factory = factory ?? DetourFactory.Current;
            dispatcher = new SharedBodyDispatcher(openSource, autoPrime, this);

            if (primedTypes is not null)
            {
                foreach (var vec in primedTypes)
                {
                    Prime(vec);
                }
            }
        }

        public void Prime(params Type[] typeArgs)
        {
            var key = new InstantiationKey(typeArgs);
            Primed[key] = 0;
            try
            {
                OnDiscovered(key);
            }
            catch
            {
                Primed.TryRemove(key, out _);
                throw;
            }
        }

        internal void OnDiscovered(InstantiationKey key)
        {
            // Lock order is always GenericHook._lock -> SharedBodyDispatcher._lock.
            lock (_lock)
            {
                if (disposed || unshared.ContainsKey(key) || dispatcher.Contains(key))
                {
                    return;
                }

                var (closedSrc, closedTgt) = GenericInstantiator.Close(openSource, openTarget, key.TypeArguments);

                if (!GenericHelper.MethodIsShared(closedSrc))
                {
                    // Unshared: owns its native body, a normal Hook works.
                    var hook = new Hook(closedSrc, closedTgt, null, factory, config, true, true);
                    unshared.Add(key, hook);
                }
                else
                {
                    dispatcher.Register(key, closedSrc, closedTgt, TargetWantsOrig(closedSrc, closedTgt));
                }
            }
        }

        // One extra leading param on the target => it's the orig delegate
        private static bool TargetWantsOrig(MethodBase src, MethodInfo tgt)
        {
            var srcSig = MethodSignature.ForMethod(src);
            var dstSig = MethodSignature.ForMethod(tgt, true);
            return dstSig.ParameterCount == srcSig.ParameterCount + 1;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;

                foreach (var h in unshared.Values)
                {
                    h.Dispose();
                }

                unshared.Clear();
                dispatcher.Dispose();
            }
        }
    }
}
