#if NET40_OR_GREATER || NETCOREAPP || NETSTANDARD
#define HAS_MONITOR_ENTER_BYREF
#endif

using System.Runtime.CompilerServices;

namespace System.Threading {
    /// <summary>
    /// Extensions to <see cref="MONITOR"/> providing consistent access to APIs introduced after the type.
    /// </summary>
    public static class MonitorEx {

#if HAS_MONITOR_ENTER_BYREF
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static void Enter(object obj, ref bool lockTaken) {
#if HAS_MONITOR_ENTER_BYREF
            Monitor.Enter(obj, ref lockTaken);
#else
            if (lockTaken)
                throw new ArgumentException("lockTaken was true.", nameof(lockTaken));
            lockTaken = false;
            Monitor.Enter(obj);
            lockTaken = true;
#endif
        }
    }
}
