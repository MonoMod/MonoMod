using AsmResolver.DotNet;
using AsmResolver.DotNet.Config.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MonoMod.Packer.Driver {
    internal static class Packer {

        public static readonly Option<string> OptOutput = new(new[] { "--output", "-o" },
            description: "The location to write the packed assembly.")
        {
            IsRequired = true,
        };

        public static readonly Option<DefaultCorlibKind> OptDefaultCorlib = new(new[] { "--default-corlib", "--corlib" },
            getDefaultValue: () => DefaultCorlibKind.Default,
            description: "The well-known corlib reference to use if the correct one cannot be detected.");
        public static readonly Option<string> OptCustomCorlib = new("--custom-corlib",
            description: "The custom corlib to use if the correct one cannot be detected. This can be either a file path or assembly name.");
        public static readonly Option<bool> OptInternalize = new("--internalize",
            getDefaultValue: () => PackOptions.Default.Internalize,
            description: "Whether or not to internalize merged assemblies.");
        public static readonly Option<bool> OptEnsurePublicApi = new("--ensure-public-api",
            getDefaultValue: () => PackOptions.Default.EnsurePublicApi,
            description: "Whether or not to ensure that all public API types are accessible.");

        public static readonly Option<TypeMergeMode> OptTypeMergeMode = new("--type-merge",
            getDefaultValue: () => PackOptions.Default.TypeMergeMode,
            description: "The rules to use when merging types.");
        public static readonly Option<MemberMergeMode> OptMemberMergeMode = new("--member-merge",
            getDefaultValue: () => PackOptions.Default.MemberMergeMode,
            description: "The rules to use when merging members.");

        public static readonly Option<bool> OptExcludeCorlib = new("--exclude-corlib",
            getDefaultValue: () => PackOptions.Default.ExcludeCorelib,
            description: "Whether or not to exclude the corlib from merge, regardless of other settings. Use with care.");

        public static readonly Option<bool> OptParallelize = new("--parallel",
            getDefaultValue: () => PackOptions.Default.Parallelize,
            description: "Whether or not to parallelize processing.");

        public static readonly Option<FileInfo?> OptRuntimeConfig = new(new[] { "--runtime-config", "--runtimeconfig", "--rc" },
            getDefaultValue: () => null,
            description: "The .runtimeconfig.json file to use for assembly resolution.");

        public static readonly Option<IEnumerable<string>> OptExplicitInternalize = new(new[] { "--internalize-assembly", "--internalize-asm", "-i" },
            getDefaultValue: Enumerable.Empty<string>,
            description: "Assemblies to internalize (or, if internalization is enabled globally, to exclude from it). " +
            "This can be either a file path or assembly name.");

        public static readonly Argument<FileInfo> ArgRootAssembly = new("root assembly",
            description: "The root assembly for the merge.")
        {
            Arity = ArgumentArity.ExactlyOne,
        };

        public static readonly Argument<IEnumerable<string>> ArgOtherAssemblies = new("other assemblies",
            description: "The set of assemblies to merge into the root assembly. May be globbed.")
        {
            Arity = ArgumentArity.OneOrMore,
        };

        public static void AddOptionsAndArguments(Command cmd) {
            cmd.Add(OptOutput);
            cmd.Add(OptDefaultCorlib);
            cmd.Add(OptCustomCorlib);
            cmd.Add(OptInternalize);
            cmd.Add(OptEnsurePublicApi);
            cmd.Add(OptTypeMergeMode);
            cmd.Add(OptMemberMergeMode);
            cmd.Add(OptExcludeCorlib);
            cmd.Add(OptParallelize);
            cmd.Add(OptRuntimeConfig);
            cmd.Add(OptExplicitInternalize);
            cmd.Add(ArgRootAssembly);
            cmd.Add(ArgOtherAssemblies);
        }

        public static void Execute(InvocationContext context) {
            var binder = context.BindingContext;
            var parseResult = binder.ParseResult;

            var packOpts = PackOptions.Default;

            var corlibKind = parseResult.GetValueForOption(OptDefaultCorlib);
            if (corlibKind is not DefaultCorlibKind.Default || parseResult.HasOption(OptCustomCorlib)) {
                if (corlibKind is DefaultCorlibKind.Default) {
                    if (parseResult.HasOption(OptDefaultCorlib)) {
                        context.Console.Error.WriteLine("--default-corlib must be Custom or not specified with --custom-corlib");
                    }
                    corlibKind = DefaultCorlibKind.Custom;
                }
                packOpts = packOpts with { DefaultCorLib = GetCorlib(corlibKind, parseResult, context.Console) };
            }

            if (parseResult.HasOption(OptInternalize)) {
                packOpts = packOpts with { Internalize = parseResult.GetValueForOption(OptInternalize) };
            }

            if (parseResult.HasOption(OptEnsurePublicApi)) {
                packOpts = packOpts with { EnsurePublicApi = parseResult.GetValueForOption(OptEnsurePublicApi) };
            }

            if (parseResult.HasOption(OptTypeMergeMode)) {
                packOpts = packOpts with { TypeMergeMode = parseResult.GetValueForOption(OptTypeMergeMode) };
            }

            if (parseResult.HasOption(OptMemberMergeMode)) {
                packOpts = packOpts with { MemberMergeMode = parseResult.GetValueForOption(OptMemberMergeMode) }; 
            }

            if (parseResult.HasOption(OptExcludeCorlib)) {
                packOpts = packOpts with { ExcludeCorelib = parseResult.GetValueForOption(OptExcludeCorlib) };
            }

            if (parseResult.HasOption(OptParallelize)) {
                packOpts = packOpts with { Parallelize = parseResult.GetValueForOption(OptParallelize) };
            }

            if (parseResult.GetValueForOption(OptExplicitInternalize) is { } strings) {
                packOpts = packOpts.AddExplicitInternalize(strings.Select(ParseAssemblyNameOrPath).ToArray());
            }

            var rootAssembly = parseResult.GetValueForArgument(ArgRootAssembly);
            var runtimeConfigFile = parseResult.GetValueForOption(OptRuntimeConfig);
            var otherAssemblies = GlobAndLoadAssemblies(parseResult.GetValueForArgument(ArgOtherAssemblies));
            var output = parseResult.GetValueForOption(OptOutput);
            Helpers.Assert(output is not null);

            RuntimeConfiguration? runtimeConfig = null;
            if (runtimeConfigFile is not null) {
                runtimeConfig = RuntimeConfiguration.FromFile(runtimeConfigFile.FullName);
            }

            var resolver = new MergingAssemblyResolver(otherAssemblies);

            context.ExitCode = 0;
            var diagReciever = new ConsoleDiagnosticReciever(context.Console, context);
            var packer = new AssemblyPacker(diagReciever);

            var rootFile = AssemblyDefinition.FromFile(rootAssembly.FullName);
            var finalAssembly = packer.Pack(rootFile, resolver, packOpts);
            finalAssembly.Write(output);
        }

        private static AssemblyDescriptor GetCorlib(DefaultCorlibKind kind, ParseResult parseResult, IConsole console) {
            switch (kind) {
                case DefaultCorlibKind.Custom:
                    var nameOrPath = parseResult.GetValueForOption(OptCustomCorlib);
                    if (nameOrPath is null) {
                        console.Error.WriteLine("If the default corlib kind is Custom, --custom-corlib must be specified");
                        Environment.Exit(1);
                    }
                    return ParseAssemblyNameOrPath(nameOrPath);

                case DefaultCorlibKind.Mscorlib2:
                    return KnownCorLibs.MsCorLib_v2_0_0_0;
                case DefaultCorlibKind.Mscorlib4:
                    return KnownCorLibs.MsCorLib_v4_0_0_0;

                case DefaultCorlibKind.SPCorlib4:
                    return KnownCorLibs.SystemPrivateCoreLib_v4_0_0_0;
                case DefaultCorlibKind.SPCorlib5:
                    return KnownCorLibs.SystemPrivateCoreLib_v5_0_0_0;
                case DefaultCorlibKind.SPCorlib6:
                    return KnownCorLibs.SystemPrivateCoreLib_v6_0_0_0;
                case DefaultCorlibKind.SPCorlib7:
                    return KnownCorLibs.SystemPrivateCoreLib_v7_0_0_0;

                case DefaultCorlibKind.Runtime4020:
                    return KnownCorLibs.SystemRuntime_v4_0_20_0;
                case DefaultCorlibKind.Runtime4100:
                    return KnownCorLibs.SystemRuntime_v4_1_0_0;
                case DefaultCorlibKind.Runtime4210:
                    return KnownCorLibs.SystemRuntime_v4_2_1_0;
                case DefaultCorlibKind.Runtime4220:
                    return KnownCorLibs.SystemRuntime_v4_2_2_0;

                case DefaultCorlibKind.Net5:
                    return KnownCorLibs.SystemRuntime_v5_0_0_0;
                case DefaultCorlibKind.Net6:
                    return KnownCorLibs.SystemRuntime_v6_0_0_0;
                case DefaultCorlibKind.Net7:
                    return KnownCorLibs.SystemRuntime_v7_0_0_0;

                case DefaultCorlibKind.Standard20:
                    return KnownCorLibs.NetStandard_v2_0_0_0;
                case DefaultCorlibKind.Standard21:
                    return KnownCorLibs.NetStandard_v2_1_0_0;

                case DefaultCorlibKind.Default:
                default:
                    throw new InvalidOperationException();
            }
        }

        private static AssemblyDescriptor ParseAssemblyNameOrPath(string nameOrPath) {
            try {
                // this ctor throws if its not valid; if it is valid, we'll use it
                var asmName = new AssemblyName(nameOrPath);

                var hasPublicKey = false;
                byte[]? keyOrToken = null;
                if (asmName.GetPublicKey() is { } pubKey) {
                    hasPublicKey = true;
                    keyOrToken = pubKey;
                } else if (asmName.GetPublicKeyToken() is { } token) {
                    hasPublicKey = false;
                    keyOrToken = token;
                }

                return new AssemblyReference(asmName.Name, asmName.Version ?? new(), hasPublicKey, keyOrToken);
            } catch (FileLoadException) {
                // the path isn't a valid AssemblyName, treat it as a path instead
            }

            return AssemblyDefinition.FromFile(nameOrPath);
        }

        private static IReadOnlyList<AssemblyDefinition> GlobAndLoadAssemblies(IEnumerable<string> args) {
            var matcher = new Matcher(PlatformDetection.OS.Is(OSKind.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            var targetDir = new DirectoryInfoWrapper(new DirectoryInfo("."));

            foreach (var arg in args) {
                matcher.AddInclude(arg);
            }

            var result = matcher.Execute(targetDir);

            if (!result.HasMatches) {
                throw new FileNotFoundException("globs didn't match any files");
            }

            var defs = new List<AssemblyDefinition>();
            foreach (var file in result.Files) {
                defs.Add(AssemblyDefinition.FromFile(file.Path));
            }

            return defs;
        }
    }
}
