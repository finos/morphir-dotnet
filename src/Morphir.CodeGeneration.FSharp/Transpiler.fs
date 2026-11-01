namespace Morphir.CodeGeneration.FSharp

open System.Text.Json

/// Converts a classic Morphir model to an ordered list of F# module files.
type Transpiler =
    static member Generate
        (
            inputPath: string,
            json: string
        ) : Result<GeneratedModule list, Diagnostic list> =
        try
            use document = JsonDocument.Parse(json)

            document.RootElement
            |> Decode.model
            |> Emit.generate
            |> Ok
        with
        | GenerationFailure(category, location, message) ->
            Error [
                {
                    Category = category
                    InputPath = inputPath
                    Location = location
                    Message = message
                }
            ]
        | :? JsonException as ex ->
            Error [
                {
                    Category = "parse"
                    InputPath = inputPath
                    Location = (if isNull ex.Path then "$" else ex.Path)
                    Message = ex.Message
                }
            ]
        | :? System.InvalidOperationException as ex ->
            Error [
                {
                    Category = "decode"
                    InputPath = inputPath
                    Location = "$"
                    Message = ex.Message
                }
            ]
        | :? System.FormatException as ex ->
            Error [
                {
                    Category = "decode"
                    InputPath = inputPath
                    Location = "$"
                    Message = ex.Message
                }
            ]
