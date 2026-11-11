// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open Morphir.Toolchain

module internal SharedJson =
    exception private Refusal of InstallationFailure

    let fail code path message =
        raise (Refusal(InstallationFailure(code, $"{path}: {message}")))

    let invalid path message =
        fail "extension.invalid_document" path message

    let protect action =
        try
            Ok(action ())
        with Refusal failure ->
            Error failure

    let members path =
        function
        | Value.Object items -> items
        | _ -> invalid path "Expected an object."

    let optional name items =
        List.tryFind (fun (key, _) -> key = name) items
        |> Option.map snd

    let required path name items =
        match optional name items with
        | Some value -> value
        | None -> invalid $"{path}.{name}" "Required member is absent."

    let text path =
        function
        | Value.String value -> value
        | _ -> invalid path "Expected a string."

    let nonempty path value =
        let value = text path value

        if value.Length = 0 then
            invalid path "Expected nonempty text."

        value

    let array path =
        function
        | Value.Array items -> items
        | _ -> invalid path "Expected an array."

    let strings path value =
        array path value
        |> List.mapi (fun index item -> text $"{path}[{index + 1}]" item)

    let distinct path items =
        let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)

        for item in items do
            if not (seen.Add item) then
                invalid path $"Repeated value '{item}'."

        items

    let parsed path parser value =
        match parser (text path value) with
        | Ok result -> result
        | Error error -> invalid path (error.ToString())

    let boolean path =
        function
        | Value.Boolean value -> value
        | _ -> invalid path "Expected a boolean."

    let private plainInteger (spelling: string) =
        let start = if spelling.StartsWith '-' then 1 else 0

        spelling.Length > start
        && spelling
           |> Seq.skip start
           |> Seq.forall Char.IsAsciiDigit

    let rec private check path value =
        match value with
        | Value.Number spelling when not (plainInteger spelling) ->
            invalid path "Shared JSON numbers must be integers without a fraction or exponent."
        | Value.Object items ->
            for name, child in items do
                check $"{path}.{name}" child
        | Value.Array items ->
            items
            |> List.iteri (fun index child -> check $"{path}[{index + 1}]" child)
        | _ -> ()

    // Syntax already refuses non-UTF8, BOM, duplicate keys, comments and trailing content.
    let read (file: string) (bytes: byte array) : Result<Value, InstallationFailure> =
        protect (fun () ->
            match Syntax.parseJson bytes with
            | Error _ -> invalid file "Expected strict UTF-8 I-JSON with unique member names."
            | Ok value ->
                check file value
                value
        )

    let schema file expected items =
        match optional "$schema" items with
        | Some(Value.String actual) when actual = expected -> ()
        | _ ->
            fail
                "extension.unsupported_contract_version"
                file
                $"Expected exact $schema '{expected}'; no conversion is defined."

    let rec ordered =
        function
        | Value.Object items ->
            items
            |> List.sortWith (fun (left, _) (right, _) ->
                StringComparer.Ordinal.Compare(left, right)
            )
            |> List.map (fun (key, value) -> key, ordered value)
            |> Value.Object
        | Value.Array items -> Value.Array(List.map ordered items)
        | value -> value

    let rec difference path left right =
        match ordered left, ordered right with
        | Value.Object left, Value.Object right ->
            let keys =
                (List.map fst left
                 @ List.map fst right)
                |> List.distinct
                |> List.sortWith (fun a b -> StringComparer.Ordinal.Compare(a, b))

            keys
            |> List.tryPick (fun key ->
                match optional key left, optional key right with
                | Some a, Some b -> difference $"{path}.{key}" a b
                | _ -> Some $"{path}.{key}"
            )
        | Value.Array left, Value.Array right when left.Length = right.Length ->
            List.zip left right
            |> List.mapi (fun index (a, b) -> difference $"{path}[{index + 1}]" a b)
            |> List.tryPick id
        | a, b when a = b -> None
        | _ -> Some path
