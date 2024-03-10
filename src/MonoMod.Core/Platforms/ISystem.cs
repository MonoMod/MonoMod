using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// Represents a host operating system.
    /// </summary>
    public interface ISystem
    {
        /// <summary>
        /// Gets the <see cref="OSKind"/> which this instance represents.
        /// </summary>
        OSKind Target { get; }
        /// <summary>
        /// Gets the set of <see cref="SystemFeature"/>s that this instance supports. Some members may only be available with certain feature flags set.
        /// </summary>
        SystemFeature Features { get; }

        /// <summary>
        /// Gets the default ABI for this OS, if there is one.
        /// </summary>
        Abi? DefaultAbi { get; }

        /// <summary>
        /// Gets the <see cref="IMemoryAllocator"/> for this OS.
        /// </summary>
        IMemoryAllocator MemoryAllocator { get; }

        /// <summary>
        /// Gets the <see cref="INativeExceptionHelper"/> for this OS, if it is needed.
        /// </summary>
        INativeExceptionHelper? NativeExceptionHelper { get; }

        /// <summary>
        /// Enumerates all modules which are loaded in the process and yields their file names.
        /// </summary>
        /// <returns>An enumerable over the file names of all loaded modules.</returns>
        IEnumerable<string?> EnumerateLoadedModuleFiles();

        /// <summary>
        /// Gets an approximate amount of memory after <paramref name="start"/> which is readable.
        /// </summary>
        /// <param name="start">The start address to check.</param>
        /// <param name="guess">The amount which is expected. This method may not return values larger than to the page following this guess.</param>
        /// <returns>A number of bytes after <paramref name="start"/> which it is safe to read.</returns>
        nint GetSizeOfReadableMemory(IntPtr start, nint guess);

        /// <summary>
        /// Patches <paramref name="patchTarget"/> to contain the data in <paramref name="data"/>, while creating a backup
        /// of the data which was previously there in <paramref name="backup"/>.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="backup"/> may be an empty span. When it is, no backup is made.</para>
        /// <para><paramref name="targetKind"/> is to be used as nothing more than a hint. The system should check the existing mapping
        /// to ensure that it is in fact correct (so that it would not remap an RW page to an RX page, or something similar).</para>
        /// </remarks>
        /// <param name="targetKind">The expected kind of data at <paramref name="patchTarget"/>.</param>
        /// <param name="patchTarget">A pointer to the memory location to patch.</param>
        /// <param name="data">The data to write into <paramref name="patchTarget"/>.</param>
        /// <param name="backup">A span to fill will the data which was already present, or an empty span.</param>
        void PatchData(PatchTargetKind targetKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup);
    }

    /// <summary>
    /// The kind of data which exists at a location to be patched.
    /// </summary>
    public enum PatchTargetKind
    {
        /// <summary>
        /// The data at the target is expected to be executable.
        /// </summary>
        Executable,
        /// <summary>
        /// The data at the target is expected to be read-only.
        /// </summary>
        ReadOnly,
    }
}
