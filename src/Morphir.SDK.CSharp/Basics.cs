using System.Numerics;

namespace Morphir.SDK.CSharp;

public static class Basics
{
    public const Order LT = Order.LT;
    public const Order EQ = Order.EQ;
    public const Order GT = Order.GT;

    public static int Add(int a, int b) => checked(a + b);
    public static double Add(double a, double b) => a + b;
    public static int Subtract(int a, int b) => checked(a - b);
    public static double Subtract(double a, double b) => a - b;
    public static int Multiply(int a, int b) => checked(a * b);
    public static double Multiply(double a, double b) => a * b;
    public static double Divide(double a, double b) => a / b;
    public static int IntegerDivide(int a, int b) => checked(a / b);
    public static int RemainderBy(int divisor, int dividend) => checked((int)((long)dividend % divisor));
    public static int ModBy(int divisor, int dividend)
    {
        var remainder = RemainderBy(divisor, dividend);
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? checked(remainder + divisor) : remainder;
    }
    public static int Negate(int value) => checked(-value);
    public static double Negate(double value) => -value;
    public static int Abs(int value) => Math.Abs(value);
    public static double Abs(double value) => Math.Abs(value);
    public static int Power(int value, int exponent)
    {
        if (exponent < 0) throw new NotSupportedException("Negative integer powers are outside the Int profile.");
        var result = 1;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result = checked(result * value);
            exponent >>= 1;
            if (exponent > 0) value = checked(value * value);
        }
        return result;
    }
    public static double Power(double value, double exponent) => Math.Pow(value, exponent);
    public static double ToFloat(int value) => value;
    public static int Round(double value)
    {
        var floor = Math.Floor(value);
        return ToInt(value - floor >= 0.5 ? floor + 1 : floor);
    }
    public static int Floor(double value) => ToInt(Math.Floor(value));
    public static int Ceiling(double value) => ToInt(Math.Ceiling(value));
    public static int Truncate(double value) => ToInt(Math.Truncate(value));
    private static int ToInt(double value)
    {
        if (!double.IsFinite(value)) throw new NonFiniteConversionException();
        return checked((int)value);
    }
    // The generic overloads for a type variable of the class number (Int or Float). The arithmetic is checked, so Int
    // keeps its overflow failure. A call with int or double selects the overload for that type, which gives the same result.
    public static T Add<T>(T a, T b) where T : INumber<T> => checked(a + b);
    public static T Subtract<T>(T a, T b) where T : INumber<T> => checked(a - b);
    public static T Multiply<T>(T a, T b) where T : INumber<T> => checked(a * b);
    public static T Negate<T>(T value) where T : INumber<T> => checked(-value);
    public static T Abs<T>(T value) where T : INumber<T> => T.Abs(value);
    public static T Clamp<T>(T low, T high, T value) where T : INumber<T> => value < low ? low : value > high ? high : value;
    public static T Power<T>(T value, T exponent) where T : INumber<T>
    {
        if (typeof(T) == typeof(double)) return T.CreateChecked(Math.Pow(double.CreateChecked(value), double.CreateChecked(exponent)));
        if (T.IsNegative(exponent) || !T.IsInteger(exponent)) throw new NotSupportedException("Negative integer powers are outside the Int profile.");
        var two = T.One + T.One;
        var result = T.One;
        while (exponent > T.Zero)
        {
            if (!T.IsEvenInteger(exponent)) result = checked(result * value);
            exponent /= two;
            if (exponent > T.Zero) value = checked(value * value);
        }
        return result;
    }
    public static bool IsNaN(double value) => double.IsNaN(value);
    public static bool IsInfinite(double value) => double.IsInfinity(value);
    public static Order Compare<T>(T a, T b) => MorphirComparers.Value.Compare(a!, b!) switch { < 0 => LT, > 0 => GT, _ => EQ };
    public static bool LessThan<T>(T a, T b) => Compare(a, b) == LT;
    public static bool LessThanOrEqual<T>(T a, T b) => Compare(a, b) != GT;
    public static bool GreaterThan<T>(T a, T b) => Compare(a, b) == GT;
    public static bool GreaterThanOrEqual<T>(T a, T b) => Compare(a, b) != LT;
    public static T Min<T>(T a, T b) => LessThan(a, b) ? a : b;
    public static T Max<T>(T a, T b) => GreaterThan(a, b) ? a : b;
    public static int Clamp(int low, int high, int value) => value < low ? low : value > high ? high : value;
    public static double Clamp(double low, double high, double value) => value < low ? low : value > high ? high : value;
    public static bool Equal<T>(T left, T right) => ValueSemantics.Equal(left, right);
    public static bool NotEqual<T>(T left, T right) => !Equal(left, right);
}

/// <summary>A Float-to-Int conversion was requested for NaN or infinity.</summary>
public sealed class NonFiniteConversionException() : ArithmeticException("NaN and infinity cannot be converted to Morphir Int.");
