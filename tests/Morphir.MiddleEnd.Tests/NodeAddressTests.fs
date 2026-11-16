// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.NodeAddressTests

open System
open System.IO
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.MiddleEnd

// ----- the upstream fixture, cases that need no resolver -----

let private fixture =
    let path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NodeAddresses", "node-addresses-draft.json")
    JsonDocument.Parse(File.ReadAllText path)

let private uriCases = [ for item in fixture.RootElement.GetProperty("uriCases").EnumerateArray() -> item ]

let private text (element: JsonElement) (name: string) = element.GetProperty(name).GetString() |> string

/// The name text of a step of the fixture and of an address, in one comparable text form.
let private describeStep (step: NodeStep) =
    match step with
    | NodeStep.TypeExpression -> "type-expression"
    | NodeStep.RecordField name -> "record-field:" + CanonicalNames.nameToString name
    | NodeStep.TupleElement index -> $"tuple-element:{index}"
    | NodeStep.Body -> "body"
    | NodeStep.ApplyArgument -> "apply-argument"
    | other -> failtest $"The fixture has a step that the test does not describe: {other}"

let private describeExpectedStep (step: JsonElement) =
    match text step "kind" with
    | "type-expression" -> "type-expression"
    | "record-field" -> "record-field:" + text step "name"
    | "tuple-element" -> "tuple-element:" + string (step.GetProperty("index").GetInt32())
    | "body" -> "body"
    | "apply-argument" -> "apply-argument"
    | other -> failtest $"The fixture has a step kind that the test does not describe: {other}"

let private pathText (PackageName path) = CanonicalNames.pathToString path

let private describeOwner (owner: NodeOwner) =
    match owner with
    | NodeOwner.OwnPackage -> "own-package"
    | NodeOwner.Dependency package -> "dependency:" + pathText package

let private describeExpectedOwner (owner: JsonElement) =
    match text owner "kind" with
    | "own-package" -> "own-package"
    | "dependency" -> "dependency:" + text owner "packagePath"
    | other -> failtest $"owner {other}"

let private describeRoot (root: NodeRoot) =
    match root with
    | NodeRoot.Distribution -> "distribution"
    | NodeRoot.Package -> "package"
    | NodeRoot.Dependency package -> "dependency:" + pathText package
    | NodeRoot.EntryPoint name -> "entry-point:" + name
    | NodeRoot.Module(owner, modulePath) -> $"module:{describeOwner owner}:{CanonicalNames.pathToString modulePath}"
    | NodeRoot.Type(owner, modulePath, name) ->
        $"type:{describeOwner owner}:{CanonicalNames.pathToString modulePath}:{CanonicalNames.nameToString name}"
    | NodeRoot.Value(owner, modulePath, name) ->
        $"value:{describeOwner owner}:{CanonicalNames.pathToString modulePath}:{CanonicalNames.nameToString name}"

let private describeExpectedRoot (root: JsonElement) =
    let owner () = describeExpectedOwner (root.GetProperty "owner")

    match text root "kind" with
    | "distribution" -> "distribution"
    | "package" -> "package"
    | "dependency" -> "dependency:" + text root "packagePath"
    | "entry-point" -> "entry-point:" + text root "name"
    | "module" -> "module:" + owner () + ":" + text root "module"
    | "type" -> "type:" + owner () + ":" + text root "module" + ":" + text root "name"
    | "value" -> "value:" + owner () + ":" + text root "module" + ":" + text root "name"
    | other -> failtest $"root {other}"

let private describeArtifact (artifact: ArtifactSelector) =
    match artifact with
    | ArtifactSelector.Package package -> "package:" + pathText package
    | ArtifactSelector.Workspace alias -> "workspace:" + alias

let private describeExpectedArtifact (artifact: JsonElement) =
    match text artifact "kind" with
    | "package" -> "package:" + text artifact "packagePath"
    | "workspace" -> "workspace:" + text artifact "alias"
    | other -> failtest $"artifact {other}"

let private describeRevision (revision: ArtifactRevision) =
    match revision with
    | ArtifactRevision.Current -> "current"
    | ArtifactRevision.Pinned digest -> "pinned:" + digest.Hex

let private describeExpectedRevision (revision: JsonElement) =
    match text revision "kind" with
    | "current" -> "current"
    | "pinned" -> "pinned:" + text revision "digest"
    | other -> failtest $"revision {other}"

