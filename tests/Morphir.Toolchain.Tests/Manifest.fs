// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// A strict reader for the contract case manifests. Every member and every kind it meets must be one it knows, so a
/// re-pin that adds a case shape this suite does not run fails instead of being skipped.
/// </summary>
module Morphir.Toolchain.Tests.Manifest

open System.Text.Json

/// An expected outcome: valid, or a refusal with an exact code and path.
type Expected =
    | Valid
    | Refused of code: string * path: string

/// The members a resolution case expects of each selected provider, by name.
type ExpectedProvider = (string * string) list

type ResolveExpected =
    | Providers of hostTarget: string option * providers: ExpectedProvider list
    | ResolveRefused of code: string * path: string

type DocumentCase =
    { Contract: string
      Document: string
      Expected: Expected
      Schema: string option
      SchemaGap: string option
      Rule: string option }

type BindingCase =
    | LockRequest of name: string * lock: string * request: string * expected: Expected
    | LockPlatform of name: string * lock: string * platform: string * expected: Expected
    | ReceiptLock of name: string * receipt: string * lock: string * expected: Expected
    | Resolve of name: string * recipe: string * request: string * platform: string * expected: ResolveExpected
    | PlanCanonical of name: string * result: string * canonical: string * expected: Expected
    /// A refused case may name the encoding change that is the likely cause.
    | LockRequestBytes of name: string * lock: string * request: string * expected: Expected * cause: string option
    | PlanLock of name: string * result: string * lock: string * expected: Expected
    /// Files under one synthetic root, and the request discovery finds from start, if any.
    | Discover of name: string * files: string list * boundary: string * start: string * request: string option
    /// Whether a MEP version is on the supported 0.2 line.
    | MepVersion of name: string * version: string * expected: Expected

    member this.Name =
        match this with
        | LockRequest(name, _, _, _)
        | LockPlatform(name, _, _, _)
        | ReceiptLock(name, _, _, _)
        | Resolve(name, _, _, _, _)
        | PlanCanonical(name, _, _, _)
        | LockRequestBytes(name, _, _, _, _)
        | PlanLock(name, _, _, _)
        | Discover(name, _, _, _, _)
        | MepVersion(name, _, _) -> name

    member this.Kind =
        match this with
        | LockRequest _ -> "lock-request"
        | LockPlatform _ -> "lock-platform"
        | ReceiptLock _ -> "receipt-lock"
        | Resolve _ -> "resolve"
        | PlanCanonical _ -> "plan-canonical"
        | LockRequestBytes _ -> "lock-request-bytes"
        | PlanLock _ -> "plan-lock"
        | Discover _ -> "discover"
        | MepVersion _ -> "mep-version"

let private members (element: JsonElement) =
    element.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Set.ofSeq

/// Fails when an object has a member outside <paramref name="known"/>.
let requireKnown (where: string) (known: string list) (element: JsonElement) =
    let unknown = Set.difference (members element) (Set.ofList known)

    if not unknown.IsEmpty then
        failwithf "%s has members this suite does not understand: %A" where (Set.toList unknown)

let str (element: JsonElement) (name: string) =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> value.GetString() |> nonNull
    | _ -> failwithf "missing string member %s" name

let optStr (element: JsonElement) (name: string) =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Some(nonNull (value.GetString()))
    | true, _ -> failwithf "member %s is not a string" name
    | false, _ -> None

let expected (where: string) (element: JsonElement) =
    let value = element.GetProperty "expected"

    match value.ValueKind with
    | JsonValueKind.String when value.GetString() = "valid" -> Valid
    | JsonValueKind.Object ->
        requireKnown $"{where} expected" [ "code"; "path" ] value
        Refused(str value "code", str value "path")
    | _ -> failwithf "%s has an expected outcome this suite does not understand" where

let expectedProviders (where: string) (value: JsonElement) =
    [ for provider in value.GetProperty("providers").EnumerateArray() do
          requireKnown $"{where} provider" [ "operation"; "language"; "target"; "delivery"; "extensionId"; "version" ] provider
          [ for p in provider.EnumerateObject() -> p.Name, nonNull (p.Value.GetString()) ] ]

