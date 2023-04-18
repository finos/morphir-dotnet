[<RequireQualifiedAccess>]
module Morphir.Tools.CommandLineHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Serilog.Sinks.SystemConsole.Themes
open Serilog
open Serilog.Events

let build (argv: string[]) =
    Host
        .CreateDefaultBuilder(argv)
        .ConfigureHostConfiguration(fun configBuilder -> () )
        .UseSerilog(fun hostingContext loggerConfiguration ->
            loggerConfiguration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                //.ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(theme = AnsiConsoleTheme.Code)
            |> ignore
        )
        .Build()

