/// Document addresses: a document of a tree is addressed by what it is in the model, and its logical path is the
/// parent tree specification's, with no extension. Scenarios of the model-document-trees spec, requirement
/// "Documents are addressed by model identity".
module Morphir.IR.Tests.Trees.DocumentAddressTests

open Expecto
open Morphir.IR
open Morphir.IR.Trees
open Morphir.Models.TestSupport
open Morphir.Models.TestSupport.DistributionBuilder

let private acmeDemo = packageName "acme/demo"

let private billing = modulePath "billing"

let private sdk = packageName "morphir/sdk"

let private addressesOf distribution =
    (V4DocumentTree.split distribution).Documents
    |> Map.keys
    |> Set.ofSeq

let private stemName (stem: string) =
    match TreeStems.parseStem stem with
    | Ok(WholeStem parsed) -> parsed
    | other -> failtestf "Expected '%s' to read as a whole name, got %A" stem other

[<Tests>]
let tests =
    testList "Trees" [
        testList "Document addresses" [
            test "Two spellings of one name have one address" {
                let fromSplit = Own(TypeDef(billing, Name.fromString "Invoice line"))
                let fromStem = Own(TypeDef(billing, stemName "invoice-line"))

                Expect.equal
                    fromStem
                    fromSplit
                    "The address read from a stem is the address of the split"

                Expect.contains
                    (addressesOf DistributionMother.acmeDemo)
                    fromSplit
                    "The split of the single file has the address"

                let path = DocumentAddress.logicalPath acmeDemo fromSplit

                Expect.equal path "pkg/acme/demo/billing/invoice-line.type" "The logical path"
                Expect.isFalse (path.EndsWith ".json") "A logical path has no extension"
            }

            test "A dependency document is addressed by its package" {
                let basics = Dependency(sdk, Module(modulePath "basics"))

                Expect.contains
                    (addressesOf DistributionMother.acmeDemo)
                    basics
                    "The split has the dependency module"

                Expect.equal
                    (DocumentAddress.logicalPath acmeDemo basics)
                    "deps/morphir/sdk/@/basics/module"
                    "The package path ends at a bare @ segment, with no version"
            }

            testList "Logical paths" [
                for address, path in
                    [
                        Manifest, "manifest"
                        Own(Module billing), "pkg/acme/demo/billing/module"
                        Own(ValueDef(billing, name "total")), "pkg/acme/demo/billing/total.value"
                        Own(Module(modulePath "IO/v2")), "pkg/acme/demo/_io/v2/module"
                        Own(TypeDef(billing, name "con")), "pkg/acme/demo/billing/con_.type"
                        Dependency(
                            packageName "morphir/SDK",
                            TypeDef(modulePath "basics", name "int")
                        ),
                        "deps/morphir/_sdk/@/basics/int.type"
                        Dependency(
                            packageName "acme/ledger/core",
                            ValueDef(modulePath "accounts", name "zero")
                        ),
                        "deps/acme/ledger/core/@/accounts/zero.value"
                    ] do
                    test path {
                        Expect.equal
                            (DocumentAddress.logicalPath acmeDemo address)
                            path
                            "The logical path"
                    }
            ]

            test "Two packages that nest have distinct dependency paths" {
                let outer =
                    Dependency(packageName "acme/ledger", Module(modulePath "core/accounts"))

                let inner =
                    Dependency(packageName "acme/ledger/core", Module(modulePath "accounts"))

                Expect.notEqual
                    (DocumentAddress.logicalPath acmeDemo outer)
                    (DocumentAddress.logicalPath acmeDemo inner)
                    "The @ segment ends each package path"
            }
        ]
    ]
