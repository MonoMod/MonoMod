using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

[assembly: CLSCompliant(false)]

const string TfmIdVar = "___tfmid";
const string TfmVerVar = "___tfmver";
const string CompileRemovedItem = "___CompileRemoved";

if (args.Length < 1) {
    Console.Error.WriteLine("Usage: filter <filter|gen> <args>...");
    return 1;
}

var cmd = args[0];
if (cmd == "filter") {
    return FilterForTfm(args);
} else if (cmd == "gen") {
    return GenerateMSBuildFile(args);
} else if (cmd == "sort") {
    return SortFiles(args);
}

Console.Error.WriteLine($"Unknown command '{cmd}'");
return -1;

static int SortFiles(string[] args) {
    if (args.Length < 3) {
        Console.Error.WriteLine("Usage: Filter sort <file list txt> <sorted list (out)>");
        return -1;
    }
    
    var list = File.ReadAllLines(args[1]);
    Array.Sort(list);
    File.WriteAllLines(args[2], list);
    
    return 0;
}

static int FilterForTfm(string[] args) {
    if (args.Length < 5) {
        Console.Error.WriteLine("Usage: Filter filter <file list txt> <tfm type> <tfm version> <remove list txt (out)>");
        return -1;
    }

    var tfmKindString = args[2];
    var tfmVerString = args[3];
    var removeFile = args[4];

    TfmKind tfmKind;
    if (tfmKindString == ".NETFramework") {
        tfmKind = TfmKind.Framework;
    } else if (tfmKindString == ".NETStandard") {
        tfmKind = TfmKind.Standard;
    } else if (tfmKindString == ".NETCoreApp") {
        tfmKind = TfmKind.Core;
    } else {
        Console.Error.WriteLine($"Unknown target framework kind '{tfmKindString}'");
        return -1;
    }

    var tfmVersion = new Version(tfmVerString);

    var files = File.ReadAllLines(args[1]);

    var toRemove = new List<string>(files.Length);

    foreach (var file in files) {
        if (Path.IsPathRooted(file)) {
            // skip absolute paths
            continue;
        }

        var expr = GetExprFor(file);

        if (expr is null)
            continue;

        if (!expr.Matches(tfmKind, tfmVersion))
            toRemove.Add(file);
    }

    File.WriteAllLines(removeFile, toRemove);

    return 0;
}

