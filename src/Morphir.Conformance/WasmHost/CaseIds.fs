// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.Globalization
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Morphir.Conformance

/// One entry of the id map: a number and the upstream case id it belongs to. A retired entry
/// belongs to a case that the corpus no longer has; its number is never given to another case.
type CaseIdEntry = {
    Number: int
    CaseId: string
    Retired: bool
}

/// The committed id map of the suite: the pin it was last generated from and its entries,
/// by number. Numbers and case ids are unique.
type CaseIdMap = { Pin: Pin; Entries: CaseIdEntry list }

/// Reads, assigns and writes the id map. A number never changes and is never reused: a case
/// keeps its number while the corpus has it, a new case takes the number after the highest one
/// in the map, retired numbers included, and a removed case's number stays in the map, retired.
/// A case that comes back gets its own number again.
module CaseIds =
    let private idPattern = Regex("^wasm-host-([0-9]{4})$")

    let private issue (path: string) (message: string) = {
        Code = "mck.invalid_id_map"
        Path = path
        Line = 0
        Message = message
    }

    /// The number of a case id that the map gives an active entry.
    let numberOf (map: CaseIdMap) (caseId: string) =
        map.Entries
        |> List.tryFind (fun entry ->
            entry.CaseId = caseId
            && not entry.Retired
        )
        |> Option.map (fun entry -> entry.Number)

    let private validate (path: string) (pin: Pin) (entries: CaseIdEntry list) =
        let numbers =
            entries
            |> List.map (fun entry -> entry.Number)

        let caseIds =
            entries
            |> List.map (fun entry -> entry.CaseId)

        if
            List.distinct numbers
            <> numbers
        then
            Error [ issue path "A number is given to more than one case" ]
        elif
            List.distinct caseIds
            <> caseIds
        then
            Error [ issue path "A case id has more than one number" ]
        elif
            entries
            |> List.exists (fun entry ->
                entry.Number < 1
                || entry.Number > 9999
            )
        then
            Error [ issue path "The suite has no number left: numbers go from 1 to 9999" ]
        else
            Ok
                {
                    Pin = pin
                    Entries =
                        entries
                        |> List.sortBy (fun entry -> entry.Number)
                }

    /// Reads an id map file. path names the file in issues.
    let parse (path: string) (bytes: byte array) : Result<CaseIdMap, CorpusIssue list> =
        let text (element: JsonElement) (name: string) =
            match element.TryGetProperty name with
            | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
            | _ -> Error [ issue path $"The member \"{name}\" is a required string" ]

        let only (element: JsonElement) (names: string list) =
            match
                element.EnumerateObject()
                |> Seq.tryFind (fun property -> not (List.contains property.Name names))
            with
            | Some property -> Error [ issue path $"Unknown member \"{property.Name}\"" ]
            | None -> Ok()

        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement

            if
                root.ValueKind
                <> JsonValueKind.Object
            then
                Error [ issue path "The id map is an object" ]
            else
                only root [ "suite"; "corpus"; "cases" ]
                |> Result.bind (fun () -> text root "suite")
                |> Result.bind (fun suite ->
                    if
                        suite
                        <> Suite.Name
                    then
                        Error [
                            issue
                                path
                                $"The id map is for the suite \"{suite}\", not \"{Suite.Name}\""
                        ]
                    else
                        match root.TryGetProperty "corpus" with
                        | true, corpus when corpus.ValueKind = JsonValueKind.Object ->
                            only corpus [ "path"; "commit"; "sha256" ]
                            |> Result.bind (fun () -> text corpus "path")
                            |> Result.bind (fun corpusPath ->
                                text corpus "commit"
                                |> Result.bind (fun commit ->
                                    text corpus "sha256"
                                    |> Result.map (fun sha -> {
                                        Corpus = corpusPath
                                        Commit = commit
                                        Sha256 = sha
                                    })
                                )
                            )
                        | _ -> Error [ issue path "The member \"corpus\" is a required object" ]
                )
                |> Result.bind (fun pin ->
                    match root.TryGetProperty "cases" with
                    | true, cases when cases.ValueKind = JsonValueKind.Array ->
                        let entries =
                            cases.EnumerateArray()
                            |> Seq.map (fun entry ->
                                if
                                    entry.ValueKind
                                    <> JsonValueKind.Object
                                then
                                    Error [ issue path "An entry is an object" ]
                                else
                                    only entry [ "id"; "caseId"; "retired" ]
                                    |> Result.bind (fun () -> text entry "id")
                                    |> Result.bind (fun id ->
                                        let matched = idPattern.Match id

                                        if not matched.Success then
                                            Error [
                                                issue
                                                    path
                                                    $"The id \"{id}\" is not wasm-host-NNNN"
                                            ]
                                        else
                                            text entry "caseId"
                                            |> Result.bind (fun caseId ->
                                                let retired =
                                                    match entry.TryGetProperty "retired" with
                                                    | false, _ -> Ok false
                                                    | true, value when
                                                        value.ValueKind = JsonValueKind.True
                                                        ->
                                                        Ok true
                                                    | true, _ ->
                                                        Error [
                                                            issue
                                                                path
                                                                $"The member \"retired\" of {id} is true or left out"
                                                        ]

                                                retired
                                                |> Result.map (fun retired -> {
                                                    Number =
                                                        Int32.Parse(
                                                            matched.Groups[1].Value,
                                                            CultureInfo.InvariantCulture
                                                        )
                                                    CaseId = caseId
                                                    Retired = retired
                                                })
                                            )
                                    )
                            )
                            |> Seq.toList

                        match
                            entries
                            |> List.tryPick (
                                function
                                | Error issues -> Some issues
                                | Ok _ -> None
                            )
                        with
                        | Some issues -> Error issues
                        | None ->
                            validate
                                path
                                pin
                                (entries
                                 |> List.choose Result.toOption)
                    | _ -> Error [ issue path "The member \"cases\" is a required array" ]
                )
        with :? JsonException as error ->
            Error [ issue path error.Message ]

    /// Assigns numbers to the case ids of a corpus, in corpus order, keeping every number of the
    /// previous map.
    let assign
        (previous: CaseIdMap option)
        (pin: Pin)
        (caseIds: string list)
        : Result<CaseIdMap, CorpusIssue list> =
        let entries =
            previous
            |> Option.map (fun map -> map.Entries)
            |> Option.defaultValue []

        let known =
            entries
            |> List.map (fun entry -> entry.CaseId, entry.Number)
            |> Map.ofList

        let first =
            (entries
             |> List.map (fun entry -> entry.Number)
             |> List.fold max 0)
            + 1

        let present = Set.ofList caseIds

        let added, _ =
            caseIds
            |> List.filter (fun caseId -> not (Map.containsKey caseId known))
            |> List.fold
                (fun (assigned, next) caseId ->
                    ({
                        Number = next
                        CaseId = caseId
                        Retired = false
                     }
                     :: assigned),
                    next + 1
                )
                ([], first)

        let kept =
            entries
            |> List.map (fun entry ->
                { entry with
                    Retired = not (Set.contains entry.CaseId present)
                }
            )

        validate
            pin.Corpus
            pin
            (kept
             @ added)

    /// The text of an id map file, with LF line endings.
    let write (map: CaseIdMap) : string =
        let builder = StringBuilder()

        let line (text: string) =
            builder.Append(text).Append('\n')
            |> ignore

        line "{"
        line $"  \"suite\": {Json.quote Suite.Name},"
        line "  \"corpus\": {"
        line $"    \"path\": {Json.quote map.Pin.Corpus},"
        line $"    \"commit\": {Json.quote map.Pin.Commit},"
        line $"    \"sha256\": {Json.quote map.Pin.Sha256}"
        line "  },"

        match map.Entries with
        | [] -> line "  \"cases\": []"
        | entries ->
            line "  \"cases\": ["

            entries
            |> List.iteri (fun index entry ->
                let retired = if entry.Retired then ", \"retired\": true" else ""

                let separator =
                    if
                        index < entries.Length
                                - 1
                    then
                        ","
                    else
                        ""

                line
                    $"    {{ \"id\": {Json.quote (Suite.id entry.Number)}, \"caseId\": {Json.quote entry.CaseId}{retired} }}{separator}"
            )

            line "  ]"

        line "}"
        builder.ToString()
