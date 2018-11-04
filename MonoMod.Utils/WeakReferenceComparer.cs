using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Utils {
    public sealed class WeakReferenceComparer : EqualityComparer<WeakReference> {
        public override bool Equals(WeakReference x, WeakReference y)
            => ReferenceEquals(x.Target, y.Target) && x.IsAlive == y.IsAlive;

        public override int GetHashCode(WeakReference obj)
            => obj.Target?.GetHashCode() ?? 0;
    }
}
