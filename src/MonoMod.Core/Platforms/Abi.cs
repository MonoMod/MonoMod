using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// The ABI classification of a type.
    /// </summary>
    /// <seealso cref="Abi"/>
    /// <seealso cref="Classifier"/>
    public enum TypeClassification
    {
        /// <summary>
        /// The type is passed by value in a register.
        /// </summary>
        InRegister,
        /// <summary>
        /// The type is passed by reference.
        /// </summary>
        ByReference,
        /// <summary>
        /// The type is passed by value on the stack.
        /// </summary>
        /// <remarks>
        /// <para>On Windows's AMD64 ABI, parameters are never passed <see cref="OnStack"/>. They are either passed
        /// <see cref="InRegister"/> or <see cref="ByReference"/>.</para>
        /// <para>Return values are never passed <see cref="OnStack"/>, and a classifier should never return <see cref="OnStack"/>
        /// for a return classification.</para>
        /// </remarks>
        OnStack
    }

    /// <summary>
    /// A delegate which classifies a type according to its ABI.
    /// </summary>
    /// <remarks>
    /// <para>Return values are never passed <see cref="TypeClassification.OnStack"/>, and a classifier should never return <see cref="TypeClassification.OnStack"/>
    /// for a return classification.</para>
    /// </remarks>
    /// <param name="type">The type to classify.</param>
    /// <param name="isReturn"><see langword="true"/> if this classification is being done for a return value; <see langword="false"/> otherwise.</param>
    /// <returns>The <see cref="TypeClassification"/> for the type.</returns>
    /// <seealso cref="Abi"/>
    /// <seealso cref="TypeClassification"/>
    public delegate TypeClassification Classifier(Type type, bool isReturn);

    /// <summary>
    /// A kind of special argument used in the ABI. Used to specify argument order.
    /// </summary>
    public enum SpecialArgumentKind
    {
        /// <summary>
        /// The this pointer, when one is present.
        /// </summary>
        ThisPointer,
        /// <summary>
        /// The return buffer pointer, when one is present.
        /// </summary>
        ReturnBuffer,
        /// <summary>
        /// The generic context pointer, when one is present.
        /// </summary>
        GenericContext,
        /// <summary>
        /// User arguments.
        /// </summary>
        /// <remarks>
        /// This is needed to be able to specify all known CLR ABIs. This is because on some architectures,
        /// notably x86, CoreCLR uses a very strange ABI which places the generic context pointer <i>after</i>
        /// user arguments.
        /// </remarks>
        UserArguments,
    }

    // TODO: include information about how many registers are used for parameter passing
    /// <summary>
    /// An ABI descriptor.
    /// </summary>
    /// <param name="ArgumentOrder">A sequence of <see cref="SpecialArgumentKind"/> indicating the ABI's argument order.</param>
    /// <param name="Classifier">A <see cref="MonoMod.Core.Platforms.Classifier"/> which classifies value types according to the ABI.</param>
    /// <param name="ReturnsReturnBuffer"><see langword="true"/> if functions are expected to return the return buffer pointer they are passed;
    /// <see langword="false"/> otherwise.</param>
    public readonly record struct Abi(
        ReadOnlyMemory<SpecialArgumentKind> ArgumentOrder,
        Classifier Classifier,
        bool ReturnsReturnBuffer
    )
    {
        /// <summary>
        /// Classifies a type according to the ABI.
        /// </summary>
        /// <remarks>
        /// <para>Prefer using this over the <see cref="Classifier"/> member.</para>
        /// <para>This method does some preliminary universal classifications, to make it easier for ABI implementers. Notably, 
        /// reference types, pointer types, and byref types are all implicitly classified as <see cref="TypeClassification.InRegister"/>
        /// since they are always exactly one machine word. <see cref="void"/> is also automatically handled.</para>
        /// </remarks>
        /// <param name="type">The type to classify.</param>
        /// <param name="isReturn"><see langword="true"/> if the classification is being done for a return value; <see langword="false"/> otherwise.</param>
        /// <returns>The <see cref="TypeClassification"/> for the type.</returns>
        public TypeClassification Classify(Type type, bool isReturn)
        {
            Helpers.ThrowIfArgumentNull(type);

            if (type == typeof(void))
                return TypeClassification.InRegister; // void can't be a parameter, and doesn't need a return buffer
            if (!type.IsValueType)
                return TypeClassification.InRegister; // while it won't *always* go in register, it will if it can
            if (type.IsPointer)
                return TypeClassification.InRegister; // same as above
            if (type.IsByRef)
                return TypeClassification.InRegister; // same as above

            return Classifier(type, isReturn);
        }
    }
}
