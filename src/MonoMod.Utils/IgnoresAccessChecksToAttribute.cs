namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public string AssemblyName { get; }
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }
    }
}
