using System.Runtime.CompilerServices;

namespace Morphir.SDK.CSharp;

/// <summary>An admitted Morphir ordering. Instances are created only by MorphirComparers.</summary>
public sealed class MorphirComparer<T> : IComparer<T>
{
    private readonly Comparison<T> _compare;
    internal MorphirComparer(Comparison<T> compare) => _compare = compare;
    public int Compare(T? x, T? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        return _compare(x, y);
    }
    internal void Validate(T value) => Compare(value, value);
}

/// <summary>Culture-independent orderings for the bounded Morphir comparable domain.</summary>
public static class MorphirComparers
{
    public static MorphirComparer<int> Int { get; } = new((x, y) => x.CompareTo(y));
    public static MorphirComparer<double> Float { get; } = new((x, y) => CompareValues(x, y));
    public static MorphirComparer<char> Char { get; } = new((x, y) => CompareValues(x, y));
    public static MorphirComparer<string> String { get; } = new(string.CompareOrdinal);
    /// <summary>Checks boxed values against the admitted domain at each comparison. No CLR fallback.</summary>
    public static MorphirComparer<object> Value { get; } = new(CompareValues);

    public static MorphirComparer<List<T>> List<T>(MorphirComparer<T> itemComparer) => new((x, y) =>
    {
        ValidateSequenceTypes(x.Select(item => (object)item!).Concat(y.Select(item => (object)item!)));
        foreach (var item in x) itemComparer.Validate(item);
        foreach (var item in y) itemComparer.Validate(item);
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var c = itemComparer.Compare(x[i], y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    });

    public static MorphirComparer<(T1, T2)> Tuple<T1, T2>(MorphirComparer<T1> first, MorphirComparer<T2> second) => new((x, y) =>
    {
        first.Validate(x.Item1); first.Validate(y.Item1); second.Validate(x.Item2); second.Validate(y.Item2);
        var c = first.Compare(x.Item1, y.Item1);
        return c != 0 ? c : second.Compare(x.Item2, y.Item2);
    });

    public static MorphirComparer<(T1, T2, T3)> Tuple<T1, T2, T3>(MorphirComparer<T1> first, MorphirComparer<T2> second, MorphirComparer<T3> third) => new((x, y) =>
    {
        first.Validate(x.Item1); first.Validate(y.Item1); second.Validate(x.Item2); second.Validate(y.Item2); third.Validate(x.Item3); third.Validate(y.Item3);
        var c = first.Compare(x.Item1, y.Item1);
        if (c != 0) return c;
        c = second.Compare(x.Item2, y.Item2);
        return c != 0 ? c : third.Compare(x.Item3, y.Item3);
    });

    internal static int CompareValues(object x, object y) => (x, y) switch
    {
        (int a, int b) => a.CompareTo(b),
        (double a, double b) when double.IsFinite(a) && double.IsFinite(b) => a.CompareTo(b),
        (string a, string b) => string.CompareOrdinal(a, b),
        (char a, char b) when !char.IsSurrogate(a) && !char.IsSurrogate(b) => a.CompareTo(b),
        (IComparableSequence a, IComparableSequence b) => a.CompareSequence(b),
        (ITuple a, ITuple b) when a.Length == b.Length && a.Length is 2 or 3 => CompareTuples(a, b),
        _ => throw new NotSupportedException("Morphir ordering admits Int, finite Float, BMP Char, String, and recursively comparable lists and 2/3-tuples only.")
    };

    private static int CompareTuples(ITuple a, ITuple b)
    {
        for (var i = 0; i < a.Length; i++) { CompareValues(a[i]!, a[i]!); CompareValues(b[i]!, b[i]!); }
        for (var i = 0; i < a.Length; i++) { var c = CompareValues(a[i]!, b[i]!); if (c != 0) return c; }
        return 0;
    }

    // A missing list element shape is an empty-list wildcard. Merge all observations
    // so an early empty nested list cannot conceal later Int/String disagreement.
    private sealed record Shape(string Kind, Shape? First = null, Shape? Second = null, Shape? Third = null);

    internal static void ValidateSequenceTypes(IEnumerable<object> values)
    {
        Shape? element = null;
        foreach (var value in values) element = Merge(element, ShapeOf(value));
    }

    private static Shape ShapeOf(object value) => value switch
    {
        int => new("Int"),
        double x when double.IsFinite(x) => new("Float"),
        string => new("String"),
        char x when !char.IsSurrogate(x) => new("Char"),
        IComparableSequence list => ListShape(list.ComparableItems),
        ITuple tuple when tuple.Length == 2 => new("Tuple2", ShapeOf(tuple[0]!), ShapeOf(tuple[1]!)),
        ITuple tuple when tuple.Length == 3 => new("Tuple3", ShapeOf(tuple[0]!), ShapeOf(tuple[1]!), ShapeOf(tuple[2]!)),
        _ => throw new NotSupportedException("Unsupported Morphir comparable element type.")
    };

    private static Shape ListShape(IEnumerable<object> items)
    {
        Shape? element = null;
        foreach (var item in items) element = Merge(element, ShapeOf(item));
        return new("List", element);
    }

    private static Shape? Merge(Shape? a, Shape? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        if (a.Kind != b.Kind) throw new NotSupportedException("Morphir lists require homogeneous comparable element types.");
        return new(a.Kind, Merge(a.First, b.First), Merge(a.Second, b.Second), Merge(a.Third, b.Third));
    }
}
