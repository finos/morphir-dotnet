namespace Morphir.Myriad.Plugins

open Myriad.Core

type ElmToFSharpGenerator() =
    interface IMyriadGenerator with
        member this.Generate(ctx) = Output.Source "Test"
        member this.ValidInputExtensions = seq { ".elm" }
