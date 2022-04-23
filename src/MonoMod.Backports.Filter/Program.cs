using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

if (args.Length < 1) {
    Console.Error.WriteLine("Usage: filter <filter|gen> <args>...");
    return 1;
}

var cmd = args[0];
if (cmd == "filter") {
    return FilterForTfm(args);
} else if (cmd == "gen") {

}

Console.Error.WriteLine($"Unknown command '{cmd}'");
return -1;

static int FilterForTfm(string[] args) {
    if (args.Length < 5) {
        Console.Error.WriteLine("Usafe: Filter filter <file list txt> <tfm type> <tfm version> <remove list txt (out)>");
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
            andBuilder.Add(new OrTfmExpr(orBuilder.ToImmutable()));
        }
    }

    if (andBuilder.Count == 0) {
        return null;
    } else if (andBuilder.Count == 1) {
        return andBuilder[0];
    } else {
        return new AndTfmExpr(andBuilder.ToImmutable());
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