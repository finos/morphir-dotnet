namespace Morphir

open Microsoft.FSharp.Control
open Microsoft.Extensions.Hosting
open Serilog.Sinks.SystemConsole.Themes
open Serilog
open Serilog.Events
open Oakton

module Main =

    let createDefaultHostBuilder (argv: string array):IHostBuilder =
        Host.CreateDefaultBuilder(argv)
            .UseSerilog( fun (context:HostBuilderContext) (loggerConfiguration:LoggerConfiguration) ->
                loggerConfiguration
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    //.ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(theme = AnsiConsoleTheme.Code)
                |> ignore
            )
            .ApplyOaktonExtensions()

    let executeAsync (createBuilder: string array -> IHostBuilder) (argv: string array): Async<int> = async {
        Log.Logger <-
            LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger()

        try
            try
                printfn ("")
                printfn ("Welcome to Morphir!")
                Log.Information("Program args: {argv}", argv)
                let builder = createBuilder argv
                do builder.RunOaktonCommands(argv)
            with ex ->
                Log.Fatal(ex, "An error occurred")
        finally
            Log.CloseAndFlush()

        return System.Environment.ExitCode
    }

    [<EntryPoint>]
    let main (argv: string array) =
        executeAsync createDefaultHostBuilder argv |> Async.RunSynchronously

