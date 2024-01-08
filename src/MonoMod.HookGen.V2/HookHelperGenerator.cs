using Microsoft.CodeAnalysis;
using MonoMod.SourceGen.Internal;
using MonoMod.SourceGen.Internal.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MonoMod.HookGen.V2 {
    [Generator]
    internal class HookHelperGenerator : IIncrementalGenerator {

        private const string GenHelperForTypeAttributeFqn = "MonoMod.HookGen.GenerateHookHelpersAttribute";
        private const string ILHookParameterType = "global::MonoMod.Cil.ILContext.Manipulator";
        private const string GenHelperForTypeAttributeSource =
            """
            #nullable enable
            using System;
            using System.Diagnostics;

            namespace MonoMod.HookGen {
            #if DEBUG
                /// <summary>
                /// Designates a type to generate implicit hook helpers for.
                /// </summary>
                /// <remarks>
                /// Non-public members of the type may or may not be included. It is recommended to use a publicizer with this generator.
                /// </remarks>
            #endif
                [AttributeUsage(AttributeTargets.Assembly)]
                [Conditional("SOURCE_GENERATOR_ONLY")]
                internal sealed class GenerateHookHelpersAttribute : Attribute {
            #if DEBUG
                    /// <summary>
                    /// The type to generate hook helpers for the members of.
                    /// </summary>
            #endif
                    public Type TargetType { get; }
                    
            #if DEBUG
                    /// <summary>
                    /// Whether to generate helpers for nested types. Defaults to <see langword="true"/>.
                    /// </summary>
            #endif
                    public bool IncludeNestedTypes { get; set; } = true;
                    
            #if DEBUG
                    /// <summary>
                    /// Whether to differentiate between overloaded members by putting their (sanitized) signature in the generated name.
                    /// Defaults to <see langword="false"/>.
                    /// </summary>
            #endif
                    public bool DistinguishOverloadsByName { get; set; }
                    
            #if DEBUG
                    /// <summary>
                    /// A list of members to generate hook helpers for in the target type, by exact name.
                    /// All members with the specified names (including overloads) will be generated.
                    /// </summary>
            #endif
                    public string[]? Members { get; set; }
                    
            #if DEBUG
                    /// <summary>
                    /// A list of member name prefixes to match members against. Members whose names have one of these
                    /// prefixes will be included.
                    /// </summary>
            #endif
                    public string[]? MemberNamePrefixes { get; set; }
                    
            #if DEBUG
                    /// <summary>
                    /// A list of member name suffixes to match members against. Members whose names have one of these
                    /// suffixes will be included.
                    /// </summary>
            #endif
                    public string[]? MemberNameSuffixes { get; set; }
                    
            #if DEBUG
                    /// <summary>
                    /// Constructs a <see cref="GenerateHookHelpersAttribute"/> indicating the specified target type.
                    /// </summary>
                    /// <param name="targetType">The type to target for generation.</param>
            #endif
                    public GenerateHookHelpersAttribute(Type targetType) {
                        TargetType = targetType;
                    }
                }
            }
            """;

        private static readonly ObjectPool<Dictionary<MetadataReference, ImmutableArrayBuilder<AttributeModel>>> asmIdentBuilderDictPool = new(() => new());
        private static readonly ObjectPool<Dictionary<TypeContext, ImmutableArrayBuilder<AttributeModel>>> typeIdentBuilderDictPool = new(() => new());
        private static readonly ObjectPool<Dictionary<(bool, bool), InProgressTypeModel>> inProgressTypeModelDictPool = new(() => new());

        private static readonly ObjectPool<HashSet<string>> stringHashSetPool = new(() => new());
        private static readonly ObjectPool<HashSet<MethodSignature>> methodSigHashSetPool = new(() => new());

        private static readonly ObjectPool<Queue<GeneratableTypeModel>> genTypeModelQueuePool = new(() => new());

        private static readonly IEqualityComparer<HashSet<string>> stringHashSetEqualityComparer = HashSet<string>.CreateSetComparer();

        private sealed record InProgressTypeModel(
            HashSet<string> MemberNames,
            HashSet<string> MemberPrefixes,
            HashSet<string> MemberSuffixes);

        public void Initialize(IncrementalGeneratorInitializationContext context) {

            context.RegisterPostInitializationOutput(ctx => {
                ctx.AddSource("GenerateHookHelpersAttribute.g.cs", GenHelperForTypeAttributeSource);
            });

            var attributes = context.SyntaxProvider.ForAttributeWithMetadataName(
                GenHelperForTypeAttributeFqn,
                static (_, _) => true,
                static (ctx, ct) => {
                    using var builder = ImmutableArrayBuilder<AttributeModel>.Rent();

                    foreach (var attr in ctx.Attributes) {
                        ct.ThrowIfCancellationRequested();
                        var model = ReadTypeModelForAttribute(ctx.SemanticModel.Compilation, ctx.TargetSymbol, attr);
                        if (model is not null) {
                            builder.Add(model);
                        }
                    }

                    return builder.ToImmutable();
                })
                .SelectMany(static (x, _) => x);

            // all attributes are now available, we now want to group them by target assembly and target type
            var groupedByAssembly = attributes.Collect()
                .SelectMany((attrs, ct) => {
                    var dict = asmIdentBuilderDictPool.Allocate();

                    foreach (var attr in attrs) {
                        ct.ThrowIfCancellationRequested();

                        if (!dict.TryGetValue(attr.TargetAssembly, out var asmBuilder)) {
                            dict.Add(attr.TargetAssembly, asmBuilder = ImmutableArrayBuilder<AttributeModel>.Rent());
                        }

                        asmBuilder.Add(attr);
                    }

                    using var builder = ImmutableArrayBuilder<(MetadataReference Assembly, EquatableArray<AttributeModel> Attributes)>.Rent();

                    foreach (var kvp in dict) {
                        ct.ThrowIfCancellationRequested();
                        builder.Add((kvp.Key, kvp.Value.ToImmutable()));
                        kvp.Value.Dispose();
                    }

                    dict.Clear();
                    asmIdentBuilderDictPool.Free(dict);

                    return builder.ToImmutable();
                });

            // now for each assembly, we want to group by type
            var groupedByType = groupedByAssembly
                .Select((t, ct) => {
                    var dict = typeIdentBuilderDictPool.Allocate();

                    foreach (var attr in t.Attributes) {
                        ct.ThrowIfCancellationRequested();

                        if (!dict.TryGetValue(attr.TargetType, out var asmBuilder)) {
                            dict.Add(attr.TargetType, asmBuilder = ImmutableArrayBuilder<AttributeModel>.Rent());
                        }

                        asmBuilder.Add(attr);
                    }

                    using var builder = ImmutableArrayBuilder<TypeModel>.Rent();

                    foreach (var kvp in dict) {
                        ct.ThrowIfCancellationRequested();
                        builder.Add(new(kvp.Key, kvp.Value.ToImmutable()));
                        kvp.Value.Dispose();
                    }

                    dict.Clear();
                    typeIdentBuilderDictPool.Free(dict);

                    return new AssemblyModel(t.Assembly, builder.ToImmutable());
                });

            // then, for each type, we want to unify the requested attribute options
            var unifiedGroupedByType = groupedByType
                .Select((model, ct) => {
                    using var typeModelBuilder = ImmutableArrayBuilder<TypeModel>.Rent();
                    using var attrsBuilder = ImmutableArrayBuilder<AttributeModel>.Rent();

                    foreach (var type in model.Types) {
                        ct.ThrowIfCancellationRequested();
                        var dict = inProgressTypeModelDictPool.Allocate();

                        foreach (var attr in type.Attributes) {
                            var tuple = (attr.Options.IncludeNested, attr.Options.DistinguishOverloads);
                            if (!dict.TryGetValue(tuple, out var inProgress)) {
                                dict.Add(tuple, inProgress = new(stringHashSetPool.Allocate(), stringHashSetPool.Allocate(), stringHashSetPool.Allocate()));
                            }

                            foreach (var name in attr.Options.ExplicitMembers) {
                                _ = inProgress.MemberNames.Add(name);
                            }
                            foreach (var name in attr.Options.MemberPrefixes) {
                                _ = inProgress.MemberPrefixes.Add(name);
                            }
                            foreach (var name in attr.Options.MemberSuffixes) {
                                _ = inProgress.MemberSuffixes.Add(name);
                            }
                        }

                        ct.ThrowIfCancellationRequested();

                        attrsBuilder.Clear();
                        foreach (var kvp in dict) {
                            attrsBuilder.Add(new(
                                model.Assembly,
                                type.TargetType,
                                new(kvp.Key.Item1, kvp.Key.Item2,
                                    kvp.Value.MemberNames,
                                    kvp.Value.MemberPrefixes.ToImmutableArray(),
                                    kvp.Value.MemberSuffixes.ToImmutableArray())));

                            kvp.Value.MemberPrefixes.Clear();
                            kvp.Value.MemberSuffixes.Clear();

                            stringHashSetPool.Free(kvp.Value.MemberPrefixes);
                            stringHashSetPool.Free(kvp.Value.MemberSuffixes);
                        }
                        typeModelBuilder.Add(type with { Attributes = attrsBuilder.ToImmutable() });
                        attrsBuilder.Clear();
                        dict.Clear();
                        inProgressTypeModelDictPool.Free(dict);
                    }

                    return model with { Types = typeModelBuilder.ToImmutable() };
                });

            // next, we want to go per-assembly, and perform the actual member lookups
            var mappedAssemblies = groupedByType.Combine(context.CompilationProvider).
                Select(GetAssemblySymbol)
                .Where(t => t.Symbol is not null);

            var generatableAssemblies = mappedAssemblies.Select(GetAllMembersToGenerate!);

            var neededSignaturesWithDupes = generatableAssemblies.SelectMany(ExtractSignatures);

            var neededSignaturesWithoutDupes = neededSignaturesWithDupes.Collect().SelectMany((arr, ct) => {
                var set = methodSigHashSetPool.Allocate();

                foreach (var method in arr) {
                    _ = set.Add(method);
                }

                var result = set.ToImmutableArray();
                set.Clear();
                methodSigHashSetPool.Free(set);
                return result;
            });

            context.RegisterSourceOutput(neededSignaturesWithoutDupes.Collect(), EmitDelegateTypes);

            var generatableTypes = generatableAssemblies.SelectMany((ass, ct) => ass.Types);

            context.RegisterSourceOutput(generatableTypes, EmitHelperTypes);
        }

        private void EmitDelegateTypes(SourceProductionContext context, ImmutableArray<MethodSignature> signatures) {
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);

            cb.WriteHeader()
                .WriteLine("namespace MonoMod.HookGen;")
                .WriteLine();

            foreach (var sig in signatures) {
                var origName = GetOrigDelegateName(sig);

                var parameters = sig.ParameterTypes.AsImmutableArray();

                // first, emit the orig delelgate
                cb.Write("internal delegate ")
                    .Write(sig.ReturnType.Refness)
                    .Write(sig.ReturnType.FqName)
                    .Write(" ")
                    .Write(origName)
                    .WriteLine("(")
                    .IncreaseIndent();

                if (sig.ThisType is { } thisType) {
                    _ = cb
                        .Write(thisType.Refness)
                        .Write(thisType.FqName)
                        .Write(" @this");

                    if (parameters.Length != 0) {
                        cb.WriteLine(",");
                    }
                }

                for (var i = 0; i < parameters.Length; i++) {
                    var param = parameters[i];

                    _ = cb
                        .Write(param.Refness)
                        .Write(param.FqName)
                        .Write(" arg")
                        .Write(i.ToString(CultureInfo.InvariantCulture));

                    if (i != parameters.Length - 1) {
                        cb.WriteLine(",");
                    }
                }

                cb.WriteLine(");").DecreaseIndent();

                // then, emit the hook delegate
                cb.Write("internal delegate ")
                    .Write(sig.ReturnType.Refness)
                    .Write(sig.ReturnType.FqName)
                    .Write(" ")
                    .Write(GetHookDelegateName(sig))
                    .WriteLine("(")
                    .IncreaseIndent()
                    .Write(origName)
                    .WriteLine(" orig");

                if (sig.ThisType is { } thisType2) {
                    _ = cb
                        .WriteLine(",")
                        .Write(thisType2.Refness)
                        .Write(thisType2.FqName)
                        .Write(" @this");
                }

                for (var i = 0; i < parameters.Length; i++) {
                    var param = parameters[i];

                    _ = cb
                        .WriteLine(",")
                        .Write(param.Refness)
                        .Write(param.FqName)
                        .Write(" arg")
                        .Write(i.ToString(CultureInfo.InvariantCulture));
                }

                cb.WriteLine(");").DecreaseIndent();
            }
        }

        private void EmitHelperTypes(SourceProductionContext context, GeneratableTypeModel type) {
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);

            cb.WriteHeader()
                .WriteLine("namespace On")
                .OpenBlock();

            type.Type.AppendEnterContext(cb, "internal static");

            EmitTypeMembers(type, cb, il: false);

            type.Type.AppendExitContext(cb);

            cb.CloseBlock();

            context.AddSource($"{type.AssemblyIdentity.Name}_{type.Type.FullContextName}.g.cs", sb.ToString());
        }

        private static void EmitTypeMembers(GeneratableTypeModel type, CodeBuilder cb, bool il) {
            foreach (var member in type.Members) {

                var bindingFlags = member.Accessibility switch {
                    Accessibility.NotApplicable => BindingFlags.NonPublic,
                    Accessibility.Private => BindingFlags.NonPublic,
                    Accessibility.ProtectedAndInternal => BindingFlags.NonPublic,
                    Accessibility.Protected => BindingFlags.NonPublic,
                    Accessibility.Internal => BindingFlags.NonPublic,
                    Accessibility.ProtectedOrInternal => BindingFlags.NonPublic,
                    Accessibility.Public => BindingFlags.Public,
                    _ => BindingFlags.NonPublic,
                };

                bindingFlags |= member.Signature.ThisType is not null ? BindingFlags.Static : BindingFlags.Instance;

                var hookType = il ? "global::MonoMod.RuntimeDetour.ILHook" : "global::MonoMod.RuntimeDetour.Hook";
                var parameterType = il ? ILHookParameterType : "global::MonoMod.HookGen." + GetHookDelegateName(member.Signature);

                cb.Write("public static ")
                    .Write(hookType)
                    .Write(" ")
                    .Write(member.Name);
                if (il || member.DistinguishByName) {
                    AppendSignatureIdentifier(cb, member.Signature);
                }
                cb.Write("(")
                    .Write(parameterType)
                    .WriteLine(" hook, bool applyByDefault = true)")
                    .OpenBlock()
                    .Write("var type = typeof(")
                    .Write(type.Type.InnermostType.FqName)
                    .WriteLine(");")
                    .Write("var method = type.GetMethod(\"")
                    .Write(member.Name)
                    .Write("\", (global::System.Reflection.BindingFlags)")
                    .Write(((int) bindingFlags).ToString(CultureInfo.InvariantCulture))
                    .WriteLine(", new Type[]")
                    .OpenBlock();

                foreach (var param in member.Signature.ParameterTypes.AsImmutableArray()) {
                    cb.Write("typeof(")
                        .Write(param.FqName)
                        .Write(")");
                    if (!string.IsNullOrWhiteSpace(param.Refness)) {
                        cb.Write(".MakeByRefType()");
                    }
                    cb.WriteLine(",");
                }

                cb.CloseBlock()
                    .WriteLine(");")
                    .Write("if (method is null) throw new global::System.MissingMethodException(\"")
                    .Write(type.Type.InnermostType.MdName)
                    .Write("\", \"")
                    .Write(member.Name)
                    .WriteLine("\");")
                    .WriteLine("return new(method, hook, applyByDefault: applyByDefault);")
                    .CloseBlock()
                    .WriteLine();
                ;
            }

            foreach (var nested in type.NestedTypes) {
                cb.Write("internal static ")
                    .Write(nested.Type.ContainingTypeDecls[0])
                    .OpenBlock();

                EmitTypeMembers(nested, cb, il);

                cb.CloseBlock();
            }
        }

        private static string GetOrigDelegateName(MethodSignature sig) {
            return "Orig" + GetHookDelegateName(sig);
        }

        private static readonly ObjectPool<StringBuilder> stringBuilderPool = new(() => new());

        private static string GetHookDelegateName(MethodSignature sig) {
            var sb = stringBuilderPool.Allocate();

            var parameters = sig.ParameterTypes.AsImmutableArray();
            sb.Append("HookSig_")
                .Append(sig.ReturnType.Refness.Replace(" ", "_"))
                .Append(sig.ReturnType.MdName.Replace(".", "_").Replace("`", "_"))
                .Append('_')
                .Append(parameters.Length);

            if (sig.ThisType is { } thisType) {
                _ = sb
                    .Append('_')
                    .Append(thisType.Refness.Replace(" ", "_"))
                    .Append(thisType.MdName.Replace(".", "_").Replace("`", "_"));
            }

            for (var i = 0; i < parameters.Length; i++) {
                var param = parameters[i];

                _ = sb
                    .Append('_')
                    .Append(param.Refness.Replace(" ", "_"))
                    .Append(param.MdName.Replace(".", "_").Replace("`", "_"));
            }

            var result = sb.ToString();
            sb.Clear();
            stringBuilderPool.Free(sb);
            return result;
        }

        private static void AppendSignatureIdentifier(CodeBuilder cb, MethodSignature sig) {
            var parameters = sig.ParameterTypes.AsImmutableArray();
            cb.Write("_")
                .Write(sig.ReturnType.Refness.Replace(" ", "_"))
                .Write(sig.ReturnType.MdName.Replace(".", "_").Replace("`", "_"))
                .Write('_')
                .Write(parameters.Length.ToString(CultureInfo.InvariantCulture));

            if (sig.ThisType is { } thisType) {
                _ = cb
                    .Write('_')
                    .Write(thisType.Refness.Replace(" ", "_"))
                    .Write(thisType.MdName.Replace(".", "_").Replace("`", "_"));
            }


            for (var i = 0; i < parameters.Length; i++) {
                var param = parameters[i];

                _ = cb
                    .Write('_')
                    .Write(param.Refness.Replace(" ", "_"))
                    .Write(param.MdName.Replace(".", "_").Replace("`", "_"));
            }
        }

        private static ImmutableArray<MethodSignature> ExtractSignatures(GeneratableAssembly assembly, CancellationToken token) {
            var set = methodSigHashSetPool.Allocate();
            var queue = genTypeModelQueuePool.Allocate();

            foreach (var type in assembly.Types) {
                queue.Enqueue(type);
            }

            while (queue.Count > 0) {
                var type = queue.Dequeue();

                foreach (var method in type.Members) {
                    set.Add(method.Signature);
                }

                foreach (var nested in type.NestedTypes) {
                    queue.Enqueue(nested);
                }
            }

            var resultSet = set.ToImmutableArray();
            set.Clear();
            methodSigHashSetPool.Free(set);
            genTypeModelQueuePool.Free(queue);
            return resultSet;
        }

        private static bool MetadataReferenceEquals(MetadataReference a, MetadataReference b) {
            if (a is CompilationReference ca) {
                if (b is CompilationReference cb) {
                    return ca.Compilation == cb.Compilation;
                }
                return false;
            }
            return ReferenceEquals(a, b);
        }

        private sealed record AssemblyModel(MetadataReference Assembly, EquatableArray<TypeModel> Types) {
            public bool Equals(AssemblyModel other)
                => MetadataReferenceEquals(Assembly, other.Assembly)
                && Types.Equals(other.Types);
            public override int GetHashCode() {
                var hc = new HashCode();
                if (Assembly is CompilationReference cr) {
                    hc.Add(cr.Compilation);
                } else {
                    hc.Add(Assembly);
                }
                hc.Add(Types);
                return hc.ToHashCode();
            }
        }

        private sealed record TypeModel(TypeContext TargetType, EquatableArray<AttributeModel> Attributes);

        private sealed record AttributeOptions(
            bool IncludeNested, bool DistinguishOverloads,
            HashSet<string> ExplicitMembers,
            EquatableArray<string> MemberPrefixes,
            EquatableArray<string> MemberSuffixes) {
            public bool Equals(AttributeOptions other)
                => IncludeNested == other.IncludeNested
                && DistinguishOverloads == other.DistinguishOverloads
                && MemberPrefixes.Equals(other.MemberPrefixes)
                && MemberSuffixes.Equals(other.MemberSuffixes)
                && stringHashSetEqualityComparer.Equals(ExplicitMembers, other.ExplicitMembers);
            public override int GetHashCode() {
                var hc = new HashCode();
                hc.Add(IncludeNested);
                hc.Add(DistinguishOverloads);
                hc.Add(MemberPrefixes);
                hc.Add(MemberSuffixes);
                hc.Add(stringHashSetEqualityComparer.GetHashCode(ExplicitMembers));
                return hc.ToHashCode();
            }
        }

        private sealed record AttributeModel(
            MetadataReference TargetAssembly,
            TypeContext TargetType,
            AttributeOptions Options) {

            public bool Equals(AttributeModel other)
                => MetadataReferenceEquals(TargetAssembly, other.TargetAssembly)
                && TargetType.Equals(other.TargetType)
                && Options.Equals(other.Options);

            public override int GetHashCode() {
                var hc = new HashCode();
                if (TargetAssembly is CompilationReference cr) {
                    hc.Add(cr.Compilation);
                } else {
                    hc.Add(TargetAssembly);
                }
                hc.Add(TargetType);
                hc.Add(Options);
                return hc.ToHashCode();
            }
        }

        private static AttributeModel? ReadTypeModelForAttribute(Compilation compilation, ISymbol targetSym, AttributeData attr) {

            if (attr.ConstructorArguments is not [{ Value: INamedTypeSymbol targetType }])
                return null;

            var includeNested = true;
            var distinguishOverloads = false;
            HashSet<string>? explicitMembers = null;
            var memberPrefixes = ImmutableArray<string>.Empty;
            var memberSuffixes = ImmutableArray<string>.Empty;

            foreach (var named in attr.NamedArguments) {
                switch (named.Key) {
                    case "IncludeNestedTypes" when named.Value is { Value: bool bval }:
                        includeNested = bval;
                        break;
                    case "DistinguishOverloadsByName" when named.Value is { Value: bool bval }:
                        distinguishOverloads = bval;
                        break;
                    case "Members" when named.Value is { Kind: TypedConstantKind.Array, Values: var values }:
                        explicitMembers = new HashSet<string>(values.Select(c => c.Value as string).Where(s => s is not null)!);
                        break;
                    case "MemberNamePrefixes" when named.Value is { Kind: TypedConstantKind.Array, Values: var values }:
                        memberPrefixes = values.Select(c => c.Value as string).Where(s => s is not null).ToImmutableArray()!;
                        break;
                    case "MemberNameSuffixes" when named.Value is { Kind: TypedConstantKind.Array, Values: var values }:
                        memberSuffixes = values.Select(c => c.Value as string).Where(s => s is not null).ToImmutableArray()!;
                        break;

                    default:
                        return null;
                }
            }

            var mr = compilation.GetMetadataReference(targetType.ContainingAssembly);
            if (mr is null) {
                // presumably, this means it's in this assembly, so we don't care about it
                return null;
            }

            return new(mr,
                GenHelpers.CreateTypeContext(targetType),
                new(includeNested, distinguishOverloads,
                explicitMembers ?? new(), memberPrefixes, memberSuffixes));
        }

        private sealed record GeneratableAssembly(EquatableArray<GeneratableTypeModel> Types);

        private sealed record GeneratableTypeModel(
            AssemblyIdentity AssemblyIdentity, TypeContext Type,
            EquatableArray<GeneratableTypeModel> NestedTypes,
            EquatableArray<GeneratableMemberModel> Members);

        private sealed record MethodSignature(TypeRef? ThisType, EquatableArray<TypeRef> ParameterTypes, TypeRef ReturnType);

        private sealed record GeneratableMemberModel(string Name, MethodSignature Signature, bool DistinguishByName, Accessibility Accessibility);

        // I'm OK putting this in the pipeline, because the IAssemblySymbol here will always represent a metadata reference.
        // The symbols for those are reused when possible, as far as I can tell.
        private static (AssemblyModel Model, IAssemblySymbol? Symbol) GetAssemblySymbol((AssemblyModel Left, Compilation Right) tuple, CancellationToken token) {
            var symbol = tuple.Right.GetAssemblyOrModuleSymbol(tuple.Left.Assembly);
            if (symbol is IModuleSymbol module) {
                symbol = module.ContainingAssembly;
            }
            if (symbol is not IAssemblySymbol assembly) {
                return (tuple.Left, null);
            }
            return (tuple.Left, assembly);
        }

        private static GeneratableAssembly GetAllMembersToGenerate((AssemblyModel Model, IAssemblySymbol Symbol) tuple, CancellationToken token) {
            using var generatableTypesBuilder = ImmutableArrayBuilder<GeneratableTypeModel>.Rent();

            var (model, assembly) = tuple;

            foreach (var type in model.Types) {
                var typeSym = assembly.GetTypeByMetadataName(type.TargetType.InnermostType.MdName);
                if (typeSym is null)
                    continue;

                var typeModel = GetTypeModel(type.TargetType, type.Attributes, typeSym, token);
                if (typeModel is not null)
                    generatableTypesBuilder.Add(typeModel);
            }

            return new(generatableTypesBuilder.ToImmutable());
        }

        private static GeneratableTypeModel? GetTypeModel(TypeContext typeContext, ImmutableArray<AttributeModel> attrModels, INamedTypeSymbol type, CancellationToken token) {
            token.ThrowIfCancellationRequested();

            // ignore generic types, they can't be patched reliably
            if (type.IsGenericType) {
                return null;
            }

            if (type.IsAnonymousType) {
                return null;
            }

            if (type.IsImplicitlyDeclared) {
                // TODO: maybe not this?
                return null;
            }

            using var membersBuilder = ImmutableArrayBuilder<GeneratableMemberModel>.Rent();
            using var typesBuilder = ImmutableArrayBuilder<GeneratableTypeModel>.Rent();

            // first, process non-type members
            foreach (var member in type.GetMembers()) {
                if (member.Kind is not SymbolKind.Method or SymbolKind.Property) {
                    continue;
                }

                token.ThrowIfCancellationRequested();

                bool? alreadyMatchedWithDistinguishedOverloads = null;

                foreach (var attr in attrModels) {
                    var options = attr.Options;

                    if (alreadyMatchedWithDistinguishedOverloads is { } matchVal && matchVal == options.DistinguishOverloads) {
                        // we already matched this member with this distinguishOverloads value, skip
                        continue;
                    }

                    if (options.ExplicitMembers.Contains(member.Name)) {
                        goto ProcessMember;
                    }

                    foreach (var prefix in options.MemberPrefixes) {
                        if (member.Name.StartsWith(prefix, StringComparison.Ordinal)) {
                            goto ProcessMember;
                        }
                    }

                    foreach (var suffix in options.MemberSuffixes) {
                        if (member.Name.EndsWith(suffix, StringComparison.Ordinal)) {
                            goto ProcessMember;
                        }
                    }

                    // TODO: attributes which don't specify anything should cause all members

                    // not a match
                    continue;

                ProcessMember:
                    // the member matched, do our processing
                    if (member.Kind is SymbolKind.Property) {
                        var prop = (IPropertySymbol) member;

                        if (prop.GetMethod is { } getMethod) {
                            var model = GetModelForMember(getMethod, options.DistinguishOverloads, token);
                            if (model is not null)
                                membersBuilder.Add(model);
                        }
                        if (prop.SetMethod is { } setMethod) {
                            var model = GetModelForMember(setMethod, options.DistinguishOverloads, token);
                            if (model is not null)
                                membersBuilder.Add(model);
                        }

                    } else if (member.Kind is SymbolKind.Method) {
                        var method = (IMethodSymbol) member;
                        var model = GetModelForMember(method, options.DistinguishOverloads, token);
                        if (model is not null)
                            membersBuilder.Add(model);
                    }

                    if (alreadyMatchedWithDistinguishedOverloads is not null) {
                        // we matched with both, no need to continue
                        break;
                    } else {
                        alreadyMatchedWithDistinguishedOverloads = options.DistinguishOverloads;
                    }
                }

            }

            // then, process type members
            foreach (var attr in attrModels) {
                var options = attr.Options;

                if (!options.IncludeNested) {
                    continue;
                }

                foreach (var nested in type.GetTypeMembers()) {
                    token.ThrowIfCancellationRequested();

                    if (options.ExplicitMembers.Contains(nested.Name)) {
                        goto ProcessMember;
                    }

                    foreach (var prefix in options.MemberPrefixes) {
                        if (nested.Name.StartsWith(prefix, StringComparison.Ordinal)) {
                            goto ProcessMember;
                        }
                    }

                    foreach (var suffix in options.MemberSuffixes) {
                        if (nested.Name.EndsWith(suffix, StringComparison.Ordinal)) {
                            goto ProcessMember;
                        }
                    }

                    // TODO: attributes which don't specify anything should cause all members

                    // not a match
                    continue;

                    ProcessMember:
                    var typeModel = GetTypeModel(GenHelpers.CreateTypeContext(nested), ImmutableArray.Create(attr), nested, token);
                    if (typeModel is not null)
                        typesBuilder.Add(typeModel);
                }
            }

            return new(type.ContainingAssembly.Identity, typeContext, typesBuilder.ToImmutable(), membersBuilder.ToImmutable());
        }

        private static GeneratableMemberModel? GetModelForMember(IMethodSymbol method, bool distingushByName, CancellationToken token) {
            // skip generic methods
            if (method.IsGenericMethod) {
                return null;
            }

            if (method.IsAbstract) {
                return null;
            }

            using var paramTypeBuilder = ImmutableArrayBuilder<TypeRef>.Rent();

            foreach (var param in method.Parameters) {
                paramTypeBuilder.Add(GenHelpers.CreateRef(param));
            }

            var returnType = GenHelpers.CreateRef(method.ReturnType, GenHelpers.GetRefString(method.RefKind, isReturn: true));

            TypeRef? thisType = null;
            if (!method.IsStatic) {
                var refKind = method.ContainingType.IsValueType
                    ? (method.IsReadOnly ? RefKind.RefReadOnly : RefKind.Ref)
                    : RefKind.None;

                thisType = GenHelpers.CreateRef(method.ContainingType, GenHelpers.GetRefString(refKind, false));
            }

            var sig = new MethodSignature(thisType, paramTypeBuilder.ToImmutable(), returnType);

            return new(method.Name, sig, distingushByName, method.DeclaredAccessibility);
        }

    }
}
