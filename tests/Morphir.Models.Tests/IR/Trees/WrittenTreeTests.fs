/// The trees that <c>FixtureCatalog.Trees</c> pins for the tests that need a tree on disk, such as the black-box
/// scenarios of <c>model verify</c>, are the trees that the store writes from their mother cases. A change of the
/// store that writes other bytes fails here, and the pinned files are then written again from the mother (see
/// <c>tests/Fixtures/MorphirTrees/PROVENANCE.md</c>).
module Morphir.IR.Tests.Trees.WrittenTreeTests

open System.Text
open Expecto
open Morphir.Files
open Morphir.Models.TestSupport
open Morphir.TestSupport.Fixtures

module Driver = DocumentTreeStoreDriver
module Invalid = DocumentTreeMother.Invalid

let private written tree =
    match Driver.write tree with
    | Ok files -> files
    | Result.Error errors -> failtestf "Expected the tree to be written, got %A" errors

/// The text of each file of a catalog tree, by location.
let private pinned (tree: FixtureTree) : Map<string, string> =
    tree.Files
    |> Seq.map (fun file -> file.Location, Encoding.UTF8.GetString(FixtureCatalog.ReadBytes file.Entry))
    |> Map.ofSeq

let private expectSameFiles (files: VFileTree) (tree: FixtureTree) =
    let expected = Driver.texts files
    let actual = pinned tree

    Expect.sequenceEqual
        (Map.keys actual)
        (Map.keys expected)
        $"The catalog tree '{tree.Intent}' has the files that the mother case has"

    for KeyValue(location, text) in expected do
        Expect.equal actual[location] text $"{location} of '{tree.Intent}' is what the mother case has. Write it again from the mother if the store changed on purpose:\n{text}"

[<Tests>]
let tests =
    testList "Store" [
        testList "Written trees of the catalog" [
            test "The written v4 tree is what the store writes for its mother case" {
                expectSameFiles
                    (written DocumentTreeMother.v4LibraryWithDependency)
                    FixtureCatalog.Trees.WrittenV4LibraryWithDependency
            }

            test "The written v3 tree is what the store writes for its mother case" {
                expectSameFiles
                    (written DocumentTreeMother.v3LibraryWithDependency)
                    FixtureCatalog.Trees.WrittenV3LibraryWithDependency
            }

            test "The written v4 tree with an unknown type tag is its invalid mother case" {
                let faulty = FixtureCatalog.Trees.WrittenV4WithAnUnknownTypeTag
                expectSameFiles (Invalid.typeFileWithAnUnknownTypeTag ()) faulty.Tree
                Expect.equal (Option.ofObj faulty.Fault.File) (Some Invalid.financeInvoiceTypeFile) "The fault is in the type file"
            }

            test "The written v3 tree with an unknown member is its invalid mother case" {
                let faulty = FixtureCatalog.Trees.WrittenV3WithANestedUnknownMember
                expectSameFiles (Invalid.v3ValueFileWithANestedUnknownMember ()) faulty.Tree
                Expect.equal (Option.ofObj faulty.Fault.File) (Some Invalid.v3DefaultCurrencyValueFile) "The fault is in the value file"
            }

            test "The written v4 tree with no file for one value is its invalid mother case" {
                let faulty = FixtureCatalog.Trees.WrittenV4WithAMissingValueFile
                expectSameFiles (Invalid.moduleListingAMissingValue ()) faulty.Tree
                Expect.equal (Option.ofObj faulty.Fault.File) (Some Invalid.financeDefaultCurrencyValueFile) "The fault is the missing value file"
            }

            test "A module file that lists a value with no file is refused at the missing file's path" {
                match Driver.read (Invalid.moduleListingAMissingValue ()) with
                | Ok _ -> failtest "Expected the tree to be refused"
                | Result.Error errors ->
                    let error = Expect.wantSome (List.tryExactlyOne errors) $"One error: {errors}"

                    Expect.equal
                        (error.Code, error.File)
                        ("missing_member", Some Invalid.financeDefaultCurrencyValueFile)
                        "The code, at the missing file's path"

                    Expect.stringContains error.Message "pkg/my-org/finance/pricing/models/module.json" "It names the module file"
                    Expect.stringContains error.Message "'default-currency'" "It names the value"
            }
        ]
    ]
