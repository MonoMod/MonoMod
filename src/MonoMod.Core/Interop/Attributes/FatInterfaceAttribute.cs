using System;

namespace MonoMod.Core.Interop.Attributes
{
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    internal sealed class FatInterfaceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    internal sealed class FatInterfaceImplAttribute : Attribute
    {
        public Type FatInterface { get; }

        public FatInterfaceImplAttribute(Type fatInterface)
        {
            FatInterface = fatInterface;
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class FatInterfaceIgnoreAttribute : Attribute { }
}
