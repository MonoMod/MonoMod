using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A description of a method argument at the ABI level.
    /// </summary>
    /// <param name="Kind">The kind of argument.</param>
    /// <param name="Type">The type of the argument.</param>
    public readonly record struct AbiArgument(
        SpecialArgumentKind Kind,
        Type Type);

    /// <summary>
    /// A method's ABI signature.
    /// </summary>
    public sealed class MethodAbi
    {
        /// <summary>
        /// The ABI arguments taken by the method.
        /// </summary>
        public ReadOnlyMemory<AbiArgument> Arguments { get; }
        /// <summary>
        /// The ABI return type of the method. If the method returns nothing, <c><see langword="typeof"/>(<see langword="void"/>)</c>.
        /// </summary>
        public Type AbiReturn { get; }

        /// <summary>
        /// The index of this method's <see langword="this"/> argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int ThisArgument { get; } = -1;
        /// <summary>
        /// The index of this method's return buffer argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int ReturnBufferArgument { get; } = -1;
        /// <summary>
        /// The index of this method's generic context argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int GenericContextArgument { get; } = -1;
        /// <summary>
        /// The index of this method's async continuation argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int AsyncContinuationArgument { get; } = -1;

        /// <summary>
        /// The index of the first user argument, or <c>-1</c> if there are no user arguments.
        /// </summary>
        public int FirstUserArgument { get; } = -1;

        /// <summary>
        /// <see langword="true"/> if the ABI returns the return buffer parameter.
        /// </summary>
        public bool AbiReturnIsReturnBuffer { get; }

        /// <summary>
        /// Constructs a <see cref="MethodAbi"/>.
        /// </summary>
        /// <param name="arguments">The <see cref="AbiArgument"/>s of the method's arguments.</param>
        /// <param name="abiReturn">The ABI return type for the method.</param>
        /// <param name="returnReturnBuffer">Whether the ABI requires the method to return the return buffer pointer.</param>
        public MethodAbi(ReadOnlyMemory<AbiArgument> arguments, Type abiReturn, bool returnReturnBuffer)
        {
            Helpers.ThrowIfNull(abiReturn);

            Arguments = arguments;
            AbiReturn = abiReturn;

            Type? argRetBufType = null;
            var argSpan = arguments.Span;
            for (var i = 0; i < argSpan.Length; i++)
            {
                var arg = argSpan[i];
                switch (arg.Kind)
                {
                    case SpecialArgumentKind.ThisPointer:
                        Helpers.Assert(ThisArgument == -1);
                        ThisArgument = i;
                        break;
                    case SpecialArgumentKind.ReturnBuffer:
                        Helpers.Assert(ReturnBufferArgument == -1);
                        ReturnBufferArgument = i;
                        argRetBufType = arg.Type;
                        break;
                    case SpecialArgumentKind.GenericContext:
                        Helpers.Assert(GenericContextArgument == -1);
                        GenericContextArgument = i;
                        break;
                    case SpecialArgumentKind.AsyncContinuation:
                        Helpers.Assert(AsyncContinuationArgument == -1);
                        AsyncContinuationArgument = i;
                        break;

                    case SpecialArgumentKind.UserArguments:
                        if (FirstUserArgument == -1)
                        {
                            FirstUserArgument = i;
                        }
                        break;

                    default:
                        throw new NotImplementedException($"SpecialArgumentKind.{arg.Kind}");
                }
            }

            AbiReturnIsReturnBuffer = returnReturnBuffer && ReturnBufferArgument >= 0;

            if (AbiReturnIsReturnBuffer)
            {
                Helpers.DAssert(argRetBufType is not null);
                Helpers.Assert(argRetBufType == abiReturn);
            }
        }
    }
}
