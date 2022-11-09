using System;

namespace MonoMod.Core.Platforms {
    public interface IAltEntryFactory {

        IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle);

    }
}
