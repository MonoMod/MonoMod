global using MethodImplOptions = MonoMod.Backports.MethodImplOptionsEx;
using SysMethodImplOpts = System.Runtime.CompilerServices.MethodImplOptions;

// TODO: maybe move this into System.Runtime.CompilerServices

namespace MonoMod.Backports
{
    public static class MethodImplOptionsEx
    {
        public const SysMethodImplOpts
            Unmanaged = (SysMethodImplOpts)4,
            NoInlining = (SysMethodImplOpts)8,
            ForwardRef = (SysMethodImplOpts)16,
            Synchronized = (SysMethodImplOpts)32,
            NoOptimization = (SysMethodImplOpts)64,
            PreserveSig = (SysMethodImplOpts)128,
            AggressiveInlining = (SysMethodImplOpts)256,
            AggressiveOptimization = (SysMethodImplOpts)512,
            InternalCall = (SysMethodImplOpts)4096;
    }
}
