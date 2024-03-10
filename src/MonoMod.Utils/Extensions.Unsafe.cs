using Mono.Cecil.Cil;
using MonoMod.Logs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        private static readonly ConcurrentDictionary<Type, int> _GetManagedSizeCache = new(new[] {
            new KeyValuePair<Type, int>(typeof(void), 0)
        });

        private static MethodInfo? _GetManagedSizeHelper;
        /// <summary>
        /// Get the managed size of a given type. This matches an IL-level sizeof(t), even if it cannot be determined normally in C#.
        /// Note that <c>sizeof(t) != Marshal.SizeOf(t)</c>, f.e. when t is char.
        /// </summary>
        /// <remarks>
        /// An IL-level <c>sizeof(t)</c> will return <c>sizeof(IntPtr)</c> for reference types, as it refers to the size on stack or in an object,
        /// not the size on heap.
        /// </remarks>
        /// <param name="t">The type to get the size from.</param>
        /// <returns>The managed type size.</returns>
        public static int GetManagedSize(this Type t)
            => _GetManagedSizeCache.GetOrAdd(Helpers.ThrowIfNull(t), ComputeManagedSize);

        private static int ComputeManagedSize(Type t)
        {
            var szHelper = _GetManagedSizeHelper;
            if (szHelper is null)
            {
                _GetManagedSizeHelper = szHelper = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!;
            }

            return szHelper.MakeGenericMethod(t).CreateDelegate<Func<int>>()();
        }

        /// <summary>
        /// Get a type which matches what the method should receive via ldarg.0
        /// </summary>
        /// <param name="method">The method to obtain the "this" parameter type from.</param>
        /// <returns>The "this" parameter type.</returns>
        public static Type GetThisParamType(this MethodBase method)
        {
            var type = Helpers.ThrowIfNull(method).DeclaringType!;
            if (type.IsValueType)
                type = type.MakeByRefType();
            return type;
        }

        private static readonly Dictionary<MethodBase, Func<IntPtr>> _GetLdftnPointerCache = new Dictionary<MethodBase, Func<IntPtr>>();
        /// <summary>
        /// Get a native function pointer for a given method. This matches an IL-level ldftn.
        /// </summary>
        /// <remarks>
        /// The result of ldftn doesn't always match that of MethodHandle.GetFunctionPointer().
        /// For example, ldftn doesn't JIT-compile the method on mono, which thus keeps the class constructor untouched.
        /// And on .NET, struct overrides (f.e. ToString) have got multiple entry points pointing towards the same code.
        /// </remarks>
        /// <param name="m">The method to get a native function pointer for.</param>
        /// <returns>The native function pointer.</returns>
        public static IntPtr GetLdftnPointer(this MethodBase m)
        {
            Helpers.ThrowIfArgumentNull(m);
            if (_GetLdftnPointerCache.TryGetValue(m, out var func))
                return func();

            using var dmd = new DynamicMethodDefinition(
                DebugFormatter.Format($"GetLdftnPointer<{m}>"),
                typeof(IntPtr), Type.EmptyTypes
            );

            var il = dmd.GetILProcessor();
            il.Emit(OpCodes.Ldftn, dmd.Definition.Module.ImportReference(m));
            il.Emit(OpCodes.Ret);

            lock (_GetLdftnPointerCache)
            {
                return (_GetLdftnPointerCache[m] = dmd.Generate().CreateDelegate<Func<IntPtr>>() as Func<IntPtr>)();
            }
        }

    }
}
