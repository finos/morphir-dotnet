// Which case types does the compiler accept, and how does a union look as a symbol?
namespace Acme.Domain;
public sealed record Circle(double Radius);
public sealed record Rect(double W, double H);
public sealed record Nothing;
public union Shape(Circle, Rect);
public union Other(Circle, Nothing);                                    // shares the case Circle with Shape
public union Amount(int, string);                                       // SDK types as cases
public union MaybeInt(int?, Nothing);
public union Tup((int, int), Nothing);                                  // a case with no name
public union Opt<T>(T, Nothing);                                        // a type parameter as a case
public union Nested(Shape, Nothing);                                    // a union as a case
public union WithList(System.Collections.Generic.List<int>, Nothing);
public union WithBody(Circle, Rect) { public bool IsRound => Value is Circle; }
public union Dup(Circle, Circle);                                       // error CS0111
