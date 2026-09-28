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
    public class GenericHookStructVirtualRefTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        private static GenericHook Hook(MethodInfo src, string targetName, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookStructVirtualRefTest), targetName), vectors);

        private static GenericHook Hook(MethodInfo src, string targetName, AutoPrimeMode mode, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookStructVirtualRefTest), targetName), vectors, mode);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string StructReplace<T>(T value) => $"hook<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string StructWrap<T>(Func<T, string> orig, T value) => $"wrap[{orig(value)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static T EchoReplace<T>(T value)
        {
            if (value is Vec3 v)
                return (T)(object)new Vec3(v.X + 1, v.Y + 1, v.Z + 1);
            if (value is Big b)
                return (T)(object)new Big(b.A + 1, b.F + 1);
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string SGenReplace<T>(ref SGen<T> self) => $"hook<{typeof(T).Name}>:{self.Value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string SGenWrap<T>(StructThisOrig<T> orig, ref SGen<T> self) => $"wrap[{orig(ref self)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string VBaseReplace<T>(VBase self, T value) => $"hookbase<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string VOverrideReplace<T>(VOverride self, T value) => $"hookover<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string VBaseWrap<T>(Func<VBase, T, string> orig, VBase self, T value) => $"wrap[{orig(self, value)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string TwoSpeakReplace<T1, T2>(VTwo self, T1 a, T2 b) => $"hook<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string RefDoubleReplace<T>(ref T value)
        {
            if (value is int i)
                value = (T)(object)(i + 1);
            else if (value is string s)
                value = (T)(object)(s + "!");
            return $"hook<{typeof(T).Name}>:{value}";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string RefDoubleWrap<T>(RefFunc<T> orig, ref T value)
        {
            var s = orig(ref value);
            if (value is int i)
                value = (T)(object)(i + 100);
            return $"wrap[{s}]";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string OutInitReplace<T>(T seed, out T result)
        {
            result = seed;
            if (result is int i)
                result = (T)(object)(i + 7);
            return $"hook<{typeof(T).Name}>:{result}";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string OutInitWrap<T>(OutFunc<T> orig, T seed, out T result)
        {
            var s = orig(seed, out result);
            if (result is int i)
                result = (T)(object)(i * 10);
            return $"wrap[{s}]";
        }

        [GenericHookFact]
        public void StructArgUnsharedWithoutOrig()
        {
            var src = GetMethod(typeof(SrcStruct), nameof(SrcStruct.Take));
            Assert.False(IsShared(src, typeof(Vec3)));

            using var hook = Hook(src, nameof(StructReplace), [typeof(Vec3)]);
            Assert.Equal("hook<Vec3>:(1,2,3)", SrcStruct.Take<Vec3>(new Vec3(1, 2, 3)));
        }

        [GenericHookFact]
        public void StructArgUnsharedWithOrig()
        {
            var src = GetMethod(typeof(SrcStruct), nameof(SrcStruct.Take));

            using var hook = Hook(src, nameof(StructWrap), [typeof(Vec3)]);
            Assert.Equal("wrap[orig<Vec3>:(1,2,3)]", SrcStruct.Take<Vec3>(new Vec3(1, 2, 3)));
        }

        [GenericHookFact]
        public void SmallStructReturnReplaced()
        {
            var src = GetMethod(typeof(SrcStruct), nameof(SrcStruct.Echo));
            using var hook = Hook(src, nameof(EchoReplace), [typeof(Vec3)]);

            Assert.Equal(new Vec3(2, 3, 4), SrcStruct.Echo<Vec3>(new Vec3(1, 2, 3)));
        }

        [GenericHookFact]
        public void LargeStructReturnReplaced()
        {
            var src = GetMethod(typeof(SrcStruct), nameof(SrcStruct.Echo));
            using var hook = Hook(src, nameof(EchoReplace), [typeof(Big)]);

            Assert.Equal(new Big(11, 61), SrcStruct.Echo<Big>(new Big(10, 60)));
        }

        [GenericHookFact]
        public void StructThisUnsharedWithoutOrig()
        {
            var src = GetMethod(typeof(SGen<>), nameof(SGen<object>.Describe));
            using var hook = Hook(src, nameof(SGenReplace), [typeof(int)]);

            var s = new SGen<int>(5);
            Assert.Equal("hook<Int32>:5", s.Describe());
        }

        [GenericHookFact]
        public void StructThisWithOrig()
        {
            var src = GetMethod(typeof(SGen<>), nameof(SGen<object>.Describe));
            using var hook = Hook(src, nameof(SGenWrap), [typeof(int)]);

            var s = new SGen<int>(9);
            Assert.Equal("wrap[orig<Int32>:9]", s.Describe());
        }

        [GenericHookFact]
        public void VirtualBaseHookDoesNotCatchOverride()
        {
            var src = GetMethod(typeof(VBase), nameof(VBase.Speak));
            using var hook = Hook(src, nameof(VBaseReplace), [typeof(int)]);

            Assert.Equal("hookbase<Int32>:5", new VBase().Speak<int>(5));
            Assert.Equal("hookbase<Int32>:5", new VInherit().Speak<int>(5));
            Assert.Equal("over<Int32>:5", new VOverride().Speak<int>(5));
        }

        [GenericHookFact]
        public void OverrideHookDoesNotCatchBase()
        {
            var src = GetMethod(typeof(VOverride), nameof(VOverride.Speak));
            using var hook = Hook(src, nameof(VOverrideReplace), [typeof(int)]);

            Assert.Equal("hookover<Int32>:5", new VOverride().Speak<int>(5));
            Assert.Equal("base<Int32>:5", new VBase().Speak<int>(5));
        }

        [GenericHookFact]
        public void OverrideHookCaughtThroughVirtualDispatch()
        {
            var src = GetMethod(typeof(VOverride), nameof(VOverride.Speak));
            using var hook = Hook(src, nameof(VOverrideReplace), [typeof(int)]);

            VBase v = new VOverride();
            Assert.Equal("hookover<Int32>:5", v.Speak<int>(5));
        }

        [GenericHookFact]
        public void VirtualBaseHookSharedWithOrig()
        {
            var src = GetMethod(typeof(VBase), nameof(VBase.Speak));
            Assert.True(IsShared(src, typeof(string)));

            using var hook = Hook(src, nameof(VBaseWrap), [typeof(string)]);
            Assert.Equal("wrap[base<String>:hi]", new VBase().Speak<string>("hi"));
        }

        [GenericHookFact]
        public void VirtualMixedKeyValueTypeAutoDiscovery()
        {
            var src = GetMethod(typeof(VTwo), nameof(VTwo.TwoSpeak));
            using var hook = Hook(src, nameof(TwoSpeakReplace), AutoPrimeMode.Compatible, [typeof(GenBase), typeof(int)]);

            Assert.Equal("hook<GenBase,Int32>:B|5", new VTwo().TwoSpeak<GenBase, int>(new GenBase(), 5));

            var derived = new VTwo().TwoSpeak<GenDerived, int>(new GenDerived(), 5);
            Assert.Equal(
                GenericHook.SupportsValueTypeAutoPrime ? "hook<GenDerived,Int32>:D|5" : "orig<GenDerived,Int32>:D|5",
                derived);
        }

        [GenericHookFact]
        public void RefArgUnsharedWithoutOrig()
        {
            var src = GetMethod(typeof(SrcRef), nameof(SrcRef.RefDouble));
            Assert.False(IsShared(src, typeof(int)));

            using var hook = Hook(src, nameof(RefDoubleReplace), [typeof(int)]);
            var x = 5;
            Assert.Equal("hook<Int32>:6", SrcRef.RefDouble<int>(ref x));
            Assert.Equal(6, x);
        }

        [GenericHookFact]
        public void RefArgUnsharedWithOrig()
        {
            var src = GetMethod(typeof(SrcRef), nameof(SrcRef.RefDouble));

            using var hook = Hook(src, nameof(RefDoubleWrap), [typeof(int)]);
            var x = 5;
            Assert.Equal("wrap[orig<Int32>:10]", SrcRef.RefDouble<int>(ref x));
            Assert.Equal(110, x);
        }

        [GenericHookFact]
        public void RefArgSharedWithoutOrig()
        {
            var src = GetMethod(typeof(SrcRef), nameof(SrcRef.RefDouble));
            Assert.True(IsShared(src, typeof(string)));

            using var hook = Hook(src, nameof(RefDoubleReplace), [typeof(string)]);
            var s = "hi";
            Assert.Equal("hook<String>:hi!", SrcRef.RefDouble<string>(ref s));
            Assert.Equal("hi!", s);
        }

        [GenericHookFact]
        public void OutArgUnsharedWithoutOrig()
        {
            var src = GetMethod(typeof(SrcRef), nameof(SrcRef.OutInit));
            Assert.False(IsShared(src, typeof(int)));

            using var hook = Hook(src, nameof(OutInitReplace), [typeof(int)]);
            Assert.Equal("hook<Int32>:12", SrcRef.OutInit<int>(5, out var r));
            Assert.Equal(12, r);
        }

        [GenericHookFact]
        public void OutArgUnsharedWithOrig()
        {
            var src = GetMethod(typeof(SrcRef), nameof(SrcRef.OutInit));

            using var hook = Hook(src, nameof(OutInitWrap), [typeof(int)]);
            Assert.Equal("wrap[orig<Int32>:3]", SrcRef.OutInit<int>(3, out var r));
            Assert.Equal(30, r);
        }
    }

    public delegate string RefFunc<T>(ref T value);
    public delegate string OutFunc<T>(T seed, out T result);
    public delegate string StructThisOrig<T>(ref SGen<T> self);

    public struct Vec3
    {
        public int X;
        public int Y;
        public int Z;
        public Vec3(int x, int y, int z) { X = x; Y = y; Z = z; }
        public override string ToString() => $"({X},{Y},{Z})";
    }

    public struct Big
    {
        public long A;
        public long B;
        public long C;
        public long D;
        public long E;
        public long F;
        public Big(long a, long f) { A = a; B = 0; C = 0; D = 0; E = 0; F = f; }
        public override string ToString() => $"[{A}..{F}]";
    }

    public struct SGen<T>
    {
        public T Value;
        public SGen(T value) => Value = value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Describe() => $"orig<{typeof(T).Name}>:{Value}";
    }

    public static class SrcStruct
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Take<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static T Echo<T>(T value) => value;
    }

    public class VBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual string Speak<T>(T value) => $"base<{typeof(T).Name}>:{value}";
    }

    public class VOverride : VBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override string Speak<T>(T value) => $"over<{typeof(T).Name}>:{value}";
    }

    public class VInherit : VBase
    {
    }

    public class VTwo
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual string TwoSpeak<T1, T2>(T1 a, T2 b) => $"orig<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";
    }

    public static class SrcRef
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RefDouble<T>(ref T value)
        {
            if (value is int i)
                value = (T)(object)(i * 2);
            return $"orig<{typeof(T).Name}>:{value}";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string OutInit<T>(T seed, out T result)
        {
            result = seed;
            return $"orig<{typeof(T).Name}>:{result}";
        }
    }
}
