using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    internal static class DetourSorter<T> where T : ISortableDetour {

        public static void Sort(List<T> detours) {
            lock (detours) {
                List<T> detoursCopy = new List<T>(detours);
                detours.Clear();
                detours.AddRange(detoursCopy
                    .OrderBy(_, BeforeAfterAll._)
                    .ThenBy(_, BeforeAfter._)
                    .ThenBy(_, Priority._)
                    .ThenBy(_, GlobalIndex._)
                );
            }
        }

        private static T _(T detour) => detour;

        private sealed class BeforeAfterAll : IComparer<T> {
            public static readonly BeforeAfterAll _ = new BeforeAfterAll();
            public int Compare(T a, T b) {
                if (a.Before.Contains("*") && !b.Before.Contains("*"))
                    return -1;
                if (a.After.Contains("*") && !b.After.Contains("*"))
                    return 1;

                return 0;
            }
        }

        private sealed class BeforeAfter : IComparer<T> {
            public static readonly BeforeAfter _ = new BeforeAfter();
            public int Compare(T a, T b) {
                if (a.Before.Contains(b.ID))
                    return -1;
                if (b.Before.Contains(a.ID))
                    return 1;

                if (a.After.Contains(b.ID))
                    return 1;
                if (b.After.Contains(a.ID))
                    return -1;

                return 0;
            }
        }

        private sealed class Priority : IComparer<T> {
            public static readonly Priority _ = new Priority();
            public int Compare(T a, T b) {
                int delta = a.Priority - b.Priority;
                if (delta != 0)
                    return delta;

                return 0;
            }
        }

        private sealed class GlobalIndex : IComparer<T> {
            public static readonly GlobalIndex _ = new GlobalIndex();
            public int Compare(T a, T b) {
                return a.GlobalIndex.CompareTo(b.GlobalIndex);
            }
        }

    }
}
