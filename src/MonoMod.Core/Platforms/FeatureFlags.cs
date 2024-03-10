using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A collection of feature flags for a particular <see cref="PlatformTriple"/>.
    /// </summary>
    public readonly struct FeatureFlags : IEquatable<FeatureFlags>
    {
        /// <summary>
        /// Gets the <see cref="ArchitectureFeature"/> flags for the architecture.
        /// </summary>
        public ArchitectureFeature Architecture { get; }
        /// <summary>
        /// Gets the <see cref="SystemFeature"/> flags for the operating system.
        /// </summary>
        public SystemFeature System { get; }
        /// <summary>
        /// Gets the <see cref="RuntimeFeature"/> flags for the runtime.
        /// </summary>
        public RuntimeFeature Runtime { get; }

        /// <summary>
        /// Constructs a <see cref="FeatureFlags"/> object from the provided flags.
        /// </summary>
        /// <param name="archFlags">The <see cref="ArchitectureFeature"/> flags.</param>
        /// <param name="sysFlags">The <see cref="SystemFeature"/> flags.</param>
        /// <param name="runtimeFlags">The <see cref="RuntimeFeature"/> flags.</param>
        public FeatureFlags(ArchitectureFeature archFlags, SystemFeature sysFlags, RuntimeFeature runtimeFlags)
        {
            Runtime = runtimeFlags;
            Architecture = archFlags;
            System = sysFlags;
        }

        /// <summary>
        /// Checks whether or not this collection has the provided set of <see cref="RuntimeFeature"/> flags.
        /// </summary>
        /// <param name="feature">The feature flags to check.</param>
        /// <returns><see langword="true"/> if this collection has all requested flags; <see langword="false"/> otherwise.</returns>
        public bool Has(RuntimeFeature feature)
            => (Runtime & feature) == feature;

        /// <summary>
        /// Checks whether or not this collection has the provided set of <see cref="ArchitectureFeature"/> flags.
        /// </summary>
        /// <param name="feature">The feature flags to check.</param>
        /// <returns><see langword="true"/> if this collection has all requested flags; <see langword="false"/> otherwise.</returns>
        public bool Has(ArchitectureFeature feature)
            => (Architecture & feature) == feature;

        /// <summary>
        /// Checks whether or not this collection has the provided set of <see cref="SystemFeature"/> flags.
        /// </summary>
        /// <param name="feature">The feature flags to check.</param>
        /// <returns><see langword="true"/> if this collection has all requested flags; <see langword="false"/> otherwise.</returns>
        public bool Has(SystemFeature feature)
            => (System & feature) == feature;

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is FeatureFlags flags && Equals(flags);
        }

        /// <inheritdoc/>
        public bool Equals(FeatureFlags other)
        {
            return Runtime == other.Runtime &&
                   Architecture == other.Architecture &&
                   System == other.System;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Runtime, Architecture, System);
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"({Architecture})({System})({Runtime})";

        /// <summary>
        /// Compares two <see cref="FeatureFlags"/> instances for equality.
        /// </summary>
        /// <param name="left">The first instance to compare.</param>
        /// <param name="right">The second instance to compare.</param>
        /// <returns><see langword="true"/> if the two instances contain exactly the same set of flags; <see langword="false"/> otherwise.</returns>
        public static bool operator ==(FeatureFlags left, FeatureFlags right) => left.Equals(right);
        /// <summary>
        /// Compares two <see cref="FeatureFlags"/> instances for inequality.
        /// </summary>
        /// <param name="left">The first instance to compare.</param>
        /// <param name="right">The second instance to compare.</param>
        /// <returns><see langword="true"/> if the two instances do not contain exactly the same set of flags; <see langword="false"/> otherwise.</returns>
        public static bool operator !=(FeatureFlags left, FeatureFlags right) => !(left == right);
    }
}
