namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A mapping of a method argument from one <see cref="MethodAbi"/> to another.
    /// </summary>
    /// <param name="SourceArgument">The source argument being mapped.</param>
    /// <param name="TargetArgument">The destination argument to map to.</param>
    /// <param name="TargetArgumentOffset">The offset of the target argument, if
    /// <paramref name="TargetArgument"/> is <see cref="SpecialArgumentKind.UserArguments"/>.</param>
    /// <remarks>
    /// <para>The following mappings are valid:
    /// <list type="bullet">
    /// <item>Any <paramref name="SourceArgument"/> except <see cref="SpecialArgumentKind.UserArguments"/> to itself</item>
    /// <item>Any <paramref name="SourceArgument"/> except <see cref="SpecialArgumentKind.UserArguments"/> to
    /// <paramref name="TargetArgument"/>=<see cref="SpecialArgumentKind.UserArguments"/>, in which case the the source
    /// argument is mapped to the user argument with index <paramref name="TargetArgumentOffset"/></item>
    /// <item><paramref name="SourceArgument"/>=<see cref="SpecialArgumentKind.UserArguments"/> to 
    /// <paramref name="TargetArgument"/>=<see cref="SpecialArgumentKind.UserArguments"/>, in which case the source user
    /// arguments will be mapped to the target user arguments starting at the user argument with index <paramref name="TargetArgumentOffset"/></item>
    /// </list></para>
    /// <para>In a full mapping, all of a method's target arguments must be bound to exactly one source argument.</para>
    /// <para>In a full mapping, if a source argument is not mapped, that argument is mapped using the identity mapping, unless
    /// that mapping would conflict with an existing mapping, in which case it is dropped.</para>
    /// </remarks>
    public readonly record struct AbiArgumentMap(
        SpecialArgumentKind SourceArgument,
        SpecialArgumentKind TargetArgument,
        int TargetArgumentOffset);
}
