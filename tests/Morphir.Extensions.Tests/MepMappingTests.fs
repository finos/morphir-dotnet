module Morphir.Extensions.Tests.MepMappingTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.IR
open Morphir.IR.Model
open Morphir.Extensions

/// The member names of a record of the pinned contract, and which members are optional. The same walk as the
/// contract test of Morphir.Mep.Tests (design MEP-3).
module PinnedContract =
    let private pin =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mep", "mep.ir.json")

    let members (typeName: string) (snakeCase: bool) : (string * bool) list =
        use contract = JsonDocument.Parse(File.ReadAllText pin)

        contract.RootElement
            .GetProperty("distribution")
            .GetProperty("Library")
            .GetProperty("def")
            .GetProperty("modules")
            .GetProperty("mep")
            .GetProperty("Public")
            .GetProperty("types")
            .GetProperty(typeName)
            .GetProperty("Public")
            .GetProperty("CustomTypeDefinition")
            .GetProperty("constructors")
            .GetProperty(typeName)
            .EnumerateArray()
        |> Seq.map (fun argument ->
            let name = argument[0].GetString() |> nonNull

            let optional =
                match argument[1].TryGetProperty "Reference" with
                | true, value -> value.GetProperty("fqname").GetString() = "morphir/SDK:maybe#maybe"
                | false, _ -> false

            let words = name.Split '-'

            let wire =
                if snakeCase then
                    name.Replace('-', '_')
                else
                    words[0]
                    + (words
                       |> Array.skip 1
                       |> Array.map (fun word -> string (Char.ToUpperInvariant word[0]) + word.Substring(1))
                       |> String.concat "")

            wire, optional)
        |> List.ofSeq

let private host = ReleaseVersion.Create "0.4.0-alpha.2"

let private formatVersion text =
    match Versioning.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad format version {text}: {error}"

let private sourceVersion text =
    match SourceFormatVersion.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad version {text}: {error}"

let private backend =
    ExtensionEntry.BuiltInBackend(
        ExtensionIdentity.Create(
            ExtensionId.Create "morphir.csharp",
            ExtensionName.Create "C# generator",
            host,
            ExtensionDescription.Create "Generates C#."
        ),
        {
            Targets = NonEmptySet.create (TargetId.Create "csharp") []
            IrVersions = NonEmptySet.create (sourceVersion "3") [ sourceVersion "4.0.0" ]
        }
    )

let private validator =
    ExtensionEntry.BuiltInValidator(
        ExtensionIdentity.Create(ExtensionId.Create "morphir.type-validator", ExtensionName.Create "Type validator", host, null),
        ValidatorClaim.Create(formatVersion "1", formatVersion "2", formatVersion "3")
    )

let private infoJson (entry: ExtensionEntry) =
    JsonSerializer.Serialize(entry.ToMep(), Morphir.Mep.MepJson.Default.ExtensionInfo)

let private capabilitiesJson (entry: ExtensionEntry) =
    JsonSerializer.Serialize(entry.ToMepCapabilities(), Morphir.Mep.MepJson.Default.ExtensionCapabilities)

let private objectOf (json: string) =
    match JsonNode.Parse json with
    | :? JsonObject as value -> value
    | _ -> failtest $"Expected an object: {json}"

/// The entry's ExtensionInfo JSON has only pinned members, every required member, and reads back.
let contractValid (entry: ExtensionEntry) =
    let json = infoJson entry
    let written = objectOf json |> Seq.map _.Key |> Set.ofSeq
    let pinned = PinnedContract.members "extension-info" true
    let allowed = pinned |> List.map fst |> Set.ofList

    let required =
        pinned
        |> List.filter (fun (_, optional) -> not optional)
        |> List.map fst
        |> Set.ofList

    Expect.isTrue (Set.isSubset written allowed) $"Only contract members: {json}"
    Expect.isTrue (Set.isSubset required written) $"Every required member: {json}"

    let read =
        JsonSerializer.Deserialize(json, Morphir.Mep.MepJson.Default.ExtensionInfo)

    Expect.equal read (entry.ToMep()) "The JSON reads back as the same record"

[<Tests>]
let tests =
    testList "MEP mapping" [
        test "a backend entry writes a contract-valid ExtensionInfo" {
            contractValid backend
            let info = backend.ToMep()
            Expect.equal info.Id "morphir.csharp" "id"
            Expect.equal info.Name "C# generator" "name"
            Expect.equal info.Version "0.4.0-alpha.2" "version"
            Expect.equal info.Description (Some "Generates C#.") "description"
            Expect.equal (List.ofSeq info.Types) [ Morphir.Mep.ExtensionType.Backend ] "types"
        }

        test "an entry with no description has no description member" {
            contractValid validator
            Expect.isFalse ((objectOf (infoJson validator)).ContainsKey "description") "no description"
        }

        test "a validator entry has validator in types and no validator claim" {
            let types = (objectOf (infoJson validator))["types"] |> nonNull
            Expect.equal (types.ToJsonString()) """["validator"]""" "types"
            let capabilities = objectOf (capabilitiesJson validator)
            Expect.isFalse (capabilities.ContainsKey "validator") "no invented validator claim"
            Expect.isFalse (capabilities.ContainsKey "backend") "no backend claim"
        }

        test "a backend entry claims only the contract backend capability" {
            let capabilities = objectOf (capabilitiesJson backend)

            let claim = capabilities["backend"] |> nonNull

            Expect.equal
                (claim.ToJsonString())
                """{"targets":["csharp"],"irVersions":["3","4.0.0"],"generate":true}"""
                "backend claim"

            let pinned =
                PinnedContract.members "extension-capabilities" false
                |> List.map fst
                |> Set.ofList

            for name in capabilities |> Seq.map _.Key do
                Expect.isTrue (pinned.Contains name) $"{name} is a contract member"
        }

        test "every extension type maps to the MEP type with the same wire value" {
            for kind in
                [
                    ExtensionType.Frontend
                    ExtensionType.Backend
                    ExtensionType.Transform
                    ExtensionType.Validator
                    ExtensionType.Workspace
                ] do
                Expect.equal ((kind.ToMep()).WireValue) kind.WireValue (string kind)
        }
    ]
