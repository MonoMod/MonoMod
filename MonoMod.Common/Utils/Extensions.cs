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
    /// <summary>
    /// Collection of extensions used by MonoMod and other projects.
    /// </summary>
    public static partial class Extensions {

        // Use this source file for any extensions which don't deserve their own source files.

        /// <summary>
        /// Determine if two types are compatible with each other (f.e. object with string, or enums with their underlying integer type).
        /// </summary>
        /// <param name="type">The first type.</param>
        /// <param name="other">The second type.</param>
        /// <returns>True if both types are compatible with each other, false otherwise.</returns>
        public static bool IsCompatible(this Type type, Type other)
            => _IsCompatible(type, other) || _IsCompatible(other, type);
        private static bool _IsCompatible(this Type type, Type other) {
            if (type == other)
                return true;

            if (type.IsAssignableFrom(other))
                return true;

            if (other.IsEnum && IsCompatible(type, Enum.GetUnderlyingType(other)))
                return true;

            return false;
        }

    }
}
