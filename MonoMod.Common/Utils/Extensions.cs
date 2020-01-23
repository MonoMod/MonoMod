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
#if !MONOMOD_INTERNAL
    public
#endif
    static partial class Extensions {

        // Use this source file for any extensions which don't deserve their own source files.

        private static readonly object[] _NoArgs = new object[0];

        private static readonly Dictionary<Type, FieldInfo> fmap_mono_assembly = new Dictionary<Type, FieldInfo>();
        // Old versions of Mono which lack the arch field in MonoAssemblyName don't parse ProcessorArchitecture.
        private static readonly bool _MonoAssemblyNameHasArch = new AssemblyName("Dummy, ProcessorArchitecture=MSIL").ProcessorArchitecture == ProcessorArchitecture.MSIL;

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

        public static T GetDeclaredMember<T>(this T member) where T : MemberInfo {
            if (member.DeclaringType == member.ReflectedType)
                return member;

            int mt = member.MetadataToken;
            foreach (MemberInfo other in member.DeclaringType.GetMembers((BindingFlags) (-1))) {
                if (other.MetadataToken == mt)
                    return (T) other;
            }

            return member;
        }

        public static unsafe void SetMonoCorlibInternal(this Assembly asm, bool value) {
            if (Type.GetType("Mono.Runtime") == null)
                return;

            // Mono doesn't know about IgnoresAccessChecksToAttribute,
            // but it lets some assemblies have unrestricted access.

            // https://github.com/mono/mono/blob/df846bcbc9706e325f3b5dca4d09530b80e9db83/mono/metadata/metadata-internals.h#L207
            // https://github.com/mono/mono/blob/1af992a5ffa46e20dd61a64b6dcecef0edb5c459/mono/metadata/appdomain.c#L1286
            // https://github.com/mono/mono/blob/beb81d3deb068f03efa72be986c96f9c3ab66275/mono/metadata/class.c#L5748
            // https://github.com/mono/mono/blob/83fc1456dbbd3a789c68fe0f3875820c901b1bd6/mcs/class/corlib/System.Reflection/Assembly.cs#L96
            // https://github.com/mono/mono/blob/cf69b4725976e51416bfdff22f3e1834006af00a/mcs/class/corlib/System.Reflection/RuntimeAssembly.cs#L59
            // https://github.com/mono/mono/blob/cf69b4725976e51416bfdff22f3e1834006af00a/mcs/class/corlib/System.Reflection.Emit/AssemblyBuilder.cs#L247

            Type asmType = asm?.GetType();
            if (asmType == null)
                return;

            // _mono_assembly has changed places between Mono versions.
            FieldInfo f_mono_assembly;
            lock (fmap_mono_assembly) {
                if (!fmap_mono_assembly.TryGetValue(asmType, out f_mono_assembly)) {
                    f_mono_assembly = asmType.GetField("_mono_assembly", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    fmap_mono_assembly[asmType] = f_mono_assembly;
                }
            }
            if (f_mono_assembly == null)
                return;

            IntPtr asmPtr = (IntPtr) f_mono_assembly.GetValue(asm);
            int offs =
                // ref_count (4 + padding)
                IntPtr.Size +
                // basedir
                IntPtr.Size +

                // aname
                // name
                IntPtr.Size +
                // culture
                IntPtr.Size +
                // hash_value
                IntPtr.Size +
                // public_key
                IntPtr.Size +
                // public_key_token (17 + padding)
                20 +
                // hash_alg
                4 +
                // hash_len
                4 +
                // flags
                4 +

                // major, minor, build, revision[, arch] (10 framework / 20 core + padding)
                (
                    !_MonoAssemblyNameHasArch ? (
                        typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ? 16 :
                        8
                    ) : (
                        typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ? (IntPtr.Size == 4 ? 20 : 24) :
                        (IntPtr.Size == 4 ? 12 : 16)
                    )
                ) +

                // image
                IntPtr.Size +
                // friend_assembly_names
                IntPtr.Size +
                // friend_assembly_names_inited
                1 +
                // in_gac
                1 +
                // dynamic
                1;
            byte* corlibInternalPtr = (byte*) ((long) asmPtr + offs);
            *corlibInternalPtr = value ? (byte) 1 : (byte) 0;
        }

    }
}
