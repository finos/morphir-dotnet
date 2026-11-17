/// <summary>
/// The named document trees that store tests share. Each case is the split of a model named for the intent a test
/// needs: most are cases of <see cref="DistributionMother"/>, and the cases of the path budget and of a case-only
/// collision are built here, since they need the logical paths of <c>DocumentAddress</c>.
/// </summary>
module Morphir.Models.TestSupport.DocumentTreeMother

open Morphir.IR
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.Models.TestSupport.DistributionBuilder

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
