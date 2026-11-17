/// <summary>
/// The named document trees that store tests share. Each case is the split of a model named for the intent a test
/// needs: most are cases of <see cref="DistributionMother"/>, and the cases of the path budget and of a case-only
/// collision are built here, since they need the logical paths of <c>DocumentAddress</c>. The classic cases hold
/// numbered attribute slots, and <see cref="Invalid"/> has one tree of files for each refusal of a read.
/// </summary>
module Morphir.Models.TestSupport.DocumentTreeMother

open System.Text.Json
open Morphir.Files
open Morphir.IR
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.Models.TestSupport.DistributionBuilder

module Driver = DocumentTreeStoreDriver

/// <summary>The stored tree of a v4 model: its split.</summary>
let treeOf (distribution: Distribution) : StoredTree =
    V4Tree(V4DocumentTree.split distribution)

/// <summary>The worked example of the design: <c>acme/demo</c> with module <c>billing</c> and dependency <c>morphir/sdk</c>.</summary>
let acmeDemo: StoredTree = treeOf DistributionMother.acmeDemo

/// <summary>A library with private members, a private module, docs, and a dependency with two modules.</summary>
let privateAndDocumentedMembers: StoredTree =
    treeOf DistributionMother.privateAndDocumentedMembers

/// <summary>An application whose dependency is a definition under <c>deps/</c>.</summary>
let v4Application: StoredTree = treeOf DistributionMother.v4Application

/// <summary>A specification whose modules carry annotations, which a 4.0.0 module file has no place for.</summary>
let v4SpecsWithModuleAnnotations: StoredTree =
    treeOf DistributionMother.v4SpecsWithModuleAnnotations

/// <summary>A library with two type names, <c>invoice line</c> and the one word <c>invoice-line</c>, that have one stem.</summary>
let collidingStems: StoredTree = treeOf DistributionMother.collidingStems

/// <summary>A library whose module and types are Windows device names.</summary>
let windowsReservedStems: StoredTree =
    treeOf DistributionMother.windowsReservedStems

/// <summary>A library with one module that has no types and no values.</summary>
let emptyModule: StoredTree = treeOf DistributionMother.emptyModule

/// <summary>A library with no modules: the tree is its manifest alone.</summary>
let noModules: StoredTree = treeOf DistributionMother.noModules

/// <summary>A library with a dependency that has no modules, which only the manifest records.</summary>
let dependencyWithoutModules: StoredTree =
    treeOf DistributionMother.dependencyWithoutModules

/// <summary>The two type names of <c>caseOnlyCollidingStems</c>: <c>Invoice</c> and <c>INVOICE</c>.</summary>
module CaseOnly =
    /// <summary>A word with one capital, <c>Invoice</c>, which no canonical spelling has.</summary>
    let capitalized = Name.fromList [ "Invoice" ]

    /// <summary>The initialism <c>INVOICE</c>.</summary>
    let initialism = name "INVOICE"

/// <summary>
/// A library with one module whose two type names, <see cref="CaseOnly.capitalized"/> and
/// <see cref="CaseOnly.initialism"/>, differ only in case. Both escape to the stem <c>_invoice</c>, so a tree on a
/// file system that ignores case has one file for the two.
/// </summary>
let caseOnlyCollidingStems: StoredTree =
    aV4Library (packageName "my-org/finance")
    |> withModule
        (modulePath "billing")
        (withType CaseOnly.capitalized (alias Sdk.int)
         >> withType CaseOnly.initialism (alias Sdk.string))
    |> buildV4
    |> treeOf

/// <summary>The path budget that a written tree uses unless the caller gives another.</summary>
let defaultBudget = 4000

/// <summary>A tree for the path budget, with the addresses of its two long values.</summary>
type BudgetCase = {
    /// <summary>The tree.</summary>
    Tree: StoredTree
    /// <summary>The value of the own package whose path is the length the case is about.</summary>
    OwnValue: DocumentAddress
    /// <summary>The value of a deep dependency package whose path is the length the case is about.</summary>
    DependencyValue: DocumentAddress
}

let private finance = packageName "my-org/finance"
let private pricing = modulePath "pricing/models"
let private deepPackage = packageName "acme/ledger/core/regional/settlement"
let private aging = modulePath "accounts/receivable/aging"

/// A name whose escaped stem has exactly `length` characters: words of "segments" joined by hyphens.
let private nameOfLength (length: int) =
    let text =
        (String.replicate
            (length / 9
             + 1)
            "segments-")
            .Substring(0, length)

    let text =
        if text.EndsWith "-" then
            text.Substring(0, length - 1)
            + "x"
        else
            text

    name text

