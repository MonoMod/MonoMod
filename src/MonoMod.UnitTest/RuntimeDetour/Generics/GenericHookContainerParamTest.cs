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
    public class GenericHookContainerParamTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        private static GenericHook Hook(MethodInfo src, string targetName, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookContainerParamTest), targetName), vectors);

        private static GenericHook Hook(MethodInfo src, string targetName, AutoPrimeMode mode, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookContainerParamTest), targetName), vectors, mode);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string RefBoxWrap<K>(Func<K, RefBox<K>, string> orig, K par1, RefBox<K> par2) => $"wrap[{orig(par1, par2)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string ValBoxWrap<K>(Func<K, ValBox<K>, string> orig, K par1, ValBox<K> par2) => $"wrap[{orig(par1, par2)}]";

        [GenericHookFact]
        public void RefContainerValueKeyNone()
        {
            var src = GetMethod(typeof(RefBoxSrc), nameof(RefBoxSrc.ValNone));
            Assert.False(IsShared(src, typeof(int)));

            using var hook = Hook(src, nameof(RefBoxWrap), AutoPrimeMode.None, [typeof(int)]);
            Assert.Equal("wrap[orig<Int32>:5|RefBox(5)]", RefBoxSrc.ValNone<int>(5, new RefBox<int>(5)));
            Assert.Equal("orig<Int64>:7|RefBox(7)", RefBoxSrc.ValNone<long>(7L, new RefBox<long>(7L)));
        }

        [GenericHookFact]
        public void RefContainerValueKeyCompatible()
        {
            var src = GetMethod(typeof(RefBoxSrc), nameof(RefBoxSrc.ValCompatible));
            using var hook = Hook(src, nameof(RefBoxWrap), AutoPrimeMode.Compatible, [typeof(int)]);

            Assert.Equal("wrap[orig<Int32>:5|RefBox(5)]", RefBoxSrc.ValCompatible<int>(5, new RefBox<int>(5)));
            Assert.Equal("orig<Int64>:7|RefBox(7)", RefBoxSrc.ValCompatible<long>(7L, new RefBox<long>(7L)));
        }

        [GenericHookFact]
        public void RefContainerValueKeyAll()
        {
            var src = GetMethod(typeof(RefBoxSrc), nameof(RefBoxSrc.ValAll));
            using var hook = Hook(src, nameof(RefBoxWrap), AutoPrimeMode.All, [typeof(int)]);

            Assert.Equal("wrap[orig<Int32>:5|RefBox(5)]", RefBoxSrc.ValAll<int>(5, new RefBox<int>(5)));
            Assert.Equal("orig<Int64>:7|RefBox(7)", RefBoxSrc.ValAll<long>(7L, new RefBox<long>(7L)));
        }

        [GenericHookFact]
        public void RefContainerRefKeyNone()
        {
            var src = GetMethod(typeof(RefBoxSrc), nameof(RefBoxSrc.RefNone));
            Assert.True(IsShared(src, typeof(GenBase)));

            using var hook = Hook(src, nameof(RefBoxWrap), AutoPrimeMode.None, [typeof(GenBase)]);
            Assert.Equal("wrap[orig<GenBase>:B|RefBox(B)]", RefBoxSrc.RefNone<GenBase>(new GenBase(), new RefBox<GenBase>(new GenBase())));
            Assert.Equal("orig<GenDerived>:D|RefBox(D)", RefBoxSrc.RefNone<GenDerived>(new GenDerived(), new RefBox<GenDerived>(new GenDerived())));
        }

        [GenericHookFact]
        public void RefContainerRefKeyCompatible()
        {
            var src = GetMethod(typeof(RefBoxSrc), nameof(RefBoxSrc.RefCompatible));
            using var hook = Hook(src, nameof(RefBoxWrap), AutoPrimeMode.Compatible, [typeof(GenBase)]);

            Assert.Equal("wrap[orig<GenBase>:B|RefBox(B)]", RefBoxSrc.RefCompatible<GenBase>(new GenBase(), new RefBox<GenBase>(new GenBase())));
            Assert.Equal("wrap[orig<GenDerived>:D|RefBox(D)]", RefBoxSrc.RefCompatible<GenDerived>(new GenDerived(), new RefBox<GenDerived>(new GenDerived())));
            Assert.Equal("orig<GenUnrelated>:U|RefBox(U)", RefBoxSrc.RefCompatible<GenUnrelated>(new GenUnrelated(), new RefBox<GenUnrelated>(new GenUnrelated())));
        }

        [GenericHookFact]
        public void RefContainerRefKeyAll()
        {
            var src = GetMethod(typeof(RefBoxSrc), nameof(RefBoxSrc.RefAll));
            using var hook = Hook(src, nameof(RefBoxWrap), AutoPrimeMode.All, [typeof(GenBase)]);

            Assert.Equal("wrap[orig<GenBase>:B|RefBox(B)]", RefBoxSrc.RefAll<GenBase>(new GenBase(), new RefBox<GenBase>(new GenBase())));
            Assert.Equal("wrap[orig<GenDerived>:D|RefBox(D)]", RefBoxSrc.RefAll<GenDerived>(new GenDerived(), new RefBox<GenDerived>(new GenDerived())));
            Assert.Equal("wrap[orig<GenUnrelated>:U|RefBox(U)]", RefBoxSrc.RefAll<GenUnrelated>(new GenUnrelated(), new RefBox<GenUnrelated>(new GenUnrelated())));
        }

        [GenericHookFact]
        public void ValueContainerValueKeyNone()
        {
            var src = GetMethod(typeof(ValBoxSrc), nameof(ValBoxSrc.ValNone));
            Assert.False(IsShared(src, typeof(int)));

            using var hook = Hook(src, nameof(ValBoxWrap), AutoPrimeMode.None, [typeof(int)]);
            Assert.Equal("wrap[orig<Int32>:5|ValBox(5)]", ValBoxSrc.ValNone<int>(5, new ValBox<int>(5)));
            Assert.Equal("orig<Int64>:7|ValBox(7)", ValBoxSrc.ValNone<long>(7L, new ValBox<long>(7L)));
        }

        [GenericHookFact]
        public void ValueContainerValueKeyCompatible()
        {
            var src = GetMethod(typeof(ValBoxSrc), nameof(ValBoxSrc.ValCompatible));
            using var hook = Hook(src, nameof(ValBoxWrap), AutoPrimeMode.Compatible, [typeof(int)]);

            Assert.Equal("wrap[orig<Int32>:5|ValBox(5)]", ValBoxSrc.ValCompatible<int>(5, new ValBox<int>(5)));
            Assert.Equal("orig<Int64>:7|ValBox(7)", ValBoxSrc.ValCompatible<long>(7L, new ValBox<long>(7L)));
        }

        [GenericHookFact]
        public void ValueContainerValueKeyAll()
        {
            var src = GetMethod(typeof(ValBoxSrc), nameof(ValBoxSrc.ValAll));
            using var hook = Hook(src, nameof(ValBoxWrap), AutoPrimeMode.All, [typeof(int)]);

            Assert.Equal("wrap[orig<Int32>:5|ValBox(5)]", ValBoxSrc.ValAll<int>(5, new ValBox<int>(5)));
            Assert.Equal("orig<Int64>:7|ValBox(7)", ValBoxSrc.ValAll<long>(7L, new ValBox<long>(7L)));
        }

        [GenericHookFact]
        public void ValueContainerRefKeyNone()
        {
            var src = GetMethod(typeof(ValBoxSrc), nameof(ValBoxSrc.RefNone));
            Assert.True(IsShared(src, typeof(GenBase)));

            using var hook = Hook(src, nameof(ValBoxWrap), AutoPrimeMode.None, [typeof(GenBase)]);
            Assert.Equal("wrap[orig<GenBase>:B|ValBox(B)]", ValBoxSrc.RefNone<GenBase>(new GenBase(), new ValBox<GenBase>(new GenBase())));
            Assert.Equal("orig<GenDerived>:D|ValBox(D)", ValBoxSrc.RefNone<GenDerived>(new GenDerived(), new ValBox<GenDerived>(new GenDerived())));
        }

        [GenericHookFact]
        public void ValueContainerRefKeyCompatible()
        {
            var src = GetMethod(typeof(ValBoxSrc), nameof(ValBoxSrc.RefCompatible));
            using var hook = Hook(src, nameof(ValBoxWrap), AutoPrimeMode.Compatible, [typeof(GenBase)]);

            Assert.Equal("wrap[orig<GenBase>:B|ValBox(B)]", ValBoxSrc.RefCompatible<GenBase>(new GenBase(), new ValBox<GenBase>(new GenBase())));
            Assert.Equal("wrap[orig<GenDerived>:D|ValBox(D)]", ValBoxSrc.RefCompatible<GenDerived>(new GenDerived(), new ValBox<GenDerived>(new GenDerived())));
            Assert.Equal("orig<GenUnrelated>:U|ValBox(U)", ValBoxSrc.RefCompatible<GenUnrelated>(new GenUnrelated(), new ValBox<GenUnrelated>(new GenUnrelated())));
        }

        [GenericHookFact]
        public void ValueContainerRefKeyAll()
        {
            var src = GetMethod(typeof(ValBoxSrc), nameof(ValBoxSrc.RefAll));
            using var hook = Hook(src, nameof(ValBoxWrap), AutoPrimeMode.All, [typeof(GenBase)]);

            Assert.Equal("wrap[orig<GenBase>:B|ValBox(B)]", ValBoxSrc.RefAll<GenBase>(new GenBase(), new ValBox<GenBase>(new GenBase())));
            Assert.Equal("wrap[orig<GenDerived>:D|ValBox(D)]", ValBoxSrc.RefAll<GenDerived>(new GenDerived(), new ValBox<GenDerived>(new GenDerived())));
            Assert.Equal("wrap[orig<GenUnrelated>:U|ValBox(U)]", ValBoxSrc.RefAll<GenUnrelated>(new GenUnrelated(), new ValBox<GenUnrelated>(new GenUnrelated())));
        }
    }

    public sealed class RefBox<K>
    {
        public K Value;
        public RefBox(K value) => Value = value;
        public override string ToString() => $"RefBox({Value})";
    }

    public struct ValBox<K>
    {
        public K Value;
        public ValBox(K value) => Value = value;
        public override string ToString() => $"ValBox({Value})";
    }

    public static class RefBoxSrc
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ValNone<K>(K par1, RefBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ValCompatible<K>(K par1, RefBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ValAll<K>(K par1, RefBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefNone<K>(K par1, RefBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefCompatible<K>(K par1, RefBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefAll<K>(K par1, RefBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
    }

    public static class ValBoxSrc
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ValNone<K>(K par1, ValBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ValCompatible<K>(K par1, ValBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ValAll<K>(K par1, ValBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefNone<K>(K par1, ValBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefCompatible<K>(K par1, ValBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefAll<K>(K par1, ValBox<K> par2) => $"orig<{typeof(K).Name}>:{par1}|{par2}";
    }
}
