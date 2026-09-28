extern alias New;
using New::MonoMod.RuntimeDetour.Generics;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.RuntimeDetour.Generics
{
    [Collection("RuntimeDetour")]
    public class GenericHookTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        private static GenericHook Hook(MethodInfo src, string targetName, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookTest), targetName), vectors);

        private static GenericHook Hook(MethodInfo src, string targetName, AutoPrimeMode mode, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookTest), targetName), vectors, mode);

        private static bool ValueTypeAutoPrimeSupported => GenericHook.SupportsValueTypeAutoPrime;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Replace<T>(T value) => $"hook<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string WrapF<T>(Func<T, string> orig, T value) => $"wrap[{orig(value)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string AorigF<T>(Func<T, string> orig, T value) => $"A({orig(value)})";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string BorigF<T>(Func<T, string> orig, T value) => $"B({orig(value)})";
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string CorigF<T>(Func<T, string> orig, T value) => $"C({orig(value)})";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string KeyA<T>(T value) => $"A:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string KeyB<T>(T value) => $"B:{value}";
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string TwoReplace<T1, T2>(T1 a, T2 b) => $"hook<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string TwoWrap<T1, T2>(Func<T1, T2, string> orig, T1 a, T2 b) => $"wrap[{orig(a, b)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string InstReplace<T>(InstGM self, T value) => $"hook<{typeof(T).Name}>:{self.Tag}:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string InstWrap<T>(Func<InstGM, T, string> orig, InstGM self, T value) => $"wrap[{orig(self, value)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string GciReplace<T>(GcInst<T> self) => $"hook<{typeof(T).Name}>:{self.Value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string GciWrap<T>(Func<GcInst<T>, string> orig, GcInst<T> self) => $"wrap[{orig(self)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string GcgmReplace<TC, TM>(GcGm<TC> self, TM second) => $"hook<{typeof(TC).Name},{typeof(TM).Name}>:{self.First}+{second}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string GcgmWrap<TC, TM>(Func<GcGm<TC>, TM, string> orig, GcGm<TC> self, TM second) => $"wrap[{orig(self, second)}]";

        [GenericHookFact]
        public void UnsharedWithOrig()
        {
            var src = GetMethod(typeof(Src), nameof(Src.UnsharedOrig));
            Assert.False(IsShared(src, typeof(int)));

            Assert.Equal("orig<Int32>:5", Src.UnsharedOrig<int>(5));
            using var hook = Hook(src, nameof(WrapF), [typeof(int)]);
            Assert.Equal("wrap[orig<Int32>:5]", Src.UnsharedOrig<int>(5));
        }

        [GenericHookFact]
        public void UnsharedWithoutOrig()
        {
            var src = GetMethod(typeof(Src), nameof(Src.UnsharedNoOrig));
            Assert.False(IsShared(src, typeof(int)));

            Assert.Equal("orig<Int32>:5", Src.UnsharedNoOrig<int>(5));
            using var hook = Hook(src, nameof(Replace), [typeof(int)]);
            Assert.Equal("hook<Int32>:5", Src.UnsharedNoOrig<int>(5));
        }

        [GenericHookFact]
        public void SharedWithOrig()
        {
            var src = GetMethod(typeof(Src), nameof(Src.SharedOrig));
            Assert.True(IsShared(src, typeof(string)));

            Assert.Equal("orig<String>:hi", Src.SharedOrig<string>("hi"));
            using var hook = Hook(src, nameof(WrapF), [typeof(string)]);
            Assert.Equal("wrap[orig<String>:hi]", Src.SharedOrig<string>("hi"));
        }

        [GenericHookFact]
        public void SharedWithoutOrig()
        {
            var src = GetMethod(typeof(Src), nameof(Src.SharedNoOrig));
            Assert.True(IsShared(src, typeof(string)));

            Assert.Equal("orig<String>:hi", Src.SharedNoOrig<string>("hi"));
            using var hook = Hook(src, nameof(Replace), [typeof(string)]);
            Assert.Equal("hook<String>:hi", Src.SharedNoOrig<string>("hi"));
        }

        [GenericHookFact]
        public void SharedAndUnsharedOnSameHook()
        {
            var src = GetMethod(typeof(Src), nameof(Src.Both));
            using var hook = Hook(src, nameof(Replace), [typeof(int)], [typeof(string)]);

            Assert.Equal("hook<Int32>:7", Src.Both<int>(7));
            Assert.Equal("hook<String>:hi", Src.Both<string>("hi"));
        }

        [GenericHookFact]
        public void ChainingSameKeyUnshared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.ChainSameUnshared));
            using var a = Hook(src, nameof(AorigF), [typeof(int)]);
            using var b = Hook(src, nameof(BorigF), [typeof(int)]);

            Assert.Equal("B(A(orig<Int32>:9))", Src.ChainSameUnshared<int>(9));
        }

        [GenericHookFact]
        public void ChainingDifferentKeysUnshared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.ChainDiffUnshared));
            using var hi = Hook(src, nameof(KeyA), [typeof(int)]);
            using var hl = Hook(src, nameof(KeyB), [typeof(long)]);

            Assert.Equal("A:3", Src.ChainDiffUnshared<int>(3));
            Assert.Equal("B:2", Src.ChainDiffUnshared<long>(2L));
        }

        [GenericHookFact]
        public void ChainingDifferentKeysShared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.ChainDiffShared));
            using var hs = Hook(src, nameof(KeyA), [typeof(string)]);
            using var ho = Hook(src, nameof(KeyB), [typeof(object)]);

            Assert.Equal("A:s", Src.ChainDiffShared<string>("s"));
            Assert.Equal("B:42", Src.ChainDiffShared<object>(42));
            Assert.Equal("orig<GenBase>:B", Src.ChainDiffShared<GenBase>(new GenBase()));
        }

        [GenericHookFact]
        public void ChainingSameKeyShared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.ChainSameShared));
            using var a = Hook(src, nameof(AorigF), [typeof(string)]);
            using var b = Hook(src, nameof(BorigF), [typeof(string)]);

            Assert.Equal("B(A(orig<String>:z))", Src.ChainSameShared<string>("z"));
        }

        [GenericHookFact]
        public void ChainingSameKeySharedThreeHooks()
        {
            var src = GetMethod(typeof(Src), nameof(Src.ChainTripleShared));
            using var a = Hook(src, nameof(AorigF), [typeof(string)]);
            using var b = Hook(src, nameof(BorigF), [typeof(string)]);
            using var c = Hook(src, nameof(CorigF), [typeof(string)]);

            Assert.Equal("C(B(A(orig<String>:z)))", Src.ChainTripleShared<string>("z"));
        }

        [GenericHookFact]
        public void ChainingSameKeySharedDisposeMiddleRefolds()
        {
            var src = GetMethod(typeof(Src), nameof(Src.ChainDisposeMiddleShared));
            using var a = Hook(src, nameof(AorigF), [typeof(string)]);
            var b = Hook(src, nameof(BorigF), [typeof(string)]);
            using var c = Hook(src, nameof(CorigF), [typeof(string)]);

            Assert.Equal("C(B(A(orig<String>:z)))", Src.ChainDisposeMiddleShared<string>("z"));

            b.Dispose();
            Assert.Equal("C(A(orig<String>:z))", Src.ChainDisposeMiddleShared<string>("z"));
        }

        [GenericHookFact]
        public void TwoGenericSameAllValue()
        {
            var src = GetMethod(typeof(SrcTwo), nameof(SrcTwo.SameValue));
            Assert.False(IsShared(src, typeof(int), typeof(long)));

            using var hook = Hook(src, nameof(TwoReplace), [typeof(int), typeof(long)]);
            Assert.Equal("hook<Int32,Int64>:1|2", SrcTwo.SameValue<int, long>(1, 2L));
        }

        [GenericHookFact]
        public void TwoGenericSameAllReference()
        {
            var src = GetMethod(typeof(SrcTwo), nameof(SrcTwo.SameRef));
            Assert.True(IsShared(src, typeof(string), typeof(object)));

            using var hook = Hook(src, nameof(TwoReplace), [typeof(string), typeof(object)]);
            Assert.Equal("hook<String,Object>:a|z", SrcTwo.SameRef<string, object>("a", "z"));
        }

        [GenericHookFact]
        public void TwoGenericMixedValueReference()
        {
            var src = GetMethod(typeof(SrcTwo), nameof(SrcTwo.Mixed));
            Assert.True(IsShared(src, typeof(int), typeof(string)));
            Assert.True(IsShared(src, typeof(string), typeof(int)));

            using var hook = Hook(src, nameof(TwoReplace), 
                [typeof(int), typeof(string)], [typeof(string), typeof(int)],
                [typeof(long), typeof(object)], [typeof(object), typeof(long)]);

            Assert.Equal("hook<Int32,String>:5|x", SrcTwo.Mixed<int, string>(5, "x"));
            Assert.Equal("hook<String,Int32>:x|5", SrcTwo.Mixed<string, int>("x", 5));
            Assert.Equal("hook<Int64,Object>:7|o", SrcTwo.Mixed<long, object>(7L, "o"));
            Assert.Equal("hook<Object,Int64>:o|7", SrcTwo.Mixed<object, long>("o", 7L));
        }

        [GenericHookFact]
        public void InstanceGenericMethodUnsharedWithOrig()
        {
            var src = GetMethod(typeof(InstGM), nameof(InstGM.Wrap));
            using var hook = Hook(src, nameof(InstWrap), [typeof(int)]);

            Assert.Equal("wrap[orig<Int32>:H:5]", new InstGM("H").Wrap<int>(5));
        }

        [GenericHookFact]
        public void InstanceGenericMethodSharedWithoutOrig()
        {
            var src = GetMethod(typeof(InstGM), nameof(InstGM.Wrap));
            Assert.True(IsShared(src, typeof(string)));

            using var hook = Hook(src, nameof(InstReplace), [typeof(string)]);
            Assert.Equal("hook<String>:H:x", new InstGM("H").Wrap<string>("x"));
        }

        [GenericHookFact]
        public void GenericClassInstanceThisObjectUnshared()
        {
            var src = GetMethod(typeof(GcInst<>), nameof(GcInst<object>.Describe));
            using var hook = Hook(src, nameof(GciReplace), [typeof(int)]);

            Assert.Equal("hook<Int32>:5", new GcInst<int>(5).Describe());
        }

        [GenericHookFact]
        public void GenericClassInstanceThisObjectSharedWithOrig()
        {
            var src = GetMethod(typeof(GcInst<>), nameof(GcInst<object>.Describe));
            using var hook = Hook(src, nameof(GciWrap), [typeof(string)]);

            Assert.Equal("wrap[orig<String>:x]", new GcInst<string>("x").Describe());
        }

        [GenericHookFact]
        public void GenericClassStaticMethodTableUnshared()
        {
            var src = GetMethod(typeof(GcStat<>), nameof(GcStat<object>.Make));
            using var hook = Hook(src, nameof(Replace), [typeof(int)]);

            Assert.Equal("hook<Int32>:5", GcStat<int>.Make(5));
        }

        [GenericHookFact]
        public void GenericClassStaticMethodTableSharedWithOrig()
        {
            var src = GetMethod(typeof(GcStat<>), nameof(GcStat<object>.Make));
            using var hook = Hook(src, nameof(WrapF), [typeof(string)]);

            Assert.Equal("wrap[orig<String>:x]", GcStat<string>.Make("x"));
        }

        [GenericHookFact]
        public void GenericClassGenericMethodMixed()
        {
            var src = GetMethod(typeof(GcGm<>), nameof(GcGm<object>.Combine));
            var src1 = GetMethod(typeof(GcGm<string>), nameof(GcGm<object>.Combine));
            var src2 = GetMethod(typeof(GcGm<int>), nameof(GcGm<object>.Combine));
            var src3 = GetMethod(typeof(GcGm<int>), nameof(GcGm<object>.Combine));
            Assert.True(IsShared(src1, typeof(int)));
            Assert.False(IsShared(src2, typeof(long)));
            Assert.True(IsShared(src3, typeof(string)));

            using var hook = Hook(src, nameof(GcgmReplace),
                [typeof(string), typeof(int)],
                [typeof(int), typeof(long)],
                [typeof(int), typeof(string)]);

            Assert.Equal("hook<String,Int32>:F+5", new GcGm<string>("F").Combine<int>(5));
            Assert.Equal("hook<Int32,Int64>:1+2", new GcGm<int>(1).Combine<long>(2L));
            Assert.Equal("hook<Int32,String>:1+x", new GcGm<int>(1).Combine<string>("x"));
        }

        [GenericHookFact]
        public void CatchNoneDoesNotCatchDerived()
        {
            var src = GetMethod(typeof(Src), nameof(Src.CatchNone));
            using var hook = Hook(src, nameof(Replace), AutoPrimeMode.None, [typeof(GenBase)]);

            Assert.Equal("hook<GenBase>:B", Src.CatchNone<GenBase>(new GenBase()));
            Assert.Equal("orig<GenDerived>:D", Src.CatchNone<GenDerived>(new GenDerived()));
        }

        [GenericHookFact]
        public void CatchAllAutoPrimesDerivedAsOwnKey()
        {
            var src = GetMethod(typeof(Src), nameof(Src.CatchAll));
            using var hook = Hook(src, nameof(Replace), AutoPrimeMode.All, [typeof(GenBase)]);

            Assert.Equal("hook<GenBase>:B", Src.CatchAll<GenBase>(new GenBase()));
            Assert.Equal("hook<GenDerived>:D", Src.CatchAll<GenDerived>(new GenDerived()));
        }

        [GenericHookFact]
        public void CatchCompatibleBaseEntryCatchesDerived()
        {
            var src = GetMethod(typeof(Src), nameof(Src.CatchCompatible));
            using var hook = Hook(src, nameof(Replace), AutoPrimeMode.Compatible, [typeof(GenBase)]);

            Assert.Equal("hook<GenBase>:B", Src.CatchCompatible<GenBase>(new GenBase()));
            Assert.Equal("hook<GenDerived>:D", Src.CatchCompatible<GenDerived>(new GenDerived()));
        }

        [GenericHookFact]
        public void CatchCompatibleUnrelatedNotCaught()
        {
            var src = GetMethod(typeof(Src), nameof(Src.CatchCompatibleUnrelated));
            using var hook = Hook(src, nameof(Replace), AutoPrimeMode.Compatible, [typeof(GenBase)]);

            Assert.Equal("orig<GenUnrelated>:U", Src.CatchCompatibleUnrelated<GenUnrelated>(new GenUnrelated()));
        }

        [GenericHookFact]
        public void MixedKeyAllAutoDiscoversReferencePosition()
        {
            var src = GetMethod(typeof(SrcTwo), nameof(SrcTwo.CompatPerPosition));
            using var hook = Hook(src, nameof(TwoReplace), AutoPrimeMode.All, [typeof(GenBase), typeof(int)]);

            Assert.Equal("hook<GenBase,Int32>:B|5", SrcTwo.CompatPerPosition<GenBase, int>(new GenBase(), 5));

            var derived = SrcTwo.CompatPerPosition<GenDerived, int>(new GenDerived(), 5);
            Assert.Equal(
                ValueTypeAutoPrimeSupported ? "hook<GenDerived,Int32>:D|5" : "orig<GenDerived,Int32>:D|5",
                derived);

            Assert.Equal("orig<GenBase,Int64>:B|7", SrcTwo.CompatPerPosition<GenBase, long>(new GenBase(), 7L));
        }

        [GenericHookFact]
        public void MixedKeyCompatibleAutoDiscoversAssignableReference()
        {
            var src = GetMethod(typeof(SrcTwo), nameof(SrcTwo.CompatPerPosition));
            using var hook = Hook(src, nameof(TwoReplace), AutoPrimeMode.Compatible, [typeof(GenBase), typeof(int)]);

            Assert.Equal("hook<GenBase,Int32>:B|5", SrcTwo.CompatPerPosition<GenBase, int>(new GenBase(), 5));

            var derived = SrcTwo.CompatPerPosition<GenDerived, int>(new GenDerived(), 5);
            Assert.Equal(
                ValueTypeAutoPrimeSupported ? "hook<GenDerived,Int32>:D|5" : "orig<GenDerived,Int32>:D|5",
                derived);

            Assert.Equal("orig<GenUnrelated,Int32>:U|5", SrcTwo.CompatPerPosition<GenUnrelated, int>(new GenUnrelated(), 5));
        }

        [GenericHookFact]
        public void ValueTypeInstantiationIsCaughtWhenExplicitlyPrimed()
        {
            var src = GetMethod(typeof(SrcTwo), nameof(SrcTwo.CompatPerPosition));
            using var hook = Hook(src, nameof(TwoReplace), [typeof(GenDerived), typeof(long)]);

            Assert.Equal("hook<GenDerived,Int64>:D|7", SrcTwo.CompatPerPosition<GenDerived, long>(new GenDerived(), 7L));
        }

        [GenericHookFact]
        public void PrimeAfterConstructionUnshared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.PrimeAfterUnshared));
            using var hook = new GenericHook(src, GetMethod(typeof(GenericHookTest), nameof(Replace)));

            Assert.Equal("orig<Int32>:5", Src.PrimeAfterUnshared<int>(5));
            hook.Prime(typeof(int));
            Assert.Equal("hook<Int32>:5", Src.PrimeAfterUnshared<int>(5));
        }

        [GenericHookFact]
        public void PrimeAfterConstructionShared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.PrimeAfterShared));
            using var hook = new GenericHook(src, GetMethod(typeof(GenericHookTest), nameof(Replace)));

            Assert.Equal("orig<String>:x", Src.PrimeAfterShared<string>("x"));
            hook.Prime(typeof(string));
            Assert.Equal("hook<String>:x", Src.PrimeAfterShared<string>("x"));
        }

        [GenericHookFact]
        public void PrimeConstructorPrimesListedKeysOnlyAndIsIdempotent()
        {
            var src = GetMethod(typeof(Src), nameof(Src.DoublePrime));
            using var hook = Hook(src, nameof(Replace), [typeof(int)]);

            hook.Prime(typeof(int));
            Assert.Equal("hook<Int32>:5", Src.DoublePrime<int>(5));
            Assert.Equal("orig<Int64>:5", Src.DoublePrime<long>(5L));
        }

        [GenericHookFact]
        public void DisposeRestoresOriginalUnshared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.DisposeUnshared));
            var hook = Hook(src, nameof(Replace), [typeof(int)]);

            Assert.Equal("hook<Int32>:5", Src.DisposeUnshared<int>(5));
            hook.Dispose();
            Assert.Equal("orig<Int32>:5", Src.DisposeUnshared<int>(5));
        }

        [GenericHookFact]
        public void DisposeRestoresOriginalShared()
        {
            var src = GetMethod(typeof(Src), nameof(Src.DisposeShared));
            var hook = Hook(src, nameof(Replace), [typeof(string)]);

            Assert.Equal("hook<String>:x", Src.DisposeShared<string>("x"));
            hook.Dispose();
            Assert.Equal("orig<String>:x", Src.DisposeShared<string>("x"));
        }

        [GenericHookFact]
        public void CtorRejectsClosedSource()
        {
            var closed = GetMethod(typeof(Src), nameof(Src.GenericHookCtor)).MakeGenericMethod(typeof(int));
            Assert.Throws<ArgumentException>(() => new GenericHook(closed, GetMethod(typeof(GenericHookTest), nameof(Replace))));
        }

        [GenericHookFact]
        public void CtorRejectsNullSource()
        {
            Assert.Throws<ArgumentNullException>(() => new GenericHook(null!, GetMethod(typeof(GenericHookTest), nameof(Replace))));
        }

        [GenericHookFact]
        public void CtorRejectsNullTarget()
        {
            Assert.Throws<ArgumentNullException>(() => new GenericHook(GetMethod(typeof(Src), nameof(Src.GenericHookCtor)), null!));
        }
    }
}
