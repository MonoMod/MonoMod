using System;
using Mono.Cecil;

if (args.Length < 2) {
    Console.Error.WriteLine("Usage: MonoMod.ILHelpers.Patcher <assembly> <new version string>");
    return 1;
}

var assemblyPath = args[0];
var verString = args[1];

using var module = ModuleDefinition.ReadModule(assemblyPath, new(ReadingMode.Deferred) {
    ReadWrite = true
});
if (module.RuntimeVersion == verString) {
    Console.WriteLine("Version already matches");
    return 0;
}

module.RuntimeVersion = verString;
module.Write();

Console.WriteLine("Version updated");

return 0;
