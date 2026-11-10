// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.GuestKit

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Morphir.Conformance.WasmHost

/// An IR version of the generic guest kit.
[<RequireQualifiedAccess>]
type internal IrVersion =
    | V3
    | V4

    /// The full version, as the kit and the model-transport tuples write it.
    member this.WireValue =
        match this with
        | V3 -> "3.0.0"
        | V4 -> "4.0.0"

    /// The full version.
    override this.ToString() = this.WireValue

/// A problem in a kit file: the JSON pointer of the member and what is wrong with it.
type KitIssue =
    {
        /// The JSON pointer of the member, such as /languages/python/steps.
        Path: string
        /// What is wrong.
        Message: string
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Path}: {this.Message}"

/// One source document of a kit language.
type internal KitDocument = { Uri: string; Text: string }

/// The source sets of one kit language.
type internal KitLanguage =
    {
        Id: string
        Documents: KitDocument list
        Malformed: KitDocument list
    }

/// The parts of a kit file after the strict read.
type internal KitParts =
    {
        Package: JsonValue
        PackageName: string
        ContextDigest: string
        Expected: (IrVersion * JsonValue) list
        Languages: KitLanguage list
    }

/// The strict reader of a kit file, as crates/morphir-mck/src/mep/generic.rs loads it (deny_unknown_fields): every
/// member that the profile does not name is an issue, never ignored.
module internal KitReader =
    /// The one profile this reader reads.
    [<Literal>]
    let Profile = "generic-json-library-v1"

    /// The one kit contract version this reader reads.
    [<Literal>]
    let ContractVersion = "0.1.0-draft.1"

    let private issue path message = { Path = path; Message = message }

    let private issuesOf (result: Result<'T, KitIssue list>) =
        match result with
        | Ok _ -> []
        | Error issues -> issues

    let private collect (results: Result<'T, KitIssue list> list) : Result<'T list, KitIssue list> =
        match results |> List.collect issuesOf with
        | [] -> Ok(results |> List.choose Result.toOption)
        | issues -> Error issues

    let private zip (left: Result<'A, KitIssue list>) (right: Result<'B, KitIssue list>) =
        match left, right with
        | Ok left, Ok right -> Ok(left, right)
        | _ -> Error(issuesOf left @ issuesOf right)

    /// The JSON pointer of a member below path.
    let pointer (path: string) (name: string) =
        path + "/" + name.Replace("~", "~0").Replace("/", "~1")

    /// A path relative to the source folder: not empty, no root, scheme, drive or backslash, no empty, . or ..
    /// segment, and, unless allowSrc, no src/ prefix. Kit documents may not start with src/; generated files may.
    let relativePath (allowSrc: bool) (path: string) =
        path <> ""
        && not (path.StartsWith '/')
        && not (path.Contains '\\' || path.Contains ':')
        && path.Split '/'
           |> Array.forall (fun segment -> segment <> "" && segment <> "." && segment <> "..")
        && (allowSrc || (path.Split '/')[0] <> "src")

    /// The members of an object whose members must be exactly known: an unknown member and a missing member are
    /// each an issue.
    let private closed (path: string) (known: string list) (value: JsonValue) =
        match value with
        | JsonValue.Object members ->
            let unknown =
                members
                |> List.filter (fun (name, _) -> not (List.contains name known))
                |> List.map (fun (name, _) ->
                    issue (pointer path name) "is not a member of the generic guest kit, so this reader refuses it")

            let missing =
                known
                |> List.filter (fun name -> not (members |> List.exists (fun (key, _) -> key = name)))
                |> List.map (fun name -> issue (pointer path name) "is missing")

            match unknown @ missing with
            | [] -> Ok(Map.ofList members)
            | issues -> Error issues
        | _ -> Error [ issue path "must be an object" ]

    let private text path value =
        match value with
        | JsonValue.String text -> Ok text
        | _ -> Error [ issue path "must be a string" ]

    let private index (number: int) =
        number.ToString(Globalization.CultureInfo.InvariantCulture)

    let private document path value =
        closed path [ "uri"; "text" ] value
        |> Result.bind (fun members ->
            match zip (text (pointer path "uri") members["uri"]) (text (pointer path "text") members["text"]) with
            | Ok(uri, body) when relativePath false uri -> Ok { Uri = uri; Text = body }
            | Ok(uri, _) ->
                Error
                    [ issue
                          (pointer path "uri")
                          $"\"{uri}\" is not relative to the source folder: no root, scheme, backslash, src/ prefix, or empty, . or .. segment" ]
            | Error issues -> Error issues)

    let private documents path value =
        match value with
        | JsonValue.Array [] -> Error [ issue path "must not be empty" ]
        | JsonValue.Array items ->
            items
            |> List.mapi (fun number item -> document (pointer path (index number)) item)
            |> collect
            |> Result.bind (fun found ->
                match found |> List.countBy _.Uri |> List.tryFind (fun (_, count) -> count > 1) with
                | Some(uri, _) -> Error [ issue path $"repeats the document URI \"{uri}\"" ]
                | None -> Ok found)
        | _ -> Error [ issue path "must be an array" ]

    let private language path (id: string) value =
        if id = "" then
            Error [ issue path "is an empty language id" ]
        else
            closed path [ "documents"; "malformed" ] value
            |> Result.bind (fun members ->
                zip
                    (documents (pointer path "documents") members["documents"])
                    (documents (pointer path "malformed") members["malformed"])
                |> Result.map (fun (found, malformed) ->
                    { Id = id
                      Documents = found
                      Malformed = malformed }))

    let private formatted path (version: IrVersion) (format: string) (ir: JsonValue) =
        match ir with
        | JsonValue.Object members when members |> List.contains ("formatVersion", JsonValue.Number format) ->
            Ok(version, ir)
        | _ -> Error [ issue (pointer path "formatVersion") $"must be {format}" ]

    let private expected path value =
        match value with
        | JsonValue.Object members ->
            members
            |> List.map (fun (key, ir) ->
                let at = pointer path key

                match key with
                | "3.0.0" -> formatted at IrVersion.V3 "3" ir
                | "4.0.0" -> formatted at IrVersion.V4 "4" ir
                | _ ->
                    Error
                        [ issue at "is an IR version the generic guest kit runner does not know; it knows 3.0.0 and 4.0.0" ])
            |> collect
            |> Result.bind (fun found ->
                let missing =
                    [ IrVersion.V3; IrVersion.V4 ]
                    |> List.filter (fun version -> not (found |> List.exists (fun (candidate, _) -> candidate = version)))
                    |> List.map (fun version -> issue (pointer path version.WireValue) "is missing")

                match missing with
                | [] ->
                    Ok(
                        [ IrVersion.V3; IrVersion.V4 ]
                        |> List.map (fun version -> found |> List.find (fun (candidate, _) -> candidate = version))
                    )
                | issues -> Error issues)
        | _ -> Error [ issue path "must be an object" ]

    let private package path value =
        closed path [ "name"; "exposedModules" ] value
        |> Result.bind (fun members ->
            let name =
                match members["name"] with
                | JsonValue.String name when name <> "" -> Ok name
                | _ -> Error [ issue (pointer path "name") "must be a non-empty string" ]

            let modules =
                match members["exposedModules"] with
                | JsonValue.Array(_ :: _ as items) when
                    items
                    |> List.forall (function
                        | JsonValue.String _ -> true
                        | _ -> false)
                    ->
                    Ok()
                | _ -> Error [ issue (pointer path "exposedModules") "must be a non-empty array of strings" ]

            zip name modules |> Result.map (fun (name, ()) -> value, name))

    let private exactly path (wanted: string) (what: string) value =
        match value with
        | JsonValue.String found when found = wanted -> Ok()
        | JsonValue.String found -> Error [ issue path $"is {found}; this reader reads {what} {wanted} only" ]
        | _ -> Error [ issue path "must be a string" ]

    let private digest path value =
        match value with
        | JsonValue.String digest when
            digest.Length = 64
            && digest |> Seq.forall (fun c -> Char.IsAsciiDigit c || (c >= 'a' && c <= 'f'))
            ->
            Ok digest
        | _ -> Error [ issue path "must be 64 lowercase hexadecimal digits" ]

    let private languages path value =
        match value with
        | JsonValue.Object [] -> Error [ issue path "must not be empty" ]
        | JsonValue.Object entries ->
            entries
            |> List.map (fun (id, sets) -> language (pointer path id) id sets)
            |> collect
            |> Result.map (List.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id)))
        | _ -> Error [ issue path "must be an object" ]

    let private parse (bytes: byte array) =
        try
            let text = UTF8Encoding(false, true).GetString bytes
            use document = JsonDocument.Parse(text, JsonDocumentOptions(MaxDepth = 256))

            Json.ofElement "" document.RootElement
            |> Result.mapError (fun message -> [ issue "" message ])
        with
        | :? DecoderFallbackException -> Error [ issue "" "is not UTF-8" ]
        | :? JsonException as error -> Error [ issue "" $"is not JSON: {error.Message}" ]

    /// Reads a kit file strictly.
    let read (bytes: byte array) : Result<KitParts, KitIssue list> =
        parse bytes
        |> Result.bind (
            closed "" [ "contractVersion"; "profile"; "package"; "contextDigest"; "expectedIrVersions"; "languages" ]
        )
        |> Result.bind (fun members ->
            let contract =
                exactly "/contractVersion" ContractVersion "the kit contract" members["contractVersion"]

            let profile = exactly "/profile" Profile "the profile" members["profile"]
            let fixedPackage = package "/package" members["package"]
            let contextDigest = digest "/contextDigest" members["contextDigest"]
            let expectedIr = expected "/expectedIrVersions" members["expectedIrVersions"]
            let kitLanguages = languages "/languages" members["languages"]

            match contract, profile, fixedPackage, contextDigest, expectedIr, kitLanguages with
            | Ok(), Ok(), Ok(packageValue, packageName), Ok contextDigest, Ok expectedIr, Ok kitLanguages ->
                Ok
                    { Package = packageValue
                      PackageName = packageName
                      ContextDigest = contextDigest
                      Expected = expectedIr
                      Languages = kitLanguages }
            | _ ->
                Error(
                    issuesOf contract
                    @ issuesOf profile
                    @ issuesOf fixedPackage
                    @ issuesOf contextDigest
                    @ issuesOf expectedIr
                    @ issuesOf kitLanguages
                ))

