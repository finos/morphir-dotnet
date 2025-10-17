using Morphir.Internals;

namespace Morphir.IR;

public static class NameExtensions
{
    extension(IName name)
    {
        public string ToTitleCase() =>
            name.ToList()
                .Select(s => s.Capitalize())
                .MakeString("");
    }
}
