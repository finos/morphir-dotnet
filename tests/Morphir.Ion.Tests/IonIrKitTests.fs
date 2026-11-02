/// Reads the Ion spellings of the vendored kit's feature files and checks each against the JSON spellings of
/// the same case, read through the JSON reader. A step this parser does not know fails rather than being
/// skipped.
module Morphir.Ion.Tests.IonIrKitTests

open System
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Morphir.IR.V4
open Morphir.Ion
open Morphir.Ion.Tests
open Morphir.Json.V4

/// The Ion and JSON spellings of one kit case.
type private KitCase = {
    Id: string
    Title: string
    Ion: string list
    Json: string list
}

let private features = [
    "names.feature"
    "values.feature"
    "types.feature"
    "patterns-and-literals.feature"
    "definitions.feature"
    "distributions.feature"
]

let private cells (row: string) =
    row.Trim().Trim('|').Split('|')
    |> Array.map (fun cell -> cell.Trim())
    |> List.ofArray

let private jsonOf (step: string) =
    let canonical = Regex.Match(step, @"^its canonical JSON spelling is (.+)$")

    let accepts =
        Regex.Match(step, @"^a reader of JSON accepts (.+?)(?: with warning \w+)?$")

    if canonical.Success then Some canonical.Groups[1].Value
    elif accepts.Success then Some accepts.Groups[1].Value
    else None

/// Reads one feature file into a case for each id that states an Ion spelling.
let private casesOf (feature: string) =
    let lines =
        (Fixtures.text $"kit/{feature}").Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> List.ofArray

    let mutable id = ""
    let mutable title = ""
    let mutable header = []
    let mutable docString: string list option = None
    let mutable isIonDocString = false
    let mutable found: (string * string * string * bool) list = []

    let add isIon text =
        found <-
            (id, title, text, isIon)
            :: found

    for line in lines do
        match docString with
        | Some collected when line.StartsWith "\"\"\"" ->
            if isIonDocString then
                add true (String.concat "\n" (List.rev collected))

            docString <- None
        | Some collected ->
            docString <-
                Some(
                    line
                    :: collected
                )
        | None when line.StartsWith "\"\"\"" ->
            docString <- Some []
            isIonDocString <- line = "\"\"\"ion"
        | None when line.StartsWith "Scenario" ->
            let name =
                line
                    .Substring(
                        line.IndexOf(':')
                        + 1
                    )
                    .Trim()

            let separator = name.IndexOf ' '
            id <- name.Substring(0, separator)

            title <-
                name.Substring(
                    separator
                    + 1
                )

            header <- []
        | None when line.StartsWith "|" ->
            let row = cells line

            if List.isEmpty header then
                header <- row
            else
                let values =
                    List.zip header row
                    |> Map.ofList

                match values.TryFind "format", values.TryFind "spelling" with
                | Some "Ion", Some spelling -> add true spelling
                | Some "JSON", Some spelling -> add false spelling
                | Some "Ion", None -> failtestf "An Ion row in %s has no spelling: %s" id line
                | _ -> ()
        | None ->
            let step = Regex.Match(line, @"^(?:Given|When|Then|And|But) (.*)$")

            if step.Success then
                let text = step.Groups[1].Value
                let inline' = Regex.Match(text, @"^its canonical Ion spelling is (.+)$")

                if
                    inline'.Success
                    && not (text.EndsWith ":")
                then
                    add true inline'.Groups[1].Value
                elif
                    text.Contains "Ion"
                    && not (text.StartsWith "its canonical Ion spelling is")
                then
                    failtestf "The kit parser does not know this Ion step: %s" text
                else
                    match jsonOf text with
                    | Some json -> add false json
                    | None -> ()

    List.rev found
    |> List.groupBy (fun (id, _, _, _) -> id)
    |> List.map (fun (id, group) ->
        let _, title, _, _ = group.Head

        let spellings isIon =
            group
            |> List.filter (fun (_, _, _, ion) -> ion = isIon)
            |> List.map (fun (_, _, text, _) -> text)

        {
            Id = id
            Title = title
            Ion = spellings true
            Json = spellings false
        }
    )
    |> List.filter (fun case -> not (List.isEmpty case.Ion))

let private readIonValue (text: string) =
    match IonReader.read IonReadLimits.defaults (Fixtures.utf8 text) with
    | Ok [ value ] ->
        try
            V4IonValues.value Root value
        with IonIrFailure error ->
            failtestf "%s should read, got %A" text error
    | other -> failtestf "%s is not one Ion value: %A" text other

let private readJsonValue (json: string) =
    use document = JsonDocument.Parse json

    match V4Json.readValue document.RootElement with
    | Ok value -> value
    | Error error -> failtestf "%s should read, got %A" json error

/// A case whose readers are YAML only, with the value its YAML spellings `.5` and `+0.5` state.
let private yamlOnly =
    Map.ofList [ "values-0045", [ "{ \"Literal\": { \"FloatLiteral\": 0.5 } }" ] ]

let private allCases =
    features
    |> List.collect (fun feature ->
        casesOf feature
        |> List.map (fun case -> feature, case)
    )

[<Tests>]
let tests =
    testList "The result matches the JSON reader: Compatibility kit spellings" [
        testList "Every Ion spelling reads to the case's JSON value" [
            for feature, case in allCases do
                testCase $"{feature} {case.Id} {case.Title}"
                <| fun () ->
                    let spellings =
                        match case.Json, yamlOnly.TryFind case.Id with
                        | [], Some stated -> stated
                        | json, _ -> json

                    Expect.isNonEmpty spellings "A case states a JSON spelling to compare with"

                    let expected =
                        spellings
                        |> List.map readJsonValue

                    for ion in case.Ion do
                        let value = readIonValue ion

                        for json in expected do
                            Expect.equal value json $"Ion {ion}"
        ]

        testCase "The parser finds every Ion spelling in the feature files"
        <| fun () ->
            let inText =
                features
                |> List.sumBy (fun feature ->
                    let text = Fixtures.text $"kit/{feature}"

                    Regex.Matches(text, "\"\"\"ion").Count
                    + Regex.Matches(text, @"(?m)^\s*\| Ion ").Count
                    + Regex.Matches(text, @"(?m)canonical Ion spelling is [^:\r\n]+$").Count
                )

            let parsed =
                allCases
                |> List.sumBy (fun (_, case) -> case.Ion.Length)

            Expect.isGreaterThan inText 30 "The kit has Ion spellings"
            Expect.equal parsed inText "No spelling is lost"
    ]
