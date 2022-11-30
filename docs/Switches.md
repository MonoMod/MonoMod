# Switches

MonoMod uses switches to allow users to control its internal behaviour, to some degree. Broadly speaking, these
switches act much like [AppContext switches](https://learn.microsoft.com/en-us/dotnet/api/system.appcontext) (and
in fact MonoMod's switches check `AppContext`, when its available) to control internal functionality, mostly useful
for debugging.

These switches are exposed (and controlled) from the [`MonoMod.Switches`](../src/MonoMod.Utils/_/Switches.cs) type.
Users of MonoMod may use `Switches.SetSwitchValue` to change the value of a switch at runtime, and `ClearSwitchValue`
to fall back to `AppContext` instead of using its own internal dictionary. It is important to note, however, that
many of the switches are only checked once, very early on in MonoMod's existence, so changes to the switches may not
have any effect. All of the logging switches are like this. They are checked before any user code can call into
MonoMod, and so must be changed only through environment variables and `AppContext` before that point.

Each MonoMod switch has a corresponding `AppContext` switch, which is `MonoMod.<switch name>` (so `RunningOnWine`
would become `MonoMod.RunningOnWine`). The `AppContext` switch will only be checked if MonoMod's own dictionary
does not have a value for that switch.

During initialization, switch values are populated from environment variables. Any environment variables which begin
with `MONOMOD_` are parsed into switches whose name is the environment variable's with the `MONOMOD_` prefix removed
(so `MONOMOD_RunningOnWine` would become the switch `RunnningOnWine`). The values are parsed on a best-effort basis
into booleans or integers, as applicable. Otherwise, they are saved as strings.

The following (case insensitive) strings parse as booleans: `true`, `false`, `yes`, `no`, `y`, and `n`. Any switch
which expects a boolean can also take an integer, which becomes `true` if it is non-zero, and `false` if it is zero.

## Used Switches

Refer to the [`MonoMod.Switches`](../src/MonoMod.Utils/_/Switches.cs#L66) for up-to-date documentation on each switch.
Also refer to [Debugging](Debugging.md) for debugging related switches (listed as environment variables) not
discussed here.

### DynamicMethodDefinion related switches

All of these are checked each time they are needed. `DMDDebug` is checked by `DynamicMethodDefinition` when it is constructed,
and `DMDType` and `DMDDumpTo` when it is being generated.

- `DMDType` (`string`) - Sets the backend to use for `DynamicMethodDefinition`s. It may be the full type name of any
  generator, or one of the following (case insensitive):
  - `dynamicmethod` or `dm` - Selects the `DynamicMethod`-based generator. Typically, this will be the default.
    However, when MonoMod detects that System.Reflection.Emit is stubbed out, the default will be the Cecil generator
    instead.
  - `cecil` or `md` - Selects the Cecil-based generator. This will use Cecil to dynamically generate assemblies which
    will be loaded as byte arrays. This generator causes a significant amount of metadata bloat due to needing to
    load a new module for each DMD. It is also the most portable.
  - `methodbuilder` or `mb` - .NET Framework only. Selects the `MethodBuilder`-based generator. This uses `System.Reflection.Emit`'s
    `Type`- and `MethodBuilder` to perform much the same role as the Cecil generator.
- `DMDDebug` (`boolean`) - Sets the `Debug` flag on `DynamicMethodDefinition`s by default. This enables the emitting
  of debug information if supported by the backend, as well as affecting the default selection of backends. On .NET
  Framework, this will cause the `MethodBuilder` backend to be selected, and on other runtimes, the Cecil backend.
  Note that if a DMD contains `Fault` or `Filter` blocks, it will still use `MethodBuilder`, despite it not
  supporting them, when the `Debug` flag is set.
- `DMDDumpTo` (`string`) - Sets the path for DMD bodies to be dumped to disk. This is best used with `DMDDebug`, as
  the `DynamicMethod` backend does not support dumps. The exact structure of the assemblies is up to the backend
  selected, but the assembly names always end with the name of the backend used.

### Miscellaneous switches

- `RunningOnWine` (`boolean`) - Forces `PlatformDetection` to detect Wine, when it detects Windows. This is only
  checked once, during the initial operating system detection.
