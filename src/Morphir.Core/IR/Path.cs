using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Generator.Equals;
using Morphir.Internals;
using static LanguageExt.Seq;
namespace Morphir.IR;

[Equatable]
public partial record Path([property:OrderedEquality]IImmutableList<Name> Names)
{
    [GeneratedRegex("[^\\w\\s]+")]
    private static partial Regex SeparatorRegex();

    /// <summary>
    /// Translates a string into a path by splitting it into names along special characters.
    /// The algorithm will treat any non-word characters that are not spaces as a path separator.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static Path FromString(string input)
    {
        var names = SeparatorRegex()
            .Split(input)
            .Select(Name.FromString);
        return new Path(names.ToImmutableList());
    }

    public IImmutableList<Name> ToList() => Names;
    public Seq<Name> ToSeq() => toSeq(Names);

    public string ToCanonicalString() => ToString(Name.ToKebabCase, "/");
    public string ToString(Func<Name, string> render, string separator) => Names.Select(render).MakeString(separator);
    public override string ToString() => ToCanonicalString();

    public static IImmutableList<Name> ToList(Path path) => path.ToList();
    
    public static string ToString(Func<Name, string> render, string separator, Path path) =>
        path.ToString(render, separator);
}
