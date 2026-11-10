// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.GuestKit

open System
open Morphir.Conformance.WasmHost

/// The kit's semantic IR comparison: its three stated rules and no other (spec/mep/mck/README.md and the requirement
/// "Semantic IR comparison" of mep/generic-guest-profile):
///
/// 1. JSON object member order is ignored.
/// 2. In IR 3, the order of the modules, types and values name-keyed lists is ignored.
/// 3. In IR 4, at an outputType or a member of inputTypes, a Reference with an fqname and no or empty args equals
///    the FQName string.
///
/// Each rule applies only where the kit states it: rule 2 only when the IR version is 3.0.0 and rule 3 only when it
/// is 4.0.0. Everything else compares exactly: array order, strings by ordinal, numbers by their literal text (as the
/// Rust runner's serde_json with arbitrary_precision does), and true, false and null by kind.
module internal IrComparison =
    let private ordinal (left: string, _) (right: string, _) = String.CompareOrdinal(left, right)

    let private isPair (entry: JsonValue) =
        match entry with
        | JsonValue.Array [ _; _ ] -> true
        | _ -> false

    /// The sort key of a name-keyed entry: the compact text of its canonical name.
    let private nameKey (entry: JsonValue) =
        match entry with
        | JsonValue.Array [ name; _ ] -> Json.compact name
        | other -> Json.compact other

    /// Rule 3: the FQName of a Reference with an fqname and no or empty args, and nothing else in it.
    let private shortReference (value: JsonValue) =
        match value with
        | JsonValue.Object [ "Reference", JsonValue.Object reference ] ->
            let fqname =
                reference
                |> List.tryPick (fun (key, item) ->
                    match key, item with
                    | "fqname", JsonValue.String fqname -> Some fqname
                    | _ -> None
                )

            let onlyKnown =
                reference
                |> List.forall (fun (key, _) ->
                    key = "fqname"
                    || key = "args"
                )

            let noArgs =
                reference
                |> List.forall (fun (key, item) ->
                    match key, item with
                    | "args", JsonValue.Array [] -> true
                    | "args", _ -> false
                    | _ -> true
                )

            if
                onlyKnown
                && noArgs
            then
                fqname
            else
                None
        | _ -> None

    /// The value after the rules of an IR version, with every object's members in ordinal order (rule 1).
    let rec canonical (version: IrVersion) (value: JsonValue) : JsonValue =
        match value with
        | JsonValue.Array items -> JsonValue.Array(List.map (canonical version) items)
        | JsonValue.Object members ->
            members
            |> List.map (fun (key, item) ->
                let item =
                    match version, key, item with
                    | IrVersion.V4, "outputType", _ -> canonicalType version item
                    | IrVersion.V4, "inputTypes", JsonValue.Object inputs ->
                        JsonValue.Object(
                            inputs
                            |> List.map (fun (name, input) -> name, canonicalType version input)
                            |> List.sortWith ordinal
                        )
                    | _ ->
                        match version, key, canonical version item with
                        | IrVersion.V3, ("modules" | "types" | "values"), JsonValue.Array entries when
                            List.forall isPair entries
                            ->
                            // List.sortWith is stable, as Rust's sort_by_key is.
                            JsonValue.Array(
                                entries
                                |> List.sortWith (fun left right ->
                                    String.CompareOrdinal(nameKey left, nameKey right)
                                )
                            )
                        | _, _, item -> item

                key, item
            )
            |> List.sortWith ordinal
            |> JsonValue.Object
        | other -> other

    and private canonicalType (version: IrVersion) (value: JsonValue) =
        match shortReference value with
        | Some fqname -> JsonValue.String fqname
        | None -> canonical version value

    let private quoted (value: JsonValue) =
        let text = Json.compact value

        if
            text.Length
            <= 240
        then
            text
        else
            text.Substring(0, 240)
            + $"... ({text.Length} characters)"

    let private pointer (path: string) (name: string) =
        path
        + "/"
        + name.Replace("~", "~0").Replace("/", "~1")

    let rec private first (path: string) (expected: JsonValue) (actual: JsonValue) : string option =
        let at = if path = "" then "/" else path

        match expected, actual with
        | JsonValue.Object expectedMembers, JsonValue.Object actualMembers ->
            let expectedNames =
                expectedMembers
                |> List.map fst

            let actualNames =
                actualMembers
                |> List.map fst

            match
                expectedNames
                |> List.tryFind (fun name -> not (List.contains name actualNames)),
                actualNames
                |> List.tryFind (fun name -> not (List.contains name expectedNames))
            with
            | Some missing, _ ->
                Some
                    $"at {pointer path missing}: the member is missing; expected {quoted (
                                                                                      expectedMembers
                                                                                      |> List.find (fun (name, _) -> name = missing)
                                                                                      |> snd
                                                                                  )}"
            | None, Some extra ->
                Some
                    $"at {pointer path extra}: the member is not expected; found {quoted (
                                                                                      actualMembers
                                                                                      |> List.find (fun (name, _) -> name = extra)
                                                                                      |> snd
                                                                                  )}"
            | None, None ->
                List.zip expectedMembers actualMembers
                |> List.tryPick (fun ((name, left), (_, right)) ->
                    first (pointer path name) left right
                )
        | JsonValue.Array expectedItems, JsonValue.Array actualItems when
            expectedItems.Length
            <> actualItems.Length
            ->
            Some
                $"at {at}: expected an array of {expectedItems.Length} items, found {actualItems.Length}: expected {quoted expected}, found {quoted actual}"
        | JsonValue.Array expectedItems, JsonValue.Array actualItems ->
            List.zip expectedItems actualItems
            |> List.indexed
            |> List.tryPick (fun (number, (left, right)) ->
                first
                    (pointer path (number.ToString(Globalization.CultureInfo.InvariantCulture)))
                    left
                    right
            )
        | _ when expected = actual -> None
        | _ -> Some $"at {at}: expected {quoted expected}, found {quoted actual}"

    /// The first difference between the expected and the actual IR of a version after the kit's rules, or None. The
    /// pointer names members as written; an array index counts items after rule 2 has ordered the list.
    let difference (version: IrVersion) (expected: JsonValue) (actual: JsonValue) : string option =
        first "" (canonical version expected) (canonical version actual)
