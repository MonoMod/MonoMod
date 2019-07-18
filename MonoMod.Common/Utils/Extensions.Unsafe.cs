// FIXME: MERGE MonoModExt AND Extensions, KEEP ONLY WHAT'S NEEDED!

using System;
using System.Reflection;
using SRE = System.Reflection.Emit;
using CIL = Mono.Cecil.Cil;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Mono.Cecil;
using System.Text;
using Mono.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils {
    public static partial class Extensions {

        private static readonly Dictionary<Type, int> _GetManagedSizeCache = new Dictionary<Type, int>() {
            { typeof(void), 0 }
        };
        /// <summary>
        /// Get the managed size of a given type. This matches an IL-level sizeof(t), even if it cannot be determined normally in C#.
        /// Note that sizeof(t) != Marshal.SizeOf(t), f.e. when t is char.
        /// </summary>
        /// <param name="t">The type to get the size from.</param>
        /// <returns>The managed type size.</returns>
        public static int GetManagedSize(this Type t) {
            if (_GetManagedSizeCache.TryGetValue(t, out int size))
                return size;

            DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"GetSize<{t.FullName}>",
                typeof(int), Type.EmptyTypes
            );

            ILProcessor il = dmd.GetILProcessor();
            il.Emit(OpCodes.Sizeof, dmd.Definition.Module.ImportReference(t));
            il.Emit(OpCodes.Ret);

            lock (_GetManagedSizeCache) {
                return _GetManagedSizeCache[t] = (dmd.Generate().CreateDelegate<Func<int>>() as Func<int>)();
            }
        }

        /// <summary>
        /// Get a type which matches what the method should receive via ldarg.0
        /// </summary>
        /// <param name="method">The method to obtain the "this" parameter type from.</param>
        /// <returns>The "this" parameter type.</returns>
        public static Type GetThisParamType(this MethodBase method) {
            Type type = method.DeclaringType;
            if (type.IsValueType)
                type = type.MakeByRefType();
            return type;
        }

        private static readonly Dictionary<MethodBase, Func<IntPtr>> _GetLdftnPointerCache = new Dictionary<MethodBase, Func<IntPtr>>();
        /// <summary>
        /// Get a native function pointer for a given method. This matches an IL-level ldftn.
        /// </summary>
        /// <remarks>
        /// ldftn doesn't JIT-compile the method on mono, which thus keeps the class constructor untouched.
        /// On the other hand, its result thus doesn't always match that of MethodHandle.GetFunctionPointer().
        /// </remarks>
        /// <param name="m">The method to get a native function pointer for.</param>
        /// <returns>The native function pointer.</returns>
        public static IntPtr GetLdftnPointer(this MethodBase m) {
            if (_GetLdftnPointerCache.TryGetValue(m, out Func<IntPtr> func))
                return func();

            DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"GetLdftnPointer<{m.GetID(simple: true)}>",
                typeof(int), Type.EmptyTypes
            );

            ILProcessor il = dmd.GetILProcessor();
            il.Emit(OpCodes.Ldftn, dmd.Definition.Module.ImportReference(m));
            il.Emit(OpCodes.Ret);

            lock (_GetLdftnPointerCache) {
                return (_GetLdftnPointerCache[m] = dmd.Generate().CreateDelegate<Func<IntPtr>>() as Func<IntPtr>)();
            }
        }

    }
}
