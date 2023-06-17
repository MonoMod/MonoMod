namespace MonoMod.Packer {
    public enum BaseTypeMergeMode {
        Exact,
        AllowMoreDerived,
    }

    internal static class BaseTypeMergeModeExtra {
        public const int MinValue = (int) BaseTypeMergeMode.Exact;
        public const int MaxValue = (int) BaseTypeMergeMode.AllowMoreDerived;
    }
}
