using AsmResolver.DotNet;
using System;
using System.Collections.Generic;
using MonoMod.Utils;

namespace MonoMod.Packer {

    public enum TypeMergeMode {
        DoNotMerge,
        UnifyIdentical,
        MergeLayoutIdentical,
        MergeAlways,
    }

    internal static class TypeMergeModeExtra {
        public const int MinValue = (int)TypeMergeMode.DoNotMerge;
        public const int MaxValue = (int)TypeMergeMode.MergeAlways;
    }

    public enum MemberMergeMode {
        UnifyIdentical,
    }

    public sealed record PackOptions {
        public static PackOptions Default { get; } = new();

        public AssemblyDescriptor DefaultCorLib { get; init; } = KnownCorLibs.SystemPrivateCoreLib_v6_0_0_0;

        public bool Internalize { get; init; } // = false;
        public bool EnsurePublicApi { get; init; } = true;

        // Note: when Internalize is true, this means exclude, when it is false, it means to do it anyway
        public IReadOnlyCollection<AssemblyDescriptor> ExplicitInternalize { get; init; } = Array.Empty<AssemblyDescriptor>();
        public PackOptions AddExplicitInternalize(AssemblyDescriptor value)
            => this with { ExplicitInternalize = AddToCollection(ExplicitInternalize, Helpers.ThrowIfNull(value)) };
        public PackOptions AddExplicitInternalize(params AssemblyDescriptor[] values)
            => this with { ExplicitInternalize = AddToCollection(ExplicitInternalize, Helpers.ThrowIfNull(values)) };

        public TypeMergeMode TypeMergeMode { get; init; } = TypeMergeMode.MergeLayoutIdentical;
        public MemberMergeMode MemberMergeMode { get; init; } = MemberMergeMode.UnifyIdentical;

        public bool ExcludeCorelib { get; init; } = true;
        
        public bool Parallelize { get; init; } // = false;

        private static IReadOnlyCollection<T> AddToCollection<T>(IReadOnlyCollection<T> orig, T value) {
            var arr = new T[orig.Count + 1];
            var i = 0;
            if (orig.GetType() == typeof(T[])) {
                Array.Copy((T[]) orig, arr, orig.Count);
            } else {
                foreach (var e in orig) {
                    arr[i++] = e;
                }
            }
            arr[i] = value;
            return arr;
        }

        private static IReadOnlyCollection<T> AddToCollection<T>(IReadOnlyCollection<T> orig, T[] values) {
            var arr = new T[orig.Count + values.Length];
            var i = 0;
            if (orig.GetType() == typeof(T[])) {
                Array.Copy((T[]) orig, arr, orig.Count);
            } else {
                foreach (var e in orig) {
                    arr[i++] = e;
                }
            }
            Array.Copy(values, 0, arr, i, values.Length);
            return arr;
        }
    }
}
