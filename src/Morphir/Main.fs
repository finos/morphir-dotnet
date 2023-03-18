namespace Morphir

module Say =
    open System

    let nothing name =
        name
        |> ignore

    let hello name = sprintf "Hello %s" name

    let colorizeIn (color: string) str =
        let oldColor = Console.ForegroundColor
        Console.ForegroundColor <- (Enum.Parse(typedefof<ConsoleColor>, color) :?> ConsoleColor)
        printfn "%s" str
        Console.ForegroundColor <- oldColor

module Main =
    open Argu

    type CLIArguments =
        | Info
        | Version
        | Favorite_Color of string // Look in App.config
        | [<MainCommand>] Hello of string

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | Info -> "More detailed information"
                | Version -> "Version of application"
                | Favorite_Color _ -> "Favorite color"
                | Hello _ -> "Who to say hello to"

    [<EntryPoint>]
    let main (argv: string array) =
        printfn "Starting..."
        let app = Cli.buildWithDefaultConfigurator argv
        app.Run()
        printfn "Done! Finito!"
        0
