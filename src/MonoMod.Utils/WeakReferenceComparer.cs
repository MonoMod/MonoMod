using System;
using System.Collections.Generic;

namespace MonoMod.Utils
{
    public sealed class WeakReferenceComparer : EqualityComparer<WeakReference>
    {

        public override bool Equals(WeakReference? x, WeakReference? y)
            => ReferenceEquals(x?.SafeGetTarget(), y?.SafeGetTarget()) && x?.SafeGetIsAlive() == y?.SafeGetIsAlive();

        public override int GetHashCode(WeakReference obj)
            => obj.SafeGetTarget()?.GetHashCode() ?? 0;

    }
}
