namespace System.Collections
{
    public interface IStructuralComparable
    {
        int CompareTo(object? other, IComparer comparer);
    }
    public interface IStructuralEquatable
    {
        bool Equals(object? other, IEqualityComparer comparer);

        int GetHashCode(IEqualityComparer comparer);
    }
}