using Mono.Cecil;
using System;

[assembly: CLSCompliant(false)]

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MonoMod.ILHelpers.Patcher <assembly> <new version string> [output]");
    return 1;
}

var assemblyPath = args[0];
var verString = args[1];
var output = args.Length > 2 ? args[2] : null;

using var module = ModuleDefinition.ReadModule(assemblyPath, new(ReadingMode.Deferred)
{
    ReadWrite = true,
    ReadSymbols = true,
});
if (module.RuntimeVersion == verString && output is null)
{
    Console.WriteLine("Version already matches");
    return 0;
}

var writerParams = new WriterParameters()
{
    DeterministicMvid = true,
    WriteSymbols = true,
    Timestamp = null,
};

module.RuntimeVersion = verString;
if (output is not null)
{
    module.Write(output, writerParams);
}
else
{
    module.Write(writerParams);
}

return 0;
