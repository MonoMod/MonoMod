using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public sealed class SortableDetourComparer<T> : IComparer<T> where T : ISortableDetour {
        public static readonly SortableDetourComparer<T> Instance = new SortableDetourComparer<T>();
        public int Compare(T a, T b) {
            if (a.Before.Contains(b.ID))
                return -1;
            if (b.Before.Contains(a.ID))
                return 1;

            int delta = a.Priority - b.Priority;
            if (delta != 0)
                return delta;

            return a.GlobalIndex - b.GlobalIndex;
        }
    }
}
