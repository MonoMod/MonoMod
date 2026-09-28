using System;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour.Generics
{
    internal static class GenericInstantiator
    {
        private const BindingFlags AllDecl = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Given the open source/target and a full type-argument vector, returns the concrete pair.
        /// Vector layout: [declaringType args ..., method args ...].
        /// </summary>
        public static (MethodBase Source, MethodInfo Target) Close(MethodBase openSource, MethodInfo openTarget, Type[] typeArgs)
        {
            var closedSource = CloseSource(openSource, typeArgs);

            if (!openTarget.IsGenericMethodDefinition ||
                openTarget.GetGenericArguments().Length != typeArgs.Length)
            {
                throw new ArgumentException("Target must be an open generic method whose arity equals the source's (declaring-type args + method args).", nameof(openTarget));
            }

            return (closedSource, openTarget.MakeGenericMethod(typeArgs));
        }

        /// <summary>
        /// Closes just the open source over a full type-argument vector (declaring-type args ++ method args).
        /// </summary>
        public static MethodBase CloseSource(MethodBase openSource, Type[] typeArgs)
        {
            var declDef = openSource.DeclaringType;
            if (declDef is { IsGenericType: true, IsGenericTypeDefinition: false })
            {
                declDef = declDef.GetGenericTypeDefinition();
            }

            var declArity = declDef is { IsGenericTypeDefinition: true } ? declDef.GetGenericArguments().Length : 0;
            var methodArity = openSource.IsGenericMethodDefinition ? openSource.GetGenericArguments().Length : 0;

            if (typeArgs.Length != declArity + methodArity)
            {
                throw new ArgumentException($"Source needs {declArity + methodArity} type args, got {typeArgs.Length}.", nameof(typeArgs));
            }

            var declArgs = typeArgs.Take(declArity).ToArray();
            var methodArgs = typeArgs.Skip(declArity).ToArray();

            var srcOnType = declArity > 0
                ? ResolveOnClosedType(openSource, declDef!.MakeGenericType(declArgs))
                : openSource;

            return methodArity > 0 ? ((MethodInfo)srcOnType).MakeGenericMethod(methodArgs) : srcOnType;
        }

        private static MethodBase ResolveOnClosedType(MethodBase openDef, Type closedType)
        {
            if (openDef is ConstructorInfo)
            {
                foreach (var c in closedType.GetConstructors(AllDecl))
                {
                    if (c.MetadataToken == openDef.MetadataToken && c.Module == openDef.Module)
                    {
                        return c;
                    }
                }
            }
            else
            {
                foreach (var m in closedType.GetMethods(AllDecl))
                {
                    if (m.MetadataToken == openDef.MetadataToken && m.Module == openDef.Module)
                    {
                        return m;
                    }
                }
            }
            throw new MissingMethodException($"Could not locate {openDef} on {closedType}.");
        }
    }
}
