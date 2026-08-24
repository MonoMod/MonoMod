using MonoMod.Utils;
using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour.Generics
{
    internal sealed class GenericOrigCloner : IDisposable
    {
        private readonly DynamicMethodDefinition dmd;

        public MethodInfo Method { get; }

        private GenericOrigCloner(DynamicMethodDefinition dmd, MethodInfo generated)
        {
            this.dmd = dmd;
            Method = generated;
        }

        public static GenericOrigCloner Create(MethodBase closedSource)
        {
            Helpers.ThrowIfArgumentNull(closedSource);
            if (closedSource.ContainsGenericParameters)
            {
                throw new ArgumentException("closedSource must be a fully closed instantiation.", nameof(closedSource));
            }

            var dmd = new DynamicMethodDefinition(closedSource);
            try
            {
                return new GenericOrigCloner(dmd, dmd.Generate());
            }
            catch
            {
                dmd.Dispose();
                throw;
            }
        }

        public Delegate CreateDelegate(Type delegateType)
        {
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException($"{delegateType} is not a delegate type.", nameof(delegateType));
            }

            return Method.CreateDelegate(delegateType);
        }

        public void Dispose() => dmd.Dispose();
    }
}
