/// The refusals of the document-tree store: a read of an invalid tree gives every error it finds, each with a stable
/// code and the file it concerns, and no tree. Scenarios of the model-document-trees spec, requirement "An invalid
/// tree is refused with a named diagnostic", and the other codes of a read. Each tree is a case of
/// <c>DocumentTreeMother.Invalid</c>.
module Morphir.IR.Tests.Trees.RefusalTests

open Expecto
open Morphir.IR.Trees
open Morphir.Models.TestSupport
open Morphir.IR.Tests.Trees.StoreTestKit

module Driver = DocumentTreeStoreDriver
module Invalid = DocumentTreeMother.Invalid

/// The errors of reading the files with the default limits.
let private readRefused files = refusal (Driver.read files)

/// The one error of a refused read.
let private oneError files =
    match readRefused files with
    | [ error ] -> error
    | errors -> failtestf "Expected one error, got %A" errors

[<Tests>]
let tests =
    testList "Store" [
        testList "Refusals" [
            test "Ambiguous profile" {
                let error = oneError (Invalid.bothManifestProfiles ())

                Expect.equal error.Code "tree.ambiguous_profile" "The code"
                Expect.equal error.File None "An error of the whole tree"
                Expect.stringContains error.Message "manifest.json" "It names the JSON manifest"
                Expect.stringContains error.Message "manifest.yaml" "It names the YAML manifest"
            }

            test "A v3 manifest of another release" {
                let error = oneError (Invalid.v3ManifestOfAnotherRelease ())

                Expect.equal
                    (error.Code, error.File)
                    ("tree.v3_version", Some "manifest.json")
                    "The code and the file"

                Expect.stringContains
                    error.Message
                    "\"3.0.0\""
                    "It names the release the manifest says"

                Expect.stringContains
                    error.Message
                    "\"3.1.0\""
                    "It names the release a v3 tree says"
            }

            test "Unknown member" {
                let error = oneError (Invalid.v3ManifestWithAV4Member ())

                Expect.equal
                    (error.Code, error.File)
                    ("unknown_member", Some "manifest.json")
                    "The code and the file"

                Expect.stringContains error.Message "version" "It names the member"
            }

            test "Path over the budget" {
                let case = DocumentTreeMother.pathAtTheBudget

                let longPaths =
                    [ case.OwnValue; case.DependencyValue ]
                    |> List.map (fun address ->
                        match Driver.write case.Tree with
                        | Ok files -> (Driver.fileAt address files).Value
                        | Result.Error errors ->
                            failtestf "Expected the tree to be written, got %A" errors
                    )
                    |> List.sort

                let errors = readRefused (Invalid.pathOverTheManifestBudget ())

                Expect.equal
                    (codesAndFiles errors
                     |> List.sortBy snd)
                    [ for path in longPaths -> "tree.over_budget", Some path ]
                    "Each path longer than the manifest's budget is named"

                for error in errors do
                    Expect.stringContains error.Message "3999" "It names the budget"
            }

            test "Missing definition file" {
                let error = oneError (Invalid.moduleListingAMissingType ())

                Expect.equal
                    (error.Code, error.File)
                    ("tree.missing_file", Some Invalid.billingModuleFile)
                    "The code and the module file"

                Expect.stringContains error.Message "'invoice'" "It names the type"

                Expect.stringContains
                    error.Message
                    Invalid.invoiceTypeFile
                    "It names the file it looked for"
            }

            test "A file outside the tree" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.typeFileOutsideTheTree ())))
                    [ "tree.outside_tree", Some "invoice.type.json" ]
                    "The type file beside pkg/ is named"
            }

            test "Versioned dependency segment" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.versionedDependencySegment ())))
                    [ "tree.versioned_dependency", Some "deps/morphir/sdk/@3.0.0/" ]
                    "The versioned directory is named once"
            }

            test "A file below another package" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.fileBelowAnotherPackage ())))
                    [ "tree.outside_package", Some "pkg/acme/other/billing/module.json" ]
                    "The file below pkg/acme/other/ is named"
            }

            test "A node file whose stem is not its name's" {
                let error = oneError (Invalid.typeFileWithAnotherName ())

                Expect.equal
                    (error.Code, error.File)
                    ("tree.name_mismatch", Some Invalid.invoiceTypeFile)
                    "The code and the file"

                Expect.stringContains error.Message "'total'" "It names the name the file states"
            }

            test "A module file whose path is not its directory's" {
                let error = oneError (Invalid.moduleFileWithAnotherPath ())

                Expect.equal
                    (error.Code, error.File)
                    ("tree.path_mismatch", Some Invalid.billingModuleFile)
                    "The code and the file"

                Expect.stringContains error.Message "'invoices'" "It names the path the file states"
            }

            test "A document in two places" {
                let error = oneError (Invalid.typeInTwoPlaces ())

                Expect.equal error.Code "tree.duplicate_document" "The code"
                Expect.stringContains error.Message Invalid.invoiceTypeFile "It names the type file"

                Expect.stringContains
                    error.Message
                    Invalid.billingModuleFile
                    "It names the module file"
            }

            test "Too many files" {
                let errors =
                    DocumentTreeStore.read
                        { ReadOptions.defaults with
                            MaxFiles = 2
                        }
                        (match Driver.write DocumentTreeMother.acmeDemo with
                         | Ok files -> files
                         | Result.Error errors ->
                             failtestf "Expected the tree to be written, got %A" errors)
                    |> refusal

                Expect.equal
                    (codesAndFiles errors)
                    [ "tree.too_many_files", None ]
                    "An error of the whole tree"
            }

            test "A file too large" {
                let files =
                    match Driver.write DocumentTreeMother.acmeDemo with
                    | Ok files -> files
                    | Result.Error errors ->
                        failtestf "Expected the tree to be written, got %A" errors

                let manifest = (Driver.texts files)["manifest.json"]
                let limit = int64 manifest.Length

                let errors =
                    DocumentTreeStore.read
                        { ReadOptions.defaults with
                            MaxFileBytes = limit
                        }
                        files
                    |> refusal

                Expect.isNonEmpty errors "A file over the limit is refused"

                for error in errors do
                    Expect.equal error.Code "tree.file_too_large" $"{error.File} is too large"

                    Expect.notEqual
                        error.File
                        (Some "manifest.json")
                        "The manifest is within the limit"

                    Expect.stringContains error.Message (string limit) "It names the limit"
            }
        ]
    ]
