using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Generator.Equals;
using Morphir.Internals;
using Morphir.IR.Codecs;

namespace Morphir.IR;

[Equatable]
[JsonConverter(typeof(NameConverter))]
public partial record Name([property: OrderedEquality] IImmutableList<string> Segments): IName
{
    public bool IsEmpty => Segments.Count == 0;
    public IImmutableList<string> ToList() => Segments;

    public IImmutableList<string> ToHumanWords()
    {
        Func<IEnumerable<string>, string> joinAbbrev = items => string.Join("",items.Select(s=>s.ToUpperInvariant()));
        throw new NotImplementedException();
    }
    
    public override string ToString() => Segments.MakeString("[", ", ", "]");
    public string ToSnakeCase() => Segments.MakeString('_');
    
    public static Name Create(params string[] segments) => new ([..segments]);
    public static Name FromList(IReadOnlyList<string> segments) => new ([..segments]);

    /// <summary>
    /// Translate a string into a name by splitting it into words.
    /// The algorithm is designed to work with most well-known naming conventions or mix of them.
    /// The general rule is that consecutive letters and numbers are treated as words,
    /// upper-case letters and non-alphanumeric characters start a new word.
    /// </summary>
    /// <param name="input">the input string</param>
    /// <returns>A new <seealso cref="Name"/> instance.</returns>
    public static Name FromString(string input) => 
        FromList(input.ToMorphirWords().Select(w => w.ToLowerInvariant()).ToImmutableList());

}
