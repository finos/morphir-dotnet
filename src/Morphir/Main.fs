namespace Morphir

open Microsoft.FSharp.Control
open Microsoft.Extensions.Hosting
open Serilog.Sinks.SystemConsole.Themes
open Serilog
open Serilog.Events
open Oakton
open Wolverine

module Main =

    let createDefaultHostBuilder (argv: string array) : IHostBuilder =
        Host
            .CreateDefaultBuilder(argv)
            .UseSerilog(fun (context: HostBuilderContext) (loggerConfiguration: LoggerConfiguration) ->
                loggerConfiguration.MinimumLevel
                    .Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    //.ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(theme = AnsiConsoleTheme.Code)
                |> ignore
            )
            .ApplyOaktonExtensions()
            .UseWolverine()

    let execute (createBuilder: string array -> IHostBuilder) (argv: string array) =
        Log.Logger <- LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger()

        let exitCode =
            try
                try
                    printfn ("")
                    printfn ("Welcome to Morphir!")
                    Log.Information("Program args: {argv}", argv)
                    let builder = createBuilder argv
                    builder.RunOaktonCommandsSynchronously(argv)
                with ex ->
                    Log.Fatal(ex, "An error occurred")
                    1
            finally
                Log.CloseAndFlush()

        exitCode

    [<EntryPoint>]
    let main (argv: string array) = execute createDefaultHostBuilder argv
