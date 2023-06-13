using MonoMod.Packer.Driver;
using System.CommandLine;

var command = new RootCommand();

Packer.AddOptionsAndArguments(command);
command.SetHandler(Packer.Execute);

return await command.InvokeAsync(args).ConfigureAwait(false);