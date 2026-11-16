using Acme;
Console.WriteLine(Trading.Notional(new Limit(Side.Buy, 2m, 10m), 9m));
Console.WriteLine(Trading.Area(new Circle(1m)));
Console.WriteLine(Trading.Describe(new Cat("Tom")));

namespace Acme
{
    public enum Side { Buy, Sell }

    // Closed hierarchy: a sum type with named cases.
    public closed record Order;
    public sealed record Market(Side Side, decimal Quantity) : Order;
    public sealed record Limit(Side Side, decimal Quantity, decimal Price) : Order;

    public closed record Shape;
    public sealed record Circle(decimal Radius) : Shape;
    public sealed record Square(decimal Edge) : Shape;

    // Union: a type union over existing types.
    public record Cat(string Name);
    public record Dog(string Name);
    public union Pet(Cat, Dog);

    public static class Trading
    {
        // No discard arm: the compiler must accept these as exhaustive.
        public static decimal Notional(Order order, decimal last) => order switch
        {
            Market m => m.Quantity * last,
            Limit l => l.Quantity * l.Price,
        };
        public static decimal Area(Shape shape) => shape switch
        {
            Circle c => 3m * c.Radius * c.Radius,
            Square s => s.Edge * s.Edge,
        };
        public static string Describe(Pet pet) => pet switch
        {
            Cat c => c.Name,
            Dog d => d.Name,
        };
    }
}
