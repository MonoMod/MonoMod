using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface IAltEntryFactory {

        IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle);

    }
}
