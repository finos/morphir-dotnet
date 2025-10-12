using System.Globalization;

namespace Morphir.Internals;

internal static class StringExtensions
{
    extension(string self)
    {
        public string Capitalize() => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(self);
    }
}