let private describe (address: NodeAddress) =
    String.Join(
        " | ",
        [ describeArtifact address.Artifact
          string address.Format
          describeRoot address.Root
          String.Join(",", address.Steps |> List.map describeStep)
          describeRevision address.Revision
          (match address.Guard with
           | Some digest -> digest.Hex
           | None -> "none") ]
    )

let private describeExpected (expected: JsonElement) =
    String.Join(
        " | ",
        [ describeExpectedArtifact (expected.GetProperty "artifact")
          text expected "formatVersion"
          describeExpectedRoot (expected.GetProperty "root")
          String.Join(",", [ for step in expected.GetProperty("steps").EnumerateArray() -> describeExpectedStep step ])
          describeExpectedRevision (expected.GetProperty "revision")
          (match expected.GetProperty("guard").ValueKind with
           | JsonValueKind.Null -> "none"
           | _ -> text (expected.GetProperty "guard") "digest") ]
    )

[<Tests>]
let upstreamCases =
    testList
        "node address: the cases of the upstream draft fixture"
        [ test "the fixture has the 29 cases that the tests expect" { Expect.equal uriCases.Length 29 "uriCases" }

          testList
              "addresses that parse and write back to their canonical spelling"
              [ for case in uriCases do
                    if case.TryGetProperty("expectedAddress") |> fst then
                        yield
                            test (text case "id" + ": " + text case "purpose") {
                                let uri = text case "uri"

                                match NodeAddress.parse uri with
                                | Error error -> failtest $"Expected an address, got {error}"
                                | Ok address ->
                                    Expect.equal (describe address) (describeExpected (case.GetProperty "expectedAddress")) "address"
                                    Expect.equal (NodeAddress.toUri address) (text case "expectedCanonicalUri") "canonical URI"
                                    Expect.equal (NodeAddress.toUri address) uri "the input is canonical"
                            } ]

          testList
              "texts that are not node URIs"
              [ for case in uriCases do
                    if case.TryGetProperty("expectedError") |> fst then
                        yield
                            test (text case "id" + ": " + text case "purpose") {
                                match NodeAddress.parse (text case "uri") with
                                | Ok address -> failtest $"Expected invalid_node_uri, got {describe address}"
                                | Error _ -> ()
                            } ]

          test "an unpinned ordered step without a guard is the missing guard error" {
              let uri = "morphir://ir/pkg/acme/orders?format=4.0.0#/module/domain/type/pair/type-exp/tuple/element/1"
              Expect.equal (NodeAddress.parse uri) (Error NodeUriError.MissingGuard) "missing guard"
          } ]

// ----- the role table -----

let private digest =
    match Sha256Digest.Parse("sha256:" + String('a', 64)) with
    | Ok digest -> digest
    | Error message -> failwith message

let private name (text: string) =
    match CanonicalNames.parseName text with
    | Ok name -> name
    | Error message -> failwith message

let private roles: (NodeStep * string) list = [
    NodeStep.TypeExpression, "type-exp"
    NodeStep.Body, "body"
    NodeStep.ValueInputType(name "amount"), "input/amount"
    NodeStep.ValueInputAnnotation(name "amount"), "input-annotation/amount"
    NodeStep.ValueOutputType, "output"
    NodeStep.DerivedBaseType, "derived/base-type"
    NodeStep.PartialTypeExpression, "incomplete/partial-type"
    NodeStep.CustomConstructor(name "quote"), "constructor/quote"
    NodeStep.ConstructorArgument 2, "argument/2"
    NodeStep.RecordField(name "customer-id"), "record/field/customer-id"
    NodeStep.ExtensibleRecordField(name "customer-id"), "extensible-record/field/customer-id"
    NodeStep.TypeFunctionParameter, "function/parameter"
    NodeStep.TypeFunctionResult, "function/result"
    NodeStep.ReferenceArgument 1, "reference/argument/1"
    NodeStep.ApplyFunction, "apply/function"
    NodeStep.ApplyArgument, "apply/argument"
    NodeStep.FieldSubject, "field/subject"
    NodeStep.DestructurePattern, "destructure/pattern"
    NodeStep.DestructureValue, "destructure/value"
    NodeStep.DestructureBody, "destructure/body"
    NodeStep.IfCondition, "if/condition"
    NodeStep.IfThen, "if/then"
    NodeStep.IfElse, "if/else"
    NodeStep.LambdaPattern, "lambda/pattern"
    NodeStep.LambdaBody, "lambda/body"
    NodeStep.LetDefinition(name "loop"), "let/definition/loop"
    NodeStep.LetBody, "let/body"
    NodeStep.ListElement 0, "list/element/0"
    NodeStep.PatternMatchSubject, "pattern-match/subject"
    NodeStep.TupleElement 1, "tuple/element/1"
    NodeStep.PatternMatchCasePattern 3, "pattern-match/case/3/pattern"
    NodeStep.PatternMatchCaseBody 3, "pattern-match/case/3/body"
    NodeStep.UpdateSubject, "update/subject"
    NodeStep.UpdateField(name "x"), "update/field/x"
    NodeStep.AsPatternChild, "as-pattern/pattern"
    NodeStep.PatternTupleElement 0, "tuple-pattern/element/0"
    NodeStep.PatternConstructorArgument 1, "constructor-pattern/argument/1"
    NodeStep.HeadTailHead, "head-tail/head"
    NodeStep.HeadTailTail, "head-tail/tail"
    NodeStep.ExternalFallback, "external/fallback"
    NodeStep.IncompletePartialBody, "incomplete/partial-body"
    NodeStep.HoleExpectedType, "hole/expected-type"
    NodeStep.InferredType, "inferred-type"
]

