namespace Morphir.CodeGeneration.FSharp

open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Json.Classic

/// Reads classic models for generation through the shared classic JSON decoder.
module internal Decode =
    type Model = {
        Version: int
        Package: PackageName
        Definition: Package.PackageDefinition<JsonElement, JsonElement>
    }

    let fail category location message =
        raise (GenerationFailure(category, location, message))

    /// Generation reports the shared decoder's failures as its own. Before the decoder was shared, a
    /// literal out of range surfaced as a general decoding failure, and generation keeps that rule.
    let private translating decode =
        try
            decode ()
        with
        | ClassicJsonFailure error when error.Rule = "literal_out_of_range" ->
            fail "decode" "$" error.Message
        | ClassicJsonFailure error -> fail error.Rule error.Location error.Message
        | :? System.InsufficientExecutionStackException ->
            fail "decode" "$" "The document is nested too deeply to read."

    let tag version family expected =
        ClassicJsonDecoder.tag version family expected

    let unwrap documented = ClassicJsonDecoder.unwrap documented

    let typ version location element : Type<JsonElement> =
        translating (fun () ->
            ClassicJsonDecoder.typ
                {
                    Version = version
                    ForGeneration = true
                }
                location
                element
        )

    let model (root: JsonElement) =
        translating (fun () ->
            let version, package, definition = ClassicJsonDecoder.libraryForGeneration root

            {
                Version = version
                Package = package
                Definition = definition
            }
        )
