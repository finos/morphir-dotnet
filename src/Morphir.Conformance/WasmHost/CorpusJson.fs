// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions
open Morphir.Conformance

/// A small Result builder for the readers of this suite.
module internal ResultSyntax =
    type ResultBuilder() =
        member _.Bind(result: Result<'T, 'E>, next: 'T -> Result<'U, 'E>) = Result.bind next result
        member _.Return(value: 'T) : Result<'T, 'E> = Ok value
        member _.ReturnFrom(result: Result<'T, 'E>) = result
        member _.Zero() : Result<unit, 'E> = Ok()

    let result = ResultBuilder()

    /// The values of a list of results, or the first error.
    let all (results: Result<'T, 'E> list) : Result<'T list, 'E> =
        let folder (item: Result<'T, 'E>) (rest: Result<'T list, 'E>) =
            match item, rest with
            | Ok value, Ok values ->
                Ok(
                    value
                    :: values
                )
            | Error error, _ -> Error error
            | Ok _, Error error -> Error error

        List.foldBack folder results (Ok [])

/// Reads the pinned JSON corpus of the WASM host kit into the case model. The reader exists to
/// generate the features and to check them; the runner reads the features. Every member, matcher
/// and convention of the corpus must be one this reader knows: a newer corpus fails with an
/// error that names what it does not know.
module CorpusJson =
    open ResultSyntax

    /// The repository-relative path of the pinned corpus.
    [<Literal>]
    let DefaultCorpusPath = "tests/Fixtures/MepWasm/wasm-host.cases.json"

    /// The repository-relative path of the pin's provenance record.
    [<Literal>]
    let DefaultProvenancePath = "tests/Fixtures/MepWasm/PROVENANCE.md"

    /// The conventions of kit 0.1.0-draft.1. The runner implements each one; a corpus with
    /// another convention, or without one of these, is refused.
    let knownConventions = [
        "expectations"
        "worker"
        "profiles"
        "frames"
        "exitCode"
        "stdout"
        "events"
        "guestOutput"
        "timing"
        "alternatives"
        "bounds"
        "gaps"
        "withoutCase"
    ]

    /// The matchers of an expectation, as the corpus names them.
    let knownMatchers = [
        "exitCode"
        "stdoutBytes"
        "stdoutFrames"
        "events"
        "noEvents"
        "guestOutput"
        "guestOutputIncludes"
        "noGuestOutput"
        "withinSeconds"
    ]

    type private Failure = { Code: string; Message: string }

    let private fail code message : Result<'T, Failure> =
        Error { Code = code; Message = message }

    let private invalid path message =
        fail "mck.invalid_case" $"{message} at {path}"

    let private child (path: string) (name: string) =
        path
        + "/"
        + name.Replace("~", "~0").Replace("/", "~1")

    let private at (path: string) =
        if path = "" then "the corpus root" else path

    /// The members of an object, after checking that each one is allowed and none is repeated.
    let private members
        (code: string)
        (allowed: string list)
        (path: string)
        (element: JsonElement)
        =
        if
            element.ValueKind
            <> JsonValueKind.Object
        then
            invalid (at path) "An object is required"
        else
            let found =
                element.EnumerateObject()
                |> Seq.map (fun property -> property.Name, property.Value)
                |> Seq.toList

            match
                found
                |> List.countBy fst
                |> List.tryFind (fun (_, count) -> count > 1)
            with
            | Some(name, _) -> invalid (at path) $"The member \"{name}\" is repeated"
            | None ->
                match
                    found
                    |> List.tryFind (fun (name, _) -> not (List.contains name allowed))
                with
                | Some(name, _) ->
                    let kind = if code = "mck.unknown_matcher" then "matcher" else "member"
                    fail code $"Unknown {kind} \"{name}\" at {at path}"
                | None -> Ok(Map.ofList found)

    let private required (path: string) (name: string) (found: Map<string, JsonElement>) =
        match Map.tryFind name found with
        | Some value -> Ok value
        | None -> invalid (at path) $"The member \"{name}\" is required"

    let private optional (name: string) (found: Map<string, JsonElement>) = Map.tryFind name found

    let private text (path: string) (element: JsonElement) =
        if element.ValueKind = JsonValueKind.String then
            try
                Ok(element.GetString())
            with :? InvalidOperationException ->
                invalid path "A string is not valid UTF-16"
        else
            invalid path "A string is required"

    let private nonEmptyText path element =
        text path element
        |> Result.bind (fun value ->
            if String.IsNullOrWhiteSpace value then
                invalid path "A non-empty string is required"
            else
                Ok value
        )

    let private integer (path: string) (minimum: int) (element: JsonElement) =
        let mutable value = 0

        if
            element.ValueKind = JsonValueKind.Number
            && element.TryGetInt32(&value)
            && Regex.IsMatch(element.GetRawText(), "^(0|[1-9][0-9]*)$")
        then
            if value < minimum then
                invalid path $"An integer of at least {minimum} is required"
            else
                Ok value
        else
            invalid path "An integer is required"

    let private seconds (path: string) (element: JsonElement) =
        let mutable value = 0m

        if
            element.ValueKind = JsonValueKind.Number
            && element.TryGetDecimal(&value)
            && value
               >= 0m
        then
            Ok value
        else
            invalid path "A non-negative number of seconds is required"

    /// A list member. An empty list is refused where leaving the member out says the same.
    let private items
        (path: string)
        (allowEmpty: bool)
        (read: string -> JsonElement -> Result<'T, Failure>)
        (element: JsonElement)
        =
        if
            element.ValueKind
            <> JsonValueKind.Array
        then
            invalid path "An array is required"
        elif
            not allowEmpty
            && element.GetArrayLength() = 0
        then
            invalid path "An empty array says nothing here; leave the member out"
        else
            element.EnumerateArray()
            |> Seq.mapi (fun index item -> read (child path (string index)) item)
            |> Seq.toList
            |> all

    let private optionalList path name allowEmpty read found =
        match optional name found with
        | None -> Ok []
        | Some element -> items (child path name) allowEmpty read element

    let private parsed (parse: string -> Result<'T, string>) (path: string) (element: JsonElement) =
        text path element
        |> Result.bind (fun value ->
            match parse value with
            | Ok parsed -> Ok parsed
            | Error message -> fail "mck.unknown_value" $"{message} at {path}"
        )

    let private json (path: string) (element: JsonElement) =
        match Json.ofElement path element with
        | Ok value -> Ok value
        | Error message -> invalid path message

    let private sha256Pattern = Regex("^[0-9a-f]{64}$")

    let private portable (path: string) (element: JsonElement) =
        nonEmptyText path element
        |> Result.bind (fun value ->
            if
                Path.IsPathRooted value
                || value.Contains '\\'
                || value.Split('/')
                   |> Array.exists (fun part ->
                       part = ""
                       || part = "."
                       || part = ".."
                   )
            then
                invalid path "A portable relative path is required"
            else
                Ok value
        )

    let private caseIdPattern = Regex("^[a-z0-9][a-z0-9._-]*(/[a-z0-9][a-z0-9._-]*)+$")

    let private eventMatcher (path: string) (element: JsonElement) = result {
        let! found =
            members
                "mck.unknown_member"
                [ "event"; "abi"; "code"; "kind"; "messageIncludes" ]
                path
                element

        let! eventElement = required path "event" found
        let! event = parsed WorkerEvent.Parse (child path "event") eventElement

        let allowed =
            match event with
            | WorkerEvent.Started -> [ "event"; "abi" ]
            | WorkerEvent.Refused -> [ "event"; "code"; "messageIncludes" ]
            | WorkerEvent.Fault -> [ "event"; "kind" ]
            | WorkerEvent.Output -> [ "event" ]

        match
            found
            |> Map.toList
            |> List.tryFind (fun (name, _) -> not (List.contains name allowed))
        with
        | Some(name, _) ->
            return!
                fail
                    "mck.unknown_member"
                    $"The member \"{name}\" is not defined for a \"{event.WireValue}\" matcher at {path}"
        | None ->
            match event with
            | WorkerEvent.Started ->
                let! abi =
                    match optional "abi" found with
                    | None -> Ok None
                    | Some value ->
                        parsed Profile.Parse (child path "abi") value
                        |> Result.map Some

                return EventMatcher.Started abi
            | WorkerEvent.Refused ->
                let! code =
                    match optional "code" found with
                    | None -> Ok None
                    | Some value ->
                        nonEmptyText (child path "code") value
                        |> Result.map Some

                let! includes = optionalList path "messageIncludes" false nonEmptyText found
                return EventMatcher.Refused(code, includes)
            | WorkerEvent.Fault ->
                let! kind =
                    match optional "kind" found with
                    | None -> Ok None
                    | Some value ->
                        parsed FaultKind.Parse (child path "kind") value
                        |> Result.map Some

                return EventMatcher.Fault kind
            | WorkerEvent.Output -> return EventMatcher.Output
    }

    let private pointerCheck (path: string) (element: JsonElement) = result {
        let! found = members "mck.unknown_member" [ "pointer"; "equals"; "type" ] path element
        let! pointerElement = required path "pointer" found
        let! pointer = text (child path "pointer") pointerElement

        if
            pointer
            <> ""
            && not (pointer.StartsWith('/'))
        then
            return! invalid (child path "pointer") "An RFC 6901 pointer is required"
        else
            match optional "equals" found, optional "type" found with
            | Some value, None ->
                let! value = json (child path "equals") value

                return {
                    Pointer = pointer
                    Test = PointerTest.Equals value
                }
            | None, Some value ->
                let! kind = parsed JsonType.Parse (child path "type") value

                return {
                    Pointer = pointer
                    Test = PointerTest.HasType kind
                }
            | _ -> return! invalid path "A pointer check has exactly one of equals and type"
    }

    let private frameMatcher (frameCount: int) (path: string) (element: JsonElement) = result {
        let! found = members "mck.unknown_member" [ "sameAsFrame"; "json"; "pointers" ] path element

        match optional "sameAsFrame" found, optional "json" found, optional "pointers" found with
        | Some value, None, None ->
            let! index = integer (child path "sameAsFrame") 0 value

            if
                index
                >= frameCount
            then
                return!
                    invalid
                        (child path "sameAsFrame")
                        $"The run has {frameCount} frames, so frame {index} does not exist"
            else
                return FrameMatcher.SameAsFrame index
        | None, Some value, None ->
            let! value = json (child path "json") value
            return FrameMatcher.Json value
        | None, None, Some value ->
            let! checks = items (child path "pointers") false pointerCheck value
            return FrameMatcher.Pointers checks
        | _ ->
            return! invalid path "A frame matcher has exactly one of sameAsFrame, json and pointers"
    }

    let private timing (path: string) (element: JsonElement) = result {
        let! found = members "mck.unknown_member" [ "until"; "range" ] path element
        let! untilElement = required path "until" found
        let! until = parsed Until.Parse (child path "until") untilElement
        let! rangeElement = required path "range" found

        if
            rangeElement.ValueKind
            <> JsonValueKind.Array
            || rangeElement.GetArrayLength()
               <> 2
        then
            return! invalid (child path "range") "A range is an array of two numbers"
        else
            let! low = seconds (child path "range/0") rangeElement[0]
            let! high = seconds (child path "range/1") rangeElement[1]

            if low > high then
                return! invalid (child path "range") "A range starts at most where it ends"
            else
                return { Until = until; From = low; To = high }
    }

    let private expectation (frameCount: int) (path: string) (element: JsonElement) = result {
        let! found = members "mck.unknown_matcher" knownMatchers path element

        let! exitCode =
            match optional "exitCode" found with
            | None -> Ok None
            | Some value ->
                integer (child path "exitCode") 0 value
                |> Result.map Some

        let! stdoutBytes =
            match optional "stdoutBytes" found with
            | None -> Ok None
            | Some value ->
                integer (child path "stdoutBytes") 0 value
                |> Result.map Some

        let! stdoutFrames =
            match optional "stdoutFrames" found with
            | None -> Ok None
            | Some value ->
                items (child path "stdoutFrames") true (frameMatcher frameCount) value
                |> Result.map Some

        let! events = optionalList path "events" false eventMatcher found
        let! noEvents = optionalList path "noEvents" false (parsed WorkerEvent.Parse) found
        let! guestOutput = optionalList path "guestOutput" false nonEmptyText found
        let! guestOutputIncludes = optionalList path "guestOutputIncludes" false nonEmptyText found
        let! noGuestOutput = optionalList path "noGuestOutput" false nonEmptyText found

        let! within =
            match optional "withinSeconds" found with
            | None -> Ok None
            | Some value ->
                timing (child path "withinSeconds") value
                |> Result.map Some

        return {
            ExitCode = exitCode
            StdoutBytes = stdoutBytes
            StdoutFrames = stdoutFrames
            Events = events
            NoEvents = noEvents
            GuestOutput = guestOutput
            GuestOutputIncludes = guestOutputIncludes
            NoGuestOutput = noGuestOutput
            WithinSeconds = within
        }
    }

    let private condition (path: string) (element: JsonElement) = result {
        let! found =
            members "mck.unknown_member" [ "bound"; "enforced"; "hostRuns"; "runs" ] path element

        let boolean name (value: JsonElement) =
            match value.ValueKind with
            | JsonValueKind.True -> Ok true
            | JsonValueKind.False -> Ok false
            | _ -> invalid (child path name) "A Boolean is required"

        match
            optional "bound" found,
            optional "enforced" found,
            optional "hostRuns" found,
            optional "runs" found
        with
        | Some bound, Some enforced, None, None ->
            let! bound = parsed Bound.Parse (child path "bound") bound
            let! enforced = boolean "enforced" enforced
            return Condition.Bound(bound, enforced)
        | None, None, Some profile, Some runs ->
            let! profile = parsed Profile.Parse (child path "hostRuns") profile
            let! runs = boolean "runs" runs
            return Condition.HostRuns(profile, runs)
        | _ -> return! invalid path "A condition is {bound, enforced} or {hostRuns, runs}"
    }

    let private run (frames: Map<string, JsonValue>) (path: string) (element: JsonElement) = result {
        let! found =
            members "mck.unknown_member" [ "frames"; "expect"; "alternatives" ] path element

        let! framesElement = required path "frames" found

        let! names =
            items
                (child path "frames")
                true
                (fun framePath frame ->
                    nonEmptyText framePath frame
                    |> Result.bind (fun name ->
                        match Map.tryFind name frames with
                        | Some value -> Ok { Name = name; Value = value }
                        | None ->
                            fail "mck.unknown_value" $"Unknown frame \"{name}\" at {framePath}"
                    )
                )
                framesElement

        let count = names.Length

        match optional "expect" found, optional "alternatives" found with
        | Some value, None ->
            let! expect = expectation count (child path "expect") value

            return {
                Frames = names
                Expect = RunExpectation.Always expect
            }
        | None, Some value ->
            let! alternatives =
                items
                    (child path "alternatives")
                    false
                    (fun alternativePath alternative -> result {
                        let! found =
                            members
                                "mck.unknown_member"
                                [ "when"; "expect" ]
                                alternativePath
                                alternative

                        let! whenElement = required alternativePath "when" found
                        let! condition = condition (child alternativePath "when") whenElement
                        let! expectElement = required alternativePath "expect" found

                        let! expect =
                            expectation count (child alternativePath "expect") expectElement

                        return { When = condition; Expect = expect }
                    })
                    value

            if
                (alternatives
                 |> List.map (fun alternative -> alternative.When)
                 |> List.distinct)
                    .Length
                <> alternatives.Length
            then
                return!
                    invalid (child path "alternatives") "The conditions of the alternatives repeat"
            else
                return {
                    Frames = names
                    Expect = RunExpectation.Alternatives alternatives
                }
        | _ -> return! invalid path "A run has exactly one of expect and alternatives"
    }

    let private worker (path: string) (element: JsonElement) = result {
        let! found =
            members
                "mck.unknown_member"
                [ "requestBytes"; "responseBytes"; "omit"; "buildIdentity" ]
                path
                element

        let! requestBytes =
            match optional "requestBytes" found with
            | None -> Ok None
            | Some value ->
                integer (child path "requestBytes") 1 value
                |> Result.map Some

        let! responseBytes =
            match optional "responseBytes" found with
            | None -> Ok None
            | Some value ->
                integer (child path "responseBytes") 1 value
                |> Result.map Some

        let! omit =
            optionalList
                path
                "omit"
                false
                (fun itemPath item ->
                    nonEmptyText itemPath item
                    |> Result.bind (fun argument ->
                        if Regex.IsMatch(argument, "^--[a-z][a-z0-9-]*$") then
                            Ok argument
                        else
                            invalid itemPath "A worker argument such as --abi is required"
                    )
                )
                found

        let! identity =
            match optional "buildIdentity" found with
            | None -> Ok BuildIdentity.Host
            | Some value ->
                text (child path "buildIdentity") value
                |> Result.bind (
                    function
                    | "other" -> Ok BuildIdentity.Other
                    | other ->
                        fail
                            "mck.unknown_value"
                            $"""Unknown build identity "{other}" at {child path "buildIdentity"}"""
                )

        return {
            RequestBytes = requestBytes
            ResponseBytes = responseBytes
            Omit = omit
            BuildIdentity = identity
        }
    }

    let private case
        (frames: Map<string, JsonValue>)
        (modules: Map<string, ModuleRef>)
        (path: string)
        (element: JsonElement)
        =
        result {
            let! found =
                members
                    "mck.unknown_member"
                    [
                        "caseId"
                        "appliesTo"
                        "module"
                        "declaredAbi"
                        "worker"
                        "bounds"
                        "tests"
                        "runs"
                        "gaps"
                    ]
                    path
                    element

            let! idElement = required path "caseId" found
            let! caseId = text (child path "caseId") idElement

            if not (caseIdPattern.IsMatch caseId) then
                return!
                    invalid
                        (child path "caseId")
                        $"The case id \"{caseId}\" is not a group and a name, such as bounds/trap,"
            else
                let path = $"{path} ({caseId})"
                let! appliesElement = required path "appliesTo" found
                let! appliesTo = parsed Profile.Parse (child path "appliesTo") appliesElement
                let! moduleElement = required path "module" found
                let! moduleName = text (child path "module") moduleElement

                let! moduleRef =
                    match Map.tryFind moduleName modules with
                    | Some found -> Ok found
                    | None ->
                        fail
                            "mck.unknown_value"
                            $"""Unknown module "{moduleName}" at {child path "module"}"""

                let! declaredAbi =
                    match optional "declaredAbi" found with
                    | None -> Ok None
                    | Some value ->
                        parsed Profile.Parse (child path "declaredAbi") value
                        |> Result.map Some

                let! workerSettings =
                    match optional "worker" found with
                    | None -> Ok WorkerSettings.defaults
                    | Some value -> worker (child path "worker") value

                let! bounds = optionalList path "bounds" false (parsed Bound.Parse) found
                let! testsElement = required path "tests" found
                let testsPath = child path "tests"

                let! testsFound =
                    members
                        "mck.unknown_member"
                        [ "requirements"; "scenarios"; "contract" ]
                        testsPath
                        testsElement

                let! requirementsElement = required testsPath "requirements" testsFound

                let! requirements =
                    items (child testsPath "requirements") false nonEmptyText requirementsElement

                let! scenarios = optionalList testsPath "scenarios" false nonEmptyText testsFound
                let! contract = optionalList testsPath "contract" false nonEmptyText testsFound
                let! runsElement = required path "runs" found
                let! runs = items (child path "runs") false (run frames) runsElement

                if
                    runs.Length > 1
                    && runs
                       |> List.exists (fun run ->
                           match run.Expect with
                           | RunExpectation.Alternatives _ -> true
                           | RunExpectation.Always _ -> false
                       )
                then
                    return!
                        fail
                            "mck.unsupported_case"
                            $"""Vocabulary {Suite.VocabularyVersion} writes alternatives only for a case with one run at {child path "runs"}"""
                else
                    let! gaps = optionalList path "gaps" false nonEmptyText found

                    return {
                        CaseId = caseId
                        AppliesTo = appliesTo
                        Module = moduleRef
                        DeclaredAbi = declaredAbi
                        Worker = workerSettings
                        Bounds = bounds
                        Tests = {
                            Requirements = requirements
                            Scenarios = scenarios
                            Contract = contract
                        }
                        Runs = runs
                        Gaps = gaps
                    }
        }

    let private moduleTable (path: string) (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.Object
        then
            invalid path "An object is required"
        else
            element.EnumerateObject()
            |> Seq.map (fun property ->
                let itemPath = child path property.Name

                result {
                    let! found =
                        members
                            "mck.unknown_member"
                            [ "wat"; "wasm"; "sha256" ]
                            itemPath
                            property.Value

                    let! watElement = required itemPath "wat" found
                    let! wat = portable (child itemPath "wat") watElement
                    let! wasmElement = required itemPath "wasm" found
                    let! wasm = portable (child itemPath "wasm") wasmElement
                    let! shaElement = required itemPath "sha256" found
                    let! sha = text (child itemPath "sha256") shaElement

                    if not (sha256Pattern.IsMatch sha) then
                        return!
                            invalid
                                (child itemPath "sha256")
                                "A lowercase hexadecimal SHA-256 is required"
                    else
                        return
                            property.Name,
                            {
                                Name = property.Name
                                Wat = wat
                                Wasm = wasm
                                Sha256 = sha
                            }
                }
            )
            |> Seq.toList
            |> all
            |> Result.map Map.ofList

    let private frameTable (path: string) (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.Object
        then
            invalid path "An object is required"
        else
            element.EnumerateObject()
            |> Seq.map (fun property ->
                json (child path property.Name) property.Value
                |> Result.map (fun value -> property.Name, value)
            )
            |> Seq.toList
            |> all
            |> Result.map Map.ofList

    let private conventions (path: string) (element: JsonElement) = result {
        let! found = members "mck.unknown_convention" knownConventions path element

        let unknown =
            found
            |> Map.toList
            |> List.tryFind (fun (_, value) ->
                value.ValueKind
                <> JsonValueKind.String
            )

        match unknown with
        | Some(name, _) -> return! invalid (child path name) "A convention is a string"
        | None ->
            match
                knownConventions
                |> List.tryFind (fun name -> not (Map.containsKey name found))
            with
            | Some name ->
                return!
                    fail
                        "mck.unknown_convention"
                        $"The convention \"{name}\" that this runner implements is missing at {path}; the corpus defines its matchers otherwise"
            | None -> return ()
    }

    let private kitOf (root: JsonElement) = result {
        let! found =
            members
                "mck.unknown_member"
                [
                    "kit"
                    "version"
                    "contract"
                    "assembler"
                    "deadlineMarginSeconds"
                    "conventions"
                    "frames"
                    "modules"
                    "withoutCase"
                    "cases"
                ]
                ""
                root

        let! kitElement = required "" "kit" found
        let! kitName = text "/kit" kitElement
        let! versionElement = required "" "version" found
        let! version = text "/version" versionElement

        if
            kitName
            <> Suite.Kit
            || version
               <> Suite.KitVersion
        then
            return!
                fail
                    "mck.kit_version"
                    $"The runner implements kit {Suite.Kit} {Suite.KitVersion}; the corpus is {kitName} {version}"
        else
            let! contractElement = required "" "contract" found

            let! contractFound =
                members "mck.unknown_member" [ "id"; "version"; "path" ] "/contract" contractElement

            let! contractIdElement = required "/contract" "id" contractFound
            let! contractId = nonEmptyText "/contract/id" contractIdElement
            let! contractVersionElement = required "/contract" "version" contractFound
            let! contractVersion = nonEmptyText "/contract/version" contractVersionElement
            let! contractPathElement = required "/contract" "path" contractFound
            let! contractPath = portable "/contract/path" contractPathElement
            let! assemblerElement = required "" "assembler" found

            let! assemblerFound =
                members
                    "mck.unknown_member"
                    [ "package"; "version"; "features" ]
                    "/assembler"
                    assemblerElement

            let! packageElement = required "/assembler" "package" assemblerFound
            let! _ = nonEmptyText "/assembler/package" packageElement
            let! assemblerVersionElement = required "/assembler" "version" assemblerFound
            let! _ = nonEmptyText "/assembler/version" assemblerVersionElement
            let! _ = optionalList "/assembler" "features" true nonEmptyText assemblerFound
            let! marginElement = required "" "deadlineMarginSeconds" found
            let! margin = seconds "/deadlineMarginSeconds" marginElement
            let! conventionsElement = required "" "conventions" found
            do! conventions "/conventions" conventionsElement
            let! framesElement = required "" "frames" found
            let! frames = frameTable "/frames" framesElement
            let! modulesElement = required "" "modules" found
            let! modules = moduleTable "/modules" modulesElement
            let! withoutElement = required "" "withoutCase" found

            let! _ =
                items
                    "/withoutCase"
                    true
                    (fun itemPath item -> result {
                        let! entry =
                            members "mck.unknown_member" [ "element"; "reason" ] itemPath item

                        let! element = required itemPath "element" entry
                        let! _ = nonEmptyText (child itemPath "element") element
                        let! reason = required itemPath "reason" entry
                        return! nonEmptyText (child itemPath "reason") reason
                    })
                    withoutElement

            let! casesElement = required "" "cases" found

            if
                casesElement.ValueKind
                <> JsonValueKind.Array
            then
                return! invalid "/cases" "An array is required"
            else
                let identity = {
                    Kit = kitName
                    Version = version
                    ContractId = contractId
                    ContractVersion = contractVersion
                    ContractPath = contractPath
                    DeadlineMarginSeconds = margin
                }

                let cases =
                    casesElement.EnumerateArray()
                    |> Seq.mapi (fun index item ->
                        case frames modules (child "/cases" (string index)) item
                    )
                    |> Seq.toList

                return identity, cases
    }

    let private kit (bytes: byte array) =
        let parsed =
            try
                Ok(JsonDocument.Parse(ReadOnlyMemory bytes))
            with :? JsonException as error ->
                fail "mck.invalid_json" error.Message

        match parsed with
        | Error failure -> Error failure
        | Ok document ->
            try
                kitOf document.RootElement
            finally
                document.Dispose()

    /// The lowercase hexadecimal SHA-256 of bytes.
    let sha256 (bytes: byte array) =
        SHA256.HashData bytes
        |> Convert.ToHexStringLower

    /// Reads the finos/morphir commit of the pin from its provenance record, the line
    /// Commit: `<40 hexadecimal digits>`.
    let commitOfProvenance (text: string) : Result<string, string> =
        let found =
            Regex.Matches(text, "^Commit: `([0-9a-f]{40})`\\s*$", RegexOptions.Multiline)

        if found.Count = 1 then
            Ok found[0].Groups[1].Value
        else
            Error "The provenance record needs exactly one line Commit: `<40 hexadecimal digits>`"

    /// Reads the corpus bytes. corpus is the corpus's repository-relative path and commit the
    /// finos/morphir commit it was copied from; both go into the pin, with the SHA-256 of the bytes.
    let read
        (corpus: string)
        (commit: string)
        (bytes: byte array)
        : Result<WasmHostCorpus, CorpusIssue list> =
        let issue (failure: Failure) = {
            Code = failure.Code
            Path = corpus
            Line = 0
            Message = failure.Message
        }

        let pin = {
            Corpus = corpus
            Commit = commit
            Sha256 = sha256 bytes
        }

        if not (Regex.IsMatch(commit, "^[0-9a-f]{40}$")) then
            Error [
                issue
                    {
                        Code = "mck.invalid_pin"
                        Message = $"The pin's commit \"{commit}\" is not 40 hexadecimal digits"
                    }
            ]
        else
            match kit bytes with
            | Error failure -> Error [ issue failure ]
            | Ok(identity, cases) ->
                let failures =
                    cases
                    |> List.choose (
                        function
                        | Error failure -> Some(issue failure)
                        | Ok _ -> None
                    )

                let read =
                    cases
                    |> List.choose Result.toOption

                let repeated =
                    read
                    |> List.countBy (fun case -> case.CaseId)
                    |> List.filter (fun (_, count) -> count > 1)
                    |> List.map (fun (caseId, _) ->
                        issue
                            {
                                Code = "mck.duplicate_id"
                                Message = $"The case id \"{caseId}\" is repeated"
                            }
                    )

                match
                    failures
                    @ repeated
                with
                | [] ->
                    Ok
                        {
                            Pin = pin
                            Kit = identity
                            Cases = read
                        }
                | issues -> Error issues

    /// Reads the pinned corpus of a repository checkout: the corpus at DefaultCorpusPath and
    /// the commit from DefaultProvenancePath.
    let load (repositoryRoot: string) : Result<WasmHostCorpus, CorpusIssue list> =
        let corpusFile = Path.Combine(repositoryRoot, DefaultCorpusPath)
        let provenanceFile = Path.Combine(repositoryRoot, DefaultProvenancePath)

        match commitOfProvenance (File.ReadAllText provenanceFile) with
        | Error message ->
            Error [
                {
                    Code = "mck.invalid_pin"
                    Path = DefaultProvenancePath
                    Line = 0
                    Message = message
                }
            ]
        | Ok commit -> read DefaultCorpusPath commit (File.ReadAllBytes corpusFile)
