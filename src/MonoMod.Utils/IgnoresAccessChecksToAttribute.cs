namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Makes the .NET runtime ignore access from this assembly to private members of the assembly with the given name.
    /// <para/>
    /// Usage: <c>[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]</c>
    /// </summary>
    /// <remarks>
    /// Use when building against publicized assemblies to prevent problems if the game ever switches from running on old Mono,
    /// where checking the "Allow Unsafe Code" option in the Project Settings is enough.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the Assembly to ignore access checks to.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// Makes the .NET runtime ignore access from this assembly to private members of the assembly with the given name.
        /// <para/>
        /// Usage: <c>[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]</c>
        /// </summary>
        /// <remarks>
        /// Use when building against publicized assemblies to prevent problems if the game ever switches from running on old Mono,
        /// where checking the "Allow Unsafe Code" option in the Project Settings is enough.
        /// </remarks>
        /// <param name="assemblyName">The name of the Assembly to ignore access checks to.</param>
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }
    }
}
