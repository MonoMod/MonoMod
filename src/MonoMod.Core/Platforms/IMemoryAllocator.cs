using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface IMemoryAllocator {
        bool TryAllocateInRange(IntPtr target, IntPtr low, IntPtr high, int size, int align, bool executable, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
    }

    public interface IAllocatedMemory : IDisposable {
        IntPtr BaseAddress { get; }
        int Size { get; }
        Span<byte> Memory { get; }
    }
}
