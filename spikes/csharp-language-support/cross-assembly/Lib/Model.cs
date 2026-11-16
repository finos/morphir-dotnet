namespace Acme.Model;
public record Cat(string Name);
public record Dog(string Name);
public union Pet(Cat, Dog);
public closed record Order;
public sealed record Market(decimal Quantity) : Order;
public sealed record Limit(decimal Quantity, decimal Price) : Order;
