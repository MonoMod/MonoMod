using System;

namespace MonoMod.Core.Platforms.Architectures
{

    internal static class x86Shared
    {
        public sealed class Rel32Kind : DetourKindBase
        {
            public static readonly Rel32Kind Instance = new();

            public override int Size => 1 + 4;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle)
            {
                buffer[0] = 0xe9;
                Unsafe.WriteUnaligned(ref buffer[1], (int)(to - ((nint)from + 5)));
                allocHandle = null;
                return Size;
            }

            public override bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo)
            {
                // if we're here, the existing 5f condition cannot be true, so we don't need to check it
                var rel = to - ((nint)orig.From + 5);
                if (Is32Bit(rel) || Is32Bit(-rel))
                {
                    // we can keep using the rel32 detour kind, just pointed at the new target
                    retargetInfo = new(orig.From, to, Instance, null);
                    return true;
                }

                // we don't know how to retarget well with this detour kind, tell caller to figure something else out
                retargetInfo = default;
                return false;
            }

            public override int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
                out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
            {
                needsRepatch = true;
                disposeOldAlloc = true;
                // the retarget logic for rel32 is just the same as the normal patch
                // the patcher should repatch the target method with the new bytes, and dispose the old allocation, if present
                return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
            }
        }

        public static void FixSizeHint(ref int sizeHint)
        {
            if (sizeHint < 0)
            {
                sizeHint = int.MaxValue;
            }
        }

        public static bool TryRel32Detour(nint from, nint to, int sizeHint, out NativeDetourInfo info)
        {
            var rel = to - (from + 5);

            if (sizeHint >= Rel32Kind.Instance.Size && (Is32Bit(rel) || Is32Bit(-rel)))
            {
                unsafe
                {
                    if (*((byte*)from + 5) != 0x5f)
                    {
                        // because Rel32 uses an E9 jump, the byte that would be immediately following the jump
                        //   must not be 0x5f, otherwise it would be picked up by the matcher on line 44 of x86_64Arch
                        info = new(from, to, Rel32Kind.Instance, null);
                        return true;
                    }
                }
            }

            info = default;
            return false;
        }

        public static bool Is32Bit(long to)
            // JMP rel32 is "sign extended to 64-bits"
            => (((ulong)to) & 0x000000007FFFFFFFUL) == ((ulong)to);
    }
}
