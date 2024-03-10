using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures
{
    internal abstract class DetourKindBase : INativeDetourKind
    {
        public abstract int Size { get; }

        public abstract int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle);

        public static int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocHandle)
        {
            Helpers.ThrowIfArgumentNull(info.InternalKind);
            if (buffer.Length < info.Size)
                throw new ArgumentException("Buffer too short", nameof(buffer));

            var kind = (DetourKindBase)info.InternalKind;
            return kind.GetBytes(info.From, info.To, buffer, info.InternalData, out allocHandle);
        }

        public abstract bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo);
        public static bool TryFindRetargetInfo(NativeDetourInfo info, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo)
        {
            Helpers.ThrowIfArgumentNull(info.InternalKind);
            var kind = (DetourKindBase)info.InternalKind;
            return kind.TryGetRetargetInfo(info, to, maxSize, out retargetInfo);
        }

        public abstract int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc);

        public static int DoRetarget(NativeDetourInfo orig, NativeDetourInfo info, Span<byte> buffer,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
        {
            Helpers.ThrowIfArgumentNull(info.InternalKind);
            if (buffer.Length < info.Size)
                throw new ArgumentException("Buffer too short", nameof(buffer));

            var kind = (DetourKindBase)info.InternalKind;
            return kind.DoRetarget(orig, info.To, buffer, info.InternalData, out allocationHandle, out needsRepatch, out disposeOldAlloc);
        }
    }
}
