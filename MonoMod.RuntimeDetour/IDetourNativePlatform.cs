using MonoMod.Utils;
using System;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour {
    public interface IDetourNativePlatform {
        NativeDetourData Create(IntPtr from, IntPtr to, byte? type = null);
        void Free(NativeDetourData detour);
        void Apply(NativeDetourData detour);
        void Copy(IntPtr src, IntPtr dst, byte type);
        void MakeWritable(NativeDetourData detour);
        void MakeExecutable(NativeDetourData detour);
        IntPtr MemAlloc(uint size);
        void MemFree(IntPtr ptr);
    }
}
