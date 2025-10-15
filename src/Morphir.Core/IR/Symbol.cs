namespace Morphir.IR;

public record Symbol(Seq<string> Segments)
{
    public static Symbol FromList(params string[] segments) => new (toSeq(segments));
}

public record PackageSymbol(Seq<string> Segments) : Symbol(Segments)
{
    public new static PackageSymbol FromList(params Seq<string> segments) => new PackageSymbol(segments);
}

