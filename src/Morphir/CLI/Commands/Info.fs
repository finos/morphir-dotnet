namespace Morphir.CLI.Commands
open Oakton
open Microsoft.FSharp.Control
open Microsoft.Extensions.Logging
open Wolverine
open Serilog

type InfoInput() =
    inherit NetCoreInput()

[<Description("Get information about the executing version of the morphir CLI tooling.")>]
type InfoCommand() =
    inherit OaktonAsyncCommand<InfoInput>()

    override this.Execute(input) = task {
        input.HostBuilder <- input.HostBuilder.UseWolverine()
        let host = input.BuildHost()
        do! host.StartAsync()
        Log.Information("Executing info command with input: {input}", input)
        do! host.StopAsync()
        return true
    }