/// The stem length that gives the address's file a physical path of `target` characters, with the extension.
let private stemLengthFor (target: int) (address: Name -> DocumentAddress) =
    let probe =
        DocumentAddress.logicalPath finance (address (name "x"))
        + ".json"

    target
    - (probe.Length
       - 1)

let private budgetCase (target: int) =
    let ownAddress n = Own(ValueDef(pricing, n))

    let dependencyAddress n =
        Dependency(deepPackage, ValueDef(aging, n))

    let ownName = nameOfLength (stemLengthFor target ownAddress)
    let dependencyName = nameOfLength (stemLengthFor target dependencyAddress)

    {
        Tree =
            aV4Library finance
            |> withModule pricing (withValue ownName (Constant(WholeValue 0L)))
            |> withDependencyModule
                deepPackage
                aging
                (withValue dependencyName (Constant(WholeValue 1L)))
            |> buildV4
            |> treeOf
        OwnValue = ownAddress ownName
        DependencyValue = dependencyAddress dependencyName
    }

/// <summary>
/// A library with a value whose physical path, <c>pkg/…/&lt;stem&gt;.value.json</c>, has exactly
/// <see cref="defaultBudget"/> characters, and a value of the deep dependency package
/// <c>acme/ledger/core/regional/settlement</c> whose path below <c>deps/…/@/</c> has the same length. Neither is cut.
/// </summary>
let pathAtTheBudget: BudgetCase = budgetCase defaultBudget

/// <summary>
/// <see cref="pathAtTheBudget"/> with each long stem one character longer, so each path is one over the budget and
/// its stem is cut.
/// </summary>
let pathOverTheBudget: BudgetCase =
    budgetCase (
        defaultBudget
        + 1
    )

/// <summary>A classic model as <c>ClassicJson</c> reads it: attributes are JSON values.</summary>
type JsonClassicModel = Morphir.IR.Classic.Distribution.Distribution<JsonElement, JsonElement>

/// <summary>
/// The classic model with each attribute slot holding its own JSON object, <c>{"slot": n}</c>, numbered in the
/// order of the model, so a store or a join that moves, swaps or drops one attribute gives other bytes.
/// </summary>
let withNumberedSlots
    (distribution: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>)
    : JsonClassicModel =
    let counter = ref 0

    let next () =
        counter.Value <-
            counter.Value
            + 1

        use document = JsonDocument.Parse($"{{\"slot\":{counter.Value}}}")
        document.RootElement.Clone()

    Morphir.IR.Classic.Attributes.mapDistribution
        (fun () -> next ())
        (fun _ -> next ())
        distribution

/// <summary>The stored tree of a classic model: its split.</summary>
let classicTreeOf (distribution: JsonClassicModel) : StoredTree =
    ClassicTree(ClassicDocumentTree.split distribution)

/// <summary>The classic library of <c>DistributionMother.v3Library</c>, with numbered attribute slots.</summary>
let v3Library: StoredTree =
    classicTreeOf (withNumberedSlots DistributionMother.v3Library)

/// <summary>The classic Specs of <c>DistributionMother.v3Specs</c>, with numbered attribute slots.</summary>
let v3Specs: StoredTree =
    classicTreeOf (withNumberedSlots DistributionMother.v3Specs)

/// <summary>
/// The classic library of <c>DistributionMother.v3PrivateAndDocumentedMembers</c>, with numbered attribute slots:
/// a private module, private members, docs and a dependency with two modules.
/// </summary>
let v3PrivateAndDocumentedMembers: StoredTree =
    classicTreeOf (withNumberedSlots DistributionMother.v3PrivateAndDocumentedMembers)

