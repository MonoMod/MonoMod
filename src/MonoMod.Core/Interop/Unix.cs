using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{
    internal static class Unix
    {
        // If this dllimport decl isn't enough to get the runtime to load the right thing, I give up
        public const string LibC = "libc";


        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "read")]
        public static extern unsafe nint Read(int fd, IntPtr buf, nint count);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "write")]
        public static extern unsafe nint Write(int fd, IntPtr buf, nint count);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pipe2")]
        public static extern unsafe int Pipe2(int* pipefd, PipeFlags flags);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mmap")]
        public static extern unsafe nint Mmap(IntPtr addr, nuint length, Protection prot, MmapFlags flags, int fd, int offset);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "munmap")]
        public static extern unsafe int Munmap(IntPtr addr, nuint length);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mprotect")]
        public static extern unsafe int Mprotect(IntPtr addr, nuint len, Protection prot);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sysconf")]
        public static extern unsafe long Sysconf(SysconfName name);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mincore")]
        public static extern unsafe int Mincore(IntPtr addr, nuint len, byte* vec);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mkstemp")]
        public static extern unsafe int MkSTemp(byte* template);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "__errno_location")]
        public static extern unsafe int* __errno_location();

        public static unsafe int Errno => *__errno_location();

        [Flags]
        public enum PipeFlags : int
        {
            CloseOnExec = 0x80000
        }

        [Flags]
        public enum Protection : int
        {
            None = 0x00,
            Read = 0x01,
            Write = 0x02,
            Execute = 0x04,
        }

        [Flags]
        public enum MmapFlags : int
        {
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

        public enum SysconfName
        {
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
