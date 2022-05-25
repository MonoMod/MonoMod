using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures {
    internal abstract class DetourKindBase : INativeDetourKind {
        public abstract int Size { get; }

        public abstract int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle);

        public static int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocHandle) {
            Helpers.ThrowIfArgumentNull(info.InternalKind);
            if (buffer.Length < info.Size)
                throw new ArgumentException("Buffer too short", nameof(buffer));

            var kind = (DetourKindBase) info.InternalKind;

            return kind.GetBytes(info.From, info.To, buffer, info.InternalData, out allocHandle);
        }
    }
}
