module Morphir.Compilation.Tests.RegistryTests

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.IR.Model

let private version text =
    match Versioning.parse text with
    | Ok version -> version
    | Error error -> failwith $"{text}: {error}"

let private writer identity family (versions: string list) =
    { new IIrWriter with
        member _.Identity = ComponentId.Create identity
        member _.Family = family

        member _.Versions =
            versions
            |> List.map version
            |> List.toArray
            :> IReadOnlyList<_>

        member _.Write(_, _, _) = Checked.pass [||]
    }

let private provider identity (patterns: string list) =
    { new IProjectFileProvider with
        member _.Identity = ComponentId.Create identity
        member _.Patterns = List.toArray patterns :> IReadOnlyList<_>

        member _.ExpandAsync(file, _) =
            Task.FromResult(Checked.pass (SourceSelection.Create("/work", [ "A.fs" ], file)))
    }

let private codes (result: Checked<'T>) =
    result.Diagnostics
    |> Seq.map (fun d ->
        d.CodeOrNull
        |> Option.ofObj
        |> Option.map (fun x -> x.Value)
    )
    |> List.ofSeq

[<Tests>]
let tests =
    testList "Registries" [
        testList "WriterRegistry" [
            let registry =
                WriterRegistry.Create [
                    writer "classic" ModelFamily.Classic [ "1"; "2"; "3" ]
                    writer "v4" ModelFamily.V4 [ "4" ]
                ]

            test "selects by the equivalence rule and keeps the requested version" {
                match registry.Resolve(version "4.0.0") with
                | Passed(resolved, []) ->
                    Expect.equal resolved.Writer.Identity.Value "v4" "writer"

                    Expect.equal
                        resolved.Target
                        {
                            Family = ModelFamily.V4
                            Version = version "4.0.0"
                        }
                        "target"
                | other -> failtest $"{other}"
            }

            test "a pre-release no writer declares is refused, keeping its spelling" {
                let result = registry.Resolve(version "4.1.0-draft.1")
                Expect.isFalse result.IsPassed "refused"
                Expect.equal (codes result) [ Some "compile.unsupported_ir_version" ] "code"
                Expect.stringContains result.Diagnostics[0].Message "4.1.0-draft.1" "spelling kept"

                Expect.stringContains
                    result.Diagnostics[0].Message
                    "1, 2, 3, 4"
                    "declared versions listed"
            }

            test "a declared pre-release is selected only for that exact version" {
                let drafts =
                    WriterRegistry.Create [
                        writer "v4" ModelFamily.V4 [ "4" ]
                        writer "draft" ModelFamily.V4 [ "4.1.0-draft.1" ]
                    ]

                match drafts.Resolve(version "4.1.0-draft.1") with
                | Passed(resolved, _) ->
                    Expect.equal resolved.Writer.Identity.Value "draft" "draft writer"
                | other -> failtest $"{other}"

                Expect.isFalse
                    (drafts.Resolve(version "4.1.0")).IsPassed
                    "the release is not the draft"
            }

            test "two writers for one version are ambiguous" {
                let ambiguous =
                    WriterRegistry.Create [
                        writer "a" ModelFamily.V4 [ "4" ]
                        writer "b" ModelFamily.V4 [ "4.0.0" ]
                    ]

                Expect.equal
                    (codes (ambiguous.Resolve(version "4")))
                    [ Some "compile.ambiguous_ir_writer" ]
                    "code"
            }
        ]

        testList "ProjectFileProviderRegistry" [
            let registry =
                ProjectFileProviderRegistry.Create [
                    provider "msbuild" [ "*.*proj" ]
                    provider "gleam" [ "gleam.toml" ]
                ]

            for file in
                [ "/work/src/Acme.CarRental.fsproj"; "/work/App.csproj"; "/work/Build.PROJ" ] do
                test $"msbuild recognizes {file}" {
                    match registry.Resolve file with
                    | Passed(found, _) -> Expect.equal found.Identity.Value "msbuild" "provider"
                    | other -> failtest $"{other}"
                }

            test "an unknown project file names the registered patterns" {
                let result = registry.Resolve "/work/build.gradle"
                Expect.equal (codes result) [ Some "sources.unknown_project_file" ] "code"
                Expect.stringContains result.Diagnostics[0].Message "*.*proj, gleam.toml" "patterns"
            }

            test "two matching providers are ambiguous" {
                let ambiguous =
                    ProjectFileProviderRegistry.Create [
                        provider "a" [ "*.fsproj" ]
                        provider "b" [ "*proj" ]
                    ]

                Expect.equal
                    (codes (ambiguous.Resolve "/work/A.fsproj"))
                    [ Some "sources.ambiguous_project_file" ]
                    "code"
            }
        ]

        testList "SourceSelection" [
            test "keeps document order" {
                let selection =
                    SourceSelection.Create("/work/src", [ "Rates.fs"; "Pricing.fs" ], null)

                Expect.sequenceEqual selection.Documents [ "Rates.fs"; "Pricing.fs" ] "order"
                Expect.isNull selection.ProjectFileOrNull "no project file"
            }

            for documents in
                [
                    [ "../shared/Common.fs" ]
                    [ "/abs/A.fs" ]
                    [ "a\\b.fs" ]
                    [ "A.fs"; "A.fs" ]
                    []
                ] do
                test $"refuses %A{documents}" {
                    Expect.throwsT<System.ArgumentException>
                        (fun () ->
                            SourceSelection.Create("/work", documents, null)
                            |> ignore
                        )
                        "refused"
                }
        ]

        testList "ComponentId" [
            for literal in [ "msbuild"; "morphir-dotnet.classic-json"; "a_b" ] do
                test $"accepts {literal}" {
                    Expect.equal (ComponentId.Create literal).Value literal "value"
                }

            for text in [ ""; "MSBuild"; "a..b"; "1a" ] do
                test $"refuses '{text}'" { Expect.isError (ComponentId.Parse text) "refused" }
        ]

        testList "SourceSelection.Parse" [
            for root, documents, failure in
                [
                    "", [ "A.fs" ], SourceSelectionFailure.EmptyRoot
                    "/work", [], SourceSelectionFailure.NoDocuments
                    "/work",
                    [ "../shared/Common.fs" ],
                    SourceSelectionFailure.NotPortable "../shared/Common.fs"
                    "/work",
                    [ "C:/outside/A.fs" ],
                    SourceSelectionFailure.NotPortable "C:/outside/A.fs"
                    "/work", [ "src/C:A.fs" ], SourceSelectionFailure.NotPortable "src/C:A.fs"
                    "/work", [ "A.fs"; "A.fs" ], SourceSelectionFailure.Repeated "A.fs"
                ] do
                test $"refuses {failure}" {
                    match SourceSelection.Parse(root, documents, null) with
                    | Error actual -> Expect.equal actual failure "failure"
                    | Ok selection -> failtest $"{selection}"
                }
        ]

        testList "SourceSelectionFailure.Match" [
            for failure, expected in
                [
                    SourceSelectionFailure.EmptyRoot, "empty root"
                    SourceSelectionFailure.NoDocuments, "no documents"
                    SourceSelectionFailure.NotPortable "C:/A.fs", "not portable C:/A.fs"
                    SourceSelectionFailure.Repeated "A.fs", "repeated A.fs"
                ] do
                test $"calls the function of {failure}" {
                    let actual =
                        failure.Match(
                            (fun () -> "empty root"),
                            (fun () -> "no documents"),
                            (fun path -> $"not portable {path}"),
                            (fun path -> $"repeated {path}")
                        )

                    Expect.equal actual expected "the case's function"
                }
        ]

        test "targets for 3 and 3.0.0 are equal and hash alike" {
            let classic = {
                Family = ModelFamily.Classic
                Version = version "3"
            }

            let release = {
                Family = ModelFamily.Classic
                Version = version "3.0.0+build.1"
            }

            Expect.equal classic release "equal under VER-1"
            Expect.equal (classic.GetHashCode()) (release.GetHashCode()) "same hash"

            Expect.notEqual
                classic
                { classic with
                    Version = version "3.1.0"
                }
                "3.1.0 differs"

            Expect.notEqual classic { classic with Family = ModelFamily.V4 } "family differs"
        }
    ]
