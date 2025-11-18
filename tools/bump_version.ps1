#Requires -Version 7

param (
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string] $BumpVersion = 'Patch',

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $Projects = @("MonoMod.Core", "MonoMod.Utils", "MonoMod.RuntimeDetour")
)

$ErrorActionPreference = 'Stop';
Set-StrictMode -Version 3;

$VersionIndex = 0;
if ($BumpVersion -eq 'Major')
{
    $VersionIndex = 0;
}
elseif ($BumpVersion -eq 'Minor')
{
    $VersionIndex = 1;
}
elseif ($BumpVersion -eq 'Patch')
{
    $VersionIndex = 2;
}

Push-Location $PSScriptRoot/..;
try
{
    $srcRoot = Join-Path $PSScriptRoot .. | Resolve-Path;
    foreach ($project in $Projects)
    {
        $props = Join-Path $srcRoot src $project "Version.props";

        # load the project up as a normal XML file
        $xml = [xml]::new();
        $xml.PreserveWhitespace = $true;
        $xml.Load($props);

        $version = $xml.Project.PropertyGroup.VersionPrefix;
        if ($VersionIndex -eq 0)
        {
            $xml.Project.PropertyGroup.PackageValidationBaselineVersion = "";
        }
        else
        {
            $xml.Project.PropertyGroup.PackageValidationBaselineVersion = $version;
        }

        $verParts = $version -csplit '\.',3;
        $verParts[$VersionIndex] = [string](([int]$verParts[$VersionIndex]) + 1);
        for ($i = $VersionIndex + 1; $i -lt $verParts.Length; $i++)
        {
            $verParts[$i] = '0';
        }
        $version = $verParts -join '.';
        $xml.Project.PropertyGroup.VersionPrefix = $version

        Write-Host "($project) New version: $version";

        # and write the project file back out
        $xml.Save($props);

        # now delete the compatability suppresisons file, if it exists, so that it can be regenerated (or not) properly
        $compatFile = Join-Path $srcRoot src $project "CompatibilitySuppressions.xml";
        if (Test-Path -Type Leaf $compatFile)
        {
            Remove-Item $compatFile;
        }
    }
}
finally
{
    Pop-Location;
}