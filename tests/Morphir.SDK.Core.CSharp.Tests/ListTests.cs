using AutoFixture.Xunit2;
using FluentAssertions;
using Xunit;

namespace Morphir.SDK
{
    public class ListTests
    {
        [Fact]
        public void When_called_with_an_empty_list_IsEmpty_should_return_true()
        {
            var list = List.Empty<string>();
            List.IsEmpty(list).Should().BeTrue();
        }
        
        [Theory,AutoData]
        public void When_called_with_a_non_empty_list_IsEmpty_should_return_false(string input)
        {
            var list = List.Singleton(input);
            List.IsEmpty(list).Should().BeFalse();
        }
    }
}
