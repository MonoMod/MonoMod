using AsmResolver.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMod.Packer {
    internal sealed class MergingAssemblyResolver : IAssemblyResolver {
        private readonly Dictionary<string, AssemblyDefinition> fullNameMap = new();

        public static MergingAssemblyResolver Create(IEnumerable<ModuleDefinition> modules) {
            var asmList = new HashSet<AssemblyDefinition>();
            foreach (var module in modules) {
                if (module.Assembly is not null)
                    asmList.Add(module.Assembly);
            }
            return new MergingAssemblyResolver(asmList);
        }

        public MergingAssemblyResolver(IReadOnlyCollection<AssemblyDefinition> assemblies) {
            foreach (var assembly in assemblies) {
                fullNameMap.Add(assembly.FullName, assembly);
            }
        }

        public AssemblyDefinition? Resolve(AssemblyDescriptor assembly) {
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
