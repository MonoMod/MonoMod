using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        public Type? FirstParameter => parameters.Length >= 1 ? parameters[0] : null;

        public MethodSignature(Type returnType, Type[] parameters) {
            ReturnType = returnType;
            this.parameters = parameters;
        }

        public MethodSignature(Type returnType, IEnumerable<Type> parameters) {
            ReturnType = returnType;
            this.parameters = parameters.ToArray();
        }

        public MethodSignature(MethodBase method) : this(method, false) { }

        public MethodSignature(MethodBase method, bool ignoreThis) {
            ReturnType = (method as MethodInfo)?.ReturnType ?? typeof(void);

            var thisCount = ignoreThis || method.IsStatic ? 0 : 1;

            var methParams = method.GetParameters();
            parameters = new Type[methParams.Length + thisCount];

            for (var i = thisCount; i < parameters.Length; i++) {
                parameters[i] = methParams[i - thisCount].ParameterType;
            }

            if (!ignoreThis && !method.IsStatic) {
                parameters[0] = method.GetThisParamType();
            }
        }

        private static readonly ConditionalWeakTable<MethodBase, MethodSignature> thisSigMap = new();
        private static readonly ConditionalWeakTable<MethodBase, MethodSignature> noThisSigMap = new();
        public static MethodSignature ForMethod(MethodBase method) => ForMethod(method, false);
        public static MethodSignature ForMethod(MethodBase method, bool ignoreThis) {
            return (ignoreThis ? noThisSigMap : thisSigMap).GetValue(method, m => new(m, ignoreThis));
        }

        private sealed class CompatableComparer : IEqualityComparer<Type> {
            public static readonly CompatableComparer Instance = new();
            public bool Equals(Type? x, Type? y) {
                if (ReferenceEquals(x, y))
                    return true;
                if (x is null || y is null)
                    return false;
                return x.IsCompatible(y);
            }

            public int GetHashCode([DisallowNull] Type obj) {
                throw new NotSupportedException();
            }
        }

        public bool IsCompatibleWith(MethodSignature other) {
            Helpers.ThrowIfArgumentNull(other);
            return ReturnType.IsCompatible(other.ReturnType)
                && parameters.SequenceEqual(other.Parameters, CompatableComparer.Instance);
        }

        public DynamicMethodDefinition CreateDmd(string name) {
            return new(name, ReturnType, parameters);
        }

        public override string ToString() {
            var literals = 2 + parameters.Length - 1;
            var holes = 1 + parameters.Length;

            var sh = new DefaultInterpolatedStringHandler(literals, holes);
            sh.AppendFormatted(ReturnType);
            sh.AppendLiteral(" (");
            for (var i = 0; i < parameters.Length; i++) {
                if (i != 0)
                    sh.AppendLiteral(", ");
                sh.AppendFormatted(parameters[i]);
            }
            sh.AppendLiteral(")");
            return sh.ToStringAndClear();
        }

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
            return hc.ToHashCode();
        }
    }
}
