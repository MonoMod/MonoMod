using Mono.Cecil;
using System.Reflection;

namespace MonoMod.Utils
{
    public class DynamicMethodReference : MethodReference
    {
        public MethodInfo DynamicMethod { get; }

        public DynamicMethodReference(ModuleDefinition module, MethodInfo dm)
            : base("", Helpers.ThrowIfNull(module).ImportReference(Helpers.ThrowIfNull(dm).ReturnType))
        {
            DynamicMethod = dm;
            foreach (var param in dm.GetParameters())
            {
                Parameters.Add(new ParameterDefinition(module.ImportReference(param.ParameterType)));
            }
        }
    }
}
