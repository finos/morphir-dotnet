module  Morphir.Tools.CommandLine
open System.CommandLine.Help
open System.CommandLine.Invocation
open FSharp.SystemCommandLine
open Microsoft.Extensions.Logging
open Serilog

let rootCommandHandler (ctx:InvocationContext) =
    let hc = HelpContext(ctx.HelpBuilder, ctx.Parser.Configuration.RootCommand, System.Console.Out)
    ctx.HelpBuilder.Write(hc)

let morphir argv =
    let ctx = Input.Context()
    rootCommand argv {
        description "Morphir CLI"
        inputs ctx
        setHandler rootCommandHandler
    }

let run argv =
    //let host = CommandLineHost.build argv
    morphir argv

