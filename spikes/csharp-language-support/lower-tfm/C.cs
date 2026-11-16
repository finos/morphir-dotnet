namespace Demo;
public closed record Shape;
public sealed record Circle(double R) : Shape;
public sealed record Square(double S) : Shape;
public static class UseShape
{
    public static double Area(Shape s) => s switch { Circle c => c.R, Square q => q.S };
}
