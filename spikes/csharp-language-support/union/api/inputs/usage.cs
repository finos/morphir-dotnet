// How does code that makes and reads a union look as operations?
namespace Acme.Domain;
public sealed record Circle(double Radius);
public sealed record Rect(double W, double H);
public union Shape(Circle, Rect);
public union Amount(int, string);
public static class Calc
{
    public static double Area(Shape s) => s switch
    {
        Circle c => 3.14 * c.Radius * c.Radius,
        Rect r => r.W * r.H,
    };
    public static double Positional(Shape s) => s switch
    {
        Circle(var radius) => radius,
        Rect { W: var w } => w,
    };
    public static double Statement(Shape s)
    {
        switch (s)
        {
            case Circle c: return c.Radius;
            case Rect r: return r.W;
        }
        return 0;
    }
    public static Shape Implicit(double r) => new Circle(r);
    public static Shape Local(double r) { Shape s = new Circle(r); return s; }
    public static Shape Explicit(double r) => new Shape(new Circle(r));
    public static Shape Cast(double r) => (Shape)new Circle(r);
    public static Amount Number() => 5;
    public static bool IsCircle(Shape s) => s is Circle;
    public static bool Same(Shape a, Shape b) => a.Equals(b);
    // The frontend must refuse the next four: they reach the state with no case.
    public static object? Raw(Shape s) => s.Value;
    public static Shape Default() => default;
    public static double NullArm(Shape s) => s switch { Circle c => 1, Rect r => 2, null => 3 };
    // A missing arm: warning CS8509.
    public static double Missing(Shape s) => s switch { Circle c => c.Radius };
}
