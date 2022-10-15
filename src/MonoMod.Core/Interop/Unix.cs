using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop {
    internal static class Unix {
        // If this dllimport decl isn't enough to get the runtime to load the right thing, I give up
        public const string LibC = "libc";


        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mmap", SetLastError = true)]
        public static extern unsafe IntPtr Mmap(IntPtr addr, nuint length, Protection prot, MmapFlags flags, int fd, int offset);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "munmap", SetLastError = true)]
        public static extern unsafe int Munmap(IntPtr addr, nuint length);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mprotect", SetLastError = true)]
        public static extern unsafe int Mprotect(IntPtr addr, nuint len, Protection prot);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sysconf", SetLastError = true)]
        public static extern unsafe long Sysconf(SysconfName name);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mincore", SetLastError = true)]
        public static extern unsafe int Mincore(IntPtr addr, nuint len, byte* vec);

        [Flags]
        public enum Protection : int {
            None = 0x00,
            Read = 0x01,
            Write = 0x02,
            Execute = 0x04,
        }

        [Flags]
        public enum MmapFlags : int {
            Shared = 0x01,
            Private = 0x02,
            SharedValidate = 0x03,

            Fixed = 0x10,
            Anonymous = 0x20,

            GrowsDown = 0x00100,
            DenyWrite = 0x00800,
            [Obsolete("Use Protection.Execute instead", true)]
            Executable = 0x01000,
            Locked = 0x02000,
            NoReserve = 0x04000,
            Populate = 0x08000,
            NonBlock = 0x10000,
            Stack = 0x20000,
            HugeTLB = 0x40000,
            Sync = 0x80000,
            FixedNoReplace = 0x100000,
        }

        public enum SysconfName {
            ArgMax,
            ChildMax,
            ClockTick,
            NGroupsMax,
            OpenMax,
            StreamMax,
            TZNameMax,
            JobControl,
            SavedIds,
            RealtimeSignals,
            PriorityScheduling,
            Timers,
            AsyncIO,
            PrioritizedIO,
            SynchronizedIO,
            FSync,
            MappedFiles,
            MemLock,
            MemLockRange,
            MemoryProtection,
            MessagePassing,
            Semaphores,
            SharedMemoryObjects,
            AIOListIOMax,
            AIOMax,
            AIOPrioDeltaMax,
            DelayTimerMax,
            MQOpenMax,
            MQPrioMax,
            Version,
            PageSize,
            RTSigMax,
            SemNSemsMax,
            SemValueMax,
            SigQueueMax,
            TimerMax,
        }
    }
}
