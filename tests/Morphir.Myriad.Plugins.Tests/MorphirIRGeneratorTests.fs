module Morphir.Myriad.Plugins.MorphirIRGeneratorTests

open System
open Morphir.Myriad.Plugins
open Myriad.Core
open Xunit

[<Fact>]
let ``Should support F# input extensions`` () =
    let sut = MorphirIRGenerator(): IMyriadGenerator
    let actual = sut.ValidInputExtensions
    let expected = [ ".fs" ]
    Assert.Equal(expected, actual)
