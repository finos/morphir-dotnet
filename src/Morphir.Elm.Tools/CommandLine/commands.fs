namespace Morphir.Elm.Tools.CommandLine

open System.CommandLine.Help
open System.CommandLine.Invocation
open System.IO
open FSharp.SystemCommandLine

[<RequireQualifiedAccess>]
module ElmCommands =

    let makeCommand =
        let handler (projectDir, outputDir, typesOnly, fallbackCli) =
            printfn "TODO: Implement make command"
            printfn $"projectDir: %A{projectDir}"
            printfn $"outputDir: %A{outputDir}"
            printfn $"typesOnly: %A{typesOnly}"
            printfn $"fallbackCli: %A{fallbackCli}"

        let projectDir =
            Input.Option<DirectoryInfo>(
                aliases = [ "--project-dir"; "-p" ],
                defaultValue = DirectoryInfo(Directory.GetCurrentDirectory()),
                description = "Root directory of the project where morphir.json is located."
            )

        let outputDir =
            Input.Option<FileInfo>(
                aliases = [ "--output-dir"; "-o" ],
                defaultValue =
                    FileInfo(Path.Combine(Directory.GetCurrentDirectory(), "morphir-ir.json")),
                description = "Target file location where the Morphir IR will be saved."
            )

        let typesOnly =
            Input.Option<bool>(
                aliases = [ "--types-only"; "-t" ],
                defaultValue = false,
                description = "Only include type information in the IR, no values."
            )

        let fallbackCli =
            Input.Option<bool>(
                aliases = [ "--fallback-cli"; "-f" ],
                defaultValue = false,
                description = "Use the old make function."
            )

        command "make" {
            description "Translate Elm source code into Morphir IR"
            inputs (projectDir, outputDir, typesOnly, fallbackCli)
            setHandler handler
        }

    let genCommand =
        let handler (input, outputDir, target, targetVersion) =
            printfn "TODO: Implement gen command"
            printfn $"input: %A{input}"
            printfn $"outputDir: %A{outputDir}"
            printfn $"target: %A{target}"
            printfn $"targetVersion: %A{targetVersion}"

        let input =
            Input.Option<FileInfo>(
                aliases = [ "--input"; "-i" ],
                defaultValue =
                    FileInfo(Path.Combine(Directory.GetCurrentDirectory(), "morphir-ir.json")),
                description = "Source location where the Morphir IR will be loaded from."
            )

        let output =
            Input.Option<DirectoryInfo>(
                aliases = [ "--output"; "-o" ],
                defaultValue = DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "dist")),
                description = "Target location where the generated code will be saved."
            )

        let target =
            Input.Option<string>(
                aliases = [ "--target"; "-t" ],
                defaultValue = "Scala",
                description =
                    "Language to generate (Scala | SpringBoot | cypher | triples | TypeScript)"
            )

        let targetVersion =
            Input.Option<string>(
                aliases = [ "--target-version"; "-e" ],
                defaultValue = "2.11",
                description = "Version of the target language to generate"
            )

        command "gen" {
            description "Generate Morphir code from Morphir IR"
            inputs (input, output, target, targetVersion)
            setHandler handler
        }

    let developCommand =
        let handler (port, host, projectDir) =
            printfn "TODO: Implement develop command"
            printfn $"port: %d{port}"
            printfn $"host: %s{host}"
            printfn $"projectDir: %A{projectDir}"

        let port =
            Input.Option<int>(
                aliases = [ "--port"; "-p" ],
                defaultValue = 3000,
                description = "Port to use for the web server"
            )

        let host =
            Input.Option<string>(
                aliases = [ "--host"; "-h" ],
                defaultValue = "localhost",
                description = "Host to use for the web server"
            )

        let projectDir =
            Input.Option<DirectoryInfo>(
                aliases = [ "--project-dir"; "-i" ],
                defaultValue = DirectoryInfo(Directory.GetCurrentDirectory()),
                description = "Root directory of the project where morphir.json is located."
            )

        command "develop" {
            description "Start up a web server and expose developer tools through a web UI"
            inputs (port, host, projectDir)
            setHandler handler
        }

    let elmCommand =
        let handler (ctx: InvocationContext) =
            let cmd =
                ctx.Parser.Configuration.RootCommand.Subcommands
                |> Seq.find (fun c -> c.Name = "elm")

            let hc = HelpContext(ctx.HelpBuilder, cmd, System.Console.Out)
            ctx.HelpBuilder.Write(hc)


        let ctx = Input.Context()

        command "elm" {
            description "Elm tooling for Morphir"

            inputs ctx
            addCommands [ makeCommand; genCommand; developCommand ]
            setHandler handler
        }
