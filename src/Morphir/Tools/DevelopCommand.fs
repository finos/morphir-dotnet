namespace Morphir.Tools

open FSharp.SystemCommandLine
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

type DevelopCommand =

    static member Handler(logger: ILogger<DevelopCommand>) =
        fun () ->
            printfn "TODO: Implement develop command"
            logger.LogInformation("Running morphir develop")

    static member Create(host: IHost) =
        let logger = host.Services.GetService<ILogger<DevelopCommand>>()
        let handler = DevelopCommand.Handler logger

        command "develop" {
            description "Start a development server"
            setHandler handler
        }
