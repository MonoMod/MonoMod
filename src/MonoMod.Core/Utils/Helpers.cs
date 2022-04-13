using MonoMod.Backports;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace MonoMod.Core.Utils {
    internal static class Helpers {
        public static void Swap<T>(ref T a, ref T b) => (b, a) = (a, b);

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ulong NumericValue<T>(T value) where T : struct, Enum {
            ulong result = 0;
            Unsafe.CopyBlock(ref Unsafe.As<ulong, byte>(ref result), ref Unsafe.As<T, byte>(ref value), (uint)Unsafe.SizeOf<T>());
            return result;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static void ThrowIfNull<T>([NotNull] T? arg, [CallerArgumentExpression("arg")] string name = "") {
            if (arg is null)
                ThrowArgumentNull(name);
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        [DoesNotReturn]
        private static void ThrowArgumentNull(string argName) {
            throw new ArgumentNullException(argName);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static T GetOrInit<T>(ref T? location, Func<T> init) where T : class {
            if (location is not null)
                return location;
            return InitializeValue(ref location, init);
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        private static T InitializeValue<T>(ref T? location, Func<T> init) where T : class {
            _ = Interlocked.CompareExchange(ref location, init(), null);
            return location!;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static T GetOrInitWithLock<T>(ref T? location, object @lock, Func<T> init) where T : class {
            if (location is not null)
                return location;
            return InitializeValueWithLock(ref location, @lock, init);
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        private static T InitializeValueWithLock<T>(ref T? location, object @lock, Func<T> init) where T : class {
            lock (@lock) {
                if (location is not null)
                    return location;
                return location = init();
            }
        }
    }
}
