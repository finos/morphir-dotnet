// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The schemas and the readers agree on which members are required, and the members a document may omit are exactly
/// the ones the contract pages say are optional. For every valid document case, each object member is removed in turn,
/// at every depth.
/// <list type="bullet">
/// <item>The members a document may omit are decided by the JSON Schema alone, never by what the reader accepts, and
/// must be exactly <see cref="optionalMembers"/>, the list the pages state.</item>
/// <item>A member the schema requires must be refused by the reader, and a member whose absence the reader refuses as
/// missing (<c>invalid_document</c> at that member) must be required by the schema. Other refusals of a removal, such
/// as a consistency rule the schema does not express, are not part of this check.</item>
/// </list>
/// This replaces finos/morphir's <c>required_members.rs</c> (crates/morphir-toolchain-contract, at
/// <c>dde74f94</c>), which ran the same removals over its own reader.
/// </summary>
module Morphir.Toolchain.Tests.RequiredMemberTests

open System
open System.Text
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest

/// <summary>The members a valid fixture may omit, by contract, in critical-path spelling: copied without change from
/// the <c>OPTIONAL</c> list of finos/morphir's <c>required_members.rs</c> at <c>dde74f94</c>, which the pages state.
/// <c>platforms.*</c> entries are map entries of a lock or recipe that lists several platforms. <c>futureSection</c>,
/// <c>host.mirrorHint</c>, <c>plan.keyOrder</c> and the lock's <c>weight</c> are unknown members some fixtures carry on
/// purpose. A member that becomes optional, or stops being optional, changes this list and its page together.</summary>
let optionalMembers =
    let decomposedE = "e" + string (char 0x301)
    let halfwidthKa = string (char 0xFF76)
    let gClef = Char.ConvertFromUtf32 0x1D11E

    set
        [ "lock: critical"
          "lock: platforms.*"
          "lock: weight"
          "recipe: critical"
          "recipe: hosts.builtIns"
          "recipe: hosts.platforms.*"
          "recipe: hosts.prerequisites"
          "recipe: hosts.prerequisites.platforms"
          "recipe: hosts.prerequisites.versions"
          "recipe: hosts.wasmAbis"
          "recipe: providers.platforms.*"
          "recipe: providers.prerequisites"
          "recipe: providers.prerequisites.platforms"
          "recipe: providers.prerequisites.versions"
          "request: critical"
          "request: futureSection"
          "request: futureSection.enabled"
          "request: host.mirrorHint"
          "request: platforms"
          "request: toolset.providers"
          "request: toolset.providers.abi"
          "request: toolset.providers.delivery"
          "request: toolset.providers.extensionId"
          "result: diagnostics.alternatives"
          "result: diagnostics.cause"
          "result: diagnostics.alternatives.deployment"
          "result: diagnostics.alternatives.implementation"
          "result: diagnostics.alternatives.platform"
          "result: diagnostics.instruction"
          "result: diagnostics.mirror"
          "result: diagnostics.role"
          "result: plan.components.prerequisites.versions"
          "result: plan.keyOrder"
          "result: plan.keyOrder.Z"
          "result: plan.keyOrder.a"
          "result: plan.keyOrder.a!"
          // The decomposed e followed by U+0301.
          $"result: plan.keyOrder.{decomposedE}"
          $"result: plan.keyOrder.{halfwidthKa}"
          $"result: plan.keyOrder.{gClef}"
          // The key pairs the reference TUF encoder orders differently.
          "result: plan.keyOrderQuote"
          "result: plan.keyOrderQuote.a\""
          "result: plan.keyOrderQuote.a#"
          "result: plan.keyOrderSpace"
          "result: plan.keyOrderSpace.a"
          "result: plan.keyOrderSpace.a b"
          "result: selection"
          "result: selection.lockSha256" ]

type private Step =
    | Key of string
    | Index of int

/// Every object member's path, as steps from the root.
let rec private memberPaths (value: Value) (prefix: Step list) : Step list list =
    match value with
    | Value.Object members ->
        [ for name, item in members do
              let path = prefix @ [ Key name ]
              yield path
              yield! memberPaths item path ]
    | Value.Array items ->
        [ for index, item in List.indexed items do
              yield! memberPaths item (prefix @ [ Index index ]) ]
    | _ -> []

/// The reader's spelling of a member path.
let private spelled (steps: Step list) =
    let text = StringBuilder()

    for step in steps do
        match step with
        | Key name when text.Length = 0 -> text.Append name |> ignore
        | Key name -> text.Append('.').Append name |> ignore
        | Index index -> text.Append($"[{index}]") |> ignore

    text.ToString()

/// The critical-path spelling of a member: array elements are named through their array, and platform keys by *.
let private generic (steps: Step list) =
    let names = ResizeArray<string>()

    for step in steps do
        match step with
        | Key name ->
            let keyed = names.Count > 0 && names[names.Count - 1] = "platforms" && Result.isOk (Platform.tryParse name)
            names.Add(if keyed then "*" else name)
        | Index _ -> ()

    String.Join(".", names)

/// The value with the object member at the path removed.
let rec private without (steps: Step list) (value: Value) =
    match steps, value with
    | [ Key name ], Value.Object members -> Value.Object(members |> List.filter (fst >> (<>) name))
    | Key name :: rest, Value.Object members -> Value.Object(members |> List.map (fun (n, item) -> if n = name then n, without rest item else n, item))
    | Index index :: rest, Value.Array items -> Value.Array(items |> List.mapi (fun i item -> if i = index then without rest item else item))
    | _ -> failwithf "no member at %A" steps

