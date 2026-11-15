using System.Runtime.CompilerServices;

namespace Morphir.SDK.CSharp;

internal interface ISdkValue;
internal interface IComparableSequence
{
    int CompareSequence(IComparableSequence other);
    IEnumerable<object> ComparableItems { get; }
}

/// <summary>
/// The Morphir equality and hashing of the SDK. A switch on the runtime type, with no reflection, so it works under Native
/// AOT: scalars, <c>double</c> with IEEE equality, tuples item by item, SDK values and <see cref="IMorphirValue"/>. Any other
/// value, such as a plain record or a plain union, uses its own <c>Equals</c> and <c>GetHashCode</c>. The backend refuses
/// equality on a type that holds a function, so the SDK has no function check at run time.
/// </summary>
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
        return left.Equals(right);
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
        return value.GetHashCode();
    }
}
