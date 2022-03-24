using System;

namespace MonoMod.Core {
    [Flags]
    public enum RuntimeFeature {
        PreciseGC = 0x01,
        CompileMethodHook = 0x02,
        // No runtime supports this *at all* at the moment, but it's here for future use
        ILDetour = 0x04,
        GenericSharing = 0x08,
        // TODO: what other runtime feature flags would be useful to have?
    }
}
