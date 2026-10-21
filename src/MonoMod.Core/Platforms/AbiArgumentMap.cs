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
        /// <summary>
        /// Constructs an <see cref="AbiArgumentMapElement"/> with a zero <see cref="TargetArgumentOffset"/>.
        /// </summary>
        /// <param name="sourceArgument">The source argument kind.</param>
        /// <param name="targetArgument">The target argument kind to map to.</param>
        public AbiArgumentMapElement(SpecialArgumentKind sourceArgument, SpecialArgumentKind targetArgument)
            : this(sourceArgument, targetArgument, 0)
        {
        }
    }

    /// <summary>
    /// A mapping which defines how arguments in one <see cref="MethodAbi"/> should be mapepd to arguments in another.
    /// </summary>
    public sealed class AbiArgumentMap
    {
        /// <summary>
        /// The identity mapping, matching each argument exactly, one-for-one.
        /// </summary>
        public static AbiArgumentMap Identity { get; } = new([]);

        /// <summary>
        /// The mapping from an instance method to a static method with the first user argument being the <see langword="this"/> argument.
        /// </summary>
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

        /// <summary>
        /// Constructs an <see cref="AbiArgumentMap"/> from a set of <see cref="AbiArgumentMapElement"/>s.
        /// </summary>
        /// <param name="mapping">The <see cref="AbiArgumentMapElement"/>s defining the mapping.</param>
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

        /// <summary>
        /// Computes the actual argument mapping for a specific pair of ABIs according to this argument map.
        /// </summary>
        /// <remarks>
        /// <para>If <paramref name="mapping"/> is non-empty, it must contain at least as many elements as <paramref name="targetAbi"/>
        /// has ABI arguments. When this method returns, each element of <paramref name="mapping"/> will contain the index of the argument
        /// in <paramref name="sourceAbi"/> corresponding the the argument at that position in <paramref name="targetAbi"/>. If no source
        /// argument was mapped to that target argument, the <paramref name="mapping"/> element is <c>-1</c>.</para>
        /// </remarks>
        /// <param name="sourceAbi">The ABI of the source method.</param>
        /// <param name="targetAbi">The ABI of the target method.</param>
        /// <param name="mapping">A span to be filled with the actual mapping indices.</param>
        /// <returns>The <see cref="AbiArgumentMapResult"/> indicating the status of the mapping.</returns>
        public AbiArgumentMapResult MapArguments(MethodAbi sourceAbi, MethodAbi targetAbi, Span<int> mapping)
        {
            Helpers.ThrowIfArgumentNull(sourceAbi);
            Helpers.ThrowIfArgumentNull(targetAbi);

            if (mapping.Length != 0)
            {
                Helpers.Assert(mapping.Length >= targetAbi.Arguments.Length,
                    "If mapping span is provided, it must have at least targetAbi.Arguments.Length elements");
            }

            AbiArgumentMapResult result = 0;

            var srcArgKindsMask = 0;
            if (sourceAbi.ThisArgument >= 0)
            {
                srcArgKindsMask = 1 << (int)SpecialArgumentKind.ThisPointer;
            }
            if (sourceAbi.ReturnBufferArgument >= 0)
            {
                srcArgKindsMask = 1 << (int)SpecialArgumentKind.ReturnBuffer;
            }
            if (sourceAbi.GenericContextArgument >= 0)
            {
                srcArgKindsMask = 1 << (int)SpecialArgumentKind.GenericContext;
            }
            if (sourceAbi.AsyncContinuationArgument >= 0)
            {
                srcArgKindsMask = 1 << (int)SpecialArgumentKind.AsyncContinuation;
            }

            var srcArgKindsMapped = 0;

            var isIdentity = true;
            var targetArgs = targetAbi.Arguments.Span;
            var sourceArgs = sourceAbi.Arguments.Span;
            for (var i = 0; i < targetArgs.Length; i++)
            {
                var tgtArg = targetArgs[i];

                SpecialArgumentKind srcArgKind;
                var srcArgUserIndex = -1;

                if (tgtArg.Kind is SpecialArgumentKind.UserArguments)
                {
                    var userArgIdx = i - targetAbi.FirstUserArgument;

                    if (userArgIdx < targetFromSourceMappedUserArgs.Length)
                    {
                        // this target arg is mapped from a source special arg
                        srcArgKind = targetFromSourceMappedUserArgs[userArgIdx];
                    }
                    else
                    {
                        // this target arg is mapped implicitly from the source user args
                        srcArgKind = SpecialArgumentKind.UserArguments;
                        srcArgUserIndex = userArgIdx - targetFromSourceMappedUserArgs.Length;
                    }
                }
                else
                {
                    srcArgKind = targetFromSourceNonUserArgs[(int)tgtArg.Kind];
                }

                int srcArgIdx;
                if (srcArgKind is SpecialArgumentKind.UserArguments)
                {
                    Helpers.Assert(srcArgUserIndex >= 0);
                    if (srcArgUserIndex < sourceAbi.UserArgumentCount)
                    {
                        srcArgIdx = sourceAbi.FirstUserArgument + srcArgUserIndex;
                    }
                    else
                    {
                        // source argument is out of bounds
                        srcArgIdx = -1;
                    }
                }
                else
                {
                    srcArgKindsMapped = 1 << (int)srcArgKind;
                    srcArgIdx = srcArgKind switch
                    {
                        SpecialArgumentKind.ThisPointer => sourceAbi.ThisArgument,
                        SpecialArgumentKind.ReturnBuffer => sourceAbi.ReturnBufferArgument,
                        SpecialArgumentKind.GenericContext => sourceAbi.GenericContextArgument,
                        SpecialArgumentKind.AsyncContinuation => sourceAbi.AsyncContinuationArgument,
                        _ => throw new NotImplementedException($"SpecialArgumentKind.{srcArgKind}"),
                    };
                }

                // track whether this is an identity mapping so far
                if (srcArgIdx != i)
                {
                    isIdentity = false;
                }

                // record the source argument index to the output
                if (mapping.Length >= i)
                {
                    mapping[i] = srcArgIdx;
                }

                if (srcArgIdx == -1)
                {
                    // we weren't able to get a source argument for this target
                    result |= AbiArgumentMapResult.TargetHasUnboundArguments;
                    if (tgtArg.Kind is SpecialArgumentKind.UserArguments)
                    {
                        result |= AbiArgumentMapResult.TargetHasUnboundUserArguments;
                    }
                }
                else
                {
                    // now we check the argument type compatibility
                    var srcArg = sourceArgs[srcArgIdx];

                    if (!tgtArg.Type.IsCompatible(srcArg.Type))
                    {
                        result |= AbiArgumentMapResult.ArgumentTypesIncompatible;
                    }
                }
            }

            if ((srcArgKindsMask & ~srcArgKindsMapped) != 0)
            {
                // at least one argument type in the source isn't mapped anywhere in the target
                result |= AbiArgumentMapResult.SourceHasUnmappedArguments;
            }

            if (targetAbi.UserArgumentCount < targetFromSourceMappedUserArgs.Length + sourceAbi.UserArgumentCount)
            {
                // some source user arguments are guaranteed to not be mapped
                result |= AbiArgumentMapResult.SourceHasUnmappedArguments | AbiArgumentMapResult.SourceHasUnmappedUserArguments;
                isIdentity = false;
            }

            if (isIdentity)
            {
                result |= AbiArgumentMapResult.MappingIsIdentity;
            }

            if (!sourceAbi.AbiReturn.IsCompatible(targetAbi.AbiReturn))
            {
                result |= AbiArgumentMapResult.ArgumentTypesIncompatible;
            }

            // a mapping is considered valid if:
            // 1. there are no incompatible arguments
            // 2. the target has no unbount arguments
            if ((result & (AbiArgumentMapResult.TargetHasUnboundArguments | AbiArgumentMapResult.ArgumentTypesIncompatible)) == 0)
            {
                result |= AbiArgumentMapResult.MappingIsValid;
            }

            return result;
        }
    }

    /// <summary>
    /// Results of <see cref="AbiArgumentMap.MapArguments(MethodAbi, MethodAbi, Span{int})"/>.
    /// </summary>
    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "<Pending>")]
    public enum AbiArgumentMapResult
    {
        /// <summary>
        /// Default value, representing an invalid result.
        /// </summary>
        Invalid = 0,
        /// <summary>
        /// The mapping is valid.
        /// </summary>
        /// <remarks>
        /// <para>This is set exactly when the target has no unbount arguments and the target arguments do not have incompatible types.</para>
        /// </remarks>
        MappingIsValid = 1 << 0,
        /// <summary>
        /// This mapping is the identity mapping.
        /// </summary>
        MappingIsIdentity = 1 << 1,

        /// <summary>
        /// The source has arguments which do not get mapped to anything in the target.
        /// </summary>
        SourceHasUnmappedArguments = 1 << 2,
        /// <summary>
        /// The target has arguments which are not bound to anything in the source.
        /// </summary>
        TargetHasUnboundArguments = 1 << 3,

        /// <summary>
        /// The source has user arguments which do not get mapped to anything in the target.
        /// </summary>
        SourceHasUnmappedUserArguments = 1 << 4,
        /// <summary>
        /// The target has arguments which are not bound to anything in the source.
        /// </summary>
        TargetHasUnboundUserArguments = 1 << 5,

        /// <summary>
        /// At least one mapped argument has incompatible types between the source and target.
        /// </summary>
        ArgumentTypesIncompatible = 1 << 6,
    }

    /// <summary>
    /// Extensions for <see cref="AbiArgumentMapResult"/>.
    /// </summary>
    public static class AbiArgumentMapResultExtensions
    {
        /// <summary>
        /// Throws if <paramref name="result"/> does not have <see cref="AbiArgumentMapResult.MappingIsValid"/> set.
        /// </summary>
        /// <remarks>
        /// <para>The message of the thrown exception encodes information stored in the other bits of <paramref name="result"/>
        /// automatically.</para>
        /// </remarks>
        /// <param name="result">The <see cref="AbiArgumentMapResult"/> to check.</param>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="result"/> does not have
        /// <see cref="AbiArgumentMapResult.MappingIsValid"/> set with an informative message.</exception>
        public static void ThrowIfInvalid(this AbiArgumentMapResult result)
        {
            if (!result.Has(AbiArgumentMapResult.MappingIsValid))
            {
                if (result.Has(AbiArgumentMapResult.ArgumentTypesIncompatible))
                {
                    throw new InvalidOperationException("Arguments have incompatible argument types");
                }
                if (result.Has(AbiArgumentMapResult.TargetHasUnboundArguments))
                {
                    throw new InvalidOperationException("Target would have unset arguments");
                }
                throw new InvalidOperationException("Could not compute a valid mapping");
            }
        }
    }
}
