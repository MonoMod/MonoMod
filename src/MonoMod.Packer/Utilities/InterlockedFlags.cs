using System;
using System.Threading;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace MonoMod.Packer.Utilities {
    internal static class InterlockedFlags {
        public static bool Set(ref int flags, int toSet) {
            int oldState, newState;
            do {
                oldState = flags;
                newState = oldState | toSet;
                if (newState == oldState) {
                    return false;
                }
            }
            while (Interlocked.CompareExchange(ref flags, newState, oldState) != oldState);
            return true;
        }

        private static Exception CreateBadEnumSizeEx() => new NotImplementedException("Only int-sized enums are supported right now");

        public static unsafe bool Set<T>(ref T flags, T toSet) where T : struct, Enum {
            if (sizeof(T) == sizeof(int)) {
                return Set(ref Unsafe.As<T, int>(ref flags), Unsafe.As<T, int>(ref toSet));
            } else {
                throw CreateBadEnumSizeEx();
            }
        }

        public static bool Clear(ref int flags, int toClear) {
            int oldState, newState;
            do {
                oldState = flags;
                newState = oldState & ~toClear;
                if (newState == oldState) {
                    return false;
                }
            }
            while (Interlocked.CompareExchange(ref flags, newState, oldState) != oldState);
            return true;
        }

        public static unsafe bool Clear<T>(ref T flags, T toClear) where T : struct, Enum {
            if (sizeof(T) == sizeof(int)) {
                return Clear(ref Unsafe.As<T, int>(ref flags), Unsafe.As<T, int>(ref toClear));
            } else {
                throw CreateBadEnumSizeEx();
            }
        }
    }
}
