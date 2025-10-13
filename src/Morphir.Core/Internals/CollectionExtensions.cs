using System.Text;

namespace Morphir.Internals;

internal static class CollectionExtensions
{
    extension<T>(IEnumerable<T> self)
    {
        public string MakeString(char separator) => String.Join(separator, self);
        public string MakeString(string separator) => String.Join(separator, self);

        public string MakeString(string start, string separator, string end)
        {
            var sb = new StringBuilder();
            sb.Append(start);
            sb.Append(self.MakeString(separator));
            sb.Append(end);
            return sb.ToString();
        }
    }
}
