namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.IO
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>
/// The files that a store reads: their profiles, the limits on their size, and the decoding of their JSON. Nothing
/// here depends on the IR version of a tree.
/// </summary>
module internal TreeFileSource =
    open TreeLayout

    let private error code file message = StoreErrors.error code file message

    type Profile =
        | Json
        | Yaml

    let profileOf (location: string) =
        if location.EndsWith(".json", StringComparison.Ordinal) then
            Some Json
        elif
            location.EndsWith(".yaml", StringComparison.Ordinal)
            || location.EndsWith(".yml", StringComparison.Ordinal)
        then
            Some Yaml
        else
            None

    let isManifestName (location: string) =
        location = ManifestFile
        || location = "manifest.yaml"
        || location = "manifest.yml"

    let isNodeFileName (name: string) =
        name = "module.json"
        || name.EndsWith(".type.json", StringComparison.Ordinal)
        || name.EndsWith(".value.json", StringComparison.Ordinal)

    /// The bytes of a file, refusing one over the limit without reading past it.
    let contentBytes
        (limit: int64)
        (location: string)
        (file: Morphir.Files.VFile)
        : Result<byte array, StoreError> =
        let tooLarge () =
            error "tree.file_too_large" (Some location) $"The file has more than {limit} bytes."

        match file.Content with
        | None -> Ok [||]
        | Some content ->
            match Morphir.Files.VFileContent.openRead content with
            | Error problem ->
                Error(
                    error
                        "tree.unreadable_file"
                        (Some location)
                        (Morphir.Files.ContentError.describe problem)
                )
            | Ok opened ->
                try
                    use stream = opened
                    use buffer = new MemoryStream()
                    let chunk = Array.zeroCreate<byte> 81920
                    let mutable total = 0L
                    let mutable over = false
                    let mutable count = stream.Read(chunk, 0, chunk.Length)

                    while count > 0
                          && not over do
                        total <-
                            total
                            + int64 count

                        if total > limit then
                            over <- true
                        else
                            buffer.Write(chunk, 0, count)
                            count <- stream.Read(chunk, 0, chunk.Length)

                    if over then Error(tooLarge ()) else Ok(buffer.ToArray())
                with ex ->
                    Error(error "tree.unreadable_file" (Some location) ex.Message)

    /// Decodes a file's JSON, skipping a UTF-8 byte order mark.
    let decode
        (location: string)
        (read: JsonElement -> Result<'a, V4JsonError>)
        (bytes: byte array)
        : Result<'a, StoreError> =
        let start =
            if
                bytes.Length
                >= 3
                && bytes[0] = 0xEFuy
                && bytes[1] = 0xBBuy
                && bytes[2] = 0xBFuy
            then
                3
            else
                0

        try
            use document =
                JsonDocument.Parse(
                    ReadOnlyMemory(
                        bytes,
                        start,
                        bytes.Length
                        - start
                    ),
                    JsonDocumentOptions(MaxDepth = V4JsonDecoding.MaxDepth)
                )

            read document.RootElement
            |> Result.mapError (fun e -> error e.Rule (Some location) $"{e.Location}: {e.Message}")
        with :? JsonException as problem ->
            Error(error "invalid_json" (Some location) problem.Message)

    let readFile limit location file read =
        contentBytes limit location file
        |> Result.bind (decode location read)

    /// <summary>
    /// Whether a manifest states a format version of major 3, which the v3 rules read: the string <c>"3"</c> or one
    /// that starts with <c>"3."</c>, or a number from 3 up to 4, such as <c>3</c> or <c>3.1</c>. The v3 reader refuses
    /// every one of them but <c>"3.1.0"</c>, and none is sent to the v4 reader.
    /// </summary>
    let isClassicManifest (root: JsonElement) =
        let mutable version = Unchecked.defaultof<JsonElement>
        let mutable number = 0.0

        root.ValueKind = JsonValueKind.Object
        && root.TryGetProperty("formatVersion", &version)
        && (
            match version.ValueKind with
            | JsonValueKind.String ->
                let text = nonNull (version.GetString())

                text = "3"
                || text.StartsWith("3.", StringComparison.Ordinal)
            | JsonValueKind.Number ->
                version.TryGetDouble(&number)
                && number
                   >= 3.0
                && number < 4.0
            | _ -> false
        )

    let errorsOf (results: Result<'a, StoreError list> list) =
        results
        |> List.collect (
            function
            | Error errors -> errors
            | Ok _ -> []
        )

    let valuesOf (results: Result<'a, StoreError list> list) =
        results
        |> List.choose (
            function
            | Ok value -> Some value
            | Error _ -> None
        )

    let one (result: Result<'a, StoreError>) =
        result
        |> Result.mapError List.singleton

    /// <summary>
    /// The tree files of a tree, by location: its one JSON manifest and every other JSON file. A tree with too many
    /// files, no manifest, two manifests or a YAML file is refused.
    /// </summary>
    let discover
        (options: ReadOptions)
        (files: Morphir.Files.VFileTree)
        : Result<(string * Morphir.Files.VFile) * (string * Morphir.Files.VFile) list, StoreError list> =
        let treeFiles =
            Morphir.Files.VFileTree.toFileMap files
            |> Map.toList
            |> List.choose (fun (location, file) ->
                profileOf location
                |> Option.map (fun profile -> location, file, profile)
            )

        if treeFiles.Length > options.MaxFiles then
            Error [
                error
                    "tree.too_many_files"
                    None
                    $"The tree has {treeFiles.Length} files, and a read accepts at most {options.MaxFiles}."
            ]
        else
            let manifests =
                treeFiles
                |> List.filter (fun (location, _, _) -> isManifestName location)

            let yamlFiles =
                treeFiles
                |> List.filter (fun (_, _, profile) -> profile = Yaml)

            match manifests with
            | [] ->
                Error [
                    error "tree.missing_manifest" None "The tree has no manifest.json at its root."
                ]
            | _ :: _ :: _ ->
                let named =
                    manifests
                    |> List.map (fun (location, _, _) -> location)
                    |> String.concat " and "

                Error [
                    error
                        "tree.ambiguous_profile"
                        None
                        $"The tree has more than one manifest, {named}; a reader does not choose one."
                ]
            | [ (_, _, Yaml) ]
            | [ (_, _, Json) ] when not yamlFiles.IsEmpty ->
                Error(
                    yamlFiles
                    |> List.map (fun (location, _, _) ->
                        error
                            "tree.unsupported_profile"
                            (Some location)
                            "This store reads the JSON profile of a tree, and the file is in the YAML profile."
                    )
                )
            | [ (manifestLocation, manifestFile, _) ] ->
                let others =
                    treeFiles
                    |> List.filter (fun (location, _, _) ->
                        location
                        <> manifestLocation
                    )
                    |> List.map (fun (location, file, _) -> location, file)

                Ok((manifestLocation, manifestFile), others)
