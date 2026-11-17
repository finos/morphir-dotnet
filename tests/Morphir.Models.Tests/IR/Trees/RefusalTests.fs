/// The refusals of the document-tree store: a read of an invalid tree gives every error it finds, each with a stable
/// code and the file it concerns, and no tree. Scenarios of the model-document-trees spec, requirement "An invalid
/// tree is refused with a named diagnostic", and the other codes of a read and a write. Where the parent's v3 or v4
/// tree page names a diagnostic, the code is the parent's, and the message starts with the member's JSON location.
/// Each tree of files is a case of <c>DocumentTreeMother.Invalid</c>.
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

/// The one error of a refused read, with its code and file, and the JSON location its message starts with.
let private expectOne files (code: string) (file: string option) (location: string) =
    let error = oneError files
    Expect.equal (error.Code, error.File) (code, file) "The code and the file"
    Expect.stringStarts error.Message location "The message starts with the member's location"
    error

let private written tree =
    match Driver.write tree with
    | Ok files -> files
    | Result.Error errors -> failtestf "Expected the tree to be written, got %A" errors

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
                let error =
                    expectOne
                        (Invalid.v3ManifestOfAnotherRelease ())
                        "version_mismatch"
                        (Some "manifest.json")
                        "$.formatVersion"

                Expect.stringContains
                    error.Message
                    "\"3.0.0\""
                    "It names the release the manifest says"

                Expect.stringContains
                    error.Message
                    "\"3.1.0\""
                    "It names the release a v3 tree says"
            }

            test "A manifest of major 3 in another spelling is read by the v3 rules" {
                for files in
                    [
                        Invalid.v3ManifestSayingTheString3 ()
                        Invalid.v3ManifestSayingTheNumber31 ()
                    ] do
                    expectOne files "version_mismatch" (Some "manifest.json") "$.formatVersion"
                    |> ignore
            }

            test "Unknown member" {
                let error =
                    expectOne
                        (Invalid.v3ManifestWithAV4Member ())
                        "unknown_member"
                        (Some "manifest.json")
                        "$.version"

                Expect.stringContains error.Message "version" "It names the member"
            }

            test "Path over the budget" {
                let case = DocumentTreeMother.pathAtTheBudget
                let files = written case.Tree

                let longPaths =
                    [ case.OwnValue; case.DependencyValue ]
                    |> List.map (fun address -> (Driver.fileAt address files).Value)
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
                    ("missing_member", Some Invalid.invoiceTypeFile)
                    "The code, at the missing file's path"

                Expect.stringContains
                    error.Message
                    Invalid.billingModuleFile
                    "It names the module file"

                Expect.stringContains error.Message "'invoice'" "It names the type"
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

            test "A tree without a manifest" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.noManifest ())))
                    [ "tree.missing_manifest", None ]
                    "An error of the whole tree"
            }

            test "A file in the YAML profile" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.yamlFileBesideTheJsonProfile ())))
                    [ "tree.unsupported_profile", Some "pkg/acme/demo/billing/invoice.type.yaml" ]
                    "The YAML file is named"
            }

            test "A tree file outside a module directory" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.moduleFileInThePackageDirectory ())))
                    [ "tree.misplaced_file", Some "pkg/acme/demo/module.json" ]
                    "The module file in the package directory is named"
            }

            test "A JSON file in a module directory that is no tree file" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.unknownJsonFileInAModule ())))
                    [ "tree.unknown_file", Some "pkg/acme/demo/billing/notes.json" ]
                    "The notes file is named"
            }

            test "A directory that is not an escaped stem" {
                let errors = readRefused (Invalid.moduleDirectoryThatIsNotAStem ())

                Expect.isNonEmpty errors "The directory is refused"

                for error in errors do
                    Expect.equal error.Code "tree.invalid_stem" "The code"

                    Expect.stringStarts
                        (defaultArg error.File "")
                        "pkg/acme/demo/Billing/"
                        "A file below the directory"
            }

            test "A file whose content cannot be read" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.unreadableTypeFile ())))
                    [ "tree.unreadable_file", Some Invalid.invoiceTypeFile ]
                    "The unreadable file is named"
            }

            test "A file that is not JSON" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.moduleFileThatIsNotJson ())))
                    [ "invalid_json", Some Invalid.billingModuleFile ]
                    "The module file is named"
            }

            test "A fileNames entry that is not the cut stem of its name" {
                let error =
                    expectOne
                        (Invalid.fileNamesWithAStemOfAnotherName ())
                        "invalid_file_name"
                        (Some Invalid.billingModuleFile)
                        "$.fileNames.invoice"

                Expect.stringContains error.Message "invoice__00000000" "It names the stem"
            }

            test "A path budget under the minimum" {
                expectOne
                    (Invalid.pathBudgetUnderTheMinimum ())
                    "invalid_type"
                    (Some "manifest.json")
                    "$.pathBudget"
                |> ignore
            }

            test "A dependency listed twice" {
                expectOne
                    (Invalid.dependencyListedTwice ())
                    "duplicate_member"
                    (Some "manifest.json")
                    "$.dependencies[1]"
                |> ignore
            }

            test "A dependency that is the own package" {
                expectOne
                    (Invalid.ownPackageAsADependency ())
                    "invalid_distribution_shape"
                    (Some "manifest.json")
                    "$.dependencies[1]"
                |> ignore
            }

            test "A node file that holds the payload of another role" {
                expectOne
                    (Invalid.definitionFileHoldingASpec ())
                    "invalid_distribution_shape"
                    (Some Invalid.invoiceTypeFile)
                    "$.spec"
                |> ignore
            }

            test "A node file with neither def nor spec" {
                expectOne
                    (Invalid.nodeFileWithNeitherDefNorSpec ())
                    "invalid_distribution_shape"
                    (Some Invalid.invoiceTypeFile)
                    "$"
                |> ignore
            }

            test "Too many files" {
                let errors =
                    DocumentTreeStore.read
                        { ReadOptions.defaults with
                            MaxFiles = 2
                        }
                        (written DocumentTreeMother.acmeDemo)
                    |> refusal

                Expect.equal
                    (codesAndFiles errors)
                    [ "tree.too_many_files", None ]
                    "An error of the whole tree"
            }

            test "A file too large" {
                let files = written DocumentTreeMother.acmeDemo
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

        testList "Refusals of a v3 tree" [
            test "An access in a module specification" {
                expectOne
                    (Invalid.v3ModuleSpecificationWithAccess ())
                    "unknown_member"
                    (Some Invalid.v3PricingModuleFile)
                    "$.access"
                |> ignore
            }

            test "The v4 module alias for path" {
                expectOne
                    (Invalid.v3ModuleFileWithTheV4ModuleAlias ())
                    "unknown_member"
                    (Some Invalid.v3PricingModuleFile)
                    "$.module"
                |> ignore
            }

            test "A payload that is not classic JSON" {
                expectOne
                    (Invalid.v3PayloadThatIsNotClassicJson ())
                    "invalid_type"
                    (Some Invalid.v3InvoiceTypeFile)
                    "$.def"
                |> ignore
            }

            test "A file without a format version" {
                expectOne
                    (Invalid.v3ModuleFileWithoutFormatVersion ())
                    "missing_format_version"
                    (Some Invalid.v3PricingModuleFile)
                    "$"
                |> ignore
            }

            test "A file with two format versions" {
                expectOne
                    (Invalid.v3ModuleFileWithTwoFormatVersions ())
                    "duplicate_format_version"
                    (Some Invalid.v3PricingModuleFile)
                    "$.formatVersion"
                |> ignore
            }

            test "A spec with an access" {
                expectOne
                    (Invalid.v3SpecWithAccess ())
                    "invalid_distribution_shape"
                    (Some Invalid.v3DefaultCurrencyValueFile)
                    "$.spec"
                |> ignore
            }

            test "A def with another member" {
                let error =
                    expectOne
                        (Invalid.v3DefinitionWithAnotherMember ())
                        "invalid_distribution_shape"
                        (Some Invalid.v3InvoiceTypeFile)
                        "$.def"

                Expect.stringContains error.Message "'since'" "It names the member"
            }

            test "Missing definition file" {
                let error = oneError (Invalid.v3ModuleListingAMissingType ())

                Expect.equal
                    (error.Code, error.File)
                    ("missing_member", Some Invalid.v3InvoiceTypeFile)
                    "The code, at the missing file's path"

                Expect.stringContains
                    error.Message
                    Invalid.v3PricingModuleFile
                    "It names the module file"

                Expect.stringContains error.Message "'invoice'" "It names the type"
            }

            test "A file outside the tree" {
                Expect.equal
                    (codesAndFiles (readRefused (Invalid.v3TypeFileOutsideTheTree ())))
                    [ "tree.outside_tree", Some "invoice.type.json" ]
                    "The type file beside pkg/ is named"
            }
        ]

        testList "Refusals of a write" [
            test "A classic float that classic JSON cannot spell" {
                let errors = refusal (Driver.write DocumentTreeMother.v3LibraryWithANonFiniteFloat)

                Expect.equal
                    (codesAndFiles errors)
                    [ "literal_out_of_range", Some Invalid.v3DefaultCurrencyValueFile ]
                    "The value file is refused, and nothing is written"
            }

            test "A path budget under the minimum" {
                let errors =
                    refusal (
                        DocumentTreeStore.write
                            { WriteOptions.defaults with
                                PathBudget = 63
                            }
                            DocumentTreeMother.v3Library
                    )

                Expect.equal
                    (codesAndFiles errors)
                    [ "tree.unsupported_option", None ]
                    "An error of the whole tree"

                Expect.stringContains errors.Head.Message "64" "It names the minimum"
            }
        ]
    ]
