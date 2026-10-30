using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonoMod.SourceGen.Internal.Extensions;
using MonoMod.SourceGen.Internal.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace MonoMod.SourceGen.Internal.Interop
{
    /// <summary>
    /// Generates the layouts of <c>[CppStruct]</c> mirrors of native C++ types: one sequential struct per combination of the
    /// preprocessor symbols the type depends on, runtime selection of the combination matching the current process, and
    /// offset-based implementations of the declared <c>partial ref</c> member properties.
    /// See <c>MonoMod.Core.Interop.Attributes.CppStructAttribute</c>.
    /// </summary>
    [Generator]
    public sealed class CppStructGenerator : IIncrementalGenerator
    {
        private const string CppStructAttribute = "MonoMod.Core.Interop.Attributes.CppStructAttribute";
        private const string CppIfAttribute = "MonoMod.Core.Interop.Attributes.CppIfAttribute";
        private const string CppUnionAttribute = "MonoMod.Core.Interop.Attributes.CppUnionAttribute";

        // Each symbol doubles the number of generated layouts.
        private const int MaxSymbols = 8;

        private const string Unsafe = "Unsafe";

        private sealed record CppMember(
            string Name, string Access, string TypeFqName, string? EmbeddedStruct, string? Condition, int? Union, string? Error);

        private sealed record CppStructInfo(
            TypeContext Type, string FqName, string? BaseStruct, string? Condition, EquatableArray<CppMember> Members, string? Error);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var structs = context.SyntaxProvider.ForAttributeWithMetadataName(CppStructAttribute,
                (n, ct) => n is StructDeclarationSyntax,
                Extract);

            context.RegisterSourceOutput(structs.Collect(), Execute);
        }

        private static CppStructInfo Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var sym = (INamedTypeSymbol)ctx.TargetSymbol;

            string? error = null;
            string? baseStruct = null;
            if (ctx.Attributes is [{ ConstructorArguments: [{ Value: INamedTypeSymbol baseType }] }])
            {
                baseStruct = baseType.GetFullyQualifiedName();
                if (!baseType.HasAttributeWithFullyQualifiedMetadataName(CppStructAttribute))
                {
                    error = $"Base type {baseStruct} of {sym.Name} is not a [CppStruct]";
                }
            }

            using var members = ImmutableArrayBuilder<CppMember>.Rent();
            foreach (var member in sym.GetMembers())
            {
                if (member is not IPropertySymbol { IsStatic: false, IsPartialDefinition: true } prop)
                {
                    continue;
                }

                string? memberError = null;
                if (prop.RefKind != RefKind.Ref || prop.IsIndexer || prop.GetMethod is null || prop.SetMethod is not null)
                {
                    memberError = $"{sym.Name}.{prop.Name} must be declared as 'partial ref T {prop.Name} {{ get; }}'";
                }

                var embedded = prop.Type is INamedTypeSymbol namedType && namedType.HasAttributeWithFullyQualifiedMetadataName(CppStructAttribute)
                    ? namedType.GetFullyQualifiedName()
                    : null;

                int? union = null;
                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass?.GetFullyQualifiedMetadataName() == CppUnionAttribute
                        && attr.ConstructorArguments is [{ Value: int index }])
                    {
                        union = index;
                    }
                }

                members.Add(new(prop.Name, GenHelpers.GetAccessibilityModifier(prop.DeclaredAccessibility),
                    prop.Type.GetFullyQualifiedName(), embedded, GetCondition(prop), union, memberError));
            }

            return new(GenHelpers.CreateTypeContext(sym), sym.GetFullyQualifiedName(), baseStruct, GetCondition(sym),
                members.ToImmutable(), error);
        }

        private static string? GetCondition(ISymbol symbol)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.GetFullyQualifiedMetadataName() == CppIfAttribute
                    && attr.ConstructorArguments is [{ Value: string condition }])
                {
                    return condition;
                }
            }
            return null;
        }

        private static void Execute(SourceProductionContext spc, ImmutableArray<CppStructInfo> structs)
        {
            var model = new Model(structs);
            foreach (var info in structs)
            {
                var sb = new StringBuilder();
                var code = new CodeBuilder(sb);
                code.WriteHeader();
                Generate(code, info, model);
                spc.AddSource($"CppStruct_{info.Type.FullContextName}.g.cs", sb.ToString());
            }
        }

        // Cross-struct information: the preprocessor symbols each struct depends on, and parsed conditions.
        private sealed class Model
        {
            private readonly Dictionary<string, CppStructInfo> structs = new();
            private readonly Dictionary<string, ImmutableArray<string>> symbols = new();
            private readonly HashSet<string> visiting = new();
            private readonly Dictionary<string, (Condition? Cond, string? Error)> conditions = new();

            public Model(ImmutableArray<CppStructInfo> infos)
            {
                foreach (var info in infos)
                {
                    structs[info.FqName] = info;
                }
            }

            public bool TryGetStruct(string fqName, out CppStructInfo info) => structs.TryGetValue(fqName, out info!);

            public (Condition? Cond, string? Error) GetCondition(string? text)
            {
                if (text is null)
                {
                    return (null, null);
                }
                if (!conditions.TryGetValue(text, out var result))
                {
                    conditions[text] = result = Condition.TryParse(text, out var cond, out var error)
                        ? (cond, null)
                        : (null, $"Invalid condition \"{text}\": {error}");
                }
                return result;
            }

            // The sorted set of preprocessor symbols that the layout of the struct depends on.
            public ImmutableArray<string> GetSymbols(string fqName)
            {
                if (symbols.TryGetValue(fqName, out var result))
                {
                    return result;
                }
                if (!structs.TryGetValue(fqName, out var info) || !visiting.Add(fqName))
                {
                    // unknown or recursive; both are reported during generation
                    return ImmutableArray<string>.Empty;
                }

                var set = new HashSet<string>(StringComparer.Ordinal);
                GetCondition(info.Condition).Cond?.CollectSymbols(set);
                if (info.BaseStruct is not null)
                {
                    set.UnionWith(GetSymbols(info.BaseStruct));
                }
                foreach (var member in info.Members)
                {
                    GetCondition(member.Condition).Cond?.CollectSymbols(set);
                    if (member.EmbeddedStruct is not null)
                    {
                        set.UnionWith(GetSymbols(member.EmbeddedStruct));
                    }
                }

                _ = visiting.Remove(fqName);
                return symbols[fqName] = set.OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray();
            }

            public bool Exists(string fqName, IReadOnlyDictionary<string, bool> config)
                => structs.TryGetValue(fqName, out var info) && (GetCondition(info.Condition).Cond?.Evaluate(config) ?? true);

            // The name of the layout variant of the struct for the given configuration (a superset of the struct's symbols).
            public string GetLayoutName(string fqName, IReadOnlyDictionary<string, bool> config)
            {
                var active = GetSymbols(fqName).Where(s => config[s]).ToArray();
                return active.Length == 0 ? "Default" : "With_" + string.Join("__", active);
            }

            public string GetLayoutType(string fqName, IReadOnlyDictionary<string, bool> config)
                => $"{fqName}.Layout.{GetLayoutName(fqName, config)}";
        }

        private static void Generate(CodeBuilder code, CppStructInfo info, Model model)
        {
            var errors = new List<string>();
            if (info.Error is not null)
            {
                errors.Add(info.Error);
            }

            var structCond = model.GetCondition(info.Condition);
            if (structCond.Error is not null)
            {
                errors.Add(structCond.Error);
            }
            if (info.BaseStruct is not null && !model.TryGetStruct(info.BaseStruct, out _))
            {
                errors.Add($"Base type {info.BaseStruct} is not a [CppStruct]");
            }

            var members = info.Members.AsImmutableArray();
            var memberConds = new Condition?[members.Length];
            for (var i = 0; i < members.Length; i++)
            {
                if (members[i].Error is { } memberError)
                {
                    errors.Add(memberError);
                }
                var (cond, condError) = model.GetCondition(members[i].Condition);
                if (condError is not null)
                {
                    errors.Add($"{members[i].Name}: {condError}");
                }
                memberConds[i] = cond;

                if (members[i].Union is { } union && i > 0 && members[i - 1].Union != union
                    && members.Take(i - 1).Any(m => m.Union == union))
                {
                    errors.Add($"{members[i].Name}: members of union {union} must be adjacent");
                }
            }

            var symbols = model.GetSymbols(info.FqName);
            if (symbols.Length > MaxSymbols)
            {
                errors.Add($"{info.Type.InnermostType.Name} depends on {symbols.Length} preprocessor symbols; at most {MaxSymbols} are supported");
            }

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    code.WriteLine($"#error {error}");
                }
                return;
            }

            // Compute the layouts. Bit i of a configuration index is the value of symbols[i].
            var layouts = new List<(int Index, string Name, Dictionary<string, bool> Config, List<LayoutEntry> Entries)>();
            for (var index = 0; index < 1 << symbols.Length; index++)
            {
                var config = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (var bit = 0; bit < symbols.Length; bit++)
                {
                    config[symbols[bit]] = (index & (1 << bit)) != 0;
                }

                if (!(structCond.Cond?.Evaluate(config) ?? true))
                {
                    continue;
                }

                var entries = new List<LayoutEntry>();
                if (info.BaseStruct is not null)
                {
                    if (!model.Exists(info.BaseStruct, config))
                    {
                        errors.Add($"Base type {info.BaseStruct} does not exist when {Describe(config)}");
                    }
                    entries.Add(new LayoutEntry(model.GetLayoutType(info.BaseStruct, config), "@base", null, -1));
                }

                for (var i = 0; i < members.Length; i++)
                {
                    var member = members[i];
                    if (!(memberConds[i]?.Evaluate(config) ?? true))
                    {
                        continue;
                    }

                    string type;
                    if (member.EmbeddedStruct is not null)
                    {
                        if (!model.Exists(member.EmbeddedStruct, config))
                        {
                            errors.Add($"Type {member.EmbeddedStruct} of {member.Name} does not exist when {Describe(config)}");
                        }
                        type = model.GetLayoutType(member.EmbeddedStruct, config);
                    }
                    else
                    {
                        type = member.TypeFqName;
                    }

                    entries.Add(new LayoutEntry(type, member.Name, member.Union, i));
                }

                layouts.Add((index, model.GetLayoutName(info.FqName, config), config, entries));
            }

            if (errors.Count > 0)
            {
                foreach (var error in errors.Distinct())
                {
                    code.WriteLine($"#error {error}");
                }
                return;
            }

            var shell = info.Type.InnermostType.Name;
            var mayBeAbsent = info.Condition is not null;

            info.Type.AppendEnterContext(code, "unsafe");

            // the base subobject
            if (info.BaseStruct is not null)
            {
                code.WriteLine("/// <summary>The base class subobject (C++ derived-to-base conversion).</summary>")
                    .WriteLine($"public ref {info.BaseStruct} @base => ref *({info.BaseStruct}*){Unsafe}.AsPointer(ref this);")
                    .WriteLine();
            }

            // the member accessors
            for (var i = 0; i < members.Length; i++)
            {
                var member = members[i];
                var offset = $"Layout.__offset_{member.Name}";
                if (mayBeAbsent || member.Condition is not null)
                {
                    offset = $"Layout.__Require({offset}, \"{member.Name}\")";
                }
                var refType = member.EmbeddedStruct ?? member.TypeFqName;
                code.WriteLine($"{member.Access}partial ref {refType} {member.Name}")
                    .OpenBlock()
                    .WriteLine($"get => ref *({refType}*)((byte*){Unsafe}.AsPointer(ref this) + {offset});")
                    .CloseBlock();
            }
            code.WriteLine();

            code.WriteLine($"/// <summary>The native layouts of <see cref=\"{shell}\"/>, depending on: {(symbols.Length == 0 ? "nothing" : string.Join(", ", symbols))}.</summary>")
                .WriteLine("public static partial class Layout")
                .OpenBlock();

            code.WriteLine("/// <summary>Whether the type exists in the current runtime configuration.</summary>")
                .WriteLine("public static readonly bool Exists;")
                .WriteLine("private static readonly int s_size;")
                .WriteLine();
            code.WriteLine($"/// <summary><c>sizeof({shell})</c> in the current runtime configuration.</summary>");
            if (mayBeAbsent)
            {
                code.WriteLine($"public static int Size => Exists ? s_size : throw new global::System.InvalidOperationException(\"{shell} does not exist in the current runtime configuration\");");
            }
            else
            {
                code.WriteLine("public static int Size => s_size;");
            }
            code.WriteLine();

            foreach (var member in members)
            {
                code.WriteLine($"internal static readonly int __offset_{member.Name};");
            }
            code.WriteLine();

            code.WriteLine("internal static int __Require(int offset, string member)")
                .OpenBlock()
                .WriteLine("if (offset < 0)")
                .OpenBlock()
                .WriteLine($"throw new global::System.InvalidOperationException(\"{shell}.\" + member + \" does not exist in the current runtime configuration\");")
                .CloseBlock()
                .WriteLine("return offset;")
                .CloseBlock()
                .WriteLine();

            // runtime layout selection
            code.WriteLine("static Layout()")
                .OpenBlock();
            foreach (var member in members)
            {
                code.WriteLine($"__offset_{member.Name} = -1;");
            }
            if (symbols.Length == 0)
            {
                code.WriteLine("const int configuration = 0;");
            }
            else
            {
                code.WriteLine($"var configuration = {string.Join(" | ", symbols.Select((s, bit) => $"({s} ? {1 << bit} : 0)"))};");
            }
            code.WriteLine("switch (configuration)")
                .OpenBlock();
            foreach (var (index, name, _, entries) in layouts)
            {
                code.WriteLine($"case {index}:")
                    .OpenBlock()
                    .WriteLine($"{name} layout = default;")
                    .WriteLine("var start = (byte*)&layout;")
                    .WriteLine("Exists = true;")
                    .WriteLine($"s_size = sizeof({name});");
                foreach (var entry in entries)
                {
                    if (entry.MemberIndex < 0)
                    {
                        continue;
                    }
                    var path = entry.Union is { } union ? $"__union{union}.{entry.Name}" : entry.Name;
                    code.WriteLine($"__offset_{entry.Name} = (int)((byte*)&layout.{path} - start);");
                }
                code.WriteLine("break;")
                    .CloseBlock();
            }
            code.CloseBlock()
                .CloseBlock()
                .WriteLine();

            // the layouts themselves
            foreach (var (_, name, config, entries) in layouts)
            {
                var desc = symbols.Length == 0 ? "the only configuration" : $"<c>{Describe(config).Replace("&", "&amp;")}</c>";
                code.WriteLine($"/// <summary>The layout of <see cref=\"{shell}\"/> for {desc}.</summary>")
                    .WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]")
                    .WriteLine($"public partial struct {name}")
                    .OpenBlock();

                var unions = new List<(int Union, List<LayoutEntry> Members)>();
                foreach (var entry in entries)
                {
                    if (entry.Union is not { } union)
                    {
                        code.WriteLine($"public {entry.Type} {entry.Name};");
                        continue;
                    }

                    if (unions.Count == 0 || unions[unions.Count - 1].Union != union)
                    {
                        unions.Add((union, new List<LayoutEntry>()));
                        code.WriteLine($"public __Union{union} __union{union};");
                    }
                    unions[unions.Count - 1].Members.Add(entry);
                }

                foreach (var (union, unionMembers) in unions)
                {
                    code.WriteLine()
                        .WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit)]")
                        .WriteLine($"public partial struct __Union{union}")
                        .OpenBlock();
                    foreach (var entry in unionMembers)
                    {
                        code.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset(0)] public {entry.Type} {entry.Name};");
                    }
                    code.CloseBlock();
                }

                code.CloseBlock()
                    .WriteLine();
            }

            code.CloseBlock();

            info.Type.AppendExitContext(code);
        }

        private static string Describe(IReadOnlyDictionary<string, bool> config)
            => string.Join(" && ", config.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Value ? "" : "!") + kv.Key));

        private sealed record LayoutEntry(string Type, string Name, int? Union, int MemberIndex);

        // A C preprocessor #if condition, restricted to symbol tests combined with !, && and ||.
        private abstract class Condition
        {
            public abstract bool Evaluate(IReadOnlyDictionary<string, bool> config);
            public abstract void CollectSymbols(ISet<string> symbols);

            private sealed class Symbol(string name) : Condition
            {
                public override bool Evaluate(IReadOnlyDictionary<string, bool> config) => config[name];
                public override void CollectSymbols(ISet<string> symbols) => symbols.Add(name);
            }

            private sealed class Not(Condition operand) : Condition
            {
                public override bool Evaluate(IReadOnlyDictionary<string, bool> config) => !operand.Evaluate(config);
                public override void CollectSymbols(ISet<string> symbols) => operand.CollectSymbols(symbols);
            }

            private sealed class Binary(Condition left, Condition right, bool isAnd) : Condition
            {
                public override bool Evaluate(IReadOnlyDictionary<string, bool> config)
                    => isAnd ? left.Evaluate(config) && right.Evaluate(config) : left.Evaluate(config) || right.Evaluate(config);
                public override void CollectSymbols(ISet<string> symbols)
                {
                    left.CollectSymbols(symbols);
                    right.CollectSymbols(symbols);
                }
            }

            public static bool TryParse(string text, out Condition? condition, out string? error)
            {
                var parser = new Parser(text);
                try
                {
                    condition = parser.ParseOr();
                    if (parser.Peek() is { } trailing)
                    {
                        throw new FormatException($"unexpected '{trailing}'");
                    }
                    error = null;
                    return true;
                }
                catch (FormatException e)
                {
                    condition = null;
                    error = e.Message;
                    return false;
                }
            }

            private sealed class Parser(string text)
            {
                private int pos;

                public string? Peek()
                {
                    while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    {
                        pos++;
                    }
                    if (pos >= text.Length)
                    {
                        return null;
                    }

                    var c = text[pos];
                    if (char.IsLetter(c) || c == '_')
                    {
                        var end = pos;
                        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                        {
                            end++;
                        }
                        return text.Substring(pos, end - pos);
                    }
                    if ((c == '&' || c == '|') && pos + 1 < text.Length && text[pos + 1] == c)
                    {
                        return text.Substring(pos, 2);
                    }
                    return c.ToString();
                }

                private string Next()
                {
                    var token = Peek() ?? throw new FormatException("unexpected end of condition");
                    pos += token.Length;
                    return token;
                }

                private void Expect(string token)
                {
                    var next = Next();
                    if (next != token)
                    {
                        throw new FormatException($"expected '{token}' but found '{next}'");
                    }
                }

                public Condition ParseOr()
                {
                    var left = ParseAnd();
                    while (Peek() == "||")
                    {
                        _ = Next();
                        left = new Binary(left, ParseAnd(), isAnd: false);
                    }
                    return left;
                }

                private Condition ParseAnd()
                {
                    var left = ParseUnary();
                    while (Peek() == "&&")
                    {
                        _ = Next();
                        left = new Binary(left, ParseUnary(), isAnd: true);
                    }
                    return left;
                }

                private Condition ParseUnary()
                {
                    if (Peek() == "!")
                    {
                        _ = Next();
                        return new Not(ParseUnary());
                    }
                    return ParsePrimary();
                }

                private Condition ParsePrimary()
                {
                    var token = Next();
                    if (token == "(")
                    {
                        var inner = ParseOr();
                        Expect(")");
                        return inner;
                    }
                    if (token == "defined")
                    {
                        if (Peek() == "(")
                        {
                            _ = Next();
                            var name = ParseSymbolName();
                            Expect(")");
                            return new Symbol(name);
                        }
                        return new Symbol(ParseSymbolName());
                    }
                    pos -= token.Length;
                    return new Symbol(ParseSymbolName());
                }

                private string ParseSymbolName()
                {
                    var token = Next();
                    if (!(char.IsLetter(token[0]) || token[0] == '_'))
                    {
                        throw new FormatException($"expected a preprocessor symbol but found '{token}'");
                    }
                    return token;
                }
            }
        }
    }
}
