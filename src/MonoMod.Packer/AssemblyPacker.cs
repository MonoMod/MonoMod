using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Diagnostics;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

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
                    var asm = asmRef.Resolve() ?? assemblyResolver.Resolve(asmRef);
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

            var entityMap = TypeEntityMap.CreateForAllTypes(modulesToMerge);
            // note: if resolver can resolve a member, it is in the merge space, and should be merged into the final assembly
            var asmResolver = MergingAssemblyResolver.Create(modulesToMerge);
            var mdResolver = new DefaultMetadataResolver(asmResolver);

            // TODO: copy more stuff over

            var moduleType = outputModule.GetOrCreateModuleType();
            var processingOptions = new TypeProcessingOptions(packer, rootAssembly, options, entityMap, importer, mdResolver);

            if (options.Parallelize) {
                foreach (var types in entityMap.EnumerateEntitySets()) {
                    var merged = MergeTypes(types, moduleType, processingOptions);
                    foreach (var type in merged) {
                        RebuildBodyWithFixedReferences(type, processingOptions);
                        outputModule.TopLevelTypes.Add(type);
                    }
                }
            } else {
                var result = entityMap.EnumerateEntitySets()
                    .AsParallel()
                    .AsUnordered()
                    .SelectMany(list
                        => MergeTypes(list, moduleType, processingOptions))
                    .AsUnordered()
                    .Select(t => {
                        RebuildBodyWithFixedReferences(t, processingOptions);
                        return t;
                    });

                foreach (var type in result) {
                    outputModule.TopLevelTypes.Add(type);
                }
            }

            return outputAsm;
        }

        private readonly record struct TypeProcessingOptions(
            AssemblyPacker Packer, AssemblyDefinition RootAssembly,
            PackOptions Options, TypeEntityMap EntityMap,
            ReferenceImporter Importer, IMetadataResolver MdResolver
        );

        private static IReadOnlyList<TypeDefinition> MergeTypes(
            IReadOnlyList<TypeEntity> entities, TypeDefinition targetModuleType, TypeProcessingOptions options
        ) {
            if (entities.Count == 0) {
                return Array.Empty<TypeDefinition>();
            }

            var isModuleType = entities[0].Definition.IsModuleType;
            if (isModuleType) {
                var target = targetModuleType;
                foreach (var source in entities) {
                    var result = TryMergeTypeInto(ref target, source, options, isModuleType: true);
                    Helpers.DAssert(result); // this should *always* succeed for the <Module> type; it has to
                }
                return new[] { target };
            }

            // this is *not* the module type; use the normal approach

            // TODO: aggressive (i.e. multiple merged results) merging
            var outputList = new List<TypeDefinition>();
            TypeDefinition? currentDef = null;

            foreach (var type in entities) {
                if (!TryMergeTypeInto(ref currentDef, type, options, isModuleType: false)) {
                    // merge failed; instead, close type and throw it on the output list
                    var result = CloneType(type.Definition, options);
                    outputList.Add(result);
                    options.EntityMap.MarkMappedDef(type, result);
                } else {
                    options.EntityMap.MarkMappedDef(type, currentDef);
                }
            }

            if (currentDef is not null) {
                outputList.Add(currentDef);
            }

            return outputList;
        }

        private static bool TryMergeTypeInto([NotNull] ref TypeDefinition? target, TypeEntity source, in TypeProcessingOptions options, bool isModuleType) {
            if (target is null) {
                Helpers.Assert(!isModuleType);
                target = CloneType(source.Definition, options);
                return true;
            }

            var srcDef = source.Definition;



            throw new NotImplementedException();
        }

        private static TypeDefinition CloneType(TypeDefinition type, in TypeProcessingOptions options) {
            throw new NotImplementedException();
        }

        private static void RebuildBodyWithFixedReferences(TypeDefinition type, TypeProcessingOptions options) {
            throw new NotImplementedException();
        }
    }
}
