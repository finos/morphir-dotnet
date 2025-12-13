namespace Morphir.Tools

open System.IO
open FSharp.SystemCommandLine
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

[<AbstractClass>]
type DockerizeCommand =

    static member Handler(logger: ILogger<DockerizeCommand>) =
        fun (projectDir: DirectoryInfo, force: bool) ->
            printfn "TODO: Wire up the dockerize command"
            logger.LogInformation("Dockerizing project at {projectDir}", projectDir)
            logger.LogInformation("Force: {force}", force)

    static member Create(host: IHost) =
        let logger = host.Services.GetService<ILogger<DockerizeCommand>>()

        let projectDir =
            Input.Option<DirectoryInfo>(
                aliases = [ "--project-dir"; "-p" ],
                description = "Root directory of the project where morphir.json is located"
            )

        let force =
            Input.Option<bool>(
                aliases = [ "--force"; "-f" ],
                description = "Overwrite any Dockerfile in target location",
                defaultValue = false
            )

        let handler = DockerizeCommand.Handler(logger)

        command "dockerize" {
            description "Creates a Docker image of a Morphir IR with Morphir Develop"
            inputs (projectDir, force)
            setHandler handler
        }
