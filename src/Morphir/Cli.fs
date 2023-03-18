module Morphir.Cli

open Argu
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Serilog.Sinks.SystemConsole.Themes
open Serilog
open Serilog.Events
open Microsoft.Extensions.Logging

type Commands =
    | [<CustomCommandLine("info")>] Info of path: string option
    | [<CustomCommandLine("run")>] Run of path: string option
    | [<CustomCommandLine("test")>] Test of path: string option
    | [<CustomCommandLine("make")>] Make of path: string option

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Info _ ->
                "info [path] - Display information about the specified path or current directory."
            | Run _ ->
                "run [path] - Execute a script at the specified path or in the current directory."
            | Test _ ->
                "test [path] - Run tests for the project at the specified path or in the current directory."
            | Make _ ->
                "make [path] - Build the project at the specified path or in the current directory."


type StartupArgs =
    {
        LoggerConfiguration: LoggerConfiguration
        Argv: string array
    }

    member this.Reconfigure(argv: string array, configurator: StartupConfigurator) =
        configurator this argv

and StartupConfigurator = StartupArgs -> string array -> StartupArgs

let defaultLoggerConfiguration () : LoggerConfiguration =
    LoggerConfiguration()
        .MinimumLevel.Information()
        //.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich
        .FromLogContext()
        .WriteTo.Console(theme = AnsiConsoleTheme.Code)

let defaultConfig =
    lazy
        {
            LoggerConfiguration = defaultLoggerConfiguration ()
            Argv = Array.empty
        }

// Define ClientService and DaemonService classes
type ClientService(logger:ILogger<ClientService>) =
    interface IHostedService with
        member _.StartAsync(cancellationToken) =
            logger.LogCritical("Starting ClientService...")
            Task.CompletedTask

        member _.StopAsync(cancellationToken) =
            logger.LogInformation("Stopping ClientService...")
            Task.CompletedTask

type DaemonService(logger:ILogger<DaemonService>) =
    interface IHostedService with
        member _.StartAsync(cancellationToken) =
            printfn "Starting DaemonService..."
            Task.CompletedTask

        member _.StopAsync(cancellationToken) =
            printfn "Stopping DaemonService..."
            Task.CompletedTask

type CliApp(startupArgs: StartupArgs) =
    member this.Run() =

        Log.Information("Starting Morphir CLI")
        let hostBuilder = Host.CreateDefaultBuilder(startupArgs.Argv)
        this.Run(hostBuilder)

    member this.Run(hostBuilder: IHostBuilder) =
        let parser = ArgumentParser.Create<Commands>(programName = "morphir")
        let results = parser.Parse(startupArgs.Argv)
        Log.Information("ParseResults are {results}", results)

        if results.IsUsageRequested then
            parser.PrintUsage()
            |> printfn "%s"
        else
            let host = hostBuilder
                           .ConfigureServices(
                               fun services ->
                                   services
                                           .AddLogging()
                                           .AddHostedService<ClientService>()
                                           .AddHostedService<DaemonService>()
                                   |> ignore
                           )
                           .UseSerilog(fun ctx lc ->
                                 lc
                                      .MinimumLevel.Information()
                                      .Enrich.FromLogContext()
                                      .WriteTo.Console(theme = AnsiConsoleTheme.Code)|>ignore
                           )
                           .Build()
            host.Run()

let build (configurator: StartupConfigurator) (argv: string array) =
    let startupArgs = configurator defaultConfig.Value argv
    Log.Logger <- startupArgs.LoggerConfiguration.CreateLogger()

    try
        let app = CliApp(startupArgs)
        app
    finally
        Log.CloseAndFlush()

let buildWithDefaultConfigurator (argv: string array) = build (fun config argv -> config) argv
