using AsmResolver.DotNet;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace MonoMod.Packer {
    public sealed class MergingAssemblyResolver : IAssemblyResolver {
        private readonly Dictionary<string, AssemblyDefinition> fullNameMap = new();

        public static MergingAssemblyResolver Create(IEnumerable<ModuleDefinition> modules) {
            Helpers.ThrowIfArgumentNull(modules);
            var asmList = new HashSet<AssemblyDefinition>();
            foreach (var module in modules) {
                if (module.Assembly is not null)
                    asmList.Add(module.Assembly);
            }
            return new MergingAssemblyResolver(asmList);
        }

        public MergingAssemblyResolver(IReadOnlyCollection<AssemblyDefinition> assemblies) {
            Helpers.ThrowIfArgumentNull(assemblies);
            foreach (var assembly in assemblies) {
                fullNameMap.TryAdd(assembly.FullName, assembly);
            }
        }

        public AssemblyDefinition? Resolve(AssemblyDescriptor assembly) {
            Helpers.ThrowIfArgumentNull(assembly);
            if (fullNameMap.TryGetValue(assembly.FullName, out var result)) {
                return result;
            } else {
                return null;
            }
        }

        public void AddToCache(AssemblyDescriptor descriptor, AssemblyDefinition definition) {
            throw new NotImplementedException();
        }

        public void ClearCache() {
            throw new NotImplementedException();
        }

        public bool HasCached(AssemblyDescriptor descriptor) {
            throw new NotImplementedException();
        }

        public bool RemoveFromCache(AssemblyDescriptor descriptor) {
            throw new NotImplementedException();
        }
    }
}
