using Mono.Cecil;
using System;
using System.Reflection;
using System.Text;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        /// <summary>
        /// Get a reference ID that is similar to the full name, but consistent between System.Reflection and Mono.Cecil.
        /// </summary>
        /// <param name="method">The method to get the ID for.</param>
        /// <param name="name">The name to use instead of the reference's own name.</param>
        /// <param name="type">The ID to use instead of the reference's declaring type ID.</param>
        /// <param name="withType">Whether the type ID should be included or not. System.Reflection avoids it by default.</param>
        /// <param name="simple">Whether the ID should be "simple" (name only).</param>
        /// <returns>The ID.</returns>
        public static string GetID(this MethodReference method, string? name = null, string? type = null, bool withType = true, bool simple = false)
        {
            Helpers.ThrowIfArgumentNull(method);

            var builder = new StringBuilder();

            if (simple)
            {
                if (withType && (type != null || method.DeclaringType != null))
                    builder.Append(type ?? method.DeclaringType.GetPatchFullName()).Append("::");
                builder.Append(name ?? method.Name);
                return builder.ToString();
            }

            builder
                .Append(method.ReturnType.GetPatchFullName())
                .Append(' ');

            if (withType && (type != null || method.DeclaringType != null))
                builder.Append(type ?? method.DeclaringType.GetPatchFullName()).Append("::");

            builder
                .Append(name ?? method.Name);

            if (method is GenericInstanceMethod gim && gim.GenericArguments.Count != 0)
            {
                builder.Append('<');
                var arguments = gim.GenericArguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    builder.Append(arguments[i].GetPatchFullName());
                }
                builder.Append('>');

            }
            else if (method.GenericParameters.Count != 0)
            {
                builder.Append('<');
                var arguments = method.GenericParameters;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    builder.Append(arguments[i].Name);
                }
                builder.Append('>');
            }

            builder.Append('(');

            if (method.HasParameters)
            {
                var parameters = method.Parameters;
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = parameters[i];
                    if (i > 0)
                        builder.Append(',');

                    if (parameter.ParameterType.IsSentinel)
                        builder.Append("...,");

                    builder.Append(parameter.ParameterType.GetPatchFullName());
                }
            }

            builder.Append(')');

            return builder.ToString();
        }

        /// <summary>
        /// Get a reference ID that is similar to the full name, but consistent between System.Reflection and Mono.Cecil.
        /// </summary>
        /// <param name="method">The call site to get the ID for.</param>
        /// <returns>The ID.</returns>
        public static string GetID(this Mono.Cecil.CallSite method)
        {
            Helpers.ThrowIfArgumentNull(method);
            var builder = new StringBuilder();

            builder
                .Append(method.ReturnType.GetPatchFullName())
                .Append(' ');

            builder.Append('(');

            if (method.HasParameters)
            {
                var parameters = method.Parameters;
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = parameters[i];
                    if (i > 0)
                        builder.Append(',');

                    if (parameter.ParameterType.IsSentinel)
                        builder.Append("...,");

                    builder.Append(parameter.ParameterType.GetPatchFullName());
                }
            }

            builder.Append(')');

            return builder.ToString();
        }

        private static readonly Type t_ParamArrayAttribute = typeof(ParamArrayAttribute);
        /// <summary>
        /// Get a reference ID that is similar to the full name, but consistent between System.Reflection and Mono.Cecil.
        /// </summary>
        /// <param name="method">The method to get the ID for.</param>
        /// <param name="name">The name to use instead of the reference's own name.</param>
        /// <param name="type">The ID to use instead of the reference's declaring type ID.</param>
        /// <param name="withType">Whether the type ID should be included or not. System.Reflection avoids it by default.</param>
        /// <param name="proxyMethod">Whether the method is regarded as a proxy method or not. Setting this paramater to true will skip the first parameter.</param>
        /// <param name="simple">Whether the ID should be "simple" (name only).</param>
        /// <returns>The ID.</returns>
        public static string GetID(this MethodBase method, string? name = null, string? type = null, bool withType = true, bool proxyMethod = false, bool simple = false)
        {
            Helpers.ThrowIfArgumentNull(method);
            while (method is MethodInfo mi && method.IsGenericMethod && !method.IsGenericMethodDefinition)
                method = mi.GetGenericMethodDefinition();

            var builder = new StringBuilder();

            if (simple)
            {
                if (withType && (type != null || method.DeclaringType != null))
                    builder.Append(type ?? method.DeclaringType!.FullName).Append("::");
                builder.Append(name ?? method.Name);
                return builder.ToString();
            }

            builder
                .Append((method as MethodInfo)?.ReturnType?.FullName ?? "System.Void")
                .Append(' ');

            if (withType && (type != null || method.DeclaringType != null))
                builder.Append(type ?? method.DeclaringType!.FullName?.Replace("+", "/", StringComparison.Ordinal)).Append("::");

            builder
                .Append(name ?? method.Name);

            if (method.ContainsGenericParameters)
            {
                builder.Append('<');
                var arguments = method.GetGenericArguments();
                for (var i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    builder.Append(arguments[i].Name);
                }
                builder.Append('>');
            }

            builder.Append('(');

            var parameters = method.GetParameters();
            for (var i = proxyMethod ? 1 : 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (i > (proxyMethod ? 1 : 0))
                    builder.Append(',');

                bool defined;
                try
                {
                    defined = parameter.GetCustomAttributes(t_ParamArrayAttribute, false).Length != 0;
                }
                catch (NotSupportedException)
                {
                    // Newer versions of Mono are stupidly strict and like to throw a NotSupportedException on DynamicMethod args.
                    defined = false;
                }
                if (defined)
                    builder.Append("...,");

                builder.Append(parameter.ParameterType.FullName);
            }

            builder.Append(')');

            return builder.ToString();
        }

    }
}
