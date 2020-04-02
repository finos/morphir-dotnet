using System;
using AutoFixture.Xunit2;
using Xunit;
using FluentAssertions;
using Microsoft.FSharp.Core;
using Morphir.SDK.Extensions;

namespace Morphir.SDK
{
    public class MaybeTests
    {
        public class Calling_withDefault
        {
            [Fact]
            public void Given_a_Just_it_should_not_return_the_default_value()
            {
                var actual = Maybe.WithDefault(5, Maybe.Just(0));
                actual.Should().Be(0);
            }
            
            [Fact]
            public void Given_a_Nothing_it_should_return_the_default_value()
            {
                var actual = Maybe.WithDefault(5, Maybe.Nothing<int>());
                actual.Should().Be(5);
            }
        }

        public class Conversions
        {
            public class When_Converting_a_Maybe_to_an_Option
            {
                
                [Theory, AutoData]
                public void Then_a_Just_should_convert_to_a_Some(int input)
                {
                    var original = Maybe.Just(input);
                    var actual = Maybe.Conversions.Options.MaybeToOptions(original);
                    actual.Should().Be(FSharpOption<int>.Some(input));
                }
                
                [Theory, AutoData]
                public void Then_a_Just_should_convert_to_a_Some_using_the_extension_method(int input)
                {
                    var original = Maybe.Just(input);
                    var actual = original.ToOption();
                    actual.Should().Be(FSharpOption<int>.Some(input));
                }
                
                [Fact]
                public void Then_a_Nothing_should_convert_to_a_None()
                {
                    var original = Maybe.Nothing<string>();
                    var actual = Maybe.Conversions.Options.MaybeToOptions(original);
                    actual.Should().Be(FSharpOption<string>.None);
                }
            }
        }
        
    }
    
}
