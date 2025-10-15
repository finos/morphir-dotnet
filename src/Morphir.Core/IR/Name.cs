using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using Generator.Equals;
using Morphir.Internals;
using Morphir.IR.Codecs;
using static LanguageExt.Seq;
namespace Morphir.IR;

/// <summary>
/// Name is an abstraction of human-readable identifiers made up of words.
/// This abstraction allows us to use the same identifiers across various naming conventions used by the different
/// frontend and backend languages Morphir integrates with.
/// </summary>
/// <param name="Segments">the segments/word parts, that make up the <see cref="Name"/>.</param>
[JsonConverter(typeof(NameConverter))]
public partial record Name(Seq<string> Segments)
{
    public bool IsEmpty => Segments.Count == 0;
    
    public IImmutableList<string> ToList() => Segments.ToImmutableList();
    public Seq<string> ToSeq() => Segments;

    /// <summary>
    /// Turns a name into a camel-case string.
    /// </summary>
    /// <returns>A camel-case string constructed from this <see cref="Name"/>.</returns>
    public string ToCamelCase()
    {
        if(Segments.IsEmpty) return string.Empty;
        var (head, tail) = Segments;
        return head.Cons(tail.Map(s => s.Capitalize())).ToFullString(string.Empty);
    }
    
    /// <summary>
    /// Turns a name into a list of human-readable strings.
    /// The only difference compared to toList is how it handles abbreviations.
    /// They will be turned into a single upper-case word instead of separate single-character words.
    /// </summary>
    /// <returns>A sequence of "words".</returns>
    public Seq<string> ToHumanWords()
    {
        var result = new List<string>();
        var abbrev = new StringBuilder();
        var seq = Segments;
        while (!seq.IsEmpty)
        {
            var (head, tail) = seq;
            if (head.Length == 1)
            {
                abbrev.Append(head);
            }
            else
            {
                if (abbrev.Length > 0)
                {
                    result.Add(abbrev.ToString().ToUpperInvariant());
                    abbrev.Clear();
                }
                result.Add(head);
            }
            seq = tail;
        }
        if(abbrev.Length > 0) result.Add(abbrev.ToString().ToUpperInvariant());
        return new Seq<string>(result);
    }

    public Seq<string> ToHumanWordsTitle()
    {
        var words = ToHumanWords();
        return words.Head.Match(head => head.Capitalize().Cons(words.Tail), () => Empty);
    }


    public override string ToString() => Segments.ToFullArrayString(", ");

    /// <summary>
    /// Turns a name into a kebab-case string.
    /// </summary>
    /// <returns>A kebab-cased string consisting of the words in this <see cref="Name"/></returns>
    public string ToKebabCase() => ToHumanWords().ToFullString("-");
    
    /// <summary>
    /// Turns a name into a snake-case string.
    /// </summary>
    /// <returns>A snake-cased string consisting of the words in this <see cref="Name"/>.</returns>
    public string ToSnakeCase() => ToHumanWords().ToFullString("_");
    
    public string ToTitleCase() => 
        Segments.Map(s => s.Capitalize()).ToFullString("");
    
    public static Name Create(params string[] segments) => new ([..segments]);
    
    /// <summary>
    /// Convert a list of strings into a name.
    /// </summary>
    /// <param name="segments">The words in the name.</param>
    /// <returns>A new <see cref="Name"/> consisting of the provided words.</returns>
    public static Name FromList(params ImmutableList<string> segments) => new (toSeq(segments));
    public static Name FromList(string[] segments) => new (toSeq(segments));

    /// <summary>
    /// Translate a string into a name by splitting it into words.
    /// The algorithm is designed to work with the most well-known naming conventions or mixes of them.
    /// The general rule is that consecutive letters and numbers are treated as words,
    /// upper-case letters and non-alphanumeric characters start a new word.
    /// </summary>
    /// <param name="input">the input string</param>
    /// <returns>A new <seealso cref="Name"/> instance.</returns>
    public static Name FromString(string input) => 
        new(toSeq(input.ToMorphirWords().Select(w => w.ToLowerInvariant())));

    public static string ToCamelCase(Name name) => name.ToCamelCase();
    public static string ToKebabCase(Name name) => name.ToKebabCase();
    public static string ToSnakeCase(Name name) => name.ToSnakeCase();
    public static string ToTitleCase(Name name) => name.ToTitleCase();
}
