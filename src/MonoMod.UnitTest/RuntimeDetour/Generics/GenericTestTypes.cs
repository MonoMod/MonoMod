using System.Runtime.CompilerServices;
using Xunit;

namespace MonoMod.UnitTest.RuntimeDetour.Generics
{
    public sealed class GenericHookFactAttribute : FactAttribute
    {
        public GenericHookFactAttribute()
        {
            /*
            if (false)
            {
                Skip = $"Generic hooking is not supported on this environment ({PlatformDetection.OS}, {PlatformDetection.Runtime}, {PlatformDetection.Architecture}).";
            }
            */
        }
    }

    public class GenBase
    {
        public override string ToString() => "B";
    }

    public class GenDerived : GenBase
    {
        public override string ToString() => "D";
    }

    public class GenUnrelated
    {
        public override string ToString() => "U";
    }

    public static class Src
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string UnsharedOrig<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string UnsharedNoOrig<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string SharedOrig<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string SharedNoOrig<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Both<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ChainSameUnshared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ChainDiffUnshared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ChainSameShared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ChainDiffShared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string PrimeAfterUnshared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string PrimeAfterShared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string DisposeUnshared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string DisposeShared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string DoublePrime<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CatchNone<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CatchAll<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CatchCompatible<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CatchCompatibleUnrelated<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GenericHookCtor<T>(T value) => $"orig<{typeof(T).Name}>:{value}";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ChainTripleShared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ChainDisposeMiddleShared<T>(T value) => $"orig<{typeof(T).Name}>:{value}";

    }

    public static class SrcTwo
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string SameValue<T1, T2>(T1 a, T2 b) => $"orig<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string SameRef<T1, T2>(T1 a, T2 b) => $"orig<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Mixed<T1, T2>(T1 a, T2 b) => $"orig<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CompatPerPosition<T1, T2>(T1 a, T2 b) => $"orig<{typeof(T1).Name},{typeof(T2).Name}>:{a}|{b}";
    }

    public class InstGM
    {
        internal string Tag;
        public InstGM(string tag) => Tag = tag;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Wrap<T>(T value) => $"orig<{typeof(T).Name}>:{Tag}:{value}";
    }

    public class GcInst<T>
    {
        internal T Value;
        public GcInst(T value) => Value = value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Describe() => $"orig<{typeof(T).Name}>:{Value}";
    }

    public static class GcStat<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Make(T value) => $"orig<{typeof(T).Name}>:{value}";
    }

    public class GcGm<TC>
    {
        internal TC First;
        public GcGm(TC first) => First = first;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Combine<TM>(TM second) => $"orig<{typeof(TC).Name},{typeof(TM).Name}>:{First}+{second}";
    }
}