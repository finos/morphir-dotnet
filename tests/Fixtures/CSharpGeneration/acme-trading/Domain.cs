namespace Acme.Trading.Domain;                     // module domain: types only

public enum Side { Buy, Sell }
public closed record Order;
public sealed record Market(Side Side, decimal Quantity) : Order;
public sealed record Limit(Side Side, decimal Quantity, decimal Price) : Order;

public static class Pricing                        // module domain/pricing: values
{
    public const decimal Fee = 2.5m;

    public static decimal Notional(Order order, decimal last) => order switch
    {
        Market m => m.Quantity * last,
        Limit l => l.Quantity * l.Price,
    };
}
