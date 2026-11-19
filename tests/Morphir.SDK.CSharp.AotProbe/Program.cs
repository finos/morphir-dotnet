// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.SDK.CSharp;
using List = Morphir.SDK.CSharp.List<Point>;

// A Native AOT program that calls the SDK equality and hashing on plain C# values, as generated code and code that people
// write do. Each line gives one check and its result. The program writes the lines and ends with exit code 1 when a check
// fails, so a wrong result is found also when the built program and the published program give the same output.
var checks = new (string Name, Func<bool> Check)[]
{
    ("equal plain records", () => Basics.Equal(new Point(1, 2), new Point(1, 2))),
    ("different plain records", () => !Basics.Equal(new Point(1, 2), new Point(2, 1))),
    ("hash of equal plain records", () => MorphirValue.Hash(new Point(1, 2)) == MorphirValue.Hash(new Point(1, 2))),
    ("equal lists of plain records", () => Basics.Equal(Points(), Points())),
    ("hash of equal lists of plain records", () => Points().GetHashCode() == Points().GetHashCode()),
    ("equal tuples that hold a plain record", () => Basics.Equal((new Point(1, 2), "a"), (new Point(1, 2), "a"))),
    ("different tuples that hold a plain record", () => !Basics.Equal((new Point(1, 2), "a"), (new Point(3, 4), "a"))),
    ("hash of equal tuples that hold a plain record", () => MorphirValue.Hash((new Point(1, 2), "a")) == MorphirValue.Hash((new Point(1, 2), "a"))),
    ("equal plain unions", () => Basics.Equal(new Shape(new Circle(1d)), new Shape(new Circle(1d)))),
    ("plain unions of different cases", () => !Basics.Equal(new Shape(new Circle(1d)), new Shape(new Square(1d)))),
    ("equal maybes of plain records", () => Basics.Equal(Maybe<Point>.Just(new Point(1, 2)), Maybe<Point>.Just(new Point(1, 2)))),
};

var failed = 0;

foreach (var (name, check) in checks)
{
    bool passed;

    try
    {
        passed = check();
    }
    catch (NotSupportedException)
    {
        passed = false;
    }

    Console.WriteLine($"{name}: {(passed ? "pass" : "fail")}");
    if (!passed) failed++;
}

return failed == 0 ? 0 : 1;

static List Points() => List.FromEnumerable([new Point(1, 2), new Point(3, 4)]);

internal sealed record Point(int X, int Y);
internal sealed record Circle(double Radius);
internal sealed record Square(double Side);
internal union Shape(Circle, Square);
