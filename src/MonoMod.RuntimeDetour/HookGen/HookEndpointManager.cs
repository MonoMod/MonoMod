using MonoMod.Cil;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace MonoMod.RuntimeDetour.HookGen
{

    /// <summary>
    /// Provided for back-compat with old versions of HookGen
    /// </summary>
    public static class HookEndpointManager
    {

        private const string ObsoleteMessage = "This member should never be used directly from user code. Use Hook or ILHook directly instead.";
        private const string HookAlreadyAppliedMsg = "Delegate has already been applied to this method as a hook!";

        private static ConcurrentDictionary<(MethodBase, Delegate), Hook> Hooks = new();
        private static ConcurrentDictionary<(MethodBase, Delegate), ILHook> ILHooks = new();

        // Both generic and non-generic variants must stay for backwards-compatibility.
        /// <summary>
        /// Adds a hook (implemented by <paramref name="hookDelegate"/>) to <paramref name="method"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="method">The method to hook.</param>
        /// <param name="hookDelegate">The hook delegate to use.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Add<T>(MethodBase method, Delegate hookDelegate) where T : Delegate => Add(method, hookDelegate);
        /// <summary>
        /// Adds a hook (implemented by <paramref name="hookDelegate"/>) to <paramref name="method"/>.
        /// </summary>
        /// <param name="method">The method to hook.</param>
        /// <param name="hookDelegate">The hook delegate to use.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Add(MethodBase method, Delegate hookDelegate)
        {
            if (!Hooks.TryAdd((method, hookDelegate), new Hook(method, hookDelegate)))
                throw new ArgumentException(HookAlreadyAppliedMsg);
        }

        /// <summary>
        /// Removes the hook implemented by <paramref name="hookDelegate"/> from <paramref name="method"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="method">The method to hook.</param>
        /// <param name="hookDelegate">The hook delegate which was used.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Remove<T>(MethodBase method, Delegate hookDelegate) where T : Delegate => Remove(method, hookDelegate);
        /// <summary>
        /// Removes the hook implemented by <paramref name="hookDelegate"/> from <paramref name="method"/>.
        /// </summary>
        /// <param name="method">The method to hook.</param>
        /// <param name="hookDelegate">The hook delegate which was used.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Remove(MethodBase method, Delegate hookDelegate)
        {
            if (Hooks.TryRemove((method, hookDelegate), out var hook))
            {
                hook.Dispose();
            }
        }

        /// <summary>
        /// Adds an IL hook (implemented by <paramref name="callback"/>) to <paramref name="method"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="method">The method to hook.</param>
        /// <param name="callback">The hook delegate to use.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Modify<T>(MethodBase method, Delegate callback) where T : Delegate => Modify(method, callback);
        /// <summary>
        /// Adds an IL hook (implemented by <paramref name="callback"/>) to <paramref name="method"/>.
        /// </summary>
        /// <param name="method">The method to hook.</param>
        /// <param name="callback">The hook delegate to use.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Modify(MethodBase method, Delegate callback)
        {
            if (!ILHooks.TryAdd((method, callback), new ILHook(method, (ILContext.Manipulator)callback)))
                throw new ArgumentException(HookAlreadyAppliedMsg);
        }

        /// <summary>
        /// Removes the IL hook implemented by <paramref name="callback"/> from <paramref name="method"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="method">The method to hook.</param>
        /// <param name="callback">The hook delegate which was used.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Unmodify<T>(MethodBase method, Delegate callback) => Unmodify(method, callback);
        /// <summary>
        /// Removes the IL hook implemented by <paramref name="callback"/> from <paramref name="method"/>.
        /// </summary>
        /// <param name="method">The method to hook.</param>
        /// <param name="callback">The hook delegate which was used.</param>
        [Obsolete(ObsoleteMessage, true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Unmodify(MethodBase method, Delegate callback)
        {
            if (ILHooks.TryRemove((method, callback), out var hook))
            {
                hook.Dispose();
            }
        }

        /// <summary>
        /// Clears all hooks an IL hooks applied to methods via this type.
        /// </summary>
        public static void Clear()
        {
            // Note: it is acceptable to miss a few due to concurrency, becuase they will be cleaned up correctly by the GC
            foreach (var hook in Hooks.Values)
                hook.Dispose();

            Hooks.Clear();

            foreach (var hook in ILHooks.Values)
                hook.Dispose();

            ILHooks.Clear();
        }
    }
}
