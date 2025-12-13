module Morphir.Tool.DotnetMorphir

open Morphir.Tools
open Morphir.Tools.CommandLine

[<EntryPoint>]
let main (args: string[]) = CommandLine.run args
