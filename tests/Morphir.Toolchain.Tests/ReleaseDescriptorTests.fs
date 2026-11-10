// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The readers of the documents a recipe binding names: the tool release metadata v1 host release descriptor, the
/// extension release descriptor (<c>2.0.0-draft.2</c>) and the capability claim set (<c>0.1.0-draft.2</c>), over the
/// TEST-ONLY signed repository's target files.
/// </summary>
module Morphir.Toolchain.Tests.ReleaseDescriptorTests

open System.Text
open Expecto
open Morphir.Toolchain

/// A target file of the signed repository as a parsed value.
let valueOf (target: string) =
    match Syntax.parseJson (SignedCaseTests.targetBytes target) with
    | Ok value -> value
    | Error _ -> failtestf "%s does not parse" target

/// The value with the member at a path replaced; a missing last member is added.
let rec setAt (path: string list) (replacement: Value) (value: Value) =
    match path, value with
    | [], _ -> replacement
    | [ head ], Value.Object members when not (members |> List.exists (fst >> (=) head)) -> Value.Object(members @ [ head, replacement ])
    | head :: rest, Value.Object members ->
        Value.Object(members |> List.map (fun (name, item) -> if name = head then name, setAt rest replacement item else name, item))
    | head :: rest, Value.Array items ->
        let index = int head
        Value.Array(items |> List.mapi (fun i item -> if i = index then setAt rest replacement item else item))
    | _ -> failwithf "cannot set %A" path

/// The value with the member at a path removed.
let rec removeAt (path: string list) (value: Value) =
    match path, value with
    | [ last ], Value.Object members -> Value.Object(members |> List.filter (fst >> (<>) last))
    | head :: rest, Value.Object members ->
        Value.Object(members |> List.map (fun (name, item) -> if name = head then name, removeAt rest item else name, item))
    | head :: rest, Value.Array items ->
        let index = int head
        Value.Array(items |> List.mapi (fun i item -> if i = index then removeAt rest item else item))
    | _ -> failwithf "cannot remove %A" path

let bytesOf (value: Value) = JsonText.toBytes value

let memberOf (name: string) (value: Value) =
    match value with
    | Value.Object members -> members |> List.find (fst >> (=) name) |> snd
    | _ -> failwithf "not an object at %s" name
let str (s: string) = Value.String s