/// TOML text for a request value: each top-level member on its own line, tables and arrays inline. Only the request's
/// own value types occur: strings, integers, booleans, arrays and tables.
let private toml (value: Value) =
    let escape (s: string) =
        let text = StringBuilder()

        for c in s do
            match c with
            | '"' -> text.Append "\\\"" |> ignore
            | '\\' -> text.Append "\\\\" |> ignore
            | c when c < ' ' || c = char 0x7F -> text.Append($"\\u{int c:X4}") |> ignore
            | c -> text.Append c |> ignore

        text.ToString()

    let key (name: string) =
        if name.Length > 0 && name |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_' || c = '-') then
            name
        else
            "\"" + escape name + "\""

    let rec inline' (value: Value) =
        match value with
        | Value.String s -> "\"" + escape s + "\""
        | Value.Number spelling -> spelling
        | Value.Boolean true -> "true"
        | Value.Boolean false -> "false"
        | Value.Array items -> "[" + (items |> List.map inline' |> String.concat ", ") + "]"
        | Value.Object [] -> "{}"
        | Value.Object members -> "{ " + (members |> List.map (fun (name, item) -> key name + " = " + inline' item) |> String.concat ", ") + " }"
        | other -> failwithf "a request holds no %A" other

    match value with
    | Value.Object members -> members |> List.map (fun (name, item) -> key name + " = " + inline' item + "\n") |> String.concat "" |> Encoding.UTF8.GetBytes
    | _ -> failwith "a request is a table"

let private instance (contract: string) (bytes: byte array) =
    let parsed = if contract = "request" then Syntax.parseToml bytes else Syntax.parseJson bytes

    match parsed with
    | Ok value -> value
    | Error refusal -> failtestf "a valid %s does not parse: %A" contract refusal

let private encode (contract: string) (value: Value) =
    if contract = "request" then toml value else JsonText.toBytes value

let private read (contract: string) (bytes: byte array) : Result<unit, ReaderCode * string> =
    let refusal (result: Result<'T, ReaderRefusal>) =
        result |> Result.map ignore |> Result.mapError (fun r -> r.Code, DocumentPath.render r.Path)

    match contract with
    | "recipe" -> refusal (RecipeDocument.read bytes)
    | "request" -> refusal (RequestDocument.read bytes)
    | "lock" -> refusal (LockDocument.read bytes)
    | "receipt" -> refusal (ReceiptDocument.read bytes)
    | "result" -> refusal (ResultDocument.read bytes)
    | other -> failwithf "no reader for %s" other

/// A plan member's removal changes the plan's digest, so it is computed again: only the removal is judged.
let private withPlanDigest (steps: Step list) (value: Value) =
    match steps, value with
    | Key "plan" :: _, Value.Object members ->
        match members |> List.tryFind (fst >> (=) "plan") |> Option.map (snd >> Canonical.bytes) with
        | Some(Ok bytes) ->
            Value.Object(members |> List.map (fun (name, item) -> if name = "planSha256" then name, Value.String (Sha256.ofBytes bytes).Text else name, item))
        | _ -> value
    | _ -> value

/// The schema-optional members, the disagreements between schema and reader, and how many removals were judged.
let sweep () =
    let failures = ResizeArray<string>()
    let optional = Collections.Generic.HashSet<string>()
    let present = Collections.Generic.HashSet<string>()
    let mutable checkedRemovals = 0

    for case in documents () do
        if case.Expected = Valid then
            let original = instance case.Contract (Fixtures.bytes case.Document)

            for steps in memberPaths original [] do
                // plan.lock is a complete lock document: the lock schema and reader govern its members, and the lock
                // fixtures sweep them.
                if not ((generic steps).StartsWith("plan.lock", StringComparison.Ordinal)) then
                    present.Add $"{case.Contract}: {generic steps}" |> ignore
                    let changed = without steps original |> withPlanDigest steps
                    let path = spelled steps
                    let outcome = read case.Contract (encode case.Contract changed)
                    let requiredBySchema = not (fst (SchemaTests.validates case.Contract changed))
                    checkedRemovals <- checkedRemovals + 1

                    match outcome with
                    | Error(ReaderCode.InvalidDocument, at) when at = path && not requiredBySchema ->
                        failures.Add $"{case.Document}: the reader requires {path}, the schema does not"
                    | Ok() when requiredBySchema -> failures.Add $"{case.Document}: the schema requires {path}, the reader does not"
                    | _ -> ()

                    if not requiredBySchema then
                        optional.Add $"{case.Contract}: {generic steps}" |> ignore

    set optional, set present, (failures |> Seq.distinct |> Seq.sort |> List.ofSeq), checkedRemovals

[<Tests>]
let tests =
    testList "Required and optional members" [
        testCase "the schemas and readers require the same members, and only the pages' members are optional" (fun () ->
            let optional, present, failures, checkedRemovals = sweep ()
            Expect.isGreaterThan checkedRemovals 1000 "removals judged"
            Expect.isEmpty failures $"{failures.Length} disagreements between schema and reader"
            Expect.isEmpty (Set.difference optional optionalMembers) "members the schema now lets a document omit"

            // The list states the corpus at dde74f94. A member no valid fixture of this corpus carries cannot be judged:
            // the pin predates some of those fixtures, but an external corpus must carry every one, as the Rust test
            // required.
            let absent = Set.difference optionalMembers present
            Expect.isEmpty (Set.difference (Set.intersect optionalMembers present) optional) "members the schema no longer lets a document omit"

            match Fixtures.external with
            | Ok(Some _) -> Expect.isEmpty absent "listed optional members that no valid fixture carries"
            | _ -> ())
    ]
