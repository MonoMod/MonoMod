using System.Collections.Generic;
using System.Linq;

namespace MonoMod.RuntimeDetour {
    public class DetourConfig {
        public string Id { get; }
        public int? Priority { get; }
        /// <summary>
        /// The detour IDs to run before this detour.
        /// </summary>
        public IEnumerable<string> Before { get; }
        /// <summary>
        /// The detour IDs to run after this detour.
        /// </summary>
        public IEnumerable<string> After { get; }

        public DetourConfig(string id, int? priority = null, IEnumerable<string>? before = null, IEnumerable<string>? after = null) {
            Id = id;
            Priority = priority;
            Before = AsFixedSize(before ?? Enumerable.Empty<string>());
            After = AsFixedSize(after ?? Enumerable.Empty<string>());
        }

        private static IEnumerable<string> AsFixedSize(IEnumerable<string> enumerable) {
            if (enumerable == Enumerable.Empty<string>())
                return enumerable;
            if (enumerable is ICollection<string>)
                return enumerable;
            return enumerable.ToArray();
        }

        public DetourConfig WithPriority(int? priority) => new(Id, priority, Before, After);
        public DetourConfig WithBefore(IEnumerable<string> before) => new(Id, Priority, before, After);
        public DetourConfig WithBefore(params string[] before) => WithBefore(before.AsEnumerable());
        public DetourConfig WithAfter(IEnumerable<string> after) => new(Id, Priority, Before, after);
        public DetourConfig WithAfter(params string[] after) => WithAfter(after.AsEnumerable());

        public DetourConfig AddBefore(IEnumerable<string> before) => WithBefore(Before.Concat(before));
        public DetourConfig AddBefore(params string[] before) => AddBefore(before.AsEnumerable());
        public DetourConfig AddAfter(IEnumerable<string> after) => WithAfter(After.Concat(after));
        public DetourConfig AddAfter(params string[] after) => AddAfter(after.AsEnumerable());
    }
}
