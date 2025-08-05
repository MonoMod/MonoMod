param (
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Exe,
    [Parameter(Mandatory=$false, Position=1, ValueFromRemainingArguments=$True)]
    [string[]]$Args = @()
)

$ErrorActionPreference = 'Stop';

$workspace = $env:WORKSPACE;
if ($null -eq $workspace)
{
    Write-Error "WORKSPACE not set!";
}

$dumpsPath = $env:DUMPS_PATH;
if ($null -eq $dumpsPath)
{
    Write-Error "DUMPS_PATH not set!";
}

# make sure the dir exists
New-Item -Type Directory $dumpsPath -Force | Out-Null;
$lldbHelpers = Join-Path $workspace '.github' 'lldb';

if ($IsWindows)
{
    # on Windows, we need to configure some registry keys before invoking
    $key = "HKLM:\\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";
    New-Item -Path $key -ErrorAction SilentlyContinue;
    New-ItemProperty -Path $key -Name 'DumpType' -PropertyType 'DWord' -Value 2 -Force;
    New-ItemProperty -Path $key -Name 'DumpCount' -PropertyType 'DWord' -Value 10 -Force;
    New-ItemProperty -Path $key -Name 'DumpFolder' -PropertyType 'String' -Value $dumpsPath -Force;

    # then we can execute the program
    &$Exe @Args;
    exit $LastExitCode;
}
elseif ($IsLinux -or $IsMacOS)
{
    # on Linux, we need to set the core_pattern and run the app with a ulimit -c unlimited
    Write-Output ($Args -join "`n") | bash -c @"
set -eo pipefail;
ulimit -c unlimited;
ulimit -t 600; # hard-limit the program to take no more than 10 minutes (nothing we will use this for needs anywhere near that much; any more is a problem)
set +e;
# on MacOS, SIGXCPU doesn't coredump by default. Thus, we use LLDB unattended to perform the dump 
# because we run our Linux stuff in containers, we can't set the core_pattern. Thus, we'll do the same thing we *must* do on MacOS and use LLDB to generate dumps when crashing
xargs lldb -x -b -O "command alias sdmp process save-core -p minidump -s full -- '$(Join-Path $dumpsPath 'dump_crash.core')'"\
    -s "$(Join-Path $lldbHelpers 'setup.lldb')" \
    -K "$(Join-Path $lldbHelpers 'crash.lldb')" \
    -s "$(Join-Path $lldbHelpers 'teardown.lldb')" -- "$Exe";
exit `$?;
"@;
    exit $LastExitCode;

}
else
{
    Write-Error "Unknown operating system; not proceeding"
}