let private refusedWith (code: ReaderCode) (path: string) (result: Result<'T, ReaderRefusal>) =
    match result with
    | Error refusal -> Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (code, path) "the refusal"
    | Ok _ -> failtestf "expected %A at %s, but it read" code path

let private releases =
    [ "releases/morphir-dotnet-framework-dependent/1.2.3.json"
      "releases/morphir-dotnet-metadata-mismatch/1.2.3.json"
      "releases/morphir-dotnet-native-aot/1.2.3.json"
      "releases/morphir-dotnet-self-contained/1.2.3.json" ]

let private nativeAot () = valueOf "releases/morphir-dotnet-native-aot/1.2.3.json"

[<Tests>]
let toolReleaseTests =
    testList "Tool release metadata v1 descriptors" [
        testCase "every release of the signed repository reads" (fun () ->
            for target in releases do
                match ToolReleaseDocument.read (SignedCaseTests.targetBytes target) with
                | Ok release ->
                    Expect.equal release.Version.Text "1.2.3" $"{target}: version"
                    Expect.isNonEmpty release.Artifacts $"{target}: artifacts"
                | Error refusal -> failtestf "%s does not read: %A" target refusal)

        testCase "the native-aot release names its tool, platforms and targets" (fun () ->
            match ToolReleaseDocument.read (bytesOf (nativeAot ())) with
            | Ok release ->
                Expect.equal release.ToolId.Text "morphir-dotnet-native-aot" "tool id"
                Expect.equal release.Status RecipeStatus.Active "status at publication"

                Expect.equal
                    (release.Artifacts |> List.map (fun a -> a.TargetPath, a.Platform.Os, a.Platform.Arch) |> List.head)
                    ("artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip", "windows", "x86_64")
                    "the first artifact"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "the fixture's deliberate non-release is refused" (fun () ->
            // Its "note" member is outside the closed schema, refused before the missing members.
            refusedWith ReaderCode.InvalidDocument "note" (ToolReleaseDocument.read (SignedCaseTests.targetBytes "releases/morphir-dotnet-not-a-release/1.2.3.json")))

        testList "a descriptor outside the version 1 schema is refused" [
            for name, edit, code, path in
                [ "schemaVersion 2", setAt [ "schemaVersion" ] (Value.Number "2"), ReaderCode.UnsupportedContractVersion, "schemaVersion"
                  "schemaVersion as a string", setAt [ "schemaVersion" ] (str "1"), ReaderCode.InvalidDocument, "schemaVersion"
                  "another kind", setAt [ "kind" ] (str "morphir-tool"), ReaderCode.InvalidValue, "kind"
                  "a member the schema does not define", setAt [ "note" ] (str "x"), ReaderCode.InvalidDocument, "note"
                  "a tool member the schema does not define", setAt [ "tool"; "extra" ] (str "x"), ReaderCode.InvalidDocument, "tool.extra"
                  "a tool id that is not a portable token", setAt [ "tool"; "id" ] (str "Morphir"), ReaderCode.InvalidValue, "tool.id"
                  "a blank tool name", setAt [ "tool"; "name" ] (str "  "), ReaderCode.InvalidValue, "tool.name"
                  "a version that is not SemVer", setAt [ "version" ] (str "1.2"), ReaderCode.InvalidValue, "version"
                  "an unknown channel", setAt [ "channels" ] (Value.Array [ str "beta" ]), ReaderCode.InvalidValue, "channels[0]"
                  "a repeated channel", setAt [ "channels" ] (Value.Array [ str "stable"; str "stable" ]), ReaderCode.InconsistentDocument, "channels[1]"
                  "an unknown status", setAt [ "status" ] (str "retired"), ReaderCode.UnsupportedCase, "status"
                  "a revoked release with channels", setAt [ "status" ] (str "revoked"), ReaderCode.InconsistentDocument, "channels"
                  "a yanked release with channels", setAt [ "status" ] (str "yanked"), ReaderCode.InconsistentDocument, "channels"
                  "an active release without artifacts", setAt [ "artifacts" ] (Value.Array []), ReaderCode.InconsistentDocument, "artifacts"
                  "a blank morphirCli", setAt [ "compatibility"; "morphirCli" ] (str ""), ReaderCode.InvalidValue, "compatibility.morphirCli"
                  "no compatibility", removeAt [ "compatibility" ], ReaderCode.InvalidDocument, "compatibility"
                  "a target path with a backslash", setAt [ "artifacts"; "0"; "targetPath" ] (str @"artifacts\x.zip"), ReaderCode.InvalidValue, "artifacts[0].targetPath"
                  "a target path with a .. segment", setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/../x.zip"), ReaderCode.InvalidValue, "artifacts[0].targetPath"
                  "a target path ending in a line feed", setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/x.zip\n"), ReaderCode.InvalidValue, "artifacts[0].targetPath"
                  "an arch that is not a token", setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "X86"), ReaderCode.InvalidValue, "artifacts[0].platform.arch"
                  "an unknown archive format", setAt [ "artifacts"; "0"; "archive"; "format" ] (str "rar"), ReaderCode.UnsupportedCase, "artifacts[0].archive.format"
                  "a launch kind other than executable", setAt [ "artifacts"; "0"; "launch"; "kind" ] (str "script"), ReaderCode.UnsupportedCase, "artifacts[0].launch.kind"
                  "a launch argument with NUL", setAt [ "artifacts"; "0"; "launch"; "args" ] (Value.Array [ str "a\u0000b" ]), ReaderCode.InvalidValue, "artifacts[0].launch.args[0]"
                  "two equal artifacts",
                  (fun v ->
                      match v with
                      | Value.Object _ ->
                          let first = match memberOf "artifacts" v with Value.Array(a :: _) -> a | _ -> failwith "artifacts"
                          setAt [ "artifacts" ] (Value.Array [ first; first ]) v
                      | _ -> v),
                  ReaderCode.InconsistentDocument,
                  "artifacts[1]" ] ->
                testCase name (fun () -> refusedWith code path (ToolReleaseDocument.read (bytesOf (edit (nativeAot ())))))
        ]

        testCase "a descriptor over 1 MiB is refused before it is parsed" (fun () ->
            refusedWith ReaderCode.InvalidDocument "" (ToolReleaseDocument.read (Array.create (1024 * 1024 + 1) (byte ' '))))
    ]
