using AsmResolver.DotNet;
using MonoMod.Packer.Diagnostics;
using MonoMod.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MonoMod.Packer {
    public sealed class AssemblyPacker
    {
        private readonly DiagnosticTranslator diagnostics;

        public AssemblyPacker(IDiagnosticReciever diagnosticReciever) {
            diagnostics = new(diagnosticReciever);
        }


        private readonly ConcurrentDictionary<string, AssemblyDefinition?> descCache = new();
        private AssemblyDefinition? Resolve(IAssemblyResolver resolver, AssemblyDescriptor asm) {
            return descCache.GetOrAdd(asm.FullName, 
                static (_, t) => t.resolver.Resolve(t.asm),// ?? t.asm.Resolve(),
                (resolver, asm));
        }

        public AssemblyDefinition Pack(AssemblyDefinition rootAssembly, IAssemblyResolver includedSetResolver, PackOptions? options = null) {
            Helpers.ThrowIfArgumentNull(rootAssembly);
            options ??= PackOptions.Default;

            // First, we want to resolve all of the assemblies (modules) we will be merging.
            var modules = new HashSet<ModuleDefinition>();
            var visited = new HashSet<ModuleDefinition>();
            var queue = new Queue<ModuleDefinition>();

            AssemblyDescriptor? corlibDescriptor = null;

            foreach (var module in rootAssembly.Modules) {
                queue.Enqueue(module);

                if (corlibDescriptor is null) {
                    corlibDescriptor = module.CorLibTypeFactory.CorLibScope.GetAssembly();
                }
            }

            if (corlibDescriptor is null) {
                diagnostics.ReportDiagnostic(ErrorCode.WRN_CouldNotFindCorLibReference);
                corlibDescriptor = options.DefaultCorLib;
            }

            var realCorlib = corlibDescriptor as AssemblyReference;
            if (realCorlib is null) {
                diagnostics.ReportDiagnostic(ErrorCode.ERR_CouldNotResolveCorLib, corlibDescriptor);
                // we reported an error, try to continue anyway, using some default reference
                realCorlib = KnownCorLibs.SystemRuntime_v7_0_0_0;
            }

            while (queue.TryDequeue(out var module)) {
                // make sure we only visit each moduledef once
                if (!visited.Add(module))
                    continue;

                // Check if we should actually be merging this module
                var assembly = module.Assembly;
                if (assembly != rootAssembly) {
                    if (assembly is null) {
                        // ???
                    } else {
                        if (options.ExcludeCorelib && assembly.IsCorLib) {
                            diagnostics.ReportDiagnostic(ErrorCode.DBG_SkippedCorelibModule, module);
                            continue;
                        }
                    }
                }

                // this is part of an assembly we want to process; add it to the list
                modules.Add(module);

                // this is part of an asesmbly we want to grab references from, do that and add them to the queue
                foreach (var asmRef in module.AssemblyReferences) {
                    var asm = Resolve(includedSetResolver, asmRef);
                    if (asm is null) {
                        // not resolved means the assembly isn't in the set to merge
                        diagnostics.ReportDiagnostic(ErrorCode.DBG_CouldNotResolveAssembly, module, new object?[] { module });
                        continue;
                    }

                    // get all of this assembly's modules and add them to the queue
                    foreach (var mod in asm.Modules) {
                        queue.Enqueue(mod);
                    }
                }

                // don't process ModuleReferences, because AFACT, those are used for PInvokes only
            }

            // All of the actual work can be done here.
            return DoPack(this, rootAssembly, realCorlib, modules, options);
        }

        private static AssemblyDefinition DoPack(AssemblyPacker packer, AssemblyDefinition rootAssembly, AssemblyReference corlib, HashSet<ModuleDefinition> modulesToMerge, PackOptions options) {

            var outputAsm = new AssemblyDefinition(rootAssembly.Name, rootAssembly.Version);
            var outputModule = new ModuleDefinition(rootAssembly.Name, corlib);
            outputAsm.Modules.Add(outputModule);

            var importer = new ReferenceImporter(outputModule);

            // note: if resolver can resolve a member, it is in the merge space, and should be merged into the final assembly
            var asmResolver = MergingAssemblyResolver.Create(modulesToMerge);
            var mdResolver = new DefaultMetadataResolver(asmResolver);

            var entityMap = TypeEntityMap.CreateForAllTypes(modulesToMerge, options, mdResolver, packer.diagnostics);

            // TODO: copy more stuff over

            var firstType = rootAssembly.ManifestModule!.TopLevelTypes
                .First(t => t.NestedTypes.Count > 0)
                .NestedTypes.First();
            var firstEntity = entityMap.Lookup(firstType);
            var unified = firstEntity.UnifiedType;
            var unifiedMethods = unified.InstanceMethods;
            var firstMethod = firstEntity.InstanceMethods.First(m => m.Definition.ParameterDefinitions.Count > 0);
            var nonUnifiedTypes = firstMethod.TypesInSignature;
            var unifiedFirstMethod = firstMethod.GetUnified();
            var unifiedTypes = unifiedFirstMethod.TypesInSignature;
            var contributing1 = unifiedFirstMethod.ContributingModules;
            var unifiedBase = unified.BaseType;
            var firstUnified = entityMap.Lookup(rootAssembly.ManifestModule!.TopLevelTypes.First()).UnifiedType;
            var mergeMode = firstUnified.TypeMergeMode;
            var contributing2 = firstUnified.ContributingModules;

            var canBeUnified2 = firstUnified.CanBeFullyUnifiedUncached();
            var canBeUnified = unified.CanBeFullyUnifiedUncached();

            return outputAsm;
        }
    }
}
