using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        /// <summary>
        /// Creates a delegate of the specified type from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <typeparam name="T">The type of the delegate to create.</typeparam>
        /// <returns>The delegate for this method.</returns>
        public static T CreateDelegate<T>(this MethodBase method) where T : Delegate
            => (T)CreateDelegate(method, typeof(T), null);
        /// <summary>
        /// Creates a delegate of the specified type with the specified target from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <typeparam name="T">The type of the delegate to create.</typeparam>
        /// <param name="target">The object targeted by the delegate.</param>
        /// <returns>The delegate for this method.</returns>
        public static T CreateDelegate<T>(this MethodBase method, object? target) where T : Delegate
            => (T)CreateDelegate(method, typeof(T), target);
        /// <summary>
        /// Creates a delegate of the specified type from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <param name="delegateType">The type of the delegate to create.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate(this MethodBase method, Type delegateType)
            => CreateDelegate(method, delegateType, null);
        /// <summary>
        /// Creates a delegate of the specified type with the specified target from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <param name="delegateType">The type of the delegate to create.</param>
        /// <param name="target">The object targeted by the delegate.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate(this MethodBase method, Type delegateType, object? target)
        {
            Helpers.ThrowIfArgumentNull(method);
            Helpers.ThrowIfArgumentNull(delegateType);
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
                throw new ArgumentException("Type argument must be a delegate type!");
            if (method is System.Reflection.Emit.DynamicMethod dm)
                return dm.CreateDelegate(delegateType, target);

            if (method is MethodInfo mi)
                return Delegate.CreateDelegate(delegateType, target, mi);

            var handle = method.MethodHandle;
            RuntimeHelpers.PrepareMethod(handle);
            var ptr = handle.GetFunctionPointer();
            return (Delegate)Activator.CreateInstance(delegateType, target, ptr)!;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "If an expection is thrown here, we want to return null as our failure case.")]
        public static T? TryCreateDelegate<T>(this MethodInfo? mi) where T : Delegate
        {
            try
            {
                return mi?.CreateDelegate<T>();
            }
            catch
            {
                // ignore
                return null;
            }
        }
    }
}
