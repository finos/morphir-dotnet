module Morphir.Myriad.Plugins.ElmToFSharpGeneratorTests

open System
open Myriad.Core
open Xunit

[<Fact>]
let ``Should support Elm input extensions`` () =
    let sut = ElmToFSharpGenerator(): IMyriadGenerator
    let actual = sut.ValidInputExtensions
    let expected = [ ".elm" ]
    Assert.Equal(expected, actual)
