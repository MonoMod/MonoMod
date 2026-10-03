using GenTestMatrix;
using GenTestMatrix.Models;

if (args is not [{ } owner, { } githubOutputFile, ..var matrixOutNames] || matrixOutNames.Length < 1)
{
    await StdErr.WriteLineAsync("Takes 3+ arguments: ${{ github.repository_owner }}, GITHUB_OUTPUT, matrix output names");
    return 1;
}

// get container dockerfile hashes
using var hasher = System.Security.Cryptography.SHA256.Create();
var containers = new Dictionary<string, string>();
foreach (var dockerfile in Directory.EnumerateFiles("./build/containers", "*.dockerfile", SearchOption.TopDirectoryOnly))
{
    var name = Path.GetFileNameWithoutExtension(dockerfile);
    using var fs = File.OpenRead(dockerfile);
    var hash = Convert.ToHexString(await hasher.ComputeHashAsync(fs));
    containers.Add(name, $"{name}-{hash}");
}

var containerNameBase = $"ghcr.io/{owner.ToLowerInvariant()}/monomod-tester:";

// build jobs
await using var jobs = new JobsWriter(File.Open(githubOutputFile, FileMode.Append, FileAccess.Write), matrixOutNames);

async Task EmitJobsForOsArch(OS os, Arch arch, Emulator? emu)
{
    var rid = $"{os.RidName}-{arch.RidName}";
    var container = os.UseContainer && containers.TryGetValue(rid, out var ctag) ? containerNameBase + ctag : null;

    foreach (var dotnet in Dotnet.Versions)
    {
        if (!dotnet.Enabled) continue;

        // skip frameworks if the OS doesn't support framework
        if (dotnet.IsFramework && !os.HasFramework) continue;

        // skip runtime if it doesn't support the current RID
        if (!dotnet.RIDs.Contains(rid)) continue;

        var archName = arch.RidName;
        var osName = os.Name;
        var runtimeName = dotnet.Name;

        if (emu is not null)
        {
            archName += $" ({emu.Name})";
        }

        var jobDotnet = dotnet with { MonoPackageSource = null, MonoPackageVersion = null }; // make sure we don't accidentally serialize these for non-Mono jobs
        if (dotnet.HasPGO)
        {
            // this runtime supports PGO, generate 2 jobs: one with it enabled, and one without
            jobs.AddJob(new()
            {
                Title = $"{runtimeName} {archName} (PGO Off)",
                OS = os,
                Dotnet = jobDotnet,
                Arch = arch.RidName,
                Emulator = emu,
                Container = container,
                UsePGO = false,
            }, osName, runtimeName);
            jobs.AddJob(new()
            {
                Title = $"{runtimeName} {archName} (PGO Off)",
                OS = os,
                Dotnet = jobDotnet,
                Arch = arch.RidName,
                Emulator = emu,
                Container = container,
                UsePGO = true,
            }, osName, runtimeName);
        }
        else
        {
            // this runtime doesn't support PGO, only add the one job
            jobs.AddJob(new()
            {
                Title = $"{runtimeName} {archName}",
                OS = os,
                Dotnet = dotnet,
                Arch = arch.RidName,
                Emulator = emu,
                Container = container,
            }, osName, runtimeName);
        }

        // if this OS specifies a .NET Mono package, add a job for it
        if (dotnet is { MonoPackageSource: not null, MonoPackageVersion: not null } && false) // TODO: We currently have a lot of problems on .NET Mono, they need to be fixed
        {
            var fillDict = new Dictionary<string, string>()
            {
                [Constants.Tmpl.RID] = rid,
                [Constants.Tmpl.TFM] = dotnet.TFM,
                [Constants.Tmpl.DllPre] = os.DllPrefix,
                [Constants.Tmpl.DllPost] = os.DllSuffix,
            };

            var packageName = Template.Fill(Constants.Mono.Package.NameTmpl, fillDict);
            var libPath = Template.Fill(Constants.Mono.Package.LibPathTmpl, fillDict);
            var dllPath = Template.Fill(Constants.Mono.Package.DllPathTmpl, fillDict);

            var monoDotnet = dotnet with
            {
                Name = $".NET Mono {dotnet.Sdk}",
                Sdk = null,
                Id = $"netmono{dotnet.MonoPackageVersion}",
                HasPGO = false,
                IsMono = true,
                NeedsRestore = true, // Mono always NeedsRestore
                MonoPackageName = packageName,
                MonoLibPath = libPath,
                MonoDllPath = dllPath,
            };

            runtimeName = monoDotnet.Name;

            jobs.AddJob(new()
            {
                Title = $"{runtimeName} {archName}",
                OS = os,
                Arch = arch.RidName,
                Dotnet = monoDotnet,
                Emulator = emu,
                Container = container,
            }, osName, runtimeName);
        }
    }

    // TODO: Unity Mono
}

foreach (var os in OS.OperatingSystems)
{
    if (!os.Enabled) continue;

    if (os.HasSystemMono && os.Arch.Any(a => a.IsRunnerArch && a.Enabled))
    {
        // this OS has a system Mono, emit a job for that
        var rid = os.Arch.First(a => a.IsRunnerArch).RidName;
        var container = os.UseContainer && containers.TryGetValue($"{os.RidName}-{rid}", out var ctag) ? containerNameBase + ctag : null;

        var osName = os.Name;
        var archName = "System";
        var runtimeName = "Mono";

        jobs.AddJob(new()
        {
            Title = $"{runtimeName} {archName}",
            OS = os,
            Arch = rid,
            Dotnet = new()
            {
                Name = "Mono",
                Id = "sysmono",
                NeedsRestore = true, // Monos always need restore
                IsMono = true,
                IsSystemMono = true,
                TFM = Constants.Mono.NonCoreTFM,
            },
            Container = container,
        }, osName, runtimeName);
    }

    foreach (var arch in os.Arch)
    {
        if (!arch.Enabled) continue;

        // emit the compatible jobs with no emulator
        await EmitJobsForOsArch(os, arch, null);
    }

    // then for all compatible emulators
    foreach (var emu in Emulator.Emulators)
    {
        if (!emu.Enabled) continue;

        if (!emu.RequiresRunner.IsDefault)
        {
            // the emulator is limited to some specific set of runners, filter to that
            if (!emu.RequiresRunner.Contains(os.Runner))
            {
                continue;
            }
        }

        if (!emu.SupportedOsRids.Contains(os.RidName))
        {
            continue;
        }

        if (!os.Arch.Any(a => a.Enabled && a.RidName == emu.HostArch))
        {
            // os doesn't expose any archs which the emulator can run on as a host
            continue;
        }

        // this emulator is usable for this host, generate jobs for it
        foreach (var emuArch in emu.EmuArch)
        {
            var arch = new Arch()
            {
                RidName = emuArch,
                UnityName = null,
                IsRunnerArch = false,
            };
            await EmitJobsForOsArch(os, arch, emu);
        }
    }
}

await jobs.Write();

return 0;
