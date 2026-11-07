// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Security.Cryptography
open System.Text.Json
open Gherkin
open Gherkin.Ast

module Corpus =
    let private diagnostics (text: string) =
        use document = JsonDocument.Parse text

        [
            for diagnostic in document.RootElement.EnumerateArray() do
                let location = diagnostic.GetProperty("location")
                let range = location.GetProperty("range")
                let start = range.GetProperty("start")
                let finish = range.GetProperty("end")

                yield {
                    Severity = diagnostic.GetProperty("severity").GetString()
                    Code = diagnostic.GetProperty("code").GetString()
                    Uri = location.GetProperty("uri").GetString()
                    StartLine = start.GetProperty("line").GetInt32()
                    StartCharacter = start.GetProperty("character").GetInt32()
                    EndLine = finish.GetProperty("line").GetInt32()
                    EndCharacter = finish.GetProperty("character").GetInt32()
                }
        ]

    let private digest (artifacts: Artifact list) =
        use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)

        for artifact in
            artifacts
            |> List.sortWith (fun left right ->
                System.StringComparer.Ordinal.Compare(left.Path, right.Path)
            ) do
            let name = Encoding.UTF8.GetBytes artifact.Path

            let frame (length: int) =
                Encoding.UTF8.GetBytes(
                    length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ":"
                )

            hash.AppendData(frame name.Length)
            hash.AppendData name
            hash.AppendData(frame artifact.Bytes.Length)
            hash.AppendData artifact.Bytes

        hash.GetHashAndReset()
        |> System.Convert.ToHexStringLower

    let private stepPatterns = [
        "^the F# package \"([^\"]+)\" exposing \"([^\"]+)\"$"
        "^the source \"([^\"]+)\":$"
        "^it is compiled from <input> with IR version <version>$"
        "^compilation fails with diagnostics:$"
        "^compilation succeeds with no error diagnostics$"
        "^the IR equals \"([^\"]+)\"$"
    ]

    let private portablePath (path: string) =
        not (System.String.IsNullOrWhiteSpace path)
        && not (Path.IsPathRooted path)
        && not (
            path.Contains(':')
            || path.Contains(char 92)
        )
        && (path.Split('/')
            |> Array.forall (fun part ->
                part <> ""
                && part
                   <> "."
                && part
                   <> ".."
            ))

    let private validateFeature (options: CorpusOptions) path (document: GherkinDocument) =
        let issues = ResizeArray<CorpusIssue>()

        let add code line message =
            issues.Add
                {
                    Code = code
                    Path = path
                    Line = line
                    Message = message
                }

        let invalid line message = add "mck.invalid_case" line message

        let tags allowExcluded (values: Tag seq) =
            for tag in values do
                if
                    Regex.IsMatch(
                        tag.Name,
                        "^@(adapter|implementation|executable|host|cli|library|mep-guest)(:|$)"
                    )
                then
                    add
                        "mck.implementation_in_feature"
                        tag.Location.Line
                        ("Features cannot select an implementation: "
                         + tag.Name)
                elif
                    tag.Name = "@excluded"
                    && not allowExcluded
                then
                    invalid tag.Location.Line "@excluded is only valid on Examples"
                elif
                    not (
                        tag.Name = "@suite:"
                                   + options.Suite
                        || List.contains tag.Name [
                            "@semantic"
                            "@refusal"
                            "@basis:hand-authored"
                            "@basis:spec-derived"
                            "@excluded"
                        ]
                    )
                then
                    add
                        "mck.unsupported_tag"
                        tag.Location.Line
                        ("Tag is outside the seed vocabulary: "
                         + tag.Name)

        if isNull document.Feature then
            invalid 1 "A feature is required"
        else
            let feature = document.Feature
            tags false feature.Tags

            if
                not (
                    feature.Tags
                    |> Seq.exists (fun tag ->
                        tag.Name = "@suite:"
                                   + options.Suite
                    )
                )
            then
                invalid
                    feature.Location.Line
                    ("Feature must declare @suite:"
                     + options.Suite)

            if Seq.isEmpty feature.Children then
                invalid feature.Location.Line "A feature needs scenarios"

            for child in feature.Children do
                match child with
                | :? Scenario as scenario ->
                    tags false scenario.Tags

                    if
                        not (
                            Regex.IsMatch(
                                scenario.Name,
                                "^"
                                + Regex.Escape(options.Suite)
                                + "-[0-9]{4}( |$)"
                            )
                        )
                    then
                        invalid scenario.Location.Line "Scenario needs a stable id in its suite"

                    let steps = Seq.toList scenario.Steps

                    for step in steps do
                        if
                            not (isNull step.Argument)
                            && not (Regex.IsMatch(step.Text, stepPatterns[1]))
                            && step.Text
                               <> "compilation fails with diagnostics:"
                        then
                            invalid step.Location.Line "This step does not take an argument"

                        if
                            not (
                                stepPatterns
                                |> List.exists (fun pattern -> Regex.IsMatch(step.Text, pattern))
                            )
                        then
                            add
                                "mck.unknown_step"
                                step.Location.Line
                                ("Step is not in vocabulary 1.0: "
                                 + step.Text)

                    let matching pattern =
                        steps
                        |> List.filter (fun step -> Regex.IsMatch(step.Text, pattern))

                    if (matching stepPatterns[5]).Length > 1 then
                        invalid
                            scenario.Location.Line
                            "A variant has one complete model expectation"

                    if
                        steps.IsEmpty
                        || not (Regex.IsMatch(steps.Head.Text, stepPatterns[0]))
                        || (matching stepPatterns[0]).Length
                           <> 1
                    then
                        invalid
                            scenario.Location.Line
                            "A case starts with exactly one package declaration"

                    let sources = matching stepPatterns[1]

                    if sources.IsEmpty then
                        invalid scenario.Location.Line "A case needs source documents"

                    let uris = ResizeArray<string>()

                    for step in sources do
                        let uri = Regex.Match(step.Text, stepPatterns[1]).Groups[1].Value
                        uris.Add uri

                        if not (portablePath uri) then
                            invalid
                                step.Location.Line
                                "Source URI must be root-relative and portable"

                        match step.Argument with
                        | :? DocString as text when text.ContentType = "fsharp" -> ()
                        | _ -> invalid step.Location.Line "A source needs a fsharp docstring"

                    if
                        (Seq.distinct uris
                         |> Seq.length)
                        <> uris.Count
                    then
                        invalid scenario.Location.Line "Source URIs must be unique"

                    if
                        (matching stepPatterns[2]).Length
                        <> 1
                    then
                        invalid scenario.Location.Line "A case needs exactly one compile step"

                    let outcomes =
                        steps
                        |> List.filter (fun step ->
                            step.Text = "compilation fails with diagnostics:"
                            || step.Text = "compilation succeeds with no error diagnostics"
                        )

                    if
                        outcomes.Length
                        <> 1
                    then
                        invalid
                            scenario.Location.Line
                            "A case needs exactly one compilation outcome"

                    for step in outcomes do
                        if step.Text = "compilation fails with diagnostics:" then
                            match step.Argument with
                            | :? DocString as text when text.ContentType = "json" ->
                                try
                                    let keys = diagnostics text.Content

                                    if
                                        not (
                                            keys
                                            |> List.exists (fun key -> key.Severity = "error")
                                        )
                                    then
                                        invalid
                                            step.Location.Line
                                            "A failed outcome needs an error diagnostic"

                                    for key in keys do
                                        if
                                            not (
                                                List.contains key.Severity [
                                                    "error"
                                                    "warning"
                                                    "info"
                                                    "hint"
                                                ]
                                            )
                                            || System.String.IsNullOrWhiteSpace key.Code
                                            || not (portablePath key.Uri)
                                            || key.StartLine < 0
                                            || key.StartCharacter < 0
                                            || key.EndLine < 0
                                            || key.EndCharacter < 0
                                            || (key.EndLine, key.EndCharacter) < (key.StartLine,
                                                                                  key.StartCharacter)
                                        then
                                            invalid
                                                step.Location.Line
                                                "Diagnostic needs severity, code and a valid UTF-16 range"
                                with
                                | :? JsonException
                                | :? System.Collections.Generic.KeyNotFoundException
                                | :? System.InvalidOperationException
                                | :? System.FormatException ->
                                    invalid step.Location.Line "Invalid diagnostic JSON"
                            | _ -> invalid step.Location.Line "Diagnostics need a json docstring"

                    if Seq.isEmpty scenario.Examples then
                        invalid
                            scenario.Location.Line
                            "The seed vocabulary requires explicit Examples"

                    for example in scenario.Examples do
                        tags true example.Tags

                        if isNull example.TableHeader then
                            invalid example.Location.Line "Examples need input and version columns"
                        else
                            let headers =
                                example.TableHeader.Cells
                                |> Seq.map (fun cell -> cell.Value)
                                |> Seq.toList

                            if
                                not (
                                    List.contains "input" headers
                                    && List.contains "version" headers
                                )
                                || (Set.ofList headers).Count
                                   <> headers.Length
                                || (headers
                                    |> List.exists (fun name ->
                                        not (List.contains name [ "input"; "version"; "reason" ])
                                    ))
                            then
                                invalid
                                    example.Location.Line
                                    "Examples columns are input, version and optional reason"

                            for row in example.TableBody do
                                let values =
                                    row.Cells
                                    |> Seq.map (fun cell -> cell.Value)
                                    |> Seq.toList

                                if
                                    headers.Length
                                    <> values.Length
                                then
                                    invalid row.Location.Line "Example cells must match the header"
                                elif
                                    List.contains "input" headers
                                    && List.contains "version" headers
                                then
                                    let fields =
                                        List.zip headers values
                                        |> Map.ofList

                                    if
                                        not (
                                            List.contains fields["input"] options.InputKinds
                                            && List.contains fields["version"] options.IrVersions
                                        )
                                    then
                                        invalid
                                            row.Location.Line
                                            "Example is outside the declared input/version matrix"
                | _ ->
                    invalid
                        child.Location.Line
                        "The seed vocabulary supports scenarios directly under a feature"

        Seq.toList issues

    let private readCurrent
        (options: CorpusOptions)
        (artifacts: Artifact list)
        : Result<CaseSet, CorpusIssue list> =
        let issues = ResizeArray<CorpusIssue>()

        let seenIds =
            System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)

        let cases = [
            for artifact in
                artifacts
                |> List.sortWith (fun left right ->
                    System.StringComparer.Ordinal.Compare(left.Path, right.Path)
                ) do
                if artifact.Path.EndsWith(".feature", System.StringComparison.Ordinal) then
                    let parsed =
                        try
                            use reader = new StringReader(Encoding.UTF8.GetString artifact.Bytes)
                            Ok(Parser().Parse reader)
                        with :? ParserException as error ->
                            Error
                                {
                                    Code = "mck.invalid_gherkin"
                                    Path = artifact.Path
                                    Line = 1
                                    Message = error.Message
                                }

                    match parsed with
                    | Error issue -> issues.Add issue
                    | Ok document ->
                        let validation = validateFeature options artifact.Path document

                        if not validation.IsEmpty then
                            issues.AddRange validation
                        else
                            for child in document.Feature.Children do
                                match child with
                                | :? Scenario as scenario ->
                                    let id = scenario.Name.Split(' ')[0]

                                    if not (seenIds.Add id) then
                                        issues.Add
                                            {
                                                Code = "mck.duplicate_id"
                                                Path = artifact.Path
                                                Line = scenario.Location.Line
                                                Message =
                                                    "Scenario id is repeated: "
                                                    + id
                                            }

                                    let steps =
                                        scenario.Steps
                                        |> Seq.toList

                                    let package = Regex.Match(steps[0].Text, stepPatterns[0])

                                    let sources = [
                                        for step in steps do
                                            let source = Regex.Match(step.Text, stepPatterns[1])

                                            if source.Success then
                                                let text = step.Argument :?> DocString

                                                yield {
                                                    Uri = source.Groups[1].Value
                                                    Text = text.Content
                                                }
                                    ]

                                    let failure =
                                        steps
                                        |> List.tryFind (fun step ->
                                            step.Text = "compilation fails with diagnostics:"
                                        )

                                    let expected = {
                                        Success = failure.IsNone
                                        Diagnostics =
                                            match failure with
                                            | Some step ->
                                                diagnostics (step.Argument :?> DocString).Content
                                            | None -> []
                                        AllowNonErrorDiagnostics = failure.IsNone
                                        ModelJson = None
                                    }

                                    for example in scenario.Examples do
                                        let headers =
                                            example.TableHeader.Cells
                                            |> Seq.map (fun cell -> cell.Value)
                                            |> Seq.toList

                                        for row in example.TableBody do
                                            let values =
                                                row.Cells
                                                |> Seq.map (fun cell -> cell.Value)
                                                |> Seq.toList

                                            let fields =
                                                List.zip headers values
                                                |> Map.ofList

                                            let excluded =
                                                example.Tags
                                                |> Seq.exists (fun tag -> tag.Name = "@excluded")

                                            let reason =
                                                fields
                                                |> Map.tryFind "reason"
                                                |> Option.filter (
                                                    not
                                                    << System.String.IsNullOrWhiteSpace
                                                )

                                            if
                                                excluded
                                                && reason.IsNone
                                            then
                                                issues.Add
                                                    {
                                                        Code = "mck.exclusion_reason"
                                                        Path = artifact.Path
                                                        Line = row.Location.Line
                                                        Message =
                                                            "An @excluded row needs a non-empty reason"
                                                    }

                                            let expectedModel =
                                                steps
                                                |> List.tryPick (fun step ->
                                                    let matched =
                                                        Regex.Match(step.Text, stepPatterns[5])

                                                    if not matched.Success then
                                                        None
                                                    else
                                                        let relative =
                                                            matched.Groups[1]
                                                                .Value.Replace(
                                                                    "<version>",
                                                                    fields["version"]
                                                                )
                                                                .Replace(
                                                                    "<input>",
                                                                    fields["input"]
                                                                )

                                                        let path =
                                                            Path
                                                                .Combine(
                                                                    Path.GetDirectoryName
                                                                        artifact.Path,
                                                                    relative
                                                                )
                                                                .Replace(
                                                                    Path.DirectorySeparatorChar,
                                                                    '/'
                                                                )

                                                        match
                                                            artifacts
                                                            |> List.tryFind (fun file ->
                                                                file.Path = path
                                                            )
                                                        with
                                                        | Some file ->
                                                            let text =
                                                                Encoding.UTF8.GetString file.Bytes

                                                            try
                                                                use document =
                                                                    JsonDocument.Parse text

                                                                Some text
                                                            with :? JsonException as error ->
                                                                issues.Add
                                                                    {
                                                                        Code =
                                                                            "mck.invalid_artifact"
                                                                        Path = file.Path
                                                                        Line = 1
                                                                        Message = error.Message
                                                                    }

                                                                None
                                                        | None ->
                                                            issues.Add
                                                                {
                                                                    Code = "mck.missing_artifact"
                                                                    Path = artifact.Path
                                                                    Line = step.Location.Line
                                                                    Message =
                                                                        "Missing expected IR: "
                                                                        + path
                                                                }

                                                            None
                                                )

                                            yield {
                                                Id = scenario.Name.Split(' ')[0]
                                                File = artifact.Path
                                                Line = scenario.Location.Line
                                                Expected =
                                                    { expected with
                                                        ModelJson = expectedModel
                                                    }
                                                Input = {
                                                    Package = package.Groups[1].Value
                                                    ExposedModules = [ package.Groups[2].Value ]
                                                    Documents = sources
                                                    InputKind = fields["input"]
                                                    IrVersion = fields["version"]
                                                }
                                                Exclusion = if excluded then reason else None
                                            }
                                | _ -> ()
        ]

        for _, variants in
            cases
            |> List.groupBy (fun case -> case.Id) do
            let first = variants.Head

            for (input, version), matching in
                variants
                |> List.groupBy (fun case -> case.Input.InputKind, case.Input.IrVersion) do
                if
                    matching.Length
                    <> 1
                then
                    issues.Add
                        {
                            Code = "mck.duplicate_combination"
                            Path = first.File
                            Line = first.Line
                            Message =
                                first.Id
                                + " repeats "
                                + input
                                + "/"
                                + version
                        }

            for input in options.InputKinds do
                for version in options.IrVersions do
                    if
                        not (
                            variants
                            |> List.exists (fun case ->
                                case.Input.InputKind = input
                                && case.Input.IrVersion = version
                            )
                        )
                    then
                        issues.Add
                            {
                                Code = "mck.missing_combination"
                                Path = first.File
                                Line = first.Line
                                Message =
                                    first.Id
                                    + " does not list or exclude "
                                    + input
                                    + "/"
                                    + version
                            }

        if cases.IsEmpty then
            issues.Add
                {
                    Code = "mck.invalid_case"
                    Path = ""
                    Line = 0
                    Message = "No case variants were selected"
                }

        if issues.Count = 0 then
            Ok
                {
                    Cases = cases
                    Sha256 = digest artifacts
                }
        else
            Error(Seq.toList issues)

    let read (options: CorpusOptions) (artifacts: Artifact list) =
        let pathIssues = [
            for path, files in
                artifacts
                |> List.groupBy (fun artifact -> artifact.Path) do
                if
                    not (portablePath path)
                    || files.Length
                       <> 1
                then
                    yield {
                        Code = "mck.artifact_path"
                        Path = path
                        Line = 0
                        Message = "Artifact paths must be unique, root-relative and portable"
                    }
        ]

        if
            options.VocabularyVersion
            <> "1.0"
        then
            Error [
                {
                    Code = "mck.vocabulary_version"
                    Path = ""
                    Line = 0
                    Message =
                        "Runner implements vocabulary 1.0; requested "
                        + options.VocabularyVersion
                }
            ]
        elif not pathIssues.IsEmpty then
            Error pathIssues
        else
            readCurrent options artifacts
