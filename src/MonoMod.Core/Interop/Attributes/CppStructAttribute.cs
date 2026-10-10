using System;

namespace MonoMod.Core.Interop.Attributes
{
    /// <summary>
    /// Declares a mirror of a native C++ class or struct whose layout depends on the preprocessor configuration that the
    /// native code was built with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each non-static data member of the C++ type is declared, in C++ declaration order, as a
    /// <c>partial ref T name { get; }</c> property. Members that only exist under some preprocessor condition are
    /// annotated with <see cref="CppIfAttribute"/>, and members of anonymous unions with <see cref="CppUnionAttribute"/>.
    /// The struct itself has no instance fields; it is only ever used through pointers into native memory, so that a
    /// <c>T*</c> means the same thing it does in C++.
    /// </para>
    /// <para>
    /// The <c>CppStructGenerator</c> source generator emits a nested static class <c>Layout</c> containing one
    /// <c>[StructLayout(LayoutKind.Sequential)]</c> partial struct per combination of the preprocessor symbols referenced by
    /// the struct (including those of its base and of embedded <see cref="CppStructAttribute"/> members). At runtime the
    /// combination matching the current process is selected by evaluating each symbol as a <see cref="bool"/> member named
    /// exactly like the symbol which must be in scope of the struct (see <c>CoreCLR.Defines.cs</c>). The member properties
    /// are then implemented as offsets into that layout, and <c>Layout.Size</c> provides <c>sizeof(T)</c>.
    /// </para>
    /// <para>
    /// Because the generated layout variants are ordinary partial structs, behavior which (unlike a member offset) differs
    /// between layouts can be dispatched with a <see cref="FatInterfaceAttribute"/> by applying
    /// <see cref="FatInterfaceImplAttribute"/> to the relevant <c>Layout</c> variants.
    /// </para>
    /// <para>
    /// Base class subobjects are laid out as the first member, as MSVC does. The Itanium ABI may place derived members in
    /// the tail padding of a non-POD base; that is not modeled, so a base must not end in padding.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class CppStructAttribute : Attribute
    {
        public CppStructAttribute() { }

        /// <param name="baseType">The <see cref="CppStructAttribute"/> struct mirroring the C++ base class (<c>: public Base</c>).</param>
        public CppStructAttribute(Type baseType)
        {
            BaseType = baseType;
        }

        public Type? BaseType { get; }
    }

    /// <summary>
    /// The C preprocessor condition (as written after <c>#if</c>) under which a <see cref="CppStructAttribute"/> member,
    /// or the whole struct, exists.
    /// </summary>
    /// <remarks>
    /// Supports symbols, <c>defined(SYMBOL)</c>, <c>defined SYMBOL</c>, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c>, and parentheses.
    /// Every symbol is evaluated at runtime through a <see cref="bool"/> member of the same name in scope of the struct.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class CppIfAttribute : Attribute
    {
        public CppIfAttribute(string condition)
        {
            Condition = condition;
        }

        public string Condition { get; }
    }

    /// <summary>
    /// Marks a <see cref="CppStructAttribute"/> member as part of an anonymous C++ <c>union</c>. Adjacent members with the
    /// same <see cref="Index"/> share storage.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class CppUnionAttribute : Attribute
    {
        public CppUnionAttribute(int index)
        {
            Index = index;
        }

        public int Index { get; }
    }
}
