using MonoMod.Utils;
using System;

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
    /// <para>In a full mapping, each source argument may be bound to at most one target argument.</para>
    /// <para>In a full mapping, if a source argument is not mapped, that argument is mapped using the identity mapping, unless
    /// that mapping would conflict with an existing mapping, in which case it is dropped.</para>
    /// </remarks>
    public readonly record struct AbiArgumentMapElement(
        SpecialArgumentKind SourceArgument,
        SpecialArgumentKind TargetArgument,
        int TargetArgumentOffset)
    {
        public AbiArgumentMapElement(SpecialArgumentKind sourceArgument, SpecialArgumentKind targetArgument)
            : this(sourceArgument, targetArgument, 0)
        {
        }
    }

    public sealed class AbiArgumentMap
    {
        public static AbiArgumentMap Identity { get; } = new([]);

        public static AbiArgumentMap InstanceToStatic { get; } = new([
            new(SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments, 0),
        ]);

        private readonly struct MappingItem
        {
            public SpecialArgumentKind TargetKind { get; init; }
            public int TargetOffset { get; init; } // == -1 if this is not mapped
        }

        // given [SourceKind] => target argument
        private readonly MappingItem[] sourceToTargetMap;

        // given [TargetKind != UserArguments] => source argument
        private readonly SpecialArgumentKind[] targetFromSourceNonUserArgs;
        // given [TargetUserArgIndex] => source argument, out-of-range maps into user args
        private readonly SpecialArgumentKind[] targetFromSourceMappedUserArgs;

        private const int NumArgumentKinds = 1 + (int)SpecialArgumentKind.AsyncContinuation;

        public AbiArgumentMap(ReadOnlySpan<AbiArgumentMapElement> mapping)
        {
            sourceToTargetMap = new MappingItem[NumArgumentKinds];
            targetFromSourceNonUserArgs = new SpecialArgumentKind[NumArgumentKinds];

            sourceToTargetMap.AsSpan().Fill(new()
            {
                TargetKind = 0,
                TargetOffset = -1,
            });
            targetFromSourceNonUserArgs.AsSpan().Fill(SpecialArgumentKind.UserArguments); // we use UserArguments as filler since its invalid here

            // these will be bitfields which track which argument kinds have already been mapped/bound
            var sourceMappedBits = 0;
            var targetBoundBits = 0;

            var numTargetUserArgMapItems = 0;
            var maxTargetUserArgOffset = -1;
            var minTargetUserArgOffset = int.MaxValue;
            var userToUserOffset = -1;
            foreach (var map in mapping)
            {
                if (map.TargetArgument is not SpecialArgumentKind.UserArguments)
                {
                    Helpers.Assert(map.TargetArgumentOffset == 0, "TargetArgumentOffset must be 0 if TargetArgument is not UserArguments");
                    Helpers.Assert(map.SourceArgument == map.TargetArgument, "Source arguments (that are not UserArguments) may only be mapped " +
                        "to target arguments of the same type or user arguments");
                }
                else
                {
                    Helpers.Assert(map.TargetArgumentOffset >= 0, "TargetArgumentOffset must be positive");
                    // in this bookkeeping, don't consider a source UserArguments mapping, since it must always point to the
                    // next argument offset after all of these if it is present
                    if (map.SourceArgument is not SpecialArgumentKind.UserArguments)
                    {
                        maxTargetUserArgOffset = Math.Max(maxTargetUserArgOffset, map.TargetArgumentOffset);
                        minTargetUserArgOffset = Math.Min(minTargetUserArgOffset, map.TargetArgumentOffset);
                        numTargetUserArgMapItems++;
                    }
                    else
                    {
                        userToUserOffset = map.TargetArgumentOffset;
                    }
                }

                {
                    // record that we've mapped this source argument
                    var bit = 1 << (int)map.SourceArgument;
                    Helpers.Assert((sourceMappedBits & bit) == 0, "Each source argument must be mapped at most once");
                    sourceMappedBits |= bit;
                }

                if (map.TargetArgument is not SpecialArgumentKind.UserArguments)
                {
                    // record that we've bound this target argument
                    var bit = 1 << (int)map.TargetArgument;
                    Helpers.Assert((targetBoundBits & bit) == 0, "Each target argument must be bound to exactly one source argument");
                    targetBoundBits |= bit;
                }

                sourceToTargetMap[(int)map.SourceArgument] = new()
                {
                    TargetKind = map.TargetArgument,
                    TargetOffset = map.TargetArgument is SpecialArgumentKind.UserArguments ? map.TargetArgumentOffset : 0,
                };

                if (map.TargetArgument is not SpecialArgumentKind.UserArguments)
                {
                    targetFromSourceNonUserArgs[(int)map.TargetArgument] = map.SourceArgument;
                }
            }

            // we've now done our first pass, and now need to do the second pass to collect the user argument mappings properly
            if (numTargetUserArgMapItems > 0)
            {
                // we have at least one mapping into user arguments, lets do some early validations that the user argument mappings
                // correctly map into the lowest-indexed arguments
                Helpers.Assert(minTargetUserArgOffset == 0, "User argument bindings must start at offset 0");
                Helpers.Assert(maxTargetUserArgOffset + 1 == numTargetUserArgMapItems, "User argument bindings must be contiguous");
            }
            if (userToUserOffset != -1)
            {
                // UserArguments -> UserArguments mapping must always map at the end of the explicitly mapped args
                Helpers.Assert(maxTargetUserArgOffset + 1 == userToUserOffset, "User argument to user argument bindings must lie exactly after all other" +
                    "user argument bindings");
            }
            else
            {
                userToUserOffset = maxTargetUserArgOffset + 1;
            }

            targetFromSourceMappedUserArgs = numTargetUserArgMapItems == 0 ? [] : new SpecialArgumentKind[numTargetUserArgMapItems];
            targetFromSourceMappedUserArgs.AsSpan().Fill(SpecialArgumentKind.UserArguments); // we use UserArguments as filler since its invalid here

            foreach (var map in mapping)
            {
                if (map.TargetArgument is not SpecialArgumentKind.UserArguments)
                {
                    // we've already fully processed this argument, no more work to do
                    continue;
                }

                // this is TargetArgument=UserArguments

                if (map.SourceArgument is SpecialArgumentKind.UserArguments)
                {
                    // SourceArgument=UserArguments is implicit, and already validated
                    continue;
                }

                Helpers.Assert(targetFromSourceMappedUserArgs[map.TargetArgumentOffset] == SpecialArgumentKind.UserArguments,
                    "Each target argument must be bound to exactly one source argument");
                targetFromSourceMappedUserArgs[map.TargetArgumentOffset] = map.SourceArgument;
            }

            // now to identity-fill any mappings that remain
            for (var iarg = 0; iarg < NumArgumentKinds; iarg++)
            {
                var arg = (SpecialArgumentKind)iarg;
                var bit = 1 << iarg;
                if ((sourceMappedBits & bit) != 0)
                {
                    // already mapped from the source, do nothing
                    continue;
                }

                // otherwise, valid unmapped we want to try to identity-map
                if ((targetBoundBits & bit) == 0)
                {
                    // not already bound to the target, safe to inject
                    sourceToTargetMap[iarg] = new()
                    {
                        TargetKind = arg,
                        TargetOffset = arg is SpecialArgumentKind.UserArguments ? userToUserOffset : 0,
                    };
                    if (arg is not SpecialArgumentKind.UserArguments)
                    {
                        targetFromSourceNonUserArgs[iarg] = arg;
                    }
                }
            }
        }

        // TODO: check if a MethodAbi is compatible with the source/target sides, whether a given source abi and target
        // abi are compatible with each other and end up identity-mapping arg order to match this mapping

    }
}
