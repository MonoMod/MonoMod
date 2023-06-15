using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Diagnostics;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MonoMod.Packer {
    public sealed class AssemblyPacker
    {
        private readonly IDiagnosticReciever diagnosticReciever;
        private readonly IAssemblyResolver assemblyResolver;

        public AssemblyPacker(IAssemblyResolver resolver, IDiagnosticReciever diagnosticReciever) {
            assemblyResolver = resolver;
            this.diagnosticReciever = diagnosticReciever;
        }

        private void ReportDiagnostic(ErrorCode code, params object?[] args) {
            // TODO: improve
            diagnosticReciever.ReportDiagnostic(code.ToString(), args);
        }

        private readonly ConcurrentDictionary<string, AssemblyDefinition?> descCache = new();
        private AssemblyDefinition? Resolve(AssemblyDescriptor asm) {
            return descCache.GetOrAdd(asm.FullName, 
                static (_, t) => t.@this.assemblyResolver.Resolve(t.asm) ?? t.asm.Resolve(),
                (@this: this, asm));
        }

        public AssemblyDefinition Pack(AssemblyDefinition rootAssembly, PackOptions? options = null) {
            Helpers.ThrowIfArgumentNull(rootAssembly);
            options ??= PackOptions.Default;

            AssemblyDefinition[] matchList = options.AssemblyFilterList
                .Select(assemblyResolver.Resolve)
                .Where(d => d is not null)
                .ToArray()!;

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
                ReportDiagnostic(ErrorCode.WRN_CouldNotFindCorLibReference);
                corlibDescriptor = options.DefaultCorLib;
            }

            var realCorlib = corlibDescriptor as AssemblyReference;
            if (realCorlib is null) {
                ReportDiagnostic(ErrorCode.ERR_CouldNotResolveCorLib, corlibDescriptor);
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
                        if (options.UseBlacklist) {
                            // we allow by default
                        } else {
                            // we're using a whitelist, this module can't be a part of this
                            ReportDiagnostic(ErrorCode.DBG_ModuleSkipped, module);
                            continue;
                        }
                    } else {
                        // check whether the assembly is in the list
                        var listHasAssembly = matchList.Contains(assembly);
                        if (listHasAssembly ^ options.UseBlacklist) {
                            // (!listHasAssembly && UseBlacklist) || (listHasAssembly && !UseBlacklist)
                            // this means we want to allow this module
                        } else {
                            // we don't want to include this module; continue to the next one
                            ReportDiagnostic(ErrorCode.DBG_ModuleSkipped, module);
                            continue;
                        }

                        if (options.ExcludeCorelib && assembly.IsCorLib) {
                            ReportDiagnostic(ErrorCode.DBG_SkippedCorelibModule, module);
                            continue;
                        }
                    }
                }

                // this is part of an assembly we want to process; add it to the list
                modules.Add(module);

                // this is part of an asesmbly we want to grab references from, do that and add them to the queue
                foreach (var asmRef in module.AssemblyReferences) {
                    var asm = Resolve(asmRef);
                    if (asm is null) {
                        ReportDiagnostic(ErrorCode.WRN_CouldNotResolveAssembly, asmRef, module);
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

            var entityMap = TypeEntityMap.CreateForAllTypes(modulesToMerge, options, mdResolver);

            // TODO: copy more stuff over

            foreach (var unifiedTypes in entityMap.EnumerateUnifiedTypeEntities()) {
                // complete this set of types
                unifiedTypes.Complete();

            }

            return outputAsm;
        }
    }
}
