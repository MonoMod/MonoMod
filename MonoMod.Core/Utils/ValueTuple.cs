#if !NET462_OR_GREATER
namespace System {
    // Note: these are not actually exact polyfills for ValueTuple as defined in the BCL, because those expose fields directly, and let the fields be mutable.
    // For our uses though, these are plenty, and are *far* smaller.
    internal record struct ValueTuple<T1>(T1 Item1);
    internal record struct ValueTuple<T1, T2>(T1 Item1, T2 Item2);
    internal record struct ValueTuple<T1, T2, T3>(T1 Item1, T2 Item2, T3 Item3);
    internal record struct ValueTuple<T1, T2, T3, T4>(T1 Item1, T2 Item2, T3 Item3, T4 Item4);
    internal record struct ValueTuple<T1, T2, T3, T4, T5>(T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5);
    internal record struct ValueTuple<T1, T2, T3, T4, T5, T6>(T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5, T6 Item6);
    internal record struct ValueTuple<T1, T2, T3, T4, T5, T6, T7>(T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5, T6 Item6, T7 Item7);
    internal record struct ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>(T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5, T6 Item6, T7 Item7, TRest Rest);
}
#endif
