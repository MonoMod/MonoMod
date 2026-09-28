using System;

namespace MonoMod.RuntimeDetour.Generics
{
    /// <summary>
    /// A full type-argument vector for one closed instantiation:
    /// declaring-type arguments followed by method arguments.
    /// </summary>
    public readonly struct InstantiationKey : IEquatable<InstantiationKey>
    {
        public Type[] TypeArguments { get; }
        public InstantiationKey(Type[] typeArguments) => TypeArguments = typeArguments;

        public bool Equals(InstantiationKey other)
        {
            var a = TypeArguments;
            var b = other.TypeArguments;
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null || a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        // Per-position IsAssignableFrom: reference positions widen, value positions match exactly.
        public static bool Matches(InstantiationKey pattern, InstantiationKey actual)
        {
            var p = pattern.TypeArguments;
            var a = actual.TypeArguments;
            if (p.Length != a.Length)
            {
                return false;
            }

            for (var i = 0; i < p.Length; i++)
            {
                if (!p[i].IsAssignableFrom(a[i]))
                {
                    return false;
                }
            }

            return true;
        }
        public override string ToString()
            => $"<{string.Join(",", Array.ConvertAll(TypeArguments ?? Type.EmptyTypes, t => t.Name))}>";

        public override bool Equals(object? obj)
        {
            return obj is InstantiationKey k && Equals(k);
        }

        public override int GetHashCode()
        {
            var h = 17;
            if (TypeArguments is not null)
            {
                foreach (var t in TypeArguments)
                {
                    h = unchecked(h * 31 + (t?.GetHashCode() ?? 0));
                }
            }

            return h;
        }

        public static bool operator ==(InstantiationKey left, InstantiationKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(InstantiationKey left, InstantiationKey right)
        {
            return !(left == right);
        }
    }
}