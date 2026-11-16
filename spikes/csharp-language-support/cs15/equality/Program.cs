using Acme;
var plainA = new PlainQuote(double.NaN); var plainB = new PlainQuote(double.NaN);
var morphirA = new Quote(double.NaN); var morphirB = new Quote(double.NaN);
Console.WriteLine($"plain record:   NaN quote == NaN quote: {plainA == plainB}");
Console.WriteLine($"morphir record: NaN quote == NaN quote: {morphirA == morphirB}");
Console.WriteLine($"morphir record: 1.5 quote == 1.5 quote: {new Quote(1.5) == new Quote(1.5)}");
Price price = new Quote(2.0);
Console.WriteLine(price switch { Quote q => q.Value, NoPrice => 0.0 });

namespace Acme
{
    public interface IMorphirValue { bool MorphirEquals(IMorphirValue other); int MorphirHashCode(); }
    public sealed record PlainQuote(double Value);

    public closed record Price;
    public sealed record NoPrice : Price;
    public sealed record Quote(double Value) : Price, IMorphirValue
    {
        public bool MorphirEquals(IMorphirValue other) => other is Quote q && Value == q.Value;   // Morphir: NaN is not equal to NaN
        public int MorphirHashCode() => Value.GetHashCode();
        public bool Equals(Quote? other) => other is not null && MorphirEquals(other);
        public override int GetHashCode() => MorphirHashCode();
    }
}
