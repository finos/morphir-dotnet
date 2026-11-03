using System.Collections;
using System.Collections.Immutable;

namespace Morphir.SDK.CSharp;

/// <summary>A persistent set traversed in ascending Morphir order.</summary>
public sealed class Set<T> : IEnumerable<T>, IEquatable<Set<T>>, ISdkValue
{
    private readonly ImmutableSortedSet<T> _items;
    public MorphirComparer<T> Comparer { get; }
    private Set(ImmutableSortedSet<T> items, MorphirComparer<T> comparer) { _items = items; Comparer = comparer; }
    public int Count => _items.Count;
    public static Set<T> Empty(MorphirComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return new(ImmutableSortedSet.Create<T>(comparer), comparer);
    }
    public static Set<T> Singleton(T value, MorphirComparer<T> comparer) => Empty(comparer).Insert(value);
    public static Set<T> FromEnumerable(IEnumerable<T> values, MorphirComparer<T> comparer)
    {
        var result = Empty(comparer);
        foreach (var value in values) result = result.Insert(value);
        return result;
    }
    public Set<T> Insert(T value) { Comparer.Validate(value); return new(_items.Add(value), Comparer); }
    public Set<T> Remove(T value) { Comparer.Validate(value); return new(_items.Remove(value), Comparer); }
    public Set<T> Union(Set<T> right) { var result = this; foreach (var value in right) result = result.Insert(value); return result; }
    public List<T> ToList() => List<T>.FromEnumerable(_items);
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(Set<T>? other) => other is not null && ToList().Equals(other.ToList());
    public override bool Equals(object? obj) => obj is Set<T> other && Equals(other);
    public override int GetHashCode() => ToList().GetHashCode();
    public static bool operator ==(Set<T>? left, Set<T>? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(Set<T>? left, Set<T>? right) => !(left == right);
}
