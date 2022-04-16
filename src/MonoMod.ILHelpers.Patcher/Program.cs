using System;
using Mono.Cecil;

if (args.Length < 2) {
    Console.Error.WriteLine("Usage: MonoMod.ILHelpers.Patcher <assembly> <new version string> [output]");
    return 1;
}

var assemblyPath = args[0];
var verString = args[1];
var output = args.Length > 2 ? args[2] : null;

using var module = ModuleDefinition.ReadModule(assemblyPath, new(ReadingMode.Deferred) {
    ReadWrite = true
});
if (module.RuntimeVersion == verString && output is null) {
    Console.WriteLine("Version already matches");
    return 0;
}

module.RuntimeVersion = verString;
if (output is not null) {
    module.Write(output);
} else {
    module.Write();
}

Console.WriteLine("Updated version written");

return 0;
