namespace Morphir.SpecFlow.Drivers

open Argu
open Morphir

type MorphirCliDriver() =

    member this.DisplayHelp() = ()

    member this.ExecuteMain(argv: string array) = Main.main argv