let resolveExpected (where: string) (element: JsonElement) =
    let value = element.GetProperty "expected"

    if value.TryGetProperty("providers") |> fst then
        requireKnown $"{where} expected" [ "providers"; "hostTarget" ] value
        Providers(optStr value "hostTarget", expectedProviders where value)
    else
        requireKnown $"{where} expected" [ "code"; "path" ] value
        ResolveRefused(str value "code", str value "path")

let private load () =
    JsonDocument.Parse(Fixtures.bytes "cases.json")

/// The unsigned manifest's document cases.
let documents () : DocumentCase list =
    use manifest = load ()
    requireKnown "cases.json" [ "contractVersion"; "documents"; "bindings" ] manifest.RootElement

    if str manifest.RootElement "contractVersion" <> "0.1.0-draft.1" then
        failwith "cases.json is for another contract version"

    [ for case in manifest.RootElement.GetProperty("documents").EnumerateArray() do
          let document = str case "document"
          requireKnown document [ "contract"; "document"; "expected"; "schema"; "schemaGap"; "rule" ] case

          { Contract = str case "contract"
            Document = document
            Expected = expected document case
            Schema = optStr case "schema"
            SchemaGap = optStr case "schemaGap"
            Rule = optStr case "rule" } ]

/// The unsigned manifest's binding cases.
let bindings () : BindingCase list =
    use manifest = load ()

    [ for case in manifest.RootElement.GetProperty("bindings").EnumerateArray() do
          let name = str case "name"
          let common = [ "name"; "kind"; "rule"; "expected" ]

          match str case "kind" with
          | "lock-request" ->
              requireKnown name (common @ [ "lock"; "request" ]) case
              LockRequest(name, str case "lock", str case "request", expected name case)
          | "lock-platform" ->
              requireKnown name (common @ [ "lock"; "platform" ]) case
              LockPlatform(name, str case "lock", str case "platform", expected name case)
          | "receipt-lock" ->
              requireKnown name (common @ [ "receipt"; "lock" ]) case
              ReceiptLock(name, str case "receipt", str case "lock", expected name case)
          | "resolve" ->
              requireKnown name (common @ [ "recipe"; "request"; "platform" ]) case
              Resolve(name, str case "recipe", str case "request", str case "platform", resolveExpected name case)
          | "plan-canonical" ->
              requireKnown name (common @ [ "result"; "canonical" ]) case
              PlanCanonical(name, str case "result", str case "canonical", expected name case)
          | "lock-request-bytes" ->
              requireKnown name (common @ [ "lock"; "request" ]) case
              let value = case.GetProperty "expected"

              let cause =
                  if value.ValueKind = JsonValueKind.Object then
                      requireKnown $"{name} expected" [ "code"; "path"; "cause" ] value
                      optStr value "cause"
                  else
                      None

              let outcome =
                  if value.ValueKind = JsonValueKind.Object then Refused(str value "code", str value "path") else expected name case

              LockRequestBytes(name, str case "lock", str case "request", outcome, cause)
          | "plan-lock" ->
              requireKnown name (common @ [ "result"; "lock" ]) case
              PlanLock(name, str case "result", str case "lock", expected name case)
          | "discover" ->
              requireKnown name (common @ [ "files"; "boundary"; "start" ]) case
              let value = case.GetProperty "expected"
              requireKnown $"{name} expected" [ "request" ] value
              let request = value.GetProperty "request"

              Discover(
                  name,
                  [ for file in case.GetProperty("files").EnumerateArray() -> nonNull (file.GetString()) ],
                  str case "boundary",
                  str case "start",
                  (if request.ValueKind = JsonValueKind.Null then None else Some(nonNull (request.GetString())))
              )
          | "mep-version" ->
              requireKnown name (common @ [ "version" ]) case
              MepVersion(name, str case "version", expected name case)
          | other -> failwithf "binding %s has kind %s, which this suite does not run" name other ]
