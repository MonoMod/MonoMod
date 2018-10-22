# Make the assembly version match the build number.
$AssemblyInfoCommonPath = [io.path]::combine('MonoMod', 'Properties', 'AssemblyInfo.Common.cs')
(Get-Content $AssemblyInfoCommonPath) -replace '(?<=\[assembly: AssemblyVersion\(")[^"]*', $BUILD_BUILDNUMBER | Set-Content $AssemblyInfoCommonPath
