namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// Represents a module loaded into a particular process.
    /// </summary>
    /// <param name="BaseAddress">The base address of the module.</param>
    /// <param name="FileName">The path that defines the location of the module.</param>
    /// <param name="Size">The size, in bytes, of the module.</param>
    public readonly record struct LoadedModule(
        ulong? BaseAddress,
        string? FileName,
        ulong? Size
    );
}
