namespace GenTestMatrix.Models;

internal sealed record Job
{
    public required string Title { get; init; }
    public required OS OS { get; init; }
    public required Dotnet Dotnet { get; init; }
    public required string Arch { get; init; }
    public Emulator? Emulator { get; init; }
    public string? Container { get; init; }
    public bool? UsePGO { get; init; }
}

internal sealed record JobGroup
{
    public required string Title { get; init; }
    public required MatrixResult<Job> Matrix { get; init; }
}

internal sealed record JobBatch
{
    public required string Title { get; init; }
    public required MatrixResult<JobGroup> Matrix { get; init; }
}