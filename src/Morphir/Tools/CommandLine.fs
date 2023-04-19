module Morphir.Tools.CommandLine

open System.CommandLine.Help
open System.CommandLine.Invocation
open FSharp.SystemCommandLine
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Morphir.Elm.Tools.CommandLine
open Serilog

let workspaceCommand =
    command "workspace" {
        description "Work with a morphir workspace"
        setHandler (fun _ -> printfn "workspace")
    }

let morphir argv =
    printfn ""

    let handler (ctx: InvocationContext) =
        let hc =
            HelpContext(ctx.HelpBuilder, ctx.Parser.Configuration.RootCommand, System.Console.Out)

        ctx.HelpBuilder.Write(hc)

    let host = CommandLineHost.build argv
    let ctx = Input.Context()

    rootCommand argv {
        description "Morphir CLI"
        inputs ctx
        addCommand (DevelopCommand.Create(host))
        addCommand ElmCommands.elmCommand
        addCommand workspaceCommand
        addCommand (DockerizeCommand.Create(host))
        setHandler handler
    }


let run argv =
    //let host = CommandLineHost.build argv
    morphir argv