static int GenerateMSBuildFile(string[] args) {
    if (args.Length < 3) {
        Console.Error.WriteLine("Usage: Filter gen <file list txt> <msbuild file (out)>");
        return -1;
    }

    const string TrueCond = "(''=='')";

    var msbFile = args[2];
    var files = File.ReadAllLines(args[1]);

    var matchConds = new List<(string File, string Cond)>(files.Length);

    foreach (var file in files) {
        if (Path.IsPathRooted(file)) {
            matchConds.Add((file, TrueCond));
            continue;
        }

        var expr = GetExprFor(file);

        if (expr is null) {
            matchConds.Add((file, TrueCond));
            continue;
        }

        matchConds.Add((file, GetCondition(expr)));
    }

    var sb = new StringBuilder();
    _ = sb.Append($@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Project>
    <PropertyGroup>
        <{TfmIdVar}>$([MSBuild]::GetTargetFrameworkIdentifier('$(TargetFramework)'))</{TfmIdVar}>
        <{TfmVerVar}>$([MSBuild]::GetTargetFrameworkVersion('$(TargetFramework)'))</{TfmVerVar}>
        <CompileRemovedItem>{CompileRemovedItem}</CompileRemovedItem>
    </PropertyGroup>
    <ItemGroup>
        <{CompileRemovedItem} Include=""@(Compile)"" />
    </ItemGroup>
");

    foreach (var grp in matchConds.GroupBy(t => t.Cond)) {
        _ = sb.Append(CultureInfo.InvariantCulture, $@"
    <ItemGroup Condition=""!{grp.Key}"">");
        foreach (var (f, _) in grp) {
            var escaped = SecurityElement.Escape(f);
            _ = sb.Append($@"
        <Compile Remove=""{escaped}"" />");
        }
        _ = sb.Append($@"
    </ItemGroup>");
    }

    _ = sb.Append($@"
    <ItemGroup>
        <{CompileRemovedItem} Remove=""@(Compile)"" />
        <None Include=""@({CompileRemovedItem})"" />
    </ItemGroup>
</Project>");

    var text = sb.ToString();

    var writeFile = true;
    if (File.Exists(msbFile)) {
        var existing = File.ReadAllText(msbFile, Encoding.UTF8);
        writeFile = existing != text;
    }

    if (writeFile) {
        File.WriteAllText(msbFile, sb.ToString(), Encoding.UTF8);
    }

    return 0;
}

static string GetCondition(TfmExpr expr) {
    return expr switch {
        IsKindTfmExpr(var kind) => $"('$({TfmIdVar})' == '{GetTfmKindString(kind)}')",
        MatchesTfmExpr(var kind, var op, var ver) => $"('$({TfmIdVar})' == '{GetTfmKindString(kind)}' and {GetOpCompareBegin(op)}('$({TfmVerVar})','{ver}')))",
        AndTfmExpr(var exprs) => "(" + string.Join(" and ", exprs.Select(GetCondition)) + ")",
        OrTfmExpr(var exprs) => "(" + string.Join(separator: " or ", exprs.Select(GetCondition)) + ")",
        _ => throw new InvalidOperationException()
    };
}

static string GetTfmKindString(TfmKind kind) {
    return kind switch {
        TfmKind.Framework => ".NETFramework",
        TfmKind.Core => ".NETCoreApp",
        TfmKind.Standard => ".NETStandard",
        _ => throw new InvalidOperationException()
    };
}

static string GetOpCompareBegin(Operation op) {
    return op switch {
        Operation.Eq => "$([MSBuild]::VersionEquals",
        Operation.Neq => "!$([MSBuild]::VersionEquals",
        Operation.Lt => "$([MSBuild]::VersionLessThan",
        Operation.Lte => "$([MSBuild]::VersionLessThanOrEquals",
        Operation.Gt => "$([MSBuild]::VersionGreaterThan",
        Operation.Gte => "$([MSBuild]::VersionGreaterThanOrEquals",
        _ => throw new InvalidOperationException()
    };
}

// TODO: basename processing, to be able to do `otherwise`
static TfmExpr? GetExprFor(string filename) {
    var dirEntries = filename.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var andBuilder = ImmutableArray.CreateBuilder<TfmExpr>();

    foreach (var dir in dirEntries) {
        var parts = dir.Split(',');
        var orBuilder = ImmutableArray.CreateBuilder<TfmExpr>();

        foreach (var part in parts.Skip(1)) {
            var usIdx = part.IndexOf('_');
            if (usIdx < 0) {
                // if there's no underscore, we can safely ignore it
                continue;
            }

            var opStr = part.AsSpan().Slice(0, usIdx).ToString();
            var rest = part.AsSpan().Slice(usIdx + 1);

            if (opStr == "is") {
                usIdx = rest.IndexOf('_');
                if (usIdx >= 0) {
                    Console.Error.WriteLine($"{filename}: Malformed 'is' expression");
                    continue;
                }
                usIdx = rest.IndexOf('.');
                if (usIdx >= 0) {
                    // remove extension
                    rest = rest.Slice(0, usIdx);
                }

                if (ParseTfmKind(filename, rest) is not { } kind)
                    continue;

                orBuilder.Add(new IsKindTfmExpr(kind));
            } else {
                // try parse the op string as an Operation
                Operation? maybeOp = opStr switch {
                    "eq" => Operation.Eq,
                    "neq" => Operation.Neq,
                    "gt" => Operation.Gt,
                    "gte" => Operation.Gte,
                    "lt" => Operation.Lt,
                    "lte" => Operation.Lte,
                    _ => null
                };
                if (maybeOp is not { } op) {
                    Console.Error.WriteLine($"{filename}: Unknown operation '{opStr}'");
                    continue;
                }

                usIdx = rest.IndexOf('_');
                if (usIdx < 0) {
                    Console.Error.WriteLine($"{filename}: Malformed comparison expression");
                    continue;
                }

                if (ParseTfmKind(filename, rest.Slice(0, usIdx)) is not { } kind)
                    continue;

                var ver = rest.Slice(usIdx + 1);

                // remote any possible extensions
                int dotIdx = ver.Length;
                while ((dotIdx = ver.Slice(0, dotIdx).LastIndexOf('.')) >= 0) {
                    var vpart = ver.Slice(dotIdx + 1);
                    if (!int.TryParse(vpart, out _)) {
                        ver = ver.Slice(0, dotIdx);
                        dotIdx = ver.Length;
                    } else {
                        break;
                    }
                }

                if (!Version.TryParse(ver, out var version)) {
                    Console.Error.WriteLine($"{filename}: Invalid version '{ver}'");
                    continue;
                }

                orBuilder.Add(new MatchesTfmExpr(kind, op, version));
            }
        }

        if (orBuilder.Count == 0) {
            continue;
        } else if (orBuilder.Count == 1) {
            andBuilder.Add(orBuilder[0]);
        } else {
            andBuilder.Add(new OrTfmExpr(orBuilder.ToImmutable().Sort(CompareTfmExpr)));
        }
    }

    if (andBuilder.Count == 0) {
        return null;
    } else if (andBuilder.Count == 1) {
        return andBuilder[0];
    } else {
        return new AndTfmExpr(andBuilder.ToImmutable().Sort(CompareTfmExpr));
    }
}

static int CompareTfmExpr(TfmExpr a, TfmExpr b) {
    var res = DoOneWay(a, b);
    if (res != 0) {
        return res;
    }
    res = DoOneWay(b, a);
    return -res;

    static int DoOneWay(TfmExpr a, TfmExpr b) {
        if (a is OrTfmExpr or AndTfmExpr && b is not OrTfmExpr and not AndTfmExpr) {
            return -1;
        }
        if (a is OrTfmExpr && b is AndTfmExpr) {
            return -1;
        }
        if (a is IsKindTfmExpr isA && b is IsKindTfmExpr isB) {
            return isA.Kind.CompareTo(isB.Kind);
        }
        if (a is MatchesTfmExpr mA && b is MatchesTfmExpr mB) {
            var res = mA.Kind.CompareTo(mB.Kind);
            if (res != 0)
                return res;
            res = mA.Operation.CompareTo(mB.Operation);
            if (res != 0)
                return res;
            return mA.Version.CompareTo(mB.Version);
        }
        if (a is IsKindTfmExpr mulA && b is MatchesTfmExpr mulB) {
            var res = mulA.Kind.CompareTo(mulB.Kind);
            if (res != 0)
                return res;
            return -1;
        }

        return 0;
    }
}

static TfmKind? ParseTfmKind(string filename, ReadOnlySpan<char> text) {
    if (text.SequenceEqual("fx")) {
        return TfmKind.Framework;
    } else if (text.SequenceEqual("core")) {
        return TfmKind.Core;
    } else if (text.SequenceEqual("std")) {
        return TfmKind.Standard;
    } else {
        Console.Error.WriteLine($"{filename}: Malformed TFM kind (must be one of 'fx', 'std', or 'core')");
        return null;
    }
}

abstract record TfmExpr() {
    public abstract bool Matches(TfmKind kind, Version version);
}
record OrTfmExpr(ImmutableArray<TfmExpr> Exprs) : TfmExpr() {
    public override bool Matches(TfmKind kind, Version version) {
        foreach (var expr in Exprs) {
            if (expr.Matches(kind, version))
                return true;
        }
        return false;
    }
}
record AndTfmExpr(ImmutableArray<TfmExpr> Exprs) : TfmExpr() {
    public override bool Matches(TfmKind kind, Version version) {
        foreach (var expr in Exprs) {
            if (!expr.Matches(kind, version))
                return false;
        }
        return true;
    }
}

enum TfmKind {
    Framework,
    Standard,
    Core
}

enum Operation {
    Eq, Neq, Gt, Gte, Lt, Lte
}

record IsKindTfmExpr(TfmKind Kind) : TfmExpr() {
    public override bool Matches(TfmKind kind, Version version) {
        return kind == Kind;
    }
}
record MatchesTfmExpr(TfmKind Kind, Operation Operation, Version Version) : TfmExpr() {
    public override bool Matches(TfmKind kind, Version version) {
        return kind == Kind && Operation switch {
            Operation.Eq => Version == version,
            Operation.Neq => Version != version,
            Operation.Gt => version > Version,
            Operation.Gte => version >= Version,
            Operation.Lt => version < Version,
            Operation.Lte => version <= Version,
            _ => throw new InvalidOperationException()
        };
    }
}