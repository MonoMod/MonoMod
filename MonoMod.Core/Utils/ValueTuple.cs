#if !NET462_OR_GREATER
namespace System {
    internal record struct ValueTuple<T1>(T1 Item1);
    internal record struct ValueTuple<T1, T2>(T1 Item1, T2 Item2);
    internal record struct ValueTuple<T1, T2, T3>(T1 Item1, T2 Item2, T3 Item3);
}
#endif
