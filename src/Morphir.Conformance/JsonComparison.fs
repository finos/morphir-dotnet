// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

open System
open System.Text.Json

module internal JsonComparison =
    let firstDifference (expected: string) (actual: string) =
        use expectedDocument = JsonDocument.Parse expected
        use actualDocument = JsonDocument.Parse actual

        let rec compare path (left: JsonElement) (right: JsonElement) =
            if
                left.ValueKind
                <> right.ValueKind
            then
                Some(
                    path
                    + ": JSON kinds differ"
                )
            else
                match left.ValueKind with
                | JsonValueKind.Object ->
                    let properties (element: JsonElement) =
                        element.EnumerateObject()
                        |> Seq.map (fun property -> property.Name, property.Value)
                        |> Seq.sortWith (fun (left, _) (right, _) ->
                            StringComparer.Ordinal.Compare(left, right)
                        )
                        |> Seq.toList

                    let leftProperties, rightProperties = properties left, properties right

                    let leftNames, rightNames =
                        List.map fst leftProperties, List.map fst rightProperties

                    if
                        leftNames
                        <> rightNames
                        || (Set.ofList leftNames).Count
                           <> leftNames.Length
                    then
                        Some(
                            path
                            + ": object members differ or are duplicated"
                        )
                    else
                        List.zip leftProperties rightProperties
                        |> List.tryPick (fun ((name, left), (_, right)) ->
                            compare
                                (path
                                 + "/"
                                 + name.Replace("~", "~0").Replace("/", "~1"))
                                left
                                right
                        )
                | JsonValueKind.Array ->
                    if
                        left.GetArrayLength()
                        <> right.GetArrayLength()
                    then
                        Some(
                            path
                            + ": array lengths differ"
                        )
                    else
                        Seq.zip (left.EnumerateArray()) (right.EnumerateArray())
                        |> Seq.indexed
                        |> Seq.tryPick (fun (index, (left, right)) ->
                            compare
                                (path
                                 + "/"
                                 + index.ToString(Globalization.CultureInfo.InvariantCulture))
                                left
                                right
                        )
                | JsonValueKind.String ->
                    if left.GetString() = right.GetString() then
                        None
                    else
                        Some(
                            path
                            + ": strings differ"
                        )
                | _ ->
                    if left.GetRawText() = right.GetRawText() then
                        None
                    else
                        Some(
                            path
                            + ": values differ"
                        )

        compare "$" expectedDocument.RootElement actualDocument.RootElement
