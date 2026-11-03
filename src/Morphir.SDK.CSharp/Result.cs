namespace Morphir.SDK.CSharp;

/// <summary>An immutable result with the error type first and success type second.</summary>
public sealed class Result<TError, TValue> : IEquatable<Result<TError, TValue>>, ISdkValue
{
    private readonly TError _error;
    private readonly TValue _value;
    private Result(bool isOk, TError error, TValue value) { IsOk = isOk; _error = error; _value = value; }

    public bool IsOk { get; }
    public static Result<TError, TValue> Ok(TValue value) { ArgumentNullException.ThrowIfNull(value); return new(true, default!, value); }
    public static Result<TError, TValue> Err(TError error) { ArgumentNullException.ThrowIfNull(error); return new(false, error, default!); }
    public TResult Match<TResult>(Func<TError, TResult> err, Func<TValue, TResult> ok) => IsOk ? ok(_value) : err(_error);
    public Result<TError, TResult> Map<TResult>(Func<TValue, TResult> callback) => IsOk ? Result<TError, TResult>.Ok(callback(_value)) : Result<TError, TResult>.Err(_error);
    public Result<TError, TResult> AndThen<TResult>(Func<TValue, Result<TError, TResult>> callback) => IsOk ? callback(_value) : Result<TError, TResult>.Err(_error);
    public bool Equals(Result<TError, TValue>? other) => other is not null && IsOk == other.IsOk && (IsOk ? ValueSemantics.Equal(_value, other._value) : ValueSemantics.Equal(_error, other._error));
    public override bool Equals(object? obj) => obj is Result<TError, TValue> other && Equals(other);
    public override int GetHashCode() => IsOk ? HashCode.Combine(1, ValueSemantics.Hash(_value)) : HashCode.Combine(0, ValueSemantics.Hash(_error));
    public static bool operator ==(Result<TError, TValue>? left, Result<TError, TValue>? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(Result<TError, TValue>? left, Result<TError, TValue>? right) => !(left == right);
}
