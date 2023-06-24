namespace MonoMod.Packer {
    public enum TypeMergeMode {
        DoNotMerge,
        UnifyIdentical,
        MergeLayoutIdentical,
        MergeWhenPossible,
    }

    internal static class TypeMergeModeExtra {
        public const int MinValue = (int) TypeMergeMode.DoNotMerge;
        public const int MaxValue = (int) TypeMergeMode.MergeWhenPossible;
    }
}
