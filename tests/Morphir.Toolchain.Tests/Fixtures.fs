// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// Locations of the finos/morphir toolchain contract files the suite runs: the pin in tests/Fixtures/Toolchain by
/// default, or, when <c>MORPHIR_TOOLCHAIN_CONTRACT_ROOT</c> names a finos/morphir checkout, that checkout's current
/// specs, schemas, fixed fixtures and case manifests. The external corpus is read in place: nothing compares it with
/// the pin or writes to either.
/// </summary>
module Morphir.Toolchain.Tests.Fixtures

open System
open System.IO

/// The environment variable that selects an external corpus: the absolute path of a finos/morphir checkout.
[<Literal>]
let externalVariable = "MORPHIR_TOOLCHAIN_CONTRACT_ROOT"

/// tests/Fixtures/Toolchain, the pin written by scripts/pin-toolchain-contract.fsx. Provenance checks always read it.
let pinRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Toolchain"))

/// The pin's own root; an alias kept for the provenance tests.
let root = pinRoot

/// Where each part of the corpus lives in a finos/morphir checkout, as scripts/pin-toolchain-contract.fsx copies it.
module Layout =
    let unsigned = [ "docs"; "spec"; "toolchain"; "fixtures"; "0.1.0-draft.1" ]
    let schemas = [ "website"; "static"; "schemas" ]
    let pages = [ "docs"; "spec"; "toolchain" ]
    let workflow = [ "spec"; "toolchain"; "workflows"; "fsharp-compile-generate" ]

    /// The files whose absence means the checkout is not a toolchain contract corpus.
    let required =
        [ yield unsigned @ [ "cases.json" ]
          yield unsigned @ [ "signed"; "cases.json" ]
          yield unsigned @ [ "signed"; "repository.json" ]
          yield workflow @ [ "case.json" ]
          for page in [ "index"; "installation-receipt"; "manager-result"; "readiness-workflow"; "toolchain-lock"; "toolchain-request"; "toolset-recipe" ] ->
              pages @ [ $"{page}.md" ]
          for schema in [ "morphirup-result"; "toolchain-lock"; "toolchain-receipt"; "toolchain-request"; "toolchain-workflow-case"; "toolset-recipe" ] ->
              schemas @ [ $"{schema}-0.1.0-draft.1.schema.json" ] ]

let private under (root: string) (segments: string list) = Path.Combine(Array.ofList (root :: segments))

/// <summary>The external corpus root a value of <see cref="externalVariable"/> names: an absolute path to an existing
/// directory that holds every <see cref="Layout.required"/> file, without a trailing separator. Anything else is an
/// error that says what is wrong, so a run never falls back to the pin.</summary>
let validateExternal (value: string) : Result<string, string> =
    if String.IsNullOrWhiteSpace value then
        Error $"{externalVariable} is set but empty; unset it to run the pinned corpus"
    elif not (Path.IsPathFullyQualified value) then
        Error $"{externalVariable} must be an absolute path to a finos/morphir checkout, not '{value}'"
    else
        let full = Path.TrimEndingDirectorySeparator(Path.GetFullPath value)

        if not (Directory.Exists full) then
            Error $"{externalVariable} names '{full}', which is not a directory"
        else
            match Layout.required |> List.map (under full) |> List.filter (File.Exists >> not) with
            | [] -> Ok full
            | missing ->
                let names = missing |> List.map (fun path -> Path.GetRelativePath(full, path).Replace('\\', '/')) |> String.concat ", "
                Error $"{externalVariable} names '{full}', which is not a toolchain contract corpus: missing {names}"

/// The selected corpus: None for the pin, the external root, or the reason the external root is refused.
let external : Result<string option, string> =
    match Environment.GetEnvironmentVariable externalVariable with
    | null -> Ok None
    | value -> validateExternal value |> Result.map Some

/// The line the test entry point prints, first, when it runs an external corpus.
let marker (root: string) = $"External toolchain corpus: {root}"

/// A corpus path. A refused root never falls back to the pin: the test entry point stops before any test reads a
/// path, so the refused value is kept only so that nothing here throws while the module is initialized.
let private select (pinned: string) (segments: string list) =
    match external with
    | Ok None -> pinned
    | Ok(Some root) -> under root segments
    | Error _ -> under (Environment.GetEnvironmentVariable externalVariable |> Option.ofObj |> Option.defaultValue "") segments

/// The unsigned fixture directory; manifest paths are relative to it.
let unsigned = select (Path.Combine(pinRoot, "fixtures", "0.1.0-draft.1")) Layout.unsigned

/// The TEST-ONLY signed repository directory.
let signed = Path.Combine(unsigned, "signed")

let schemas = select (Path.Combine(pinRoot, "schemas")) Layout.schemas

let pages = select (Path.Combine(pinRoot, "pages")) Layout.pages

let workflow = select (Path.Combine(pinRoot, "workflows", "fsharp-compile-generate")) Layout.workflow

/// The pinned readiness workflow case, which the provenance checks read whatever corpus is selected.
let pinnedWorkflow = Path.Combine(pinRoot, "workflows", "fsharp-compile-generate")

/// The exact bytes of a fixture named relative to a directory, with `/` separators.
let bytesIn (directory: string) (relative: string) =
    File.ReadAllBytes(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)))

let bytes relative = bytesIn unsigned relative
