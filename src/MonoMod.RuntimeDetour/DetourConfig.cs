using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A configuration for detours, which allows for the ordering of detours relative to each other.
    /// </summary>
    /// <remarks>
    /// See the detailed documentation on detour order calculation for specifics on how this affects relative ordering.
    /// </remarks>
    public class DetourConfig
    {
        /// <summary>
        /// Gets the ID of the detours represented by this config. Typically, this will be the name of the mod which creates this detour.
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// Gets the priority of the detours represented by this config, if present.
        /// </summary>
        /// <remarks>
        /// The priority only affects the relative ordering of detours which are not otherwise ordered by e.g. <see cref="Before"/> or <see cref="After"/>.
        /// Detours with no priority are ordered <i>after</i> all detours which have a priority.
        /// </remarks>
        public int? Priority { get; }
        /// <summary>
        /// Gets the detour IDs to run before this detour.
        /// </summary>
        /// <remarks>
        /// This takes takes priority over <see cref="Priority"/>.
        /// </remarks>
        public IEnumerable<string> Before { get; }
        /// <summary>
        /// Gets the detour IDs to run after this detour.
        /// </summary>
        /// <remarks>
        /// This takes takes priority over <see cref="Priority"/>.
        /// </remarks>
        public IEnumerable<string> After { get; }

        /// <summary>
        /// Gets the sub-priority of the detours represented by this config, which controls the order of hooks with the same priority.
        /// </summary>
        /// <remarks>
        /// This is only intended to be used for advanced applications - you should use <see cref="Priority"/> for almost all regular use cases.
        /// </remarks>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public int SubPriority { get; }

        /// <summary>
        /// Constructs a <see cref="DetourConfig"/> with a specific ID, and any of the ordering options.
        /// </summary>
        /// <param name="id">The ID of the detour config.</param>
        /// <param name="priority">The priority of the detour config. Refer to <see cref="Priority"/> for details.</param>
        /// <param name="before">An enumerable containing the list of IDs of detours to run before detours with this config.</param>
        /// <param name="after">An enumerable containing the list of IDs of detours to run after detours with this config.</param>
        public DetourConfig(string id, int? priority = null, IEnumerable<string>? before = null, IEnumerable<string>? after = null)
            : this(id, priority, before, after, 0) { }

        /// <summary>
        /// Constructs a <see cref="DetourConfig"/> with a specific ID, and any of the ordering options (including advanced options).
        /// </summary>
        /// <param name="id">The ID of the detour config.</param>
        /// <param name="priority">The priority of the detour config. Refer to <see cref="Priority"/> for details.</param>
        /// <param name="before">An enumerable containing the list of IDs of detours to run before detours with this config.</param>
        /// <param name="after">An enumerable containing the list of IDs of detours to run after detours with this config.</param>
        /// <param name="subPriority">The sub-priority of the detour config. Refer to <see cref="SubPriority"/> for details.</param>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public DetourConfig(string id, int? priority, IEnumerable<string>? before, IEnumerable<string>? after, int subPriority)
        {
            Id = id;
            Priority = priority;
            Before = AsFixedSize(before ?? Enumerable.Empty<string>());
            After = AsFixedSize(after ?? Enumerable.Empty<string>());
            SubPriority = subPriority;
        }

        private static IEnumerable<string> AsFixedSize(IEnumerable<string> enumerable)
        {
            if (enumerable == Enumerable.Empty<string>())
                return enumerable;
            if (enumerable is ICollection<string>)
                return enumerable;
            return enumerable.ToArray();
        }

        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <see cref="Priority"/> equal to <paramref name="priority"/>.
        /// </summary>
        /// <param name="priority">The priority of the new <see cref="DetourConfig"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> identical to this one, but with <see cref="Priority"/> equal to <paramref name="priority"/>.</returns>
        public DetourConfig WithPriority(int? priority) => new(Id, priority, Before, After, SubPriority);
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <see cref="Before"/> equal to <paramref name="before"/>.
        /// </summary>
        /// <param name="before">The <see cref="Before"/> list for the new <see cref="DetourConfig"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> identical to this one, but with <see cref="Before"/> equal to <paramref name="before"/>.</returns>
        public DetourConfig WithBefore(IEnumerable<string> before) => new(Id, Priority, before, After, SubPriority);
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <see cref="Before"/> equal to <paramref name="before"/>.
        /// </summary>
        /// <param name="before">The <see cref="Before"/> list for the new <see cref="DetourConfig"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> identical to this one, but with <see cref="Before"/> equal to <paramref name="before"/>.</returns>
        public DetourConfig WithBefore(params string[] before) => WithBefore(before.AsEnumerable());
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <see cref="After"/> equal to <paramref name="after"/>.
        /// </summary>
        /// <param name="after">The <see cref="After"/> list for the new <see cref="DetourConfig"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> identical to this one, but with <see cref="After"/> equal to <paramref name="after"/>.</returns>
        public DetourConfig WithAfter(IEnumerable<string> after) => new(Id, Priority, Before, after, SubPriority);
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <see cref="After"/> equal to <paramref name="after"/>.
        /// </summary>
        /// <param name="after">The <see cref="After"/> list for the new <see cref="DetourConfig"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> identical to this one, but with <see cref="After"/> equal to <paramref name="after"/>.</returns>
        public DetourConfig WithAfter(params string[] after) => WithAfter(after.AsEnumerable());

        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <paramref name="before"/> added to <see cref="Before"/>.
        /// </summary>
        /// <param name="before">The list of IDs to add to <see cref="Before"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> with <paramref name="before"/> added to <see cref="Before"/>.</returns>
        public DetourConfig AddBefore(IEnumerable<string> before) => WithBefore(Before.Concat(before));
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <paramref name="before"/> added to <see cref="Before"/>.
        /// </summary>
        /// <param name="before">The list of IDs to add to <see cref="Before"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> with <paramref name="before"/> added to <see cref="Before"/>.</returns>
        public DetourConfig AddBefore(params string[] before) => AddBefore(before.AsEnumerable());
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <paramref name="after"/> added to <see cref="After"/>.
        /// </summary>
        /// <param name="after">The list of IDs to add to <see cref="After"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> with <paramref name="after"/> added to <see cref="After"/>.</returns>
        public DetourConfig AddAfter(IEnumerable<string> after) => WithAfter(After.Concat(after));
        /// <summary>
        /// Creates a new <see cref="DetourConfig"/> which is identical to this one, but with <paramref name="after"/> added to <see cref="After"/>.
        /// </summary>
        /// <param name="after">The list of IDs to add to <see cref="After"/>.</param>
        /// <returns>A <see cref="DetourConfig"/> with <paramref name="after"/> added to <see cref="After"/>.</returns>
        public DetourConfig AddAfter(params string[] after) => AddAfter(after.AsEnumerable());
    }
}
