# Debugging (With) MonoMod

TODO: fill with tips/tricks for debugging

## Handy Environment Variables

### MonoMod

All of the MonoMod environment variables are actually [Switches](Switches.md). Refer to that document for details.

#### Logging

- `MONOMOD_LogRecordHoles` (`boolean`) - Always capture the holes of log messages. Mostly useful with the logger's replay queue.
- `MONOMOD_LogInMemory` (`boolean`) - Saves log messages to an in-memory buffer, which can be read from a native debugger. A script
  to read this buffer in WinDbg on FX/CoreCLR is available in [`tools/windbg-memlog.js`](../tools/windbg-memlog.js).
  (At the time of writing, that script assumes a 64-bit process. Change line 57 to only add 8 for 32-bit.)
- `MONOMOD_LogSpam` (`boolean`) - Forces 'default' (file and memory) log sinks to log `Spam` level messages.
- `MONOMOD_LogReplayQueueLength` (`integer`) - Sets the length (in log messages) of the replay queue. If this is non-zero, the replay
  queue is enabled, and all entries in the replay queue will be replayed to new handlers when they subscribe.
- `MONOMOD_LogToFile` (`string`) - Enables a log sink which writes to the file specified in this environment variable. If the value is
  `-`, then it writes to standard out.
- `MONOMOD_LogToFileFilter` (`string`) - Sets a list of message sources to restrict the log file to. This should contain a comma or
  semicolon separated list of source names. Within MonoMod, these are the names of the assemblies (`MonoMod.Utils`,
  `MonoMod.Core`, etc).

### CoreCLR (and some .NET Framework)

Note that for recent versions of CoreCLR, the `COMPlus_` prefix can be replaced with `DOTNET_`.

#### Crash Dumps

- `COMPlus_DbgEnableMiniDump` - Set to 1 to enable mini crash dumps on crash, whether due to managed exception bubbling to the
  top of the stack, or due to some internal runtime failure.
- `COMPlus_DbgMiniDumpName` - Sets the name of the crash dump file.
- `COMPlus_DbgMiniDumpType` - Sets the kind of dump to create. A value of 4 indicates a full dump. Refer to the config definition
  in the CLR source for other values.
- `COMPlus_CreateDumpDiagnostics=1`
- `COMPlus_EnableDumpOnSigTerm` - Set to 1 to enable crash dumps on SIGTERM.

#### Stress Log

- `COMPlus_LogEnable` - Set to 1 to enable the stress log.
- `COMPlus_LogFacility`
- `COMPlus_LogFacility2`
- `COMPlus_LogLevel`

#### PGO Data

- `DOTNET_PGODataPath`
- `DOTNET_ReadPGOData`
- `DOTNET_WritePGOData`
- `DOTNET_TieredPGO`
