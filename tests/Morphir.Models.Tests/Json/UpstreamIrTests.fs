/// Task 2.4: the writers against independent upstream IR. Each pinned upstream JSON document (see
/// tests/Fixtures/MorphirIon/PROVENANCE.md) is read, written in its own version, and compared structurally
/// with the original. Only the normalizations named below apply; every exclusion states its reason.
module Morphir.Json.Tests.UpstreamIrTests

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Json.Classic
open Morphir.Json.V4

let private root =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Fixtures", "MorphirIon")

/// Documents whose upstream spelling contradicts the pinned kit (conformance/deviations.md). They are
/// compared as models: the written document must read back to the model of the original.
let private kitDeviations =
    set [
        "pairs/complete-example.json"
        "v4/complete-example.json"
        "pairs/v4-library-nodes.json"
        "pairs/v4-specs.json"
    ]

/// Documents that no writer produces, with the reason.
let private excluded =
    Map.ofList [
        "pairs/v3-specs.json",
        "A 3.1.0 Specs distribution: the classic writer writes Library distributions only."
    ]

let private documents = [
    for folder in [ "pairs"; "records"; "v4" ] do
        for file in
            Directory.GetFiles(Path.Combine(root, folder), "*.json")
            |> Array.sort do
            yield $"{folder}/{Path.GetFileName file}"
    yield "mep/fixtures/plain-ir.json"
]

/// N1: an integer format version n equals the release string "n.0.0".
let private normalizeVersion (node: JsonNode | null) =
    match node with
    | :? JsonValue as value ->
        match value.GetValueKind() with
        | JsonValueKind.Number -> $"{value.GetValue<int>()}.0.0"
        | _ -> value.GetValue<string>()
    | _ -> failtest "formatVersion is missing"

/// N2: in classic IR, every list that the classic model holds in a map is compared without order, because
/// the model keeps no order for it: dependencies, modules, a module's types and values, constructors, record
/// and record-update fields, and let-recursion definitions. The upstream kit applies the same rule to modules,
/// types and values. All other lists, such as parameters, constructor arguments and match cases, keep order.
let private tagIs (node: JsonNode | null) (name: string) =
    match node with
    | :? JsonValue as value when value.GetValueKind() = JsonValueKind.String ->
        System.String.Equals(
            value.GetValue<string>().Replace("_", ""),
            name,
            System.StringComparison.OrdinalIgnoreCase
        )
    | _ -> false

let private sortPairs (node: JsonNode | null) =
    match node with
    | :? JsonArray as pairs ->
        let sorted =
            pairs
            |> Seq.map (fun pair -> pair.DeepClone())
            |> Seq.sortBy (fun pair -> (pair.AsArray()[0]).ToJsonString())
            |> Array.ofSeq

        pairs.Clear()

        for pair in sorted do
            pairs.Add pair
    | _ -> ()

let rec private canonicalize (node: JsonNode | null) =
    match node with
    | :? JsonObject as o ->
        for key in [ "modules"; "types"; "values" ] do
            if o.ContainsKey key then
                sortPairs o[key]

        for kv in
            o
            |> Seq.toArray do
            canonicalize kv.Value
    | :? JsonArray as a ->
        if a.Count > 0 then
            if
                tagIs a[0] "CustomTypeSpecification"
                && a.Count = 3
            then
                sortPairs a[2]

            if
                tagIs a[0] "CustomTypeDefinition"
                && a.Count = 3
            then
                match a[2] with
                | :? JsonObject as wrapper -> sortPairs wrapper["value"]
                | :? JsonArray as pair when pair.Count = 2 -> sortPairs pair[1]
                | _ -> ()

            if
                (tagIs a[0] "Record"
                 || tagIs a[0] "LetRecursion")
                && a.Count = 3
            then
                match a[2] with
                | :? JsonArray as fields when
                    fields.Count > 0
                    && (fields[0] :? JsonArray)
                    ->
                    sortPairs fields
                | _ -> ()

            if
                tagIs a[0] "UpdateRecord"
                && a.Count = 4
            then
                sortPairs a[3]

        for item in
            a
            |> Seq.toArray do
            canonicalize item
    | _ -> ()

