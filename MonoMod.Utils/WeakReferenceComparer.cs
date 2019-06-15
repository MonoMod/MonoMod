using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Utils {
    public sealed class WeakReferenceComparer : EqualityComparer<WeakReference> {
        public override bool Equals(WeakReference x, WeakReference y)
            => x.IsAlive == y.IsAlive && (x.IsAlive ? ReferenceEquals(x.Target, y.Target) : ReferenceEquals(x, y));

        public override int GetHashCode(WeakReference obj)
            => 0;
    }
}
