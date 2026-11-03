using System.Runtime.CompilerServices;

namespace Morphir.SDK.CSharp;

internal interface ISdkValue;
internal interface IComparableSequence
{
    int CompareSequence(IComparableSequence other);
    IEnumerable<object> ComparableItems { get; }
}

internal static class ValueSemantics
{
    internal static bool Equal<T>(T left, T right)
    {
        if (left is null || right is null) throw new NotSupportedException("Null is outside the Morphir value domain.");
        if (left is IMorphirValue custom && right is IMorphirValue other) return custom.MorphirEquals(other);
        if (left is double a && right is double b) return a == b;
        if (left is ITuple x && right is ITuple y)
        {
            if (x.Length != y.Length) return false;
            for (var i = 0; i < x.Length; i++) if (!Equal(x[i], y[i])) return false;
            return true;
        }
        if (left is int or decimal or string or char or bool or Unit or Order or ISdkValue) return left.Equals(right);
        throw new NotSupportedException("Equality requires a supported scalar, SDK value, tuple or explicit IMorphirValue implementation.");
    }

    internal static int Hash<T>(T value)
    {
        if (value is null) throw new NotSupportedException("Null is outside the Morphir value domain.");
        if (value is IMorphirValue custom) return custom.MorphirHashCode();
        if (value is ITuple tuple)
        {
            var hash = new HashCode();
            for (var i = 0; i < tuple.Length; i++) hash.Add(Hash(tuple[i]));
            return hash.ToHashCode();
        }
        if (value is int or double or decimal or string or char or bool or Unit or Order or ISdkValue)
            return value.GetHashCode();
        throw new NotSupportedException("Hashing requires a supported immutable value.");
    }
}
