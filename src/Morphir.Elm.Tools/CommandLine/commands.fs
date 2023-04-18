namespace Morphir.Elm.Tools.CommandLine

open FSharp.SystemCommandLine

[<AutoOpen>]
module Commands =

    let makeCommand =
        let handler () = ()
        command "make" {
            description "Compile Elm source code"
            setHandler handler
        }

    let genCommand =
        let handler () = ()
        command "gen" {
            description "Generate Morphir code from Elm source code"
            setHandler handler
        }

    let elmCommand =
        let handler () = ()
        command "elm" {
            description "Elm tooling for Morphir"
            addCommand makeCommand
            addCommand genCommand
            setHandler handler
        }
