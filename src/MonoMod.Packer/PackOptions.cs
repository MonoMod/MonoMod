using AsmResolver.DotNet;
using System;
using System.Collections.Generic;
using MonoMod.Utils;

namespace MonoMod.Packer {

    public enum TypeMergeMode {
        UnifyIdentical,
        MergeLayoutIdentical,
        MergeAnyWithoutConflict,
    }

    public enum MemberMergeMode {
        UnifyIdentical,
        RenameConflicts,
    }

    public sealed record PackOptions {
        public static PackOptions Default { get; } = new();

        public AssemblyDescriptor DefaultCorLib { get; init; } = KnownCorLibs.SystemPrivateCoreLib_v6_0_0_0;

        public bool Internalize { get; init; } = true;
        public bool EnsurePublicApi { get; init; } = true;

        // Note: when Internalize is true, this means exclude, when it is false, it means to do it anyway
        public IReadOnlyCollection<AssemblyDescriptor> ExplicitInternalize { get; init; } = Array.Empty<AssemblyDescriptor>();
        public PackOptions AddExplicitInternalize(AssemblyDescriptor value)
            => this with { ExplicitInternalize = AddToCollection(ExplicitInternalize, Helpers.ThrowIfNull(value)) };
        public PackOptions AddExplicitInternalize(params AssemblyDescriptor[] values)
            => this with { ExplicitInternalize = AddToCollection(ExplicitInternalize, Helpers.ThrowIfNull(values)) };

        public TypeMergeMode TypeMergeMode { get; init; }
        public MemberMergeMode MemberMergeMode { get; init; }

        public bool ExcludeCorelib { get; init; } = true;
        public bool UseBlacklist { get; init; } // = false;
        public IReadOnlyCollection<AssemblyDescriptor> AssemblyFilterList { get; init; } = Array.Empty<AssemblyDescriptor>();
        public PackOptions AddFiltered(AssemblyDescriptor value)
            => this with { AssemblyFilterList = AddToCollection(AssemblyFilterList, Helpers.ThrowIfNull(value)) };
        public PackOptions AddFiltered(params AssemblyDescriptor[] values)
            => this with { AssemblyFilterList = AddToCollection(AssemblyFilterList, Helpers.ThrowIfNull(values)) };

        // -1 means default system concurrency
        // 0 or 1 means non-concurrent
        public int Concurrency { get; init; } = -1;

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