let private classicCanonical (root: JsonNode) =
    let copy = root.DeepClone()
    sortPairs (copy["distribution"].AsArray()[2])
    canonicalize copy
    copy

let rec private same (left: JsonNode | null) (right: JsonNode | null) : bool =
    match left, right with
    | null, null -> true
    | (:? JsonObject as a), (:? JsonObject as b) ->
        let keys (o: JsonObject) =
            o
            |> Seq.map (fun kv -> kv.Key)
            |> set

        keys a = keys b
        && keys a
           |> Seq.forall (fun k ->
               k = "formatVersion"
               || same a[k] b[k]
           )
    | (:? JsonArray as a), (:? JsonArray as b) ->
        a.Count = b.Count
        && Seq.forall2 same a b
    | a, b -> JsonNode.DeepEquals(a, b)

type private Written =
    | Classic of version: int * bytes: byte array
    | V4 of bytes: byte array

let private write (element: JsonElement) =
    if (string (element.GetProperty "formatVersion")).StartsWith "4" then
        match V4Json.readDistribution element with
        | Ok document ->
            match V4JsonWriter.writeDistribution false document.Distribution with
            | Ok bytes -> V4 bytes
            | Error error -> failtest $"v4 write: {error}"
        | Error error -> failtest $"v4 read: {error}"
    else
        match ClassicJson.readDistribution element with
        | Ok document ->
            let profile =
                match document.Version with
                | 1 -> ClassicWireProfile.v1
                | 2 -> ClassicWireProfile.v2
                | _ -> ClassicWireProfile.v3

            match ClassicJsonWriter.writeJsonAttributes profile false document.Distribution with
            | Ok bytes -> Classic(document.Version, bytes)
            | Error error -> failtest $"classic write: {error.Rule}: {error.Message}"
        | Error error -> failtest $"classic read: {error.Rule}: {error.Message}"

let private bytesOf written =
    match written with
    | Classic(_, bytes)
    | V4 bytes -> bytes

let private v4Model (bytes: byte array) =
    use document = JsonDocument.Parse(System.ReadOnlyMemory bytes)

    match V4Json.readDistribution document.RootElement with
    | Ok read -> read.Distribution
    | Error error -> failtest $"{error}"

[<Tests>]
let tests =
    testList "Writers against upstream IR" [
        test "every pinned upstream JSON document is covered" {
            Expect.isGreaterThanOrEqual (List.length documents) 20 "documents found"

            for name in
                Map.keys excluded
                |> Seq.append kitDeviations do
                Expect.contains documents name $"{name} is pinned"
        }

        for name in documents do
            match Map.tryFind name excluded with
            | Some reason ->
                test $"{name} is excluded: {reason}" { Expect.isTrue (reason.Length > 0) "reason" }
            | None ->
                test $"{name} is written structurally equal to the upstream document" {
                    let original = File.ReadAllBytes(Path.Combine(root, name))
                    use document = JsonDocument.Parse(System.ReadOnlyMemory original)
                    let written = write document.RootElement
                    let again = write document.RootElement

                    Expect.sequenceEqual
                        (bytesOf again)
                        (bytesOf written)
                        "the bytes are deterministic"

                    let expected = JsonNode.Parse(System.Text.Encoding.UTF8.GetString original)

                    let actual =
                        JsonNode.Parse(System.Text.Encoding.UTF8.GetString(bytesOf written))

                    Expect.equal
                        (normalizeVersion actual["formatVersion"])
                        (normalizeVersion expected["formatVersion"])
                        "the same format version (N1)"

                    if kitDeviations.Contains name then
                        Expect.equal
                            (v4Model (bytesOf written))
                            (v4Model original)
                            "reads back to the upstream model"
                    else
                        match written with
                        | Classic _ ->
                            Expect.isTrue
                                (same (classicCanonical expected) (classicCanonical actual))
                                "structurally equal under N1 and N2"
                        | V4 _ -> Expect.isTrue (same expected actual) "structurally equal under N1"
                }
    ]
