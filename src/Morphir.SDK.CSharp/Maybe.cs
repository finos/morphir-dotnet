namespace Morphir.SDK.CSharp;

/// <summary>An immutable optional value. The default value is Nothing.</summary>
public readonly struct Maybe<T> : IEquatable<Maybe<T>>, ISdkValue
{
    private readonly T _value;
    private Maybe(T value) { ArgumentNullException.ThrowIfNull(value); _value = value; IsJust = true; }

    public bool IsJust { get; }
    public static Maybe<T> Nothing => default;
    public static Maybe<T> Just(T value) => new(value);
    public TResult Match<TResult>(Func<T, TResult> just, Func<TResult> nothing) => IsJust ? just(_value) : nothing();
    public Maybe<TResult> Map<TResult>(Func<T, TResult> callback) => IsJust ? Maybe<TResult>.Just(callback(_value)) : Maybe<TResult>.Nothing;
    public Maybe<TResult> AndThen<TResult>(Func<T, Maybe<TResult>> callback) => IsJust ? callback(_value) : Maybe<TResult>.Nothing;
    public bool Equals(Maybe<T> other) => IsJust == other.IsJust && (!IsJust || ValueSemantics.Equal(_value, other._value));
    public override bool Equals(object? obj) => obj is Maybe<T> other && Equals(other);
    public override int GetHashCode() => IsJust ? HashCode.Combine(1, ValueSemantics.Hash(_value)) : 0;
    public static bool operator ==(Maybe<T> left, Maybe<T> right) => left.Equals(right);
    public static bool operator !=(Maybe<T> left, Maybe<T> right) => !left.Equals(right);
}
