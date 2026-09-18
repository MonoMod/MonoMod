using System.Text.Json.Serialization;

namespace GenTestMatrix.Models;

internal sealed record Dotnet : Enableable
{
    public required string Name { get; init; }
    public string? Id { get; init; }
    public string? Sdk { get; init; }
    public required string TFM { get; init; }
    public bool NeedsRestore { get; init; }

    public bool IsFramework { get; init; }
    public bool IsMono { get; init; }
    [JsonPropertyName("systemMono")]
    public bool IsSystemMono { get; init; }


    [JsonPropertyName("pgo")]
    public bool HasPGO { get; init; }
    [JsonPropertyName("netMonoPkgVer")]
    public string? MonoPackageVersion { get; init; }
    [JsonPropertyName("netMonoPkgSrc")]
    public string? MonoPackageSource { get; init; }
    [JsonPropertyName("netMonoPkgName")]
    public string? MonoPackageName { get; init; }
    public string? MonoLibPath { get; init; }
    public string? MonoDllPath { get; init; }

    // NOTE: this is semantically required, but cannot be marked so for serialization
    [JsonIgnore]
    public ImmutableArray<string> RIDs { get; init; }

    public static readonly ImmutableArray<Dotnet> Versions = [
        new()
        {
            Name = ".NET Framework 4.x",
            Id = "fx",
            TFM = "net472",
            IsFramework = true,
            RIDs = ["win-x86", "win-x64", "win-arm64"]
        },
        new()
        {
            Name = ".NET Core 2.1",
            Sdk = "2.1",
            TFM = "netcoreapp2.1",
            NeedsRestore = true,
            RIDs = ["win-x86", "win-x64", "linux-x64", "osx-x64"]
        },
        new()
        {
            Name = ".NET Core 3.0",
            Sdk = "3.0",
            TFM = "netcoreapp3.0",
            RIDs = ["win-x86", "win-x64", "linux-x64", "osx-x64"]
        },
        new()
        {
            Name = ".NET Core 3.1",
            Sdk = "3.1",
            TFM = "netcoreapp3.1",
            RIDs = ["win-x86", "win-x64", "linux-x64", "osx-x64"]
        },
        new()
        {
            Name = ".NET 5.0",
            Sdk = "5.0",
            TFM = "net5.0",
            RIDs = ["win-x86", "win-x64", "linux-x64", "linux-musl-x64", "osx-x64"]
        },
        new()
        {
            Name = ".NET 6.0",
            Sdk = "6.0",
            TFM = "net6.0",
            HasPGO = true,
            MonoPackageSource = Constants.NuGetSource.NugetOrg,
            MonoPackageVersion = "6.0.31",

            RIDs = ["win-x86", "win-x64", "win-arm64", "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "osx-x64", "osx-arm64"]
        },
        new()
        {
            Name = ".NET 7.0",
            Sdk = "7.0",
            TFM = "net7.0",
            HasPGO = true,
            MonoPackageSource = Constants.NuGetSource.NugetOrg,
            MonoPackageVersion = "7.0.20",

            RIDs = ["win-x86", "win-x64", "win-arm64", "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "osx-x64", "osx-arm64"]
        },
        new()
        {
            Name = ".NET 8.0",
            Sdk = "8.0",
            TFM = "net8.0",
            HasPGO = true,
            MonoPackageSource = Constants.NuGetSource.NugetOrg,
            MonoPackageVersion = "8.0.6",

            RIDs = ["win-x86", "win-x64", "win-arm64", "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "osx-x64", "osx-arm64"]
        },
        new()
        {
            Name = ".NET 9.0",
            Sdk = "9.0",
            TFM = "net9.0",
            HasPGO = true,
            MonoPackageSource = Constants.NuGetSource.NugetOrg,
            MonoPackageVersion = "9.0.0",

            RIDs = ["win-x86", "win-x64", "win-arm64", "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "osx-x64", "osx-arm64"]
        },
        new()
        {
            Name = ".NET 10.0",
            Sdk = "10.0",
            TFM = "net10.0",
            HasPGO = true,
            MonoPackageSource = Constants.NuGetSource.NugetOrg,
            MonoPackageVersion = "10.0.0",

            RIDs = ["win-x86", "win-x64", "win-arm64", "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "osx-x64", "osx-arm64"]
        }
    ];
}
