using System.Text.Json.Serialization;

namespace GenTestMatrix.Models;

internal sealed record OS : Enableable
{
    public required string Name { get; init; }
    public required string Runner { get; init; }

    [JsonIgnore]
    public bool UseContainer { get; init; }

    [JsonIgnore]
    public bool HasFramework { get; init; }
    [JsonIgnore]
    public bool HasSystemMono { get; init; }

    // NOTE: Most of these are actually semantically required, but we have to make them not for JSON serialization to be happy
    [JsonIgnore]
    public string RidName { get; init; } = "";
    [JsonIgnore]
    public string UnityDllName { get; init; } = "";
    [JsonIgnore]
    public string DllPrefix { get; init; } = "";
    [JsonIgnore]
    public string DllSuffix { get; init; } = "";

    [JsonIgnore]
    public ImmutableArray<Arch> Arch { get; init; } = [];

    public static readonly ImmutableArray<OS> OperatingSystems = [
        new()
        {
            Name = "Windows",
            Runner = "windows-latest",
            HasFramework = true,
            RidName = "win",
            UnityDllName = "mono-2.0-bdwgc",
            DllSuffix = ".dll",

            Arch = [
                new() { RidName = "x86", UnityName = "win32" },
                new() { RidName = "x64", UnityName = "win64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "Windows 11 ARM64",
            Runner = "windows-11-arm",
            HasFramework = true,
            RidName = "win",
            UnityDllName = "mono-2.0-bdwgc",
            DllSuffix = ".dll",

            Arch = [
                new() { RidName = "x86", UnityName = "win32" },
                new() { RidName = "x64", UnityName = "win64" },
                new() { RidName = "arm64", UnityName = "win_arm64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "Linux",
            Runner = "ubuntu-latest",
            UseContainer = true,
            HasSystemMono = true,
            RidName = "linux",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".so",

            Arch = [
                new() { RidName = "x64", UnityName = "linux64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "Linux musl",
            Runner = "ubuntu-latest",
            UseContainer = true,
            HasSystemMono = true,
            RidName = "linux-musl",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".so",

            Arch = [
                new() { RidName = "x64", UnityName = "linux64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "Linux ARM64",
            Runner = "ubuntu-24.04-arm",
            UseContainer = true,
            HasSystemMono = true,
            RidName = "linux",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".so",

            Arch = [
                new() { RidName = "arm64", UnityName = null, IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "Linux musl ARM64",
            Runner = "ubuntu-24.04-arm",
            UseContainer = true,
            HasSystemMono = true,
            RidName = "linux-musl",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".so",

            Enabled = false, // TODO: re-enable when PowerShell ships linux-musl-arm64 archives

            Arch = [
                new() { RidName = "arm64", UnityName = null, IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "MacOS 13",
            Runner = "macos-13",
            HasSystemMono = true,
            RidName = "osx",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".dylib",

            // GitHub has retired all MacOS 13-based images.
            Enabled = false,

            Arch = [
                new() { RidName = "x64", UnityName = "macos_x64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "MacOS 14",
            Runner = "macos-14",
            HasSystemMono = true,
            RidName = "osx",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".dylib",

            Arch = [
                new() { RidName = "x64", UnityName = "macos_x64" }, // note: this comes from Rosetta
                new() { RidName = "arm64", UnityName = "macos_arm64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "MacOS 15",
            Runner = "macos-15",
            HasSystemMono = true,
            RidName = "osx",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".dylib",

            Arch = [
                new() { RidName = "x64", UnityName = "macos_x64" }, // note: this comes from Rosetta
                new() { RidName = "arm64", UnityName = "macos_arm64", IsRunnerArch = true },
            ]
        },
        new()
        {
            Name = "MacOS 15 Intel",
            Runner = "macos-15-intel",
            HasSystemMono = true,
            RidName = "osx",
            UnityDllName = "monobdwgc-2.0", // TODO: is this correct?
            DllPrefix = "lib",
            DllSuffix = ".dylib",

            Arch = [
                new() { RidName = "x64", UnityName = "macos_x64", IsRunnerArch = true },
            ]
        }
    ];
}

internal sealed record Arch : Enableable
{
    public required string RidName { get; init; }
    public required string? UnityName { get; init; }
    public bool IsRunnerArch { get; init; }
}
