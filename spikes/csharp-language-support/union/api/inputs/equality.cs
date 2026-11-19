// Can a union carry an interface list, equality members and operators? (The IEEE equality option.)
namespace Acme.Domain;
public interface IMorphirValue { bool MorphirEquals(IMorphirValue other); int MorphirHashCode(); }
public sealed record Circle(double Radius) : IMorphirValue
{
    public bool MorphirEquals(IMorphirValue other) => other is Circle c && Radius == c.Radius;
    public int MorphirHashCode() => Radius.GetHashCode();
    public bool Equals(Circle? other) => other is not null && MorphirEquals(other);
    public override int GetHashCode() => MorphirHashCode();
}
public sealed record Rect(double W, double H);
public union Shape(Circle, Rect) : IMorphirValue, System.IEquatable<Shape>
{
    public bool MorphirEquals(IMorphirValue other) => other is Shape s && Equals(s);
    public int MorphirHashCode() => GetHashCode();
    public bool Equals(Shape other) => this switch
    {
        Circle a => other is Circle b && a.MorphirEquals(b),
        Rect a => other is Rect b && a.Equals(b),
    };
    public override bool Equals(object? obj) => obj is Shape s && Equals(s);
    public override int GetHashCode() => this switch { Circle a => a.MorphirHashCode(), Rect a => a.GetHashCode() };
    public static bool operator ==(Shape l, Shape r) => l.Equals(r);
    public static bool operator !=(Shape l, Shape r) => !l.Equals(r);
}
public static class Calc
{
    public static bool Same(Shape a, Shape b) => a == b;
}
