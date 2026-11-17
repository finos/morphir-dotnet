/// The spellings a context table admits: every name, path and FQName of the parent's naming grammar, as its naming
/// corpus (FixtureCatalog.Context.Naming) gives them in the shipped style, and nothing else. A reference expands to
/// its entry's spelling exactly as the file writes it.
module Morphir.IR.Tests.Files.SpellingGrammarTests

open System
open System.Text.Json
open Expecto
open Morphir.IR.Files
open Morphir.IR.Tests.Trees.StoreTestKit
open Morphir.Models.TestSupport

module Mother = MorphirFileMother

let private familyOf (file: MorphirFileMother.RespelledFile) =
    if file.Ir = 3 then IrFamily.Classic3 else IrFamily.V4

let private read (file: MorphirFileMother.RespelledFile) =
    MorphirFile.readEnvelope ContextLimits.draft1 file.File (ReadOnlyMemory file.Bytes)
    |> Result.bind (MorphirFile.expand (familyOf file) file.File)

let private expandsToItsSpelling (file: MorphirFileMother.RespelledFile) =
    match read file with
    | Ok document -> Expect.equal (compact (document.GetRawText())) (compact file.ExpandedText) "the reference expands to the entry's spelling"
    | Error errors -> failtestf "The spelling was not admitted: %A" errors

let private corpus = Mother.namingCorpus ()

[<Tests>]
let tests =
    testList "Context spellings follow the naming grammar" [
        testList "Each canonical name is admitted as a names entry and expands to its text" [
            for name in corpus.Names -> test name.Canonical { expandsToItsSpelling (Mother.withNameSpelling name.Canonical) }
        ]

        // The corpus records no word array for FR2052A: its classic form is ambiguous.
        testList "Each canonical name with a legacy word array expands in a classic slot to that array" [
            for name in corpus.Names do
                match name.LegacyArray with
                | null -> ()
                | words -> yield test name.Canonical { expandsToItsSpelling (Mother.withClassicNameSpelling name.Canonical words) }
        ]

        testList "Each canonical path is admitted as a packagePaths entry and expands to its text" [
            for path in corpus.Paths -> test path { expandsToItsSpelling (Mother.withPackagePathSpelling path) }
        ]

        testList "Each canonical path is admitted as a modulePaths entry and expands to its text" [
            for path in corpus.Paths -> test path { expandsToItsSpelling (Mother.withModulePathSpelling path) }
        ]

        testList "Each canonical FQName is admitted as an fqNames entry and expands to its text" [
            for fqName in corpus.FqNames -> test fqName { expandsToItsSpelling (Mother.withFqNameSpelling fqName) }
        ]

        testList "Each text the grammar refuses as a name is refused as a names entry" [
            for reject in corpus.Rejects |> Seq.filter (fun reject -> not reject.ValidAsName) ->
                test $"'{reject.Input}'" {
                    let file = Mother.withNameSpelling reject.Input

                    match read file |> refusal with
                    | [ error ] ->
                        Expect.equal error.Code "context.invalid_entry" "the code"
                        Expect.equal error.File (Some file.File) "the file"
                    | errors -> failtestf "Expected one context.invalid_entry, got %A" errors
                }
        ]

        testList "Each text the grammar accepts as a name is admitted as a names entry" [
            for reject in corpus.Rejects |> Seq.filter (fun reject -> reject.ValidAsName) ->
                test reject.Input { expandsToItsSpelling (Mother.withNameSpelling reject.Input) }
        ]
    ]
