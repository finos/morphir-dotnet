/// Reads the JSON steps of the vendored kit's feature files, so that the kit itself states what the reader
/// must accept and refuse. A step in a form this parser does not know raises rather than being skipped.
module Morphir.Json.Tests.V4.KitCases

open System
open System.IO
open System.Text.RegularExpressions

/// A kit step that a JSON reader can check.
type KitStep =
    /// The JSON a writer emits, which a reader accepts too.
    | Canonical of json: string
    /// A JSON spelling a reader accepts, with or without a warning.
    | Accepts of json: string
    /// A JSON array that a reader reads as a List.
    | ReadsAsList of json: string
    /// A JSON input a reader refuses.
    | Rejects of json: string * diagnostic: string
    /// A file in the upstream repository that a reader accepts.
    | AcceptsFile of path: string

/// One kit case: every scenario that shares a case id.
type KitCase = {
    Feature: string
    Id: string
    Title: string
    Tags: string list
    Steps: KitStep list
}

let fixtureRoot =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "Fixtures", "MorphirIon")

let private stepKeyword =
    Regex(@"^(Given|When|Then|And|But) (.*)$", RegexOptions.Compiled)

let private parseStep (step: string) =
    let matching (pattern: string) = Regex.Match(step, pattern)
    let accepts = matching @"^a reader of JSON accepts (.+?)(?: with warning \w+)?$"
    let rejects = matching @"^a reader of JSON rejects (.+) with (\w+)$"
    let list = matching @"^a reader of JSON reads (.+) as a List$"
    let canonical = matching @"^its canonical JSON spelling is (.+)$"
    let file = matching @"^a reader of text accepts (.+)$"

    if accepts.Success then
        Some(Accepts accepts.Groups[1].Value)
    elif rejects.Success then
        Some(Rejects(rejects.Groups[1].Value, rejects.Groups[2].Value))
    elif list.Success then
        Some(ReadsAsList list.Groups[1].Value)
    elif canonical.Success then
        Some(Canonical canonical.Groups[1].Value)
    elif file.Success then
        Some(AcceptsFile file.Groups[1].Value)
    elif
        step.StartsWith "a reader of JSON"
        || step.StartsWith "its canonical JSON"
    then
        failwith $"The kit parser does not know this JSON step: {step}"
    else
        None

let private cells (row: string) =
    row.Trim().Trim('|').Split('|')
    |> Array.map (fun cell -> cell.Trim())
    |> List.ofArray

/// Expands a scenario's steps over its example rows, if it has any.
let private expand (steps: string list) (table: string list list) =
    match table with
    | [] -> steps
    | header :: rows -> [
        for row in rows do
            for step in steps do
                List.zip header row
                |> List.fold
                    (fun (text: string) (name, cell) -> text.Replace($"<{name}>", cell))
                    step
      ]

/// Reads every scenario of one feature file into its JSON steps.
let private scenarios (feature: string) (text: string) =
    let lines =
        text.Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> List.ofArray

    let mutable featureTags = []
    let mutable pendingTags = []
    let mutable title = None
    let mutable scenarioTags = []
    let mutable steps = []
    let mutable table = []
    let mutable inDocString = false
    let mutable results = []

    let flush () =
        match title with
        | Some(name: string) ->
            let separator = name.IndexOf ' '

            results <-
                {
                    Feature = feature
                    Id = name.Substring(0, separator)
                    Title =
                        name.Substring(
                            separator
                            + 1
                        )
                    Tags =
                        featureTags
                        @ scenarioTags
                    Steps =
                        expand (List.rev steps) (List.rev table)
                        |> List.choose parseStep
                }
                :: results
        | None -> ()

        title <- None
        steps <- []
        table <- []

    for line in lines do
        if line.StartsWith "\"\"\"" then
            inDocString <- not inDocString
        elif inDocString then
            ()
        elif line.StartsWith "@" then
            pendingTags <-
                pendingTags
                @ List.ofArray (line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        elif line.StartsWith "Feature:" then
            featureTags <- pendingTags
            pendingTags <- []
        elif line.StartsWith "Scenario" then
            flush ()

            title <-
                Some(
                    line
                        .Substring(
                            line.IndexOf(':')
                            + 1
                        )
                        .Trim()
                )

            scenarioTags <- pendingTags
            pendingTags <- []
        elif line.StartsWith "|" then
            table <-
                cells line
                :: table
        else
            let keyword = stepKeyword.Match line

            if
                keyword.Success
                && title.IsSome
            then
                steps <-
                    keyword.Groups[2].Value
                    :: steps

    flush ()
    List.rev results

/// The cases of one feature with at least one JSON step, leaving out classic v3 cases and pending ones.
let casesOf (feature: string) =
    File.ReadAllText(Path.Combine(fixtureRoot, "kit", feature))
    |> scenarios feature
    |> List.groupBy (fun scenario -> scenario.Id)
    |> List.map (fun (_, group) ->
        { group.Head with
            Tags =
                group
                |> List.collect (fun scenario -> scenario.Tags)
                |> List.distinct
            Steps =
                group
                |> List.collect (fun scenario -> scenario.Steps)
        }
    )
    |> List.filter (fun case ->
        not (List.isEmpty case.Steps)
        && not (List.contains "@version:3" case.Tags)
        && not (List.contains "@pending" case.Tags)
    )
