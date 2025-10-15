using System.Collections.Immutable;

namespace Morphir.Internals;

public static class ImmutableCollectionExtensions
{
    extension<T>(ImmutableList<T> self)
    {
        public Option<T> HeadOption => self.Count == 0 ? None : Some(self[0]);
        public void Deconstruct(out T? head, out ImmutableList<T> tail)
        {
            switch(self.Count)
            {
                case 0:
                    head = default;
                    tail = ImmutableList<T>.Empty;
                    break;
                case 1:
                    head = self[0];
                    tail = ImmutableList<T>.Empty;
                    break;
                default:
                    head = self[0];
                    tail = self.GetRange(1, self.Count - 1);
                    break;
            }
        }
    }
}
