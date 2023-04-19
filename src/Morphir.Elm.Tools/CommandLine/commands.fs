namespace Morphir.Elm.Tools.CommandLine

open System.CommandLine.Help
open System.CommandLine.Invocation
open FSharp.SystemCommandLine

[<RequireQualifiedAccess>]
module ElmCommands =

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
        let handler (ctx: InvocationContext) =
            let cmd =
                ctx.Parser.Configuration.RootCommand.Subcommands
                |> Seq.find (fun c -> c.Name = "elm")

            let hc = HelpContext(ctx.HelpBuilder, cmd, System.Console.Out)
            ctx.HelpBuilder.Write(hc)


        let ctx = Input.Context()

        command "elm" {
            description "Elm tooling for Morphir"

            inputs ctx
            addCommands [ makeCommand; genCommand ]
            setHandler handler
        }
