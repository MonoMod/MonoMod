using Mono.Cecil;
using System.Reflection;

namespace MonoMod.Utils
{
    public class DynamicMethodReference : MethodReference
    {
        public MethodInfo DynamicMethod { get; }

        public DynamicMethodReference(ModuleDefinition module, MethodInfo dm)
            : base("", Helpers.ThrowIfNull(module).TypeSystem.Void)
        {
            DynamicMethod = dm;
        }
    }
}
