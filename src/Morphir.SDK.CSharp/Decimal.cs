using System.Globalization;
using System.Numerics;

namespace Morphir.SDK.CSharp;

/// <summary>Exact Decimal96 operations. Unrepresentable fractions fail rather than round.</summary>
public static class Decimal
{
    private static readonly BigInteger Maximum = (BigInteger.One << 96) - 1;
    public static decimal Add(decimal a, decimal b) => AddParts(a, b, false);
    public static decimal Sub(decimal a, decimal b) => AddParts(a, b, true);
    private static decimal AddParts(decimal a, decimal b, bool subtract)
    {
        var (x, xs) = Parts(a); var (y, ys) = Parts(b);
        var scale = Math.Max(xs, ys);
        x *= BigInteger.Pow(10, scale - xs); y *= BigInteger.Pow(10, scale - ys);
        return Exact(subtract ? x - y : x + y, scale);
    }
    public static decimal Mul(decimal a, decimal b) { var (x, xs) = Parts(a); var (y, ys) = Parts(b); return Exact(x * y, xs + ys); }
    public static Maybe<decimal> Div(decimal a, decimal b)
    {
        if (b == 0) return Maybe<decimal>.Nothing;
        var (x, xs) = Parts(a); var (y, ys) = Parts(b);
        x *= BigInteger.Pow(10, ys); y *= BigInteger.Pow(10, xs);
        if (y.Sign < 0) { x = -x; y = -y; }
        var gcd = BigInteger.GreatestCommonDivisor(x, y); x /= gcd; y /= gcd;
        var twos = 0; var fives = 0;
        while (y % 2 == 0) { y /= 2; twos++; }
        while (y % 5 == 0) { y /= 5; fives++; }
        if (y != 1) throw new NotSupportedException("Nonterminating decimal division is outside the Decimal96 exact subset.");
        var scale = Math.Max(twos, fives);
        return Maybe<decimal>.Just(Exact(x * BigInteger.Pow(2, scale - twos) * BigInteger.Pow(5, scale - fives), scale));
    }
    public static Order Compare(decimal a, decimal b) => a < b ? Order.LT : a > b ? Order.GT : Order.EQ;
    public static string ToString(decimal value)
    {
        var (coefficient, scale) = Parts(value);
        Normalize(ref coefficient, ref scale);
        return Exact(coefficient, scale).ToString(CultureInfo.InvariantCulture);
    }
    private static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64;
        return (bits[3] < 0 ? -coefficient : coefficient, (bits[3] >> 16) & 255);
    }
    private static void Normalize(ref BigInteger coefficient, ref int scale)
    {
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
    }
    private static decimal Exact(BigInteger coefficient, int scale)
    {
        Normalize(ref coefficient, ref scale);
        var magnitude = BigInteger.Abs(coefficient);
        if (magnitude > Maximum) throw new OverflowException("Exact result exceeds Decimal96 coefficient magnitude.");
        if (scale > 28) throw new NotSupportedException("Exact result requires decimal scale greater than 28.");
        return new decimal((int)(uint)(magnitude & uint.MaxValue), (int)(uint)((magnitude >> 32) & uint.MaxValue),
            (int)(uint)((magnitude >> 64) & uint.MaxValue), coefficient.Sign < 0, (byte)scale);
    }
}
