using System.Collections;
using System.Collections.Immutable;

namespace Morphir.SDK.CSharp;

/// <summary>A persistent list with structural Morphir equality and hashing.</summary>
public sealed class List<T> : IReadOnlyList<T>, IEquatable<List<T>>, ISdkValue, IComparableSequence
{
    private readonly ImmutableList<T> _items;
    private List(ImmutableList<T> items) => _items = items;
    public static List<T> Empty { get; } = new(ImmutableList<T>.Empty);
    public static List<T> Singleton(T value) { ArgumentNullException.ThrowIfNull(value); return new(ImmutableList.Create(value)); }
    public static List<T> FromEnumerable(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new(values.Select(x => { ArgumentNullException.ThrowIfNull(x); return x; }).ToImmutableList());
    }
    public int Length => _items.Count;
    public int Count => Length;
    public T this[int index] => _items[index];
    public List<T> Cons(T head) { ArgumentNullException.ThrowIfNull(head); return new(_items.Insert(0, head)); }
    public List<TResult> Map<TResult>(Func<T, TResult> callback) => List<TResult>.FromEnumerable(_items.Select(callback));
    public List<T> Append(List<T> right) => new(_items.AddRange(right._items));
    public Maybe<T> Head() => Length == 0 ? Maybe<T>.Nothing : Maybe<T>.Just(_items[0]);
    public Maybe<List<T>> Tail() => Length == 0 ? Maybe<List<T>>.Nothing : Maybe<List<T>>.Just(new(_items.RemoveAt(0)));
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(List<T>? other)
    {
        if (other is null || Length != other.Length) return false;
        for (var i = 0; i < Length; i++) if (!ValueSemantics.Equal(this[i], other[i])) return false;
        return true;
    }
    public override bool Equals(object? obj) => obj is List<T> other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var item in _items) hash.Add(ValueSemantics.Hash(item)); return hash.ToHashCode(); }
    public static bool operator ==(List<T>? left, List<T>? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(List<T>? left, List<T>? right) => !(left == right);
    IEnumerable<object> IComparableSequence.ComparableItems => _items.Select(x => (object)x!);
    int IComparableSequence.CompareSequence(IComparableSequence other)
    {
        var a = ((IComparableSequence)this).ComparableItems.ToArray();
        var b = other.ComparableItems.ToArray();
        MorphirComparers.ValidateSequenceTypes(a.Concat(b));
        foreach (var item in a) MorphirComparers.Value.Validate(item);
        foreach (var item in b) MorphirComparers.Value.Validate(item);
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) { var c = MorphirComparers.Value.Compare(a[i], b[i]); if (c != 0) return c; }
        return a.Length.CompareTo(b.Length);
    }
}
