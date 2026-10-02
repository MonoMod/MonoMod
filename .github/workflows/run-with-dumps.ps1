param (
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Exe,
    [Parameter(Mandatory=$false, Position=1, ValueFromRemainingArguments=$True)]
    [string[]]$ExeArgs = @()
)

$ErrorActionPreference = 'Stop';

$dumpsPath = $env:DUMPS_PATH;
if ($null -eq $dumpsPath)
{
    Write-Error "DUMPS_PATH not set!";
}

# make sure the dir exists
New-Item -Type Directory $dumpsPath -Force | Out-Null;

if ($IsWindows)
{
    # on Windows, we need to configure some registry keys before invoking
    $key = "HKLM:\\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";
    New-Item -Path $key -ErrorAction SilentlyContinue;
    New-ItemProperty -Path $key -Name 'DumpType' -PropertyType 'DWord' -Value 2 -Force;
    New-ItemProperty -Path $key -Name 'DumpCount' -PropertyType 'DWord' -Value 10 -Force;
    New-ItemProperty -Path $key -Name 'DumpFolder' -PropertyType 'String' -Value $dumpsPath -Force;

    # then we can execute the program
    &$Exe @ExeArgs;
    exit $LastExitCode;
}
elseif ($IsLinux -or $IsMacOS)
{
    # We can't rely on the kernel for dumps (our Linux jobs run in containers, where we can't set core_pattern), so we run
    # the program under LLDB, which saves a dump when it crashes or hangs. See run_with_dumps.py for details.
    $script = Join-Path $PSScriptRoot '..' 'lldb' 'run_with_dumps.py';
    $env:RWD_COMMAND = ConvertTo-Json -Compress -InputObject @(@($Exe) + $ExeArgs);
    & lldb --version;
    & lldb -x -b -o "command script import '$script'";
    exit $LastExitCode;
}
else
{
    Write-Error "Unknown operating system; not proceeding"
}