/// <summary>
/// One tree of files for each refusal of a read, each named for what is wrong with it. Each is a tree that the store
/// writes when it is asked for, with one change. The files of <see cref="acmeDemo"/> are <c>manifest.json</c>,
/// <c>pkg/acme/demo/billing/module.json</c>, <c>…/invoice.type.json</c>, <c>…/total.value.json</c> and the
/// files of <c>morphir/sdk</c> below <c>deps/morphir/sdk/@/</c>.
/// </summary>
module Invalid =
    let private written tree =
        match Driver.write tree with
        | Ok files -> files
        | Result.Error errors -> failwithf "The mother's tree is not written: %A" errors

    let private acmeFiles () = written acmeDemo

    let private billing = "pkg/acme/demo/billing/"

    /// <summary>The module file of <c>billing</c> in <see cref="acmeDemo"/>.</summary>
    let billingModuleFile =
        billing
        + "module.json"

    /// <summary>The type file of <c>invoice</c> in <see cref="acmeDemo"/>.</summary>
    let invoiceTypeFile =
        billing
        + "invoice.type.json"

    /// <summary><see cref="acmeDemo"/> with a YAML manifest, <c>manifest.yaml</c>, beside its JSON one.</summary>
    let bothManifestProfiles () : VFileTree =
        acmeFiles ()
        |> Driver.withFile
            "manifest.yaml"
            "formatVersion: 4\ndistribution: Library\npackage: acme/demo\npathBudget: 4000\n"

    /// <summary><see cref="v3Library"/> with a manifest that says the release <c>"3.0.0"</c>, not <c>"3.1.0"</c>.</summary>
    let v3ManifestOfAnotherRelease () : VFileTree =
        written v3Library
        |> Driver.withMember "manifest.json" "formatVersion" "\"3.0.0\""

    /// <summary>
    /// <see cref="v3Library"/> with a <c>version</c> member in its manifest, which a v4 manifest may carry and a v3
    /// manifest may not.
    /// </summary>
    let v3ManifestWithAV4Member () : VFileTree =
        written v3Library
        |> Driver.withMember "manifest.json" "version" "\"1.2.0\""

    /// <summary>
    /// <see cref="pathAtTheBudget"/>, whose two long paths have 4000 characters, with a manifest that says a path
    /// budget of 3999.
    /// </summary>
    let pathOverTheManifestBudget () : VFileTree =
        written pathAtTheBudget.Tree
        |> Driver.withMember
            "manifest.json"
            "pathBudget"
            (string (
                defaultBudget
                - 1
            ))

    /// <summary><see cref="acmeDemo"/> with no file for the type <c>invoice</c>, which its module file lists.</summary>
    let moduleListingAMissingType () : VFileTree =
        acmeFiles ()
        |> Driver.withoutFile invoiceTypeFile

    /// <summary>
    /// <see cref="acmeDemo"/> with a copy of the type file of <c>invoice</c> at the root of the tree, beside
    /// <c>pkg/</c>, where it claims to be part of the tree.
    /// </summary>
    let typeFileOutsideTheTree () : VFileTree =
        acmeFiles ()
        |> Driver.copied invoiceTypeFile "invoice.type.json"

    /// <summary>
    /// <see cref="acmeDemo"/> with the files of its dependency below <c>deps/morphir/sdk/@3.0.0/</c>, a version
    /// segment that carries a version, rather than the bare <c>@</c>.
    /// </summary>
    let versionedDependencySegment () : VFileTree =
        acmeFiles ()
        |> Driver.moved "deps/morphir/sdk/@/" "deps/morphir/sdk/@3.0.0/"

    /// <summary>
    /// <see cref="acmeDemo"/> with a copy of the module file of <c>billing</c> below the directory of another
    /// package, <c>pkg/acme/other/</c>.
    /// </summary>
    let fileBelowAnotherPackage () : VFileTree =
        acmeFiles ()
        |> Driver.copied billingModuleFile "pkg/acme/other/billing/module.json"

    /// <summary>
    /// <see cref="acmeDemo"/> with the type file of <c>invoice</c> naming the type <c>total</c>, whose stem is not the
    /// file's.
    /// </summary>
    let typeFileWithAnotherName () : VFileTree =
        acmeFiles ()
        |> Driver.withMember invoiceTypeFile "name" "\"total\""

    /// <summary>
    /// <see cref="acmeDemo"/> with the module file of <c>billing</c> stating the path <c>invoices</c>, which is not its
    /// directory's.
    /// </summary>
    let moduleFileWithAnotherPath () : VFileTree =
        acmeFiles ()
        |> Driver.withMember billingModuleFile "path" "\"invoices\""

    /// <summary>
    /// <see cref="acmeDemo"/> with the type <c>invoice</c> both in its own file and inline in the module file of
    /// <c>billing</c>, which holds it inline as its only type.
    /// </summary>
    let typeInTwoPlaces () : VFileTree =
        let definition = (Driver.jsonAt invoiceTypeFile (acmeFiles ()))["def"]

        acmeFiles ()
        |> Driver.withMember
            billingModuleFile
            "types"
            $"""{{ "invoice": {(nonNull definition).ToJsonString()} }}"""
