using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Reflection.Emit;

namespace MonoMod.Utils {
#pragma warning disable IDE1006 // Naming Styles
    internal interface _IDMDGenerator {
#pragma warning restore IDE1006 // Naming Styles
        MethodInfo Generate(DynamicMethodDefinition dmd, object context);
    }
    /// <summary>
    /// A DynamicMethodDefinition "generator", responsible for generating a runtime MethodInfo from a DMD MethodDefinition.
    /// </summary>
    /// <typeparam name="TSelf"></typeparam>
    public abstract class DMDGenerator<TSelf> : _IDMDGenerator where TSelf : DMDGenerator<TSelf>, new() {

        private static TSelf _Instance;

        protected abstract MethodInfo _Generate(DynamicMethodDefinition dmd, object context);

        MethodInfo _IDMDGenerator.Generate(DynamicMethodDefinition dmd, object context) {
            return _Postbuild(_Generate(dmd, context));
        }

        public static MethodInfo Generate(DynamicMethodDefinition dmd, object context = null)
            => _Postbuild((_Instance ?? (_Instance = new TSelf()))._Generate(dmd, context));

        private static unsafe MethodInfo _Postbuild(MethodInfo mi) {
            if (mi == null)
                return null;

            if (DynamicMethodDefinition._IsMono) {
                if (!(mi is DynamicMethod) && mi.DeclaringType != null) {
                    // Mono doesn't know about IgnoresAccessChecksToAttribute,
                    // but it lets some assemblies have unrestricted access.

                    if (DynamicMethodDefinition._IsOldMonoSRE) {
                        // If you're reading this:
                        // You really should've chosen the SRE backend instead...

                    } else {
                        // https://github.com/mono/mono/blob/df846bcbc9706e325f3b5dca4d09530b80e9db83/mono/metadata/metadata-internals.h#L207
                        // https://github.com/mono/mono/blob/1af992a5ffa46e20dd61a64b6dcecef0edb5c459/mono/metadata/appdomain.c#L1286
                        // https://github.com/mono/mono/blob/beb81d3deb068f03efa72be986c96f9c3ab66275/mono/metadata/class.c#L5748
                        IntPtr asmPtr = (IntPtr) DynamicMethodDefinition.f_mono_assembly.GetValue(mi.Module.Assembly);
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

                            // major, minor, build, revision, arch (10 framework / 20 core + padding)
                            (
                                typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ? (IntPtr.Size == 4 ? 20 : 24) :
                                (IntPtr.Size == 4 ? 12 : 16)
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
                        *corlibInternalPtr = 1;
                    }
                }


            }

            return mi;
        }

    }
}