/// The generic JSON Library profile of the MEP guest kit (profile generic-json-library-v1), read strictly from its
/// file: the package, its source documents for each language and the expected IR of each version. Build it with
/// Parse (F#) or TryParse (C#).
[<Sealed>]
type GuestKit
    internal
    (
        package: JsonValue,
        packageName: string,
        contextDigest: string,
        expected: (IrVersion * JsonValue) list,
        languages: KitLanguage list,
        sha256: string
    ) =

    /// The profile: generic-json-library-v1.
    member _.Profile = "generic-json-library-v1"

    /// The kit contract version: 0.1.0-draft.1.
    member _.ContractVersion = "0.1.0-draft.1"

    /// The SHA-256 of the kit file, in lowercase hexadecimal.
    member _.Sha256 = sha256

    /// The name of the fixed package.
    member _.PackageName = packageName

    /// The kit's context digest.
    member _.ContextDigest = contextDigest

    /// The kit language ids, in ordinal order.
    member _.Languages: IReadOnlyList<string> =
        languages
        |> List.map _.Id
        |> List.toArray
        :> IReadOnlyList<_>

    /// The IR versions with an expected IR: 3.0.0 and 4.0.0.
    member _.IrVersions: IReadOnlyList<string> =
        expected
        |> List.map (fun (version, _) -> version.WireValue)
        |> List.toArray
        :> IReadOnlyList<_>

    /// The package member of a compile request, as the kit writes it.
    member internal _.Package = package

    /// The expected IR of a version.
    member internal _.Expected(version: IrVersion) =
        expected
        |> List.find (fun (candidate, _) -> candidate = version)
        |> snd

    /// The kit languages, in ordinal order of their ids.
    member internal _.LanguageSets = languages

    /// Reads a kit file. Every unknown member, unknown IR version and wrong value is an issue that names its member.
    static member Parse(bytes: byte array) : Result<GuestKit, IReadOnlyList<KitIssue>> =
        match KitReader.read bytes with
        | Ok kit ->
            Ok(
                GuestKit(
                    kit.Package,
                    kit.PackageName,
                    kit.ContextDigest,
                    kit.Expected,
                    kit.Languages,
                    Convert.ToHexStringLower(SHA256.HashData bytes)
                )
            )
        | Error issues -> Error(List.toArray issues :> IReadOnlyList<_>)

    /// For C# callers: reads a kit file. On success kit is set and failure is null; otherwise failure lists every
    /// issue, one per line.
    static member TryParse
        (
            bytes: byte array | null,
            [<Out; MaybeNullWhen(false)>] kit: byref<GuestKit>,
            [<Out; MaybeNullWhen(true)>] failure: byref<string>
        ) : bool =
        let result =
            match bytes with
            | null -> Error([| { Path = ""; Message = "No kit bytes were given." } |] :> IReadOnlyList<_>)
            | bytes -> GuestKit.Parse bytes

        match result with
        | Ok parsed ->
            kit <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Error issues ->
            kit <- Unchecked.defaultof<_>
            failure <- issues |> Seq.map string |> String.concat Environment.NewLine
            false

    /// Stable text for diagnostics.
    override _.ToString() = $"Guest kit generic-json-library-v1 {sha256}"
