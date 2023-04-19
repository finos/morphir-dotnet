namespace Morphir.Tools
open FSharp.SystemCommandLine
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

type WorkspaceCommand =
    static member Handler(logger:ILogger<WorkspaceCommand>) =
        fun () ->
            printfn "TODO: Implement workspace command"

    static member Create(host:IHost) =
        let logger = host.Services.GetService<ILogger<WorkspaceCommand>>()
        let handler = WorkspaceCommand.Handler(logger)

        command "workspace" {
            description "Work with and manage a Morphir workspace"
            setHandler handler
        }

