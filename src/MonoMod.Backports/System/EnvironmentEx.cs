#if NET45_OR_GREATER || NETCOREAPP || NETSTANDARD
#define HAS_CURRENTMANAGEDTHREADID
#endif

#if !HAS_CURRENTMANAGEDTHREADID
using System.Threading;
#endif

namespace System
{
    public static class EnvironmentEx
    {

        public static int CurrentManagedThreadId
#if HAS_CURRENTMANAGEDTHREADID
            => Environment.CurrentManagedThreadId;
#else
            => Thread.CurrentThread.ManagedThreadId;
#endif

    }
}
