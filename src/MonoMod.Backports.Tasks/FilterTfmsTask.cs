#nullable enable

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace System.Runtime.CompilerServices {
    internal static class IsExternalInit {
    }
}

namespace MonoMod.Backports.Tasks {
    public class FilterTfmsTask : Task {
        [Required]
        public ITaskItem[] Items { get; set; } = Array.Empty<ITaskItem>();
        [Required]
        public string TargetFrameworkKind { get; set; } = "";
        [Required]
        public string TargetFrameworkVersion { get; set; } = "";

        [Output]
        public ITaskItem[] Filtered { get; set; } = Array.Empty<ITaskItem>();

        public override bool Execute() {
            var tfmKindString = TargetFrameworkKind;
            var tfmVerString = TargetFrameworkVersion;

            TfmKind tfmKind;
            if (tfmKindString == ".NETFramework") {
                tfmKind = TfmKind.Framework;
            } else if (tfmKindString == ".NETStandard") {
                tfmKind = TfmKind.Standard;
            } else if (tfmKindString == ".NETCoreApp") {
                tfmKind = TfmKind.Core;
            } else {
                Log.LogError($"Unknown target framework kind '{tfmKindString}'");
                return false;
            }

            try {
                var tfmVersion = new Version(tfmVerString);

                var filtered = new List<ITaskItem>();
                foreach (var item in Items) {
                    if (Path.IsPathRooted(item.ItemSpec)) {
                        filtered.Add(item);
                        continue;
                    }

                    var expr = GetExprFor(item.ItemSpec);

                    if (expr is null) {
                        filtered.Add(item);
                        continue;
                    }

                    if (expr.Matches(tfmKind, tfmVersion))
                        filtered.Add(item);
                }

                Filtered = filtered.ToArray();
            } catch (Exception e) {
                Log.LogErrorFromException(e);
                return false;
            }

            return !Log.HasLoggedErrors;
        }

        // TODO: basename processing, to be able to do `otherwise`
        TfmExpr? GetExprFor(string filename) {
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

                    var opStr = part.Substring(0, usIdx);
                    var rest = part.Substring(usIdx + 1);

                    if (opStr == "is") {
                        usIdx = rest.IndexOf('_');
                        if (usIdx >= 0) {
                            LogError(filename, "Malformed 'is' expression");
                            continue;
                        }
                        usIdx = rest.IndexOf('.');
                        if (usIdx >= 0) {
                            // remove extension
                            rest = rest.Substring(0, usIdx);
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
                            LogError(filename, $"Unknown operation '{opStr}'");
                            continue;
                        }

                        usIdx = rest.IndexOf('_');
                        if (usIdx < 0) {
                            LogError(filename, "Malformed comparison expression");
                            continue;
                        }

                        if (ParseTfmKind(filename, rest.Substring(0, usIdx)) is not { } kind)
                            continue;

                        var ver = rest.Substring(usIdx + 1);

                        // remote any possible extensions
                        var dotIdx = ver.Length;
                        while ((dotIdx = ver.Substring(0, dotIdx).LastIndexOf('.')) >= 0) {
                            var vpart = ver.Substring(dotIdx + 1);
                            if (!int.TryParse(vpart, out _)) {
                                ver = ver.Substring(0, dotIdx);
                                dotIdx = ver.Length;
                            } else {
                                break;
                            }
                        }

                        if (!Version.TryParse(ver.ToString(), out var version)) {
                            LogError(filename, $"Invalid version '{ver}'");
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

        TfmKind? ParseTfmKind(string filename, string text) {
            if (text == "fx") {
                return TfmKind.Framework;
            } else if (text == "core") {
                return TfmKind.Core;
            } else if (text == "std") {
                return TfmKind.Standard;
            } else {
                LogError(filename, "Malformed TFM kind (must be one of 'fx', 'std', or 'core')");
                return null;
            }
        }

        private void LogError(string filename, string message) {
            Log.LogError(null, null, null, filename, 0, 0, 0, 0,
                message);
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
    }
}
