using System.Text.Json.Serialization;

namespace GenTestMatrix.Models;

internal sealed record Emulator : Enableable
{
    public required string Name { get; init; }
    public required string Id { get; init; }

    // NOTE: Most of these are actually semantically required, but we have to make them not for JSON serialization to be happy
    [JsonIgnore]
    public ImmutableArray<string> SupportedOsRids { get; init; }
    [JsonIgnore]
    public string HostArch { get; init; } = "";
    [JsonIgnore]
    public ImmutableArray<string> EmuArch { get; init; }

    // note: if not set, viable on any matching other rules
    [JsonIgnore]
    public ImmutableArray<string> RequiresRunner { get; init; }

    public static readonly ImmutableArray<Emulator> Emulators = [
        new()
        {
            Name = "Prism",
            Id = "prism",
            RequiresRunner = ["windows-11-arm"], // Prism only works on Win11 ARM
            SupportedOsRids = ["win"],
            HostArch = "arm64",
            EmuArch = ["x86", "x64"],
        },
        new()
        {
            Name = "Rosetta",
            Id = "rosetta",
            SupportedOsRids = ["osx"],
            HostArch = "arm64",
            EmuArch = ["x64"],

            Enabled = false, // disabled since Rosetta is really flaky for some reason
        },
        new()
        {
            Name = "FEX",
            Id = "fex",
            SupportedOsRids = ["linux"],
            HostArch = "arm64",
            EmuArch = ["x86", "x64"],

            Enabled = false, // TODO: enable once we can configure it on the test runners
        }
    ];
}
