using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{
    internal static class Android
    {
        // If this dllimport decl isn't enough to get the runtime to load the right thing, I give up
        public const string LibC = "libc";


        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "read")]
        public static extern unsafe nint Read(int fd, IntPtr buf, nint count);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "write")]
        public static extern unsafe nint Write(int fd, IntPtr buf, nint count);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pipe2")]
        public static extern unsafe int Pipe2(int* pipefd, PipeFlags flags);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "close")]
        public static extern unsafe int Close(int fd);

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

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "__errno")]
        public static extern unsafe int* __errno();

        public static unsafe int Errno => *__errno();

        static Android()
        {
            // Preload pinvoke initialization so it doesn't affect errno when accessed the first time
            _ = Errno;
        }

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
            Shared = 0x01,             // MAP_SHARED
            Private = 0x02,            // MAP_PRIVATE
            SharedValidate = 0x03,     // MAP_SHARED_VALIDATE
            Droppable = 0x08,          // MAP_DROPPABLE

            Fixed = 0x10,              // MAP_FIXED
            Anonymous = 0x20,          // MAP_ANONYMOUS

            GrowsDown = 0x0100,        // MAP_GROWSDOWN
            DenyWrite = 0x0800,        // MAP_DENYWRITE
            [Obsolete("Use Protection.Execute instead", true)]
            Executable = 0x1000,       // MAP_EXECUTABLE
            Locked = 0x2000,           // MAP_LOCKED
            NoReserve = 0x4000,        // MAP_NORESERVE
            Populate = 0x8000,         // MAP_POPULATE
            NonBlock = 0x10000,        // MAP_NONBLOCK
            Stack = 0x20000,           // MAP_STACK
            HugeTLB = 0x40000,         // MAP_HUGETLB
            Sync = 0x80000,            // MAP_SYNC
            FixedNoReplace = 0x100000, // MAP_FIXED_NOREPLACE
        }

        public enum SysconfName
        {
            // Values from Android NDK sysconf.h
            ArgMax = 0x0000,           // _SC_ARG_MAX
            ChildMax = 0x0005,         // _SC_CHILD_MAX
            ClockTick = 0x0006,        // _SC_CLK_TCK
            NGroupsMax = 0x000a,       // _SC_NGROUPS_MAX
            OpenMax = 0x000b,          // _SC_OPEN_MAX
            StreamMax = 0x001b,        // _SC_STREAM_MAX
            TZNameMax = 0x001c,        // _SC_TZNAME_MAX
            JobControl = 0x0017,       // _SC_JOB_CONTROL
            SavedIds = 0x0018,         // _SC_SAVED_IDS
            Version = 0x0019,          // _SC_VERSION

            // Async / AIO / MQ
            AIOListIOMax = 0x002e,     // _SC_AIO_LISTIO_MAX
            AIOMax = 0x002f,           // _SC_AIO_MAX
            AIOPrioDeltaMax = 0x0030,  // _SC_AIO_PRIO_DELTA_MAX (Unimplemented on Android)
            DelayTimerMax = 0x0031,    // _SC_DELAYTIMER_MAX
            MQOpenMax = 0x0032,        // _SC_MQ_OPEN_MAX
            MQPrioMax = 0x0033,        // _SC_MQ_PRIO_MAX

            // Signals / semaphores / timers
            RTSigMax = 0x0034,         // _SC_RTSIG_MAX
            SemNSemsMax = 0x0035,      // _SC_SEM_NSEMS_MAX
            SemValueMax = 0x0036,      // _SC_SEM_VALUE_MAX
            SigQueueMax = 0x0037,      // _SC_SIGQUEUE_MAX
            TimerMax = 0x0038,         // _SC_TIMER_MAX

            // POSIX feature queries
            AsyncIO = 0x0039,          // _SC_ASYNCHRONOUS_IO
            FSync = 0x003a,            // _SC_FSYNC
            MappedFiles = 0x003b,      // _SC_MAPPED_FILES
            MemLock = 0x003c,          // _SC_MEMLOCK
            MemLockRange = 0x003d,     // _SC_MEMLOCK_RANGE
            MemoryProtection = 0x003e, // _SC_MEMORY_PROTECTION
            MessagePassing = 0x003f,   // _SC_MESSAGE_PASSING
            PrioritizedIO = 0x0040,    // _SC_PRIORITIZED_IO
            PriorityScheduling = 0x0041,// _SC_PRIORITY_SCHEDULING
            RealtimeSignals = 0x0042,  // _SC_REALTIME_SIGNALS
            Semaphores = 0x0043,       // _SC_SEMAPHORES
            SharedMemoryObjects = 0x0044,// _SC_SHARED_MEMORY_OBJECTS
            SynchronizedIO = 0x0045,   // _SC_SYNCHRONIZED_IO
            Timers = 0x0046,           // _SC_TIMERS

            // Thread / pthread related
            // (these retain Android values if needed elsewhere)
            // _SC_THREAD_* values live in 0x004a..0x0055 etc in the header

            // Page / memory info
            PageSize = 0x0028,         // _SC_PAGE_SIZE (use _SC_PAGE_SIZE as in header)
            NProcessorsConf = 0x0060,  // _SC_NPROCESSORS_CONF
            NProcessorsOnln = 0x0061,  // _SC_NPROCESSORS_ONLN
            PhysPages = 0x0062,        // _SC_PHYS_PAGES
            AvPhysPages = 0x0063,      // _SC_AVPHYS_PAGES
            MonotonicClock = 0x0064,   // _SC_MONOTONIC_CLOCK

            // Cache query values (where available)
            Level1ICacheSize = 0x008f,     // _SC_LEVEL1_ICACHE_SIZE
            Level1ICacheAssoc = 0x0090,    // _SC_LEVEL1_ICACHE_ASSOC
            Level1ICacheLinesize = 0x0091, // _SC_LEVEL1_ICACHE_LINESIZE
            Level1DCacheSize = 0x0092,     // _SC_LEVEL1_DCACHE_SIZE
            Level1DCacheAssoc = 0x0093,    // _SC_LEVEL1_DCACHE_ASSOC
            Level1DCacheLinesize = 0x0094, // _SC_LEVEL1_DCACHE_LINESIZE
            Level2CacheSize = 0x0095,      // _SC_LEVEL2_CACHE_SIZE
            Level2CacheAssoc = 0x0096,     // _SC_LEVEL2_CACHE_ASSOC
            Level2CacheLinesize = 0x0097,  // _SC_LEVEL2_CACHE_LINESIZE
            Level3CacheSize = 0x0098,      // _SC_LEVEL3_CACHE_SIZE
            Level3CacheAssoc = 0x0099,     // _SC_LEVEL3_CACHE_ASSOC
            Level3CacheLinesize = 0x009a,  // _SC_LEVEL3_CACHE_LINESIZE
            Level4CacheSize = 0x009b,      // _SC_LEVEL4_CACHE_SIZE
            Level4CacheAssoc = 0x009c,     // _SC_LEVEL4_CACHE_ASSOC
            Level4CacheLinesize = 0x009d,  // _SC_LEVEL4_CACHE_LINESIZE

            NSig = 0x009e                 // _SC_NSIG (available from API level 37)
        }
    }
}