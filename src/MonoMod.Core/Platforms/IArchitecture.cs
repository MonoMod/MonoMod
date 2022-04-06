using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface IArchitecture {
        ArchitectureFeature Features { get; }

        BytePatternCollection KnownMethodThunks { get; }
    }
}
