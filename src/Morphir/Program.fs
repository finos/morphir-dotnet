open Argu
open System
open Morphir.Commands
open Morphir.Commands.Workspace
open Serilog
open Serilog.Events

let toolsVersion = "0.1.0"

type MorphirCLIExiter() =
    interface IExiter with
        member _.Name = "Morphir CLI exiter"
        member _.Exit(msg,code) =
            Console.Error.WriteLine(msg)
            exit <| int code

type Arguments =
    | Version
    | [<Inherit>] Json
    | [<Inherit; AltCommandLine("-v"); Unique>] Verbosity of LogEventLevel
    | [<CliPrefix(CliPrefix.None)>] Workspace of ParseResults<Workspace.Arguments>
with
    interface IArgParserTemplate with
        member x.Usage =
            match x with
            | Version -> "Display the program's version info."
            | Json -> "Encode output in JSON and print in a single line in stdout. "
            | Verbosity _ -> "Set the verbosity of the tool's logs."
            | Workspace _ -> "Execute actions on a workspace"

[<EntryPoint>]
let main _ =
    let parser = ArgumentParser.Create("morphir", "Help was requested", errorHandler = MorphirCLIExiter())
    let results = parser.Parse()
    let json = results.Contains Json
    let verbosity =
        results.TryGetResult(Verbosity)
        |> Option.defaultValue (if json then LogEventLevel.Error else LogEventLevel.Information)
    Log.Logger <- LoggerConfiguration()
        .MinimumLevel.Is(verbosity)
        .WriteTo.Console()
        .CreateLogger()

    try
        try
            if results.Contains Version then
                Log.Information("Version: {toolsVersion}", toolsVersion)
                0
            else
                match results.GetSubCommand() with
                | Workspace _ -> Ok ()
                | Version _  | Json | Verbosity _ -> Ok ()
                |> function | Ok () -> 0 | Error () -> 1
        with
        | ex ->
            Log.Fatal(ex, "Exception occurred")
            1
    finally
        Log.CloseAndFlush()

