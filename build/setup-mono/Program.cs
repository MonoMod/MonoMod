using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.IO.Compression;
using System.Runtime.CompilerServices;

if (args is not [{ } matrixJson, { } githubOutputFile, { } githubEnvFile, { } runnerOsName])
{
    await StdErr.WriteLineAsync("takes 4 arguments: matrixJson, GITHUB_OUTPUT, GITHUB_ENV, runner.os");
    return 1;
}

githubOutputFile = Path.GetFullPath(githubOutputFile);
githubEnvFile = Path.GetFullPath(githubEnvFile);

var jobInfo = FromJson(matrixJson, new
{
    arch = "",
    dotnet = new
    {
        isMono = false,
        systemMono = false,
        tfm = "",
        netMonoPkgSrc = (string?)null,
        netMonoPkgName = (string?)null,
        netMonoPkgVer = (string?)null,
        monoLibPath = (string?)null,
        monoDllPath = (string?)null,
    }
});

if (jobInfo is null)
{
    await StdErr.WriteLineAsync("Job info was null");
    return 1;
}

if (!jobInfo.dotnet.isMono)
{
    await StdOut.WriteLineAsync("Nothing needs to be done, job is not a Mono job");
    return 0;
}

var ScriptRoot = GetScriptRoot();
var RepoRoot = Path.GetFullPath(Path.Combine(ScriptRoot, "..", ".."));

// resolve runner_tfm
var tfm = jobInfo.dotnet.tfm;
var ntfm = NuGetFramework.Parse(tfm);
var resolvedRunnerTfm = NuGetFrameworkUtility.GetNearest([
    // note: these are the TFMs in the /tools/ folder of xunit.runner.console that we use
    // https://nuget.info/packages/xunit.runner.console/2.4.2
    "net452",
    "net46",
    "net461",
    "net462",
    "net47",
    "net471",
    "net472",
    "netcoreapp1.0",
    "netcoreapp2.0",
], ntfm, NuGetFramework.Parse);
// write out the target framework
await StdOut.WriteLineAsync($"runner_tfm={resolvedRunnerTfm}");
await File.AppendAllLinesAsync(githubOutputFile, [
    $"runner_tfm={resolvedRunnerTfm}"
]);

if (jobInfo.dotnet.systemMono)
{
    if (!TryWhich("mono", out var sysMonoPath))
    {
        await StdErr.WriteLineAsync("Job is for system Mono, but could not find Mono on PATH");
        return 1;
    }

    await StdOut.WriteLineAsync($"Job is for system Mono; using mono={sysMonoPath}");
    await File.AppendAllLinesAsync(githubOutputFile, [
        "use_mdh=false",
        $"mono_dll={sysMonoPath}",
    ]);
    return 0;
}

var pkgSrc = jobInfo.dotnet.netMonoPkgSrc;
var pkgName = jobInfo.dotnet.netMonoPkgName;
var pkgVer = jobInfo.dotnet.netMonoPkgVer;
var libPath = jobInfo.dotnet.monoLibPath;
var dllPath = jobInfo.dotnet.monoDllPath;

if (pkgSrc is null || pkgName is null || pkgVer is null || libPath is null || dllPath is null)
{
    await StdErr.WriteLineAsync("Job info is missing some required properties");
    return 1;
}

var monoDir = Path.Combine(RepoRoot, ".mono");
var pkgDir = Path.Combine(monoDir, "pkg");
Directory.CreateDirectory(monoDir);
Directory.CreateDirectory(pkgDir);

// lets grab mdh
var mdhDir = Path.Combine(monoDir, "mdh");
var mdhExe = Path.Combine(mdhDir, "mdh");
{
    var mdhZip = Path.Combine(monoDir, "mdh.zip");

    var archName = jobInfo.arch;
    var osName = runnerOsName.ToLowerInvariant();

    // fix up name for download
    archName = archName switch
    {
        "x64" => "x86_64",
        "arm64" => "aarch64",
        var x => x,
    };
    if (osName is "linux") osName = "linux-gnu.2.10";

    // download the zip
    var url = $"https://github.com/nike4613/mono-dynamic-host/releases/latest/download/{archName}-{osName}.zip";
    using (var file = File.Create(mdhZip))
    {
        using var stream = await FetchStreamAsync(url);
        await stream.CopyToAsync(file);
    }

    // delete the existing extract if it exists
    if (Directory.Exists(mdhDir))
    {
        Directory.Delete(mdhDir, true);
    }
    // extract the archive
    await ZipFile.ExtractToDirectoryAsync(mdhZip, mdhDir);

    // select Windows executable if it exists
    if (File.Exists(mdhExe + ".exe"))
    {
        mdhExe += ".exe";
    }
    
    // mark it as executable on non-windows
    if (!Env.IsWindows)
    {
        await Run($"chmod +x {mdhExe}");
    }
}

// load NuGet stuff
var nugetSettings = Settings.LoadDefaultSettings(RepoRoot);
var packageSourceProvider = new PackageSourceProvider(nugetSettings);
var packageManager = new NuGetPackageManager(new CachingSourceProvider(packageSourceProvider), nugetSettings, pkgDir);
var resolutionContext = new ResolutionContext();

// now lets try to grab the Mono package
{
    var packageSource = packageSourceProvider.GetPackageSourceByName(pkgSrc);
    if (packageSource is null)
    {
        await StdErr.WriteLineAsync($"There is no package source with name '{pkgSrc}'");
        return 1;
    }

    var dir = Path.Combine(pkgDir, pkgName + "." + pkgVer);

    var packageRepo = Repository.Factory.GetCoreV3(packageSource);
    var pkgByIdResource = await packageRepo.GetResourceAsync<FindPackageByIdResource>();

    var nupkg = Path.Combine(pkgDir, pkgName + ".nupkg");
    using (var file = File.Create(nupkg))
    {
        using var cacheCtx = new SourceCacheContext();
        var result = await pkgByIdResource.CopyNupkgToStreamAsync(
            pkgName, NuGetVersion.Parse(pkgVer), 
            file, cacheCtx,
            NullLogger.Instance, default);

        if (!result)
        {
            await StdErr.WriteLineAsync($"Could not download {pkgName},{pkgVer}");
            return 1;
        }
    }

    using (var file = File.OpenRead(nupkg))
    {
        await PackageExtractor.ExtractPackageAsync(packageSource.Source,
            file,
            new PackagePathResolver(pkgDir, true),
            new PackageExtractionContext(
                PackageSaveMode.Files, XmlDocFileSaveMode.Skip, 
                ClientPolicyContext.GetClientPolicy(nugetSettings, NullLogger.Instance), 
                NullLogger.Instance),
            default);
    }

    var fullLibPath = Path.GetFullPath(Path.Combine(dir, libPath));
    var fullDllPath = Path.GetFullPath(Path.Combine(dir, dllPath));

    await StdOut.WriteLineAsync("Job is for .NET Mono");
    await StdOut.WriteLineAsync($"mdh={mdhExe}");
    await StdOut.WriteLineAsync($"mono_dll={fullDllPath}");
    await StdOut.WriteLineAsync($"MONO_PATH={fullLibPath}");

    await File.AppendAllLinesAsync(githubOutputFile, [
        "use_mdh=true",
        $"mdh={mdhExe}",
        $"mono_dll={fullDllPath}",
    ]);
    await File.AppendAllLinesAsync(githubEnvFile, [
        $"MONO_PATH={fullLibPath}",
    ]);
}

return 0;

static string GetScriptRoot([CallerFilePath] string path = "") => Path.GetDirectoryName(path) ?? ".";