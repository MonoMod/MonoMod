# Debugging (With) MonoMod

TODO: fill with tips/tricks for debugging

## Handy Environment Variables

### MonoMod

#### Logging

- `MONOMOD_LogRecordHoles` - Always capture the holes of log messages. Mostly useful with the logger's replay queue.
- `MONOMOD_LogInMemory` - Saves log messages to an in-memory buffer, which can be read from a native debugger. A script
  to read this buffer in WinDbg on FX/CoreCLR is available in [`tools/windbg-memlog.js`](../tools/windbg-memlog.js).
  (At the time of writing, that script assumes a 64-bit process. Change line 57 to only add 8 for 32-bit.)
- `MONOMOD_LogSpam` - Forces 'default' (file and memory) log sinks to log `Spam` level messages.
- `MONOMOD_LogReplayQueueLength` - Sets the length (in log messages) of the replay queue. If this is non-zero, the replay
  queue is enabled, and all entries in the replay queue will be replayed to new handlers when they subscribe.
- `MONOMOD_LogToFile` - Enables a log sink which writes to the file specified in this environment variable. If the value is
  `-`, then it writes to standard out.
- `MONOMOD_LogToFileFilter` - Sets a list of message sources to restrict the log file to. This should contain a comma or
  semicolon separated list of source names. Within MonoMod, these are the names of the assemblies (`MonoMod.Utils`,
  `MonoMod.Core`, etc).

#### DynamicMethodDefinition

- `MONOMOD_DMDType` - Sets the backend to use for `DynamicMethodDefinition`s. It may be the full type name of any generator,
  or one of the following (case insensitive):
  - `dynamicmethod` or `dm` - Selects the `DynamicMethod`-based generator. Typically, this will be the default. However, when
    MonoMod detects that System.Reflection.Emit is stubbed out, the default will be the Cecil generator instead.
  - `cecil` or `md` - Selects the Cecil-based generator. This will use Cecil to dynamically generate assemblies which will be
    loaded as byte arrays. This generator causes a significant amount of metadata bloat due to needing to load a new module
    for each DMD. It is also the most portable.
  - `methodbuilder` or `mb` - .NET Framework only. Selects the `MethodBuilder`-based generator. This uses System.Reflection.Emit's
    `Type`- and `MethodBuilder` to perform much the same role as the Cecil generator.
- `MONOMOD_DMDDebug` - Sets the `Debug` flag on `DynamicMethodDefinition`s by default. This enables the emitting of debug
  if supported by the backend, as well as affecting the default selection of backends. On .NET Framework, this will cause
  the `MethodBuilder` backend to be selected, and on other runtimes, the Cecil backend. Note that if a DMD contains `Fault` or
  `Filter` blocks, it will still use `MethodBuilder`, despite it not supporting them, when the `Debug` flag is set.
- `MONOMOD_DMDDumpTo` - Sets the path for DMD bodies to be dumped to disk. This is best used with `MONOMOD_DMDDebug`, as the
  `DynamicMethod` backend does not support dumps. The exact structure of the assemblies is up to the backend selected, but
  the assembly names always end with the name of the backend used.

### CoreCLR (and some .NET Framework)

Note that for recent versions of CoreCLR, the `COMPLUS_` prefix can be replaced with `DOTNET_`.

#### Crash Dumps

- `COMPLUS_DbgEnableMiniDump` - Set to 1 to enable mini crash dumps on crash, whether due to managed exception bubbling to the
  top of the stack, or due to some internal runtime failure.
- `COMPLUS_DbgMiniDumpName` - Sets the name of the crash dump file.
- `COMPLUS_DbgMiniDumpType` - Sets the kind of dump to create. A value of 4 indicates a full dump. Refer to the config definition
  in the CLR source for other values.
- `COMPLUS_CreateDumpDiagnostics=1`
- `COMPLUS_EnableDumpOnSigTerm` - Set to 1 to enable crash dumps on SIGTERM.

#### Stress Log

- `COMPLUS_LogEnable` - Set to 1 to enable the stress log.
- `COMPLUS_LogFacility`
- `COMPLUS_LogFacility2`
- `COMPLUS_LogLevel`

#### PGO Data

- `DOTNET_PGODataPath`
- `DOTNET_ReadPGOData`
- `DOTNET_WritePGOData`
- `DOTNET_TieredPGO`
