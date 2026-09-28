namespace MonoMod.RuntimeDetour.Generics
{
    /// <summary>
    /// Controls automatic priming of generic-hook instantiations beyond those explicitly passed to <c>Prime</c>.
    /// Reference positions vary per the chosen mode; value positions always match exactly, and a pure value-type
    /// vector is unshared and never auto-caught. Reachability of value-type-containing vectors is runtime-dependent,
    /// see <see cref="GenericHook.SupportsValueTypeAutoPrime"/>.
    /// </summary>
    public enum AutoPrimeMode
    {
        /// <summary>Only the instantiations explicitly passed to <c>Prime</c> are hooked.</summary>
        None,
        /// <summary>Also hook instantiations whose reference positions are assignable to a primed pattern.</summary>
        Compatible,
        /// <summary>Also hook every reference-position variation of a primed value signature.</summary>
        All
    }
}
