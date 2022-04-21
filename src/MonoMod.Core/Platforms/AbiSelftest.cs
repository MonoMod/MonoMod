using MonoMod.Backports;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms {
    public static partial class AbiSelftest {

        private static readonly object SelftestLock = new();

        public static Abi DetectAbi(PlatformTriple triple) {
            Helpers.ThrowIfNull(triple);

            bool returnsRetbuf;
            SelftestArgumentOrder argOrder;
            StructKindFlags returnByVal, passByVal;

            lock (SelftestLock) {
                returnsRetbuf = DetectReturnsRetBuf();
                argOrder = DetectArgumentOrder(triple);
                (returnByVal, passByVal) = DetectStructPassing(triple, argOrder);
            }

            // TODO: selftest generic pointer position somehow

            var classifier = new SelftestClassifier(returnByVal, passByVal);
            var argOrderArr = argOrder switch {
                SelftestArgumentOrder.RetThisArgs => new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                SelftestArgumentOrder.ThisRetArgs => new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.UserArguments },
                SelftestArgumentOrder.ThisArgsRet => new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments, SpecialArgumentKind.ReturnBuffer },
                SelftestArgumentOrder.RetArgsThis => new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.UserArguments, SpecialArgumentKind.ThisPointer },
                SelftestArgumentOrder.ArgsThisRet => new[] { SpecialArgumentKind.UserArguments, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer },
                SelftestArgumentOrder.ArgsRetThis => new[] { SpecialArgumentKind.UserArguments, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer },
                _ => throw new InvalidOperationException("Invalid argument order"),
            };

            return new(
                argOrderArr,
                classifier.Classifier,
                returnsRetbuf);
        }


        [DoesNotReturn]
        private static void ThrowFunkyAbi(IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e) {
            throw new PlatformNotSupportedException($"What kind of ABI is this? {a:x16} {b:x16} {c:x16} {d:x16} {e:x16}");
        }

        private const BindingFlags AllFlgs = (BindingFlags) (-1);

        #region DetectArgumentOrder

        private static readonly MethodInfo SelftestHelper_ArgumentOrderTest = typeof(SelftestHelper).GetMethod(nameof(SelftestHelper.ArgumentOrderTest), AllFlgs)!;
        private static readonly MethodInfo Self_ArgumentOrderTestTarget = typeof(AbiSelftest).GetMethod(nameof(ArgumentOrderTestTarget), AllFlgs)!;

        private static SelftestArgumentOrder DetectArgumentOrder(PlatformTriple triple) {

            using (triple.PinMethodIfNeeded(SelftestHelper_ArgumentOrderTest))
            using (triple.PinMethodIfNeeded(Self_ArgumentOrderTestTarget)) {
                var from = triple.GetNativeMethodBody(SelftestHelper_ArgumentOrderTest);
                var to = triple.GetNativeMethodBody(Self_ArgumentOrderTestTarget);

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

        [Flags]
        private enum StructKindFlags : ulong {
            None = 0,

            HfaFloat1 = 0x00000000_00000001,
            HfaFloat2 = 0x00000000_00000002,
            HfaFloat3 = 0x00000000_00000004,
            HfaFloat4 = 0x00000000_00000008,

            HfaDouble1 = 0x00000000_00000010,
            HfaDouble2 = 0x00000000_00000020,
            HfaDouble3 = 0x00000000_00000040,
            HfaDouble4 = 0x00000000_00000080,

            Int1 = 0x00000000_00000100,
            Int2 = 0x00000000_00000200,
            Int3 = 0x00000000_00000400,
            Int4 = 0x00000000_00000800,

            Long1 = 0x00000000_00001000,
            Long2 = 0x00000000_00002000,
            Long3 = 0x00000000_00004000,
            Long4 = 0x00000000_00008000,

            Byte1 = 0x00000000_00010000,
            Byte2 = 0x00000000_00020000,
            Byte3 = 0x00000000_00040000,
            Byte4 = 0x00000000_00080000,

            Short1 = 0x00000000_00100000,
            Short2 = 0x00000000_00200000,
            Short3 = 0x00000000_00400000,
            Short4 = 0x00000000_00800000,

            OddSize3 = 0x00000000_01000000,
            OddSize5 = 0x00000000_02000000,
            OddSize6 = 0x00000000_04000000,
            OddSize7 = 0x00000000_08000000,
            OddSize9 = 0x00000000_10000000,

            X_1 = 0x00000001_00000000,
            X_2 = 0x00000002_00000000,
            X_3 = 0x00000004_00000000,
            X_4 = 0x00000008_00000000,
            X_5 = 0x00000010_00000000,
            X_6 = 0x00000020_00000000,
            X_7 = 0x00000040_00000000,
            X_8 = 0x00000080_00000000,
            X_9 = 0x00000100_00000000,
            X10 = 0x00000200_00000000,
            X11 = 0x00000400_00000000,
            X12 = 0x00000800_00000000,
            X13 = 0x00001000_00000000,
            X14 = 0x00002000_00000000,
            X15 = 0x00004000_00000000,
            X16 = 0x00008000_00000000,

            Empty = 0x80000000_00000000,
        }

        private static StructKindFlags GetFlagFor(bool value, StructKindFlags setFlag)
            => value ? setFlag : 0;

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private static bool IsClose(nint reference, nint test) {
            var val = test - reference;
            if (val < 0)
                val = -val;
            return val < 0x1000; // we give a vairly generous page of space for them to be in
        }

        private static (StructKindFlags ByValRet, StructKindFlags ByValPass) DetectStructPassing(PlatformTriple triple, SelftestArgumentOrder argOrder) {
            StructKindFlags ret = 0, pass = 0;

            TestPassReturnStruct<HfaFloat1>(triple, argOrder, StructKindFlags.HfaFloat1, ref ret, ref pass);
            TestPassReturnStruct<HfaFloat2>(triple, argOrder, StructKindFlags.HfaFloat2, ref ret, ref pass);
            TestPassReturnStruct<HfaFloat3>(triple, argOrder, StructKindFlags.HfaFloat3, ref ret, ref pass);
            TestPassReturnStruct<HfaFloat4>(triple, argOrder, StructKindFlags.HfaFloat4, ref ret, ref pass);

            TestPassReturnStruct<HfaDouble1>(triple, argOrder, StructKindFlags.HfaDouble1, ref ret, ref pass);
            TestPassReturnStruct<HfaDouble2>(triple, argOrder, StructKindFlags.HfaDouble2, ref ret, ref pass);
            TestPassReturnStruct<HfaDouble3>(triple, argOrder, StructKindFlags.HfaDouble3, ref ret, ref pass);
            TestPassReturnStruct<HfaDouble4>(triple, argOrder, StructKindFlags.HfaDouble4, ref ret, ref pass);

            TestPassReturnStruct<Int1>(triple, argOrder, StructKindFlags.Int1, ref ret, ref pass);
            TestPassReturnStruct<Int2>(triple, argOrder, StructKindFlags.Int2, ref ret, ref pass);
            TestPassReturnStruct<Int3>(triple, argOrder, StructKindFlags.Int3, ref ret, ref pass);
            TestPassReturnStruct<Int4>(triple, argOrder, StructKindFlags.Int4, ref ret, ref pass);

            TestPassReturnStruct<Long1>(triple, argOrder, StructKindFlags.Long1, ref ret, ref pass);
            TestPassReturnStruct<Long2>(triple, argOrder, StructKindFlags.Long2, ref ret, ref pass);
            TestPassReturnStruct<Long3>(triple, argOrder, StructKindFlags.Long3, ref ret, ref pass);
            TestPassReturnStruct<Long4>(triple, argOrder, StructKindFlags.Long4, ref ret, ref pass);

            TestPassReturnStruct<Byte1>(triple, argOrder, StructKindFlags.Byte1, ref ret, ref pass);
            TestPassReturnStruct<Byte2>(triple, argOrder, StructKindFlags.Byte2, ref ret, ref pass);
            TestPassReturnStruct<Byte3>(triple, argOrder, StructKindFlags.Byte3, ref ret, ref pass);
            TestPassReturnStruct<Byte4>(triple, argOrder, StructKindFlags.Byte4, ref ret, ref pass);

            TestPassReturnStruct<Short1>(triple, argOrder, StructKindFlags.Short1, ref ret, ref pass);
            TestPassReturnStruct<Short2>(triple, argOrder, StructKindFlags.Short2, ref ret, ref pass);
            TestPassReturnStruct<Short3>(triple, argOrder, StructKindFlags.Short3, ref ret, ref pass);
            TestPassReturnStruct<Short4>(triple, argOrder, StructKindFlags.Short4, ref ret, ref pass);

            TestPassReturnStruct<OddSize3>(triple, argOrder, StructKindFlags.OddSize3, ref ret, ref pass);
            TestPassReturnStruct<OddSize5>(triple, argOrder, StructKindFlags.OddSize5, ref ret, ref pass);
            TestPassReturnStruct<OddSize6>(triple, argOrder, StructKindFlags.OddSize6, ref ret, ref pass);
            TestPassReturnStruct<OddSize7>(triple, argOrder, StructKindFlags.OddSize7, ref ret, ref pass);
            TestPassReturnStruct<OddSize9>(triple, argOrder, StructKindFlags.OddSize9, ref ret, ref pass);

            TestPassReturnStruct<X_1>(triple, argOrder, StructKindFlags.X_1, ref ret, ref pass);
            TestPassReturnStruct<X_2>(triple, argOrder, StructKindFlags.X_2, ref ret, ref pass);
            TestPassReturnStruct<X_3>(triple, argOrder, StructKindFlags.X_3, ref ret, ref pass);
            TestPassReturnStruct<X_4>(triple, argOrder, StructKindFlags.X_4, ref ret, ref pass);
            TestPassReturnStruct<X_5>(triple, argOrder, StructKindFlags.X_5, ref ret, ref pass);
            TestPassReturnStruct<X_6>(triple, argOrder, StructKindFlags.X_6, ref ret, ref pass);
            TestPassReturnStruct<X_7>(triple, argOrder, StructKindFlags.X_7, ref ret, ref pass);
            TestPassReturnStruct<X_8>(triple, argOrder, StructKindFlags.X_8, ref ret, ref pass);
            TestPassReturnStruct<X_9>(triple, argOrder, StructKindFlags.X_9, ref ret, ref pass);
            TestPassReturnStruct<X10>(triple, argOrder, StructKindFlags.X10, ref ret, ref pass);
            TestPassReturnStruct<X11>(triple, argOrder, StructKindFlags.X11, ref ret, ref pass);
            TestPassReturnStruct<X12>(triple, argOrder, StructKindFlags.X12, ref ret, ref pass);
            TestPassReturnStruct<X13>(triple, argOrder, StructKindFlags.X13, ref ret, ref pass);
            TestPassReturnStruct<X14>(triple, argOrder, StructKindFlags.X14, ref ret, ref pass);
            TestPassReturnStruct<X15>(triple, argOrder, StructKindFlags.X15, ref ret, ref pass);
            TestPassReturnStruct<X16>(triple, argOrder, StructKindFlags.X16, ref ret, ref pass);

            TestPassReturnStruct<Empty>(triple, argOrder, StructKindFlags.Empty, ref ret, ref pass);

            return (ret, pass);
        }

        private static void TestPassReturnStruct<T>(PlatformTriple triple, SelftestArgumentOrder argOrder, StructKindFlags flag, ref StructKindFlags byvalReturn, ref StructKindFlags byvalPass) where T : struct {
            byvalReturn |= GetFlagFor(TestReturnForStruct<T>(triple, argOrder), flag);
            byvalPass |= GetFlagFor(TestPassByValue<T>(triple), flag);
        }

        #region Test value type ret buffer classifications
        private static readonly MethodInfo Self_RetBufTest = typeof(AbiSelftest).GetMethod(nameof(RetBufTest), AllFlgs)!;
        private static readonly MethodInfo Self_RetBufTestTarget = typeof(AbiSelftest).GetMethod(nameof(RetBufTestTarget), AllFlgs)!;

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        private static bool TestReturnForStruct<T>(PlatformTriple triple, SelftestArgumentOrder argOrder) where T : struct {
            var RetBufTest_T = Self_RetBufTest.MakeGenericMethod(typeof(T));

            var bufferIsFirst = argOrder switch {
                SelftestArgumentOrder.RetThisArgs => true,
                SelftestArgumentOrder.ThisRetArgs => true,
                SelftestArgumentOrder.ThisArgsRet => false,
                SelftestArgumentOrder.RetArgsThis => true,
                SelftestArgumentOrder.ArgsThisRet => false,
                SelftestArgumentOrder.ArgsRetThis => false,
                _ => true, // if the argument order is unknown, default to true
            };

            using (triple.PinMethodIfNeeded(RetBufTest_T))
            using (triple.PinMethodIfNeeded(Self_RetBufTestTarget)) {
                var from = triple.GetNativeMethodBody(RetBufTest_T);
                var to = triple.GetNativeMethodBody(Self_RetBufTestTarget);

                using (triple.CreateNativeDetour(from, to)) {
                    T value = default;
                    bool hasBuf = false;

                    // the JIT actually gives the return buffer a different address, possibly *because* we're also passing it in as a byref
                    // as a result, we can't rely on them being the same
                    // we *can* however rely on them being fairly close, as they're both on the stack
                    // that *also* means that we don't actually need `ref value`
                    // removing it also removes the potential issues caused by stack spillage
                    value = RetBufTest<T>(ref hasBuf, bufferIsFirst, ref hasBuf);
                    _ = value; // make the compiler not yell at me

                    // we return true if it has no retbuf
                    return !hasBuf;
                }
            }
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        private static T RetBufTest<T>(ref bool hasBuf1, in bool bufFirst, ref bool hasBuf2) where T : struct {
            throw new InvalidOperationException("Call should have been detoured");
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        private static unsafe void RetBufTestTarget(IntPtr a, IntPtr b, IntPtr c, IntPtr d) {
            if (a == c) {
                // a and d are hasBuf
                ref var hasBuf = ref Unsafe.AsRef<bool>((void*) a);
                // b is bufFirst
                var bufFirst = Unsafe.AsRef<bool>((void*) b);

                if (bufFirst) {
                    hasBuf = false;
                } else {
                    hasBuf = IsClose(a, d);
                }
            } else if (b == d) {
                // b and e are hasBuf
                ref var hasBuf = ref Unsafe.AsRef<bool>((void*) b);
                // c is bufFirst
                var bufFirst = Unsafe.AsRef<bool>((void*) c);

                if (bufFirst) {
                    // if the buffer comes first, then in this branch, it should always have a ret buf, but lets check d anyway
                    hasBuf = IsClose(a, b);
                } else {
                    // strictly, this is possibly wrong, but I worry about destroying the stack accidentally if I add another argument
                    hasBuf = false;
                }
            } else {
                ThrowFunkyAbi(a, b, c, d, IntPtr.Zero);
            }
        }

        #endregion

        #region Test value type pass-by-value classifications
        private static readonly MethodInfo Self_PassByValueTest = typeof(AbiSelftest).GetMethod(nameof(PassByValueTest), AllFlgs)!;
        private static readonly MethodInfo Self_PassByValueTarget = typeof(AbiSelftest).GetMethod(nameof(PassByValueTarget), AllFlgs)!;

        private static bool TestPassByValue<T>(PlatformTriple triple) where T : struct {
            var PassByValueTest_T = Self_PassByValueTest.MakeGenericMethod(typeof(T));

            // set up our sentinel data and value
            Span<byte> sentinelData = stackalloc byte[typeof(T).GetManagedSize()];
            // the data we want to fill in should never look like a pointer
            // it should also be somewhat varied so that garbage will not look like it
            // we'll do decreasing from 0xff in high->low bytes
            for (int i = 0; i < sentinelData.Length; i++) {
                sentinelData[sentinelData.Length - i - 1] = (byte)(0xff - i); 
            }

            var value = MemoryMarshal.Read<T>(sentinelData);

            using (triple.PinMethodIfNeeded(PassByValueTest_T))
            using (triple.PinMethodIfNeeded(Self_PassByValueTarget)) {
                var from = triple.GetNativeMethodBody(PassByValueTest_T);
                var to = triple.GetNativeMethodBody(Self_PassByValueTarget);

                using (triple.CreateNativeDetour(from, to)) {
                    int stackRef = 0;
                    return PassByValueTest(value, ref stackRef, sentinelData);
                }
            }
        }

        [MethodImpl(MethodImplOptionsEx.NoInlining)]
        private static bool PassByValueTest<T>(T value, ref int stackRef, ReadOnlySpan<byte> sentinel) where T : struct {
            throw new InvalidOperationException("Call should have been detoured");
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveOptimization)]
        private static unsafe bool PassByValueTarget(IntPtr value, IntPtr stackRef, ReadOnlySpan<byte> sentinel) {
            // the first check we do is see if the value directly in `value` matches our sentinel
            // if it does, chances are it's properly passed by value in register
            Span<byte> valueData = stackalloc byte[Math.Max(IntPtr.Size, sentinel.Length)];
            MemoryMarshal.Write(valueData, ref value);

            if (valueData.Slice(0, sentinel.Length).SequenceEqual(sentinel)) {
                // the by-value value matches our sentinel, pass-by-value success
                return true;
            }

            // if our sentinel test failed, we'll compare value to stackRef to see if we think they're close
            if (IsClose(stackRef, value)) {
                // if they're close, it's probably pass by reference
                return false;
            }

            // if neither match, make a *wild* guess that it's by-value, our sentinel shenanigans just didn't work.
            // this is because the stack reference is *going* to be fairly close to the value if its byref, at least
            // on any sane platform.
            return true;
        }

        #endregion

        #region Struct kinds

        #region HFA
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaFloat1 {
            public float A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaFloat2 {
            public float A;
            public float B;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaFloat3 {
            public float A;
            public float B;
            public float C;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaFloat4 {
            public float A;
            public float B;
            public float C;
            public float D;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaDouble1 {
            public double A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaDouble2 {
            public double A;
            public double B;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaDouble3 {
            public double A;
            public double B;
            public double C;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HfaDouble4 {
            public double A;
            public double B;
            public double C;
            public double D;
        }
        #endregion

        #region Small sized
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Byte1 {
            public byte A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Byte2 {
            public byte A;
            public byte B;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Byte3 {
            public byte A;
            public byte B;
            public byte C;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Byte4 {
            public byte A;
            public byte B;
            public byte C;
            public byte D;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Short1 {
            public short A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Short2 {
            public short A;
            public short B;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Short3 {
            public short A;
            public short B;
            public short C;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Short4 {
            public short A;
            public short B;
            public short C;
            public short D;
        }
        #endregion

        #region Int fields
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Int1 {
            public int A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Int2 {
            public int A;
            public int B;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Int3 {
            public int A;
            public int B;
            public int C;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Int4 {
            public int A;
            public int B;
            public int C;
            public int D;
        }
        #endregion

        #region Long fields
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Long1 {
            public long A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Long2 {
            public long A;
            public long B;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Long3 {
            public long A;
            public long B;
            public long C;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Long4 {
            public long A;
            public long B;
            public long C;
            public long D;
        }
        #endregion

        #region Explicitly sized structs
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        private struct X_1 { }
        [StructLayout(LayoutKind.Sequential, Size = 2)]
        private struct X_2 { }
        [StructLayout(LayoutKind.Sequential, Size = 3)]
        private struct X_3 { }
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        private struct X_4 { }
        [StructLayout(LayoutKind.Sequential, Size = 5)]
        private struct X_5 { }
        [StructLayout(LayoutKind.Sequential, Size = 6)]
        private struct X_6 { }
        [StructLayout(LayoutKind.Sequential, Size = 7)]
        private struct X_7 { }
        [StructLayout(LayoutKind.Sequential, Size = 8)]
        private struct X_8 { }
        [StructLayout(LayoutKind.Sequential, Size = 9)]
        private struct X_9 { }
        [StructLayout(LayoutKind.Sequential, Size = 10)]
        private struct X10 { }
        [StructLayout(LayoutKind.Sequential, Size = 11)]
        private struct X11 { }
        [StructLayout(LayoutKind.Sequential, Size = 12)]
        private struct X12 { }
        [StructLayout(LayoutKind.Sequential, Size = 13)]
        private struct X13 { }
        [StructLayout(LayoutKind.Sequential, Size = 14)]
        private struct X14 { }
        [StructLayout(LayoutKind.Sequential, Size = 15)]
        private struct X15 { }
        [StructLayout(LayoutKind.Sequential, Size = 16)]
        private struct X16 { }
        #endregion

        #region Odd sizes
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct OddSize3 {
            public short S;
            public byte A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct OddSize5 {
            public int I;
            public byte A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct OddSize6 {
            public int I;
            public short A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct OddSize7 {
            public int I;
            public short S;
            public byte A;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct OddSize9 {
            public int I;
            public int S;
            public byte A;
        }
        #endregion

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Empty { }

        #endregion
    }
}
