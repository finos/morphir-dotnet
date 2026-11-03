using System.Collections;
using System.Collections.Immutable;

namespace Morphir.SDK.CSharp;

/// <summary>A persistent, ascending dictionary. Duplicate construction keeps the last value; union keeps the left value.</summary>
public sealed class Dict<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IEquatable<Dict<TKey, TValue>>, ISdkValue where TKey : notnull
{
    private readonly ImmutableSortedDictionary<TKey, TValue> _items;
    public MorphirComparer<TKey> Comparer { get; }
    private Dict(ImmutableSortedDictionary<TKey, TValue> items, MorphirComparer<TKey> comparer) { _items = items; Comparer = comparer; }
    public int Count => _items.Count;
    public static Dict<TKey, TValue> Empty(MorphirComparer<TKey> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return new(ImmutableSortedDictionary.Create<TKey, TValue>(comparer), comparer);
    }
    public static Dict<TKey, TValue> Singleton(TKey key, TValue value, MorphirComparer<TKey> comparer) => Empty(comparer).Insert(key, value);
    public static Dict<TKey, TValue> FromEnumerable(IEnumerable<(TKey Key, TValue Value)> entries, MorphirComparer<TKey> comparer)
    {
        var result = Empty(comparer);
        foreach (var (key, value) in entries) result = result.Insert(key, value);
        return result;
    }
    public Maybe<TValue> Get(TKey key) { Comparer.Validate(key); return _items.TryGetValue(key, out var value) ? Maybe<TValue>.Just(value) : Maybe<TValue>.Nothing; }
    public Dict<TKey, TValue> Insert(TKey key, TValue value)
    {
        Comparer.Validate(key); ArgumentNullException.ThrowIfNull(value);
        // SetItem may retain an equal CLR value, losing a replacement's signed-zero
        // bits or authored decimal scale. Remove/Add always retains the supplied payload.
        return new(_items.Remove(key).Add(key, value), Comparer);
    }
    public Dict<TKey, TValue> Update(TKey key, Func<Maybe<TValue>, Maybe<TValue>> callback) => callback(Get(key)).Match(value => Insert(key, value), () => Remove(key));
    public Dict<TKey, TValue> Remove(TKey key) { Comparer.Validate(key); return new(_items.Remove(key), Comparer); }
    public List<(TKey, TValue)> ToList() => List<(TKey, TValue)>.FromEnumerable(_items.Select(x => (x.Key, x.Value)));
    public Dict<TKey, TValue> Union(Dict<TKey, TValue> right)
    {
        var result = this;
        foreach (var entry in right) if (!result.Get(entry.Key).IsJust) result = result.Insert(entry.Key, entry.Value);
        return result;
    }
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => ((IEnumerable<KeyValuePair<TKey, TValue>>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(Dict<TKey, TValue>? other)
    {
        if (other is null || Count != other.Count) return false;
        using var left = GetEnumerator(); using var right = other.GetEnumerator();
        while (left.MoveNext() && right.MoveNext())
            if (!ValueSemantics.Equal(left.Current.Key, right.Current.Key) || !ValueSemantics.Equal(left.Current.Value, right.Current.Value)) return false;
        return true;
    }
    public override bool Equals(object? obj) => obj is Dict<TKey, TValue> other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var (key, value) in _items) { hash.Add(ValueSemantics.Hash(key)); hash.Add(ValueSemantics.Hash(value)); } return hash.ToHashCode(); }
    public static bool operator ==(Dict<TKey, TValue>? left, Dict<TKey, TValue>? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(Dict<TKey, TValue>? left, Dict<TKey, TValue>? right) => !(left == right);
}
