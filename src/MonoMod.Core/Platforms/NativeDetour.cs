using System;

namespace MonoMod.Core.Platforms {
    public sealed class NativeDetour : SimpleNativeDetour, ICoreNativeDetour {
        private readonly IDisposable? altEntryHandle;
        
        public IntPtr OrigEntry { get; }
        public bool HasOrigEntry => OrigEntry != IntPtr.Zero;

        internal NativeDetour(
            PlatformTriple triple, NativeDetourInfo detourInfo, Memory<byte> backup, IDisposable? allocHandle,
            IntPtr origEntry, IDisposable? altEntryHandle
        ) : base(triple, detourInfo, backup, allocHandle) {
            OrigEntry = origEntry;
            this.altEntryHandle = altEntryHandle;
        }

        protected override void Cleanup() {
            altEntryHandle?.Dispose();
            base.Cleanup();
        }

        IntPtr ICoreNativeDetour.From => Source;
        IntPtr ICoreNativeDetour.To => Destination;
        bool ICoreNativeDetour.HasOrigEntrypoint => HasOrigEntry;
        IntPtr ICoreNativeDetour.OrigEntrypoint => OrigEntry;

        void ICoreNativeDetour.Undo() => Undo();
    }
}