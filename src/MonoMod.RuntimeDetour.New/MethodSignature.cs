using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.RuntimeDetour {
    public class MethodSignature : IEquatable<MethodSignature> {
        public Type ReturnType { get; }

        private readonly Type[] parameters;
        public int ParameterCount => parameters.Length;
        public IEnumerable<Type> Parameters => parameters;

        public MethodSignature(Type returnType, Type[] parameters) {
            ReturnType = returnType;
            this.parameters = parameters;
        }

        public MethodSignature(MethodBase method) {
            ReturnType = (method as MethodInfo)?.ReturnType ?? typeof(void);

            var thisCount = method.IsStatic ? 0 : 1;

            var methParams = method.GetParameters();
            parameters = new Type[methParams.Length + thisCount];

            for (var i = thisCount; i < parameters.Length + thisCount; i++) {
                parameters[i] = methParams[i].ParameterType;
            }

            if (!method.IsStatic) {
                parameters[0] = method.GetThisParamType();
            }
        }

        public override string ToString() {
            var sb = new StringBuilder();
            _ = sb.Append(ReturnType.ToString())
                .Append('(');
            for (var i = 0; i < parameters.Length; i++) {
                if (i != 0)
                    _ = sb.Append(',');
                _ = sb.Append(parameters[i].ToString());
            }
            return sb.Append(')').ToString();
        }

        private static readonly ConditionalWeakTable<MethodBase, MethodSignature> sigMap = new();
        public static MethodSignature ForMethod(MethodBase method)
            => sigMap.GetValue(method, m => new(m));

        public bool Equals(MethodSignature? other) {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (!ReturnType.Equals(other.ReturnType))
                return false;
            return Parameters.SequenceEqual(other.Parameters);
        }

        public override bool Equals(object? obj)
            => obj is MethodSignature sig && Equals(sig);

        public override int GetHashCode() {
            HashCode hc = default;
            hc.Add(ReturnType);
            hc.Add(parameters.Length);
            foreach (var type in parameters) {
                hc.Add(type);
            }
            return hc.GetHashCode();
        }
    }
}
