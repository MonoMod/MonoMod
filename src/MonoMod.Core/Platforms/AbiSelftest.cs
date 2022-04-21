using MonoMod.Backports;
using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms {
    public static class AbiSelftest {

        private static readonly object SelftestLock = new();

        public static Abi DetectAbi(PlatformTriple triple) {
            Helpers.ThrowIfNull(triple);

            lock (SelftestLock) {
                var returnsRetbuf = DetectReturnsRetBuf();
                var argOrder = DetectArgumentOrder(triple);
            }

            throw new NotImplementedException();
        }

        private const BindingFlags AllFlgs = (BindingFlags) (-1);

        #region DetectArgumentOrder
        private static SelftestArgumentOrder DetectArgumentOrder(PlatformTriple triple) {
            var SelftestHelper_DoSelftest1 = typeof(SelftestHelper).GetMethod(nameof(SelftestHelper.ArgumentOrderTest), AllFlgs)!;
            var Self_Selftest1Target = typeof(AbiSelftest).GetMethod(nameof(ArgumentOrderTestTarget), AllFlgs)!;

            using (triple.PinMethodIfNeeded(SelftestHelper_DoSelftest1))
            using (triple.PinMethodIfNeeded(Self_Selftest1Target)) {
                var from = triple.GetNativeMethodBody(SelftestHelper_DoSelftest1);
                var to = triple.GetNativeMethodBody(Self_Selftest1Target);

                using (triple.CreateNativeDetour(from, to)) {
                    var argOrder = SelftestArgumentOrder.None;
                    SelftestHelper helper = default;

                    _ = helper.ArgumentOrderTest(ref argOrder, ref argOrder, ref helper);

                    if (argOrder == SelftestArgumentOrder.None) {
                        throw new PlatformNotSupportedException($"Selftest 1 failed! Argument order was not assigned");
                    }

                    return argOrder;
                }
            }
        }

        private enum SelftestArgumentOrder {
            None,

            RetThisArgs,
            ThisRetArgs,

            ThisArgsRet, // ???
            RetArgsThis, // ???

            ArgsThisRet, // ?????
            ArgsRetThis, // ????????
        }

        private static unsafe IntPtr ArgumentOrderTestTarget(IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e) {
            [DoesNotReturn]
            static void ThrowFunkyAbi(IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e) {
                throw new PlatformNotSupportedException($"What kind of ABI is this? {a:x16} {b:x16} {c:x16} {d:x16} {e:x16}");
            }
            if (c == d) {
                // c and d are the first two user arguments
                if (b == e) {
                    // b is the this pointer
                    Unsafe.AsRef<SelftestArgumentOrder>((void*) c) = SelftestArgumentOrder.RetThisArgs;
                    return a; // return the this ptr to be safe
                } else if (a == e) {
                    // a is the this pointer
                    Unsafe.AsRef<SelftestArgumentOrder>((void*) c) = SelftestArgumentOrder.ThisRetArgs;
                    return b;
                } else {
                    // huh???
                    ThrowFunkyAbi(a, b, c, d, e);
                }
            } else if (b == c) {
                // b and c are the first two user arguments
                if (a == d) {
                    // a is the this ptr, b c d are user args, and e is the ret buffer
                    Unsafe.AsRef<SelftestArgumentOrder>((void*) b) = SelftestArgumentOrder.ThisArgsRet;
                    return e;
                } else if (d == e) {
                    // a is the ret buffer, b c d are user args, and e is the this ptr
                    Unsafe.AsRef<SelftestArgumentOrder>((void*) b) = SelftestArgumentOrder.RetArgsThis;
                    return a;
                } else {
                    // huh???
                    ThrowFunkyAbi(a, b, c, d, e);
                }
            } else if (a == b) {
                // a and b are the first two user arguments
                if (c == d) {
                    // d is the this ptr, e is the ret buffer
                    Unsafe.AsRef<SelftestArgumentOrder>((void*) a) = SelftestArgumentOrder.ArgsThisRet;
                    return e;
                } else if (c == e) {
                    // e is the this ptr, d is the ret buffer
                    Unsafe.AsRef<SelftestArgumentOrder>((void*) a) = SelftestArgumentOrder.ArgsRetThis;
                    return d;
                } else {
                    // huh???
                    ThrowFunkyAbi(a, b, c, d, e);
                }
            }

            // huh???
            ThrowFunkyAbi(a, b, c, d, e);
            // stoopid compiler thinks ThrowFunkyAbi can return for some reason
            throw new InvalidOperationException();
        }
        #endregion

        #region DetectReturnRetbuf
        private static unsafe bool DetectReturnsRetBuf() {
            // this test doesn't require detours, only ABI shenanigans with delegate*s

            delegate*<SelftestRetbufStruct> ptr = &TestReturnsRetBuf;

            SelftestRetbufStruct target = default;
            ref var targetRef = ref target;
            ref var result = ref ((delegate*<ref SelftestRetbufStruct, ref SelftestRetbufStruct>) ptr)(ref targetRef);

            return Unsafe.AreSame(ref targetRef, ref result);
        }

        // By making this parameterless, the only parameter will be the return buffer, meaning we don't have to worry about argument order
        private static SelftestRetbufStruct TestReturnsRetBuf() {
            return default;
        }

        #endregion

        // TODO: more struct sizes to selftest when retbufs are needed

        private struct SelftestRetbufStruct {
            public readonly long L1;
            public readonly short S1;
            public readonly byte B1;
        }

        // this is a struct because I can control the address of a struct much more easily than I can a class
        private struct SelftestHelper {
            private readonly SelftestRetbufStruct filler;

            [MethodImpl(MethodImplOptionsEx.NoInlining)]
            public SelftestRetbufStruct ArgumentOrderTest(ref SelftestArgumentOrder argOrder1, ref SelftestArgumentOrder argOrder2, ref SelftestHelper self) {
                throw new InvalidOperationException("ABI selftest failed! The method was not detoured.");
            }
        }
    }
}