let private package =
    match CanonicalNames.parsePath "acme/orders" with
    | Ok path -> PackageName path
    | Error message -> failwith message

let private format = { Major = 4; Minor = 0; Patch = 0 }

let private valueRoot =
    NodeRoot.Value(NodeOwner.OwnPackage, (match CanonicalNames.parsePath "domain" with Ok p -> p | Error m -> failwith m), name "total")

let private pinned steps =
    NodeAddress.create (ArtifactSelector.Package package) format valueRoot steps (ArtifactRevision.Pinned digest) None

[<Tests>]
let roleTable =
    let perStep =
        [ for step, fragment in roles ->
              test $"the step {fragment} is written and read back" {
                  match pinned [ step ] with
                  | Error error -> failtest $"Expected an address, got {error}"
                  | Ok address ->
                      let uri = NodeAddress.toUri address
                      Expect.isTrue (uri.EndsWith("/value/total/" + fragment)) $"{uri} ends with the fragment"
                      Expect.equal (NodeAddress.parse uri |> Result.toOption) (Some address) "round trip"
              } ]

    let others =
        [ test "an annotation argument follows its entry" {
              match pinned [ NodeStep.AnnotationEntry 1; NodeStep.AnnotationArgument 0 ] with
              | Error error -> failtest $"{error}"
              | Ok address ->
                  let uri = NodeAddress.toUri address
                  Expect.isTrue (uri.EndsWith("/annotation/entry/1/argument/0")) uri
                  Expect.equal (NodeAddress.parse uri |> Result.toOption) (Some address) "round trip"
          }

          test "a step that is positional is the ordered children and only those" {
              let positional = roles |> List.filter (fun (step, _) -> step.IsPositional) |> List.map snd

              Expect.equal
                  positional
                  [ "argument/2"
                    "reference/argument/1"
                    "list/element/0"
                    "tuple/element/1"
                    "pattern-match/case/3/pattern"
                    "pattern-match/case/3/body"
                    "tuple-pattern/element/0"
                    "constructor-pattern/argument/1" ]
                  "positional steps"
          }

          test "an address that is not pinned and has an ordered step needs a guard" {
              let result =
                  NodeAddress.create (ArtifactSelector.Package package) format valueRoot [ NodeStep.TupleElement 0 ] ArtifactRevision.Current None

              Expect.equal result (Error NodeUriError.MissingGuard) "missing guard"
          }

          test "one address has one spelling" {
              match pinned [ NodeStep.Body; NodeStep.ApplyArgument ] with
              | Error error -> failtest $"{error}"
              | Ok address ->
                  Expect.equal
                      (NodeAddress.toUri address)
                      ("morphir://ir/pkg/acme/orders?format=4.0.0&rev=sha256:" + String('a', 64) + "#/module/domain/value/total/body/apply/argument")
                      "canonical"
          }

          test "an unknown role is invalid_node_uri" {
              let uri = "morphir://ir/pkg/acme/orders?format=4.0.0#/module/domain/value/total/body/while/condition"
              Expect.isTrue (match NodeAddress.parse uri with Error(NodeUriError.Invalid _) -> true | _ -> false) "invalid"
          } ]

    testList "node address: the role table" (perStep @ others)
