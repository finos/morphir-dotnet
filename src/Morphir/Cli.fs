module Morphir.Cli

open System
open Argu
open System.Threading.Tasks
open Argu.ArguAttributes
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Morphir.CLI.Services
open Serilog.Sinks.SystemConsole.Themes
open Serilog
open Serilog.Events
open Microsoft.Extensions.Logging

type RunArgs =
    | [<MainCommand>] Path of path: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Path _ -> "path - The path to the script to run."

and MorphirArgs =
    | [<CliPrefix(CliPrefix.None)>] Info
    | [<CliPrefix(CliPrefix.None)>] Run of ParseResults<RunArgs>
    | [<CustomCommandLine("test")>] Test of path: string option
    | [<CustomCommandLine("make")>] Make of path: string option

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Info -> "info - Display information about the specified path or current directory."
            | Run _ -> "Run a morphir module"
            | Test _ ->
                "test [path] - Run tests for the project at the specified path or in the current directory."
            | Make _ ->
                "make [path] - Build the project at the specified path or in the current directory."


type StartupArgs =
    {
        LoggerConfigurator: HostBuilderContext -> LoggerConfiguration -> unit
        Argv: string array
    }

    member this.Reconfigure(argv: string array, configurator: StartupConfigurator) =
        configurator this argv

and StartupConfigurator = StartupArgs -> string array -> StartupArgs

let defaultLoggerConfiguration
    (hostingContext: HostBuilderContext)
    (loggerConfiguration: LoggerConfiguration)
    : unit =
    loggerConfiguration
        .MinimumLevel
        .Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(theme = AnsiConsoleTheme.Code)
    //.ReadFrom.Configuration(hostingContext.Configuration)
    |> ignore


let defaultStartupArgs =
    lazy
        {
            LoggerConfigurator = defaultLoggerConfiguration
            Argv = Array.empty
        }

// Define ClientService and DaemonService classes
type ClientService(logger: ILogger<ClientService>) =
    interface IHostedService with
        member _.StartAsync(cancellationToken) =
            logger.LogInformation("Starting ClientService...")
            Task.CompletedTask

        member _.StopAsync(cancellationToken) =
            logger.LogInformation("Stopping ClientService...")
            Task.CompletedTask

type DaemonService
    (
        hostApplicationLifetime: IHostApplicationLifetime,
        logger: ILogger<DaemonService>
    ) =
    inherit BackgroundService()

    override this.ExecuteAsync(stoppingToken) = task {
        // while (not stoppingToken.IsCancellationRequested) do
        //     logger.LogInformation("DaemonService is running...")
        //     do! Async.Sleep(5000)
        logger.LogInformation("DaemonService is running...")
        do! Async.Sleep(5000)
        hostApplicationLifetime.StopApplication()
    }


type CliApp(startupArgs: StartupArgs) =
    member this.Run() =

        Log.Information("Starting Morphir CLI")
        let hostBuilder = Host.CreateDefaultBuilder(startupArgs.Argv)
        this.Run(hostBuilder)

    member this.Run(hostBuilder: IHostBuilder) =
        hostBuilder.UseSerilog(defaultLoggerConfiguration)
        |> ignore

        let parser = ArgumentParser.Create<MorphirArgs>(programName = "morphir")

        try
            let results = parser.ParseCommandLine startupArgs.Argv

            Log.Information("ParseResults are {parse_results}", results.GetAllResults())

            let (resolvedHostBuilder, shouldRunHost) =
                if results.Contains Info then
                    Log.Information("Printing info")
                    printfn "Morphir CLI version 0.1.0"

                    hostBuilder.ConfigureServices(fun services ->
                        services.AddHostedService<InfoService>()
                        |> ignore
                    ),
                    true
                elif results.Contains Run then
                    Log.Information("Running script")

                    let cmd = results.GetResult Run
                    let path = cmd.GetResult Path
                    Log.Information("Running script at path {path}", path)

                    hostBuilder, false
                elif results.Contains Test then
                    Log.Information("Running tests")

                    results.GetResult Test
                    |> Option.iter (fun path ->
                        Log.Information("Running tests at path {path}", path)
                    )

                    hostBuilder, false
                elif results.Contains Make then
                    Log.Information("Making project")

                    results.GetResult Make
                    |> Option.iter (fun path ->
                        Log.Information("Making project at path {path}", path)
                    )

                    hostBuilder, false
                else
                    Log.Information("Printing usage")

                    parser.PrintUsage()
                    |> printfn "%s"

                    hostBuilder, false

            if shouldRunHost then
                resolvedHostBuilder.Build().Run()
        with
        | :? ArguException as ex ->
            //Log.Error(ex, "Error parsing command line arguments")
            printfn "%s" ex.Message
        | ex ->
            Log.Fatal(ex, "Error running Morphir CLI")
            raise ex

let build (configurator: StartupConfigurator) (argv: string array) =
    let startupArgs = configurator defaultStartupArgs.Value argv
    let app = CliApp(startupArgs)
    app

let buildWithDefaultConfigurator (argv: string array) =
    build (fun startupArgs argv -> { startupArgs with Argv = argv }) argv
