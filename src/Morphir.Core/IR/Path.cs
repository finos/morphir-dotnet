using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Text.RegularExpressions;
using Generator.Equals;
using Morphir.Internals;

namespace Morphir.IR;

[Equatable]
public partial record Path([property:OrderedEquality]ImmutableList<Name> Names)
{
    [GeneratedRegex("[^\\w\\s]+")]
    private static partial Regex SeparatorRegex();
    
    public static Path FromList(params ImmutableList<Name> names) => new (names);

    /// <summary>
    /// Translates a string into a path by splitting it into names along special characters.
    /// The algorithm will treat any non-word characters that are not spaces as a path separator.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [Pure]
    public static Path FromString(string input) => new(NamesFromString(input).ToImmutableList());
    
    /// <summary>
    /// Checks if this <see cref="Path"/> is a prefix of the provided path.
    /// </summary>
    /// <param name="path">the path</param>
    /// <returns>True if this path is a prefix of the provided path.</returns>
    public bool IsPrefixOf(Path path) => IsPrefixOf(path, this);

    public void Deconstruct(out Name? head, out ImmutableList<Name> tail)
    {
        Names.Deconstruct(out head, out tail);
    }

    public ImmutableList<Name> ToList() => Names;
    public Seq<Name> ToSeq() => toSeq(Names);

    public string ToCanonicalString() => ToString(Name.ToKebabCase, "/");
    public string ToString(Func<Name, string> render, string separator) => 
        Names.Select(render).MakeString(separator);
    
    /// <summary>
    /// Provides a curried factory for constructing <see cref="Path"/> instances.
    /// </summary>
    public static Func<Func<Name,String>, Func<String, Func<Path, String>>> toString = 
        render => separator => path => ToString(render, separator, path);
    
    public override string ToString() => ToCanonicalString();

    public static Path Empty => new Path(ImmutableList<Name>.Empty);
    
    public static IImmutableList<Name> ToList(Path path) => path.ToList();

    [Pure]
    protected static IEnumerable<Name> NamesFromString(string input) => 
        SeparatorRegex()
            .Split(input)
            .Select(Name.FromString);

    public static string ToString(Func<Name, string> render, string separator, Path path) =>
        path.ToString(render, separator);

    /// <summary>
    /// Checks if a <see cref="Path"/> is a prefix of another.
    /// </summary>
    /// <param name="path">the path to check.</param>
    /// <param name="prefix">the prefix path.</param>
    /// <returns>True if the prefix path is a prefix of the provided path.</returns>
    public static bool IsPrefixOf(Path path, Path prefix)
    {
        return Loop(path.Names, prefix.Names).Run();
        
        Trampoline<bool> Loop(ImmutableList<Name> pathNames, ImmutableList<Name> prefixNames)
        {
            return (prefixNames, pathNames) switch
            {
                // An Empty path is a prefix of any other path
                ([],_) => Trampoline.Pure(true),
                (_,[]) => Trampoline.Pure(false),
                var ((prefixHead, prefixTail), (pathHead, pathTail)) => 
                    prefixHead == pathHead 
                        ? Trampoline.More(() => Loop(pathTail, prefixTail))
                        : Trampoline.Pure(false)
            };
        }    
    }
    
}
public static class PathExtensions 
{
    extension(Path self) 
    {
        public Name? Head => self.Names.IsEmpty ? null : self.Names[0];
    }
}
