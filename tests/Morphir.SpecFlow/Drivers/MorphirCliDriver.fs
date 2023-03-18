namespace Morphir.SpecFlow.Drivers

open Argu
open Morphir

type MorphirCliDriver() =
    let mutable _cliCommand: ParseResults<Main.CLIArguments> option = None

    member this.DisplayHelp() = ()

    member this.ExecuteMain(argv: string array) =
        Main.main argv

