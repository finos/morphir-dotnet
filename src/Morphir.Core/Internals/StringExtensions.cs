using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Morphir.Internals;

internal static partial class StringExtensions
{
    [GeneratedRegex("[a-zA-Z][a-z]*|[0-9]+")]
    private static partial Regex WordPattern();

    extension(string self)
    {
        public string Capitalize() => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(self);

        public IEnumerable<String> ToMorphirWords() => WordPattern().Matches(self).Select(m => m.Value);
    }
}
