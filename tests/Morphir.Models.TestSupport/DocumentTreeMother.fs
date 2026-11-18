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

/// <summary>
/// The tree of <c>DistributionMother.v4LibraryWithDependency</c>: the tree that <c>model convert</c> writes from it.
/// <c>FixtureCatalog.Trees.WrittenV4LibraryWithDependency</c> pins its files, for the tests that need it on disk.
/// </summary>
let v4LibraryWithDependency: StoredTree =
    treeOf DistributionMother.v4LibraryWithDependency

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

/// <summary>A tree whose module holds a type and a value of the same long name, for names a path budget cuts.</summary>
type SharedNameCase = {
    /// <summary>The tree.</summary>
    Tree: StoredTree
    /// <summary>The module that holds the types and values, <c>pricing/models</c> of <c>my-org/finance</c>.</summary>
    Module: DocumentAddress
    /// <summary>
    /// The names, each of a type and a value: the first one's value file has a path one character over the budget, so
    /// its type file, one character shorter, fits uncut; the second one's files are both over.
    /// </summary>
    Names: Name list
}

let private sharedNames () =
    let valueAddress n = Own(ValueDef(pricing, n))

    [
        nameOfLength (stemLengthFor (defaultBudget + 1) valueAddress)
        nameOfLength (stemLengthFor (defaultBudget + 20) valueAddress)
    ]

let private withSharedNames (names: Name list) : ModuleBuilder -> ModuleBuilder =
    names
    |> List.map (fun shared ->
        withType shared (alias Sdk.int)
        >> withValue shared (Constant(WholeValue 0L))
    )
    |> List.reduce (>>)

/// <summary>
/// A v4 library whose module <c>pricing/models</c> has a type and a value of each of two long names (see
/// <see cref="SharedNameCase"/>). The parent's <c>fileNames</c> maps a name to one stem, so both files of a name share
/// one cut stem, chosen so the value file, the longer of the two, fits the default budget.
/// </summary>
let sharedNameOverTheBudget: SharedNameCase =
    let names = sharedNames ()

    {
        Tree =
            aV4Library finance
            |> withModule pricing (withSharedNames names)
            |> buildV4
            |> treeOf
        Module = Own(Module pricing)
        Names = names
    }

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

/// <summary>
/// The classic library of <c>DistributionMother.v3LibraryWithDependency</c>, with numbered attribute slots.
/// <c>FixtureCatalog.Trees.WrittenV3LibraryWithDependency</c> pins its files, for the tests that need it on disk.
/// </summary>
let v3LibraryWithDependency: StoredTree =
    classicTreeOf (withNumberedSlots DistributionMother.v3LibraryWithDependency)

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
/// The classic v3 form of <see cref="sharedNameOverTheBudget"/>, with numbered attribute slots: a v3 tree's stems,
/// truncation and <c>fileNames</c> are the v4 tree's.
/// </summary>
let v3SharedNameOverTheBudget: SharedNameCase =
    let names = sharedNames ()

    {
        Tree =
            aV3Library finance
            |> withModule pricing (withSharedNames names)
            |> buildV3
            |> withNumberedSlots
            |> classicTreeOf
        Module = Own(Module pricing)
        Names = names
    }

/// <summary>The value of <see cref="v3Library"/> that <see cref="v3LibraryWithANonFiniteFloat"/> changes.</summary>
let defaultCurrency =
    Own(ValueDef(modulePath "pricing/models", name "default-currency"))

/// <summary>
/// <see cref="v3Library"/> with the body of its value <c>default-currency</c> the float literal NaN, which classic JSON
/// cannot spell, so a writer refuses it.
/// </summary>
let v3LibraryWithANonFiniteFloat: StoredTree =
    match v3Library with
    | ClassicTree tree ->
        let toNaN (document: TreeDocument<_, _, ClassicValueDocument<JsonElement, JsonElement>>) =
            match document with
            | ValueDoc(ClassicValueDocument.DefinedValue controlled) ->
                let withNaN
                    (definition: Morphir.IR.Classic.ValueDefinition<JsonElement, JsonElement>)
                    =
                    match definition.Body with
                    | Morphir.IR.Classic.Value.Literal(attribute, _) ->
                        { definition with
                            Body =
                                Morphir.IR.Classic.Value.Literal(
                                    attribute,
                                    Morphir.IR.Classic.FloatLiteral nan
                                )
                        }
                    | _ -> failwith "The value default-currency of v3Library is a literal."

                ValueDoc(
                    ClassicValueDocument.DefinedValue
                        { controlled with
                            Value = Morphir.IR.Classic.Documented.map withNaN controlled.Value
                        }
                )
            | other -> other

        ClassicTree
            { tree with
                Documents = Map.change defaultCurrency (Option.map toNaN) tree.Documents
            }
    | V4Tree _ -> failwith "v3Library is a classic tree."

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

    /// <summary>The files of <see cref="acmeDemo"/> without <c>manifest.json</c>.</summary>
    let noManifest () : VFileTree =
        acmeFiles ()
        |> Driver.withoutFile "manifest.json"

    /// <summary>
    /// <see cref="acmeDemo"/> with a type file in the YAML profile, <c>invoice.type.yaml</c>, beside the JSON files of
    /// <c>billing</c>.
    /// </summary>
    let yamlFileBesideTheJsonProfile () : VFileTree =
        acmeFiles ()
        |> Driver.withFile
            (billing
             + "invoice.type.yaml")
            "formatVersion: 4\nname: invoice\n"

    /// <summary>
    /// <see cref="acmeDemo"/> with a copy of the module file of <c>billing</c> directly in the package directory,
    /// <c>pkg/acme/demo/module.json</c>, which is no module's directory.
    /// </summary>
    let moduleFileInThePackageDirectory () : VFileTree =
        acmeFiles ()
        |> Driver.copied billingModuleFile "pkg/acme/demo/module.json"

    /// <summary>
    /// <see cref="acmeDemo"/> with a JSON file in the directory of <c>billing</c> that is no module, type or value file,
    /// <c>notes.json</c>.
    /// </summary>
    let unknownJsonFileInAModule () : VFileTree =
        acmeFiles ()
        |> Driver.withFile
            (billing
             + "notes.json")
            """{ "note": "Not a tree file." }"""

    /// <summary>
    /// <see cref="acmeDemo"/> with the directory of <c>billing</c> spelled <c>Billing</c>, which is no escaped stem: a
    /// stem has no capital letter.
    /// </summary>
    let moduleDirectoryThatIsNotAStem () : VFileTree =
        acmeFiles ()
        |> Driver.moved billing "pkg/acme/demo/Billing/"

    /// <summary><see cref="acmeDemo"/> with a type file of <c>invoice</c> whose content cannot be read.</summary>
    let unreadableTypeFile () : VFileTree =
        acmeFiles ()
        |> Driver.withUnreadableFile invoiceTypeFile

    /// <summary><see cref="acmeDemo"/> with a module file of <c>billing</c> whose text is not JSON.</summary>
    let moduleFileThatIsNotJson () : VFileTree =
        acmeFiles ()
        |> Driver.withText billingModuleFile (fun _ -> "{ \"formatVersion\": 4, \"path\": ")

    /// <summary>
    /// <see cref="acmeDemo"/> with a module file of <c>billing</c> whose <c>fileNames</c> maps <c>invoice</c> to a cut
    /// stem whose digest is not the name's.
    /// </summary>
    let fileNamesWithAStemOfAnotherName () : VFileTree =
        acmeFiles ()
        |> Driver.withMember billingModuleFile "fileNames" """{ "invoice": "invoice__00000000" }"""

    /// <summary><see cref="acmeDemo"/> with a manifest whose path budget, 10, is under the minimum of 64.</summary>
    let pathBudgetUnderTheMinimum () : VFileTree =
        acmeFiles ()
        |> Driver.withMember "manifest.json" "pathBudget" "10"

    /// <summary><see cref="acmeDemo"/> with a manifest that lists its dependency <c>morphir/sdk</c> twice.</summary>
    let dependencyListedTwice () : VFileTree =
        acmeFiles ()
        |> Driver.withMember "manifest.json" "dependencies" """[ "morphir/sdk", "morphir/sdk" ]"""

    /// <summary><see cref="acmeDemo"/> with a manifest that lists its own package, <c>acme/demo</c>, as a dependency.</summary>
    let ownPackageAsADependency () : VFileTree =
        acmeFiles ()
        |> Driver.withMember "manifest.json" "dependencies" """[ "morphir/sdk", "acme/demo" ]"""

    /// <summary>
    /// <see cref="acmeDemo"/> with the type file of <c>invoice</c>, a type of the package definition, holding a
    /// <c>spec</c> rather than its <c>def</c>.
    /// </summary>
    let definitionFileHoldingASpec () : VFileTree =
        acmeFiles ()
        |> Driver.withoutMember invoiceTypeFile "def"
        |> Driver.withMember
            invoiceTypeFile
            "spec"
            """{ "TypeAliasSpecification": { "typeParams": [], "typeExp": "morphir/SDK:basics#int" } }"""

    /// <summary><see cref="acmeDemo"/> with the type file of <c>invoice</c> holding neither <c>def</c> nor <c>spec</c>.</summary>
    let nodeFileWithNeitherDefNorSpec () : VFileTree =
        acmeFiles ()
        |> Driver.withoutMember invoiceTypeFile "def"

    let private finance = "pkg/my-org/finance/pricing/models/"

    /// <summary>The type file of <c>invoice</c> in <see cref="v4LibraryWithDependency"/>.</summary>
    let financeInvoiceTypeFile =
        finance
        + "invoice.type.json"

    /// <summary>The value file of <c>default-currency</c> in <see cref="v4LibraryWithDependency"/>.</summary>
    let financeDefaultCurrencyValueFile =
        finance
        + "default-currency.value.json"

    /// <summary>
    /// <see cref="v4LibraryWithDependency"/> with the type expression of <c>invoice</c>, a record, spelled as a node
    /// with the tag <c>Struct</c>, which no type node has.
    /// </summary>
    let typeFileWithAnUnknownTypeTag () : VFileTree =
        written v4LibraryWithDependency
        |> Driver.withText financeInvoiceTypeFile (fun text -> text.Replace("\"Record\"", "\"Struct\""))

    /// <summary>
    /// <see cref="v4LibraryWithDependency"/> with no file for the value <c>default-currency</c>, which its module file
    /// lists.
    /// </summary>
    let moduleListingAMissingValue () : VFileTree =
        written v4LibraryWithDependency
        |> Driver.withoutFile financeDefaultCurrencyValueFile

    let private v3Files () = written v3Library

    let private v3Pricing = "pkg/my-org/finance/pricing/models/"

    /// <summary>The module file of <c>pricing/models</c> in <see cref="v3Library"/> and <see cref="v3Specs"/>.</summary>
    let v3PricingModuleFile =
        v3Pricing
        + "module.json"

    /// <summary>The type file of <c>invoice</c> in <see cref="v3Library"/> and <see cref="v3Specs"/>.</summary>
    let v3InvoiceTypeFile =
        v3Pricing
        + "invoice.type.json"

    /// <summary>The value file of <c>default-currency</c> in <see cref="v3Library"/> and <see cref="v3Specs"/>.</summary>
    let v3DefaultCurrencyValueFile =
        v3Pricing
        + "default-currency.value.json"

    /// <summary><see cref="v3Library"/> with a manifest that says the string <c>"3"</c>, a release of major 3.</summary>
    let v3ManifestSayingTheString3 () : VFileTree =
        v3Files ()
        |> Driver.withMember "manifest.json" "formatVersion" "\"3\""

    /// <summary><see cref="v3Library"/> with a manifest that says the number <c>3.1</c>, of major 3.</summary>
    let v3ManifestSayingTheNumber31 () : VFileTree =
        v3Files ()
        |> Driver.withMember "manifest.json" "formatVersion" "3.1"

    /// <summary><see cref="v3Specs"/> with an <c>access</c> in a module file, which a module specification has not.</summary>
    let v3ModuleSpecificationWithAccess () : VFileTree =
        written v3Specs
        |> Driver.withMember v3PricingModuleFile "access" "\"Public\""

    /// <summary>
    /// <see cref="v3Library"/> with a module file that states its path in <c>module</c>, the v4 alias that a v3 module
    /// file does not accept.
    /// </summary>
    let v3ModuleFileWithTheV4ModuleAlias () : VFileTree =
        v3Files ()
        |> Driver.withoutMember v3PricingModuleFile "path"
        |> Driver.withMember v3PricingModuleFile "module" "\"pricing/models\""

    /// <summary>
    /// <see cref="v3Library"/> with the type file of <c>invoice</c> holding a <c>def</c> whose type definition is no
    /// classic type definition.
    /// </summary>
    let v3PayloadThatIsNotClassicJson () : VFileTree =
        v3Files ()
        |> Driver.withMember
            v3InvoiceTypeFile
            "def"
            """{ "access": "Public", "value": { "doc": "", "value": [ "NotATypeDefinition", [] ] } }"""

    /// <summary><see cref="v3Library"/> with a module file that states no <c>formatVersion</c>.</summary>
    let v3ModuleFileWithoutFormatVersion () : VFileTree =
        v3Files ()
        |> Driver.withoutMember v3PricingModuleFile "formatVersion"

    /// <summary><see cref="v3Library"/> with a module file that states <c>formatVersion</c> twice.</summary>
    let v3ModuleFileWithTwoFormatVersions () : VFileTree =
        v3Files ()
        |> Driver.withText
            v3PricingModuleFile
            (fun text ->
                "{ \"formatVersion\": \"3.1.0\","
                + text.Substring(
                    text.IndexOf '{'
                    + 1
                )
            )

    /// <summary>
    /// <see cref="v3Specs"/> with an <c>access</c> in the <c>spec</c> of the value <c>default-currency</c>, which a
    /// specification has not.
    /// </summary>
    let v3SpecWithAccess () : VFileTree =
        let files = written v3Specs
        let spec = (Driver.jsonAt v3DefaultCurrencyValueFile files)["spec"]
        let withAccess = (nonNull spec).AsObject()
        withAccess["access"] <- Nodes.JsonValue.Create "Public"

        files
        |> Driver.withMember v3DefaultCurrencyValueFile "spec" (withAccess.ToJsonString())

    /// <summary>
    /// <see cref="v3Library"/> with a member beside <c>access</c> and <c>value</c> in the <c>def</c> of the type
    /// <c>invoice</c>.
    /// </summary>
    let v3DefinitionWithAnotherMember () : VFileTree =
        let files = v3Files ()

        let definition =
            (nonNull (Driver.jsonAt v3InvoiceTypeFile files).["def"]).AsObject()

        definition["since"] <- Nodes.JsonValue.Create "3.0.0"

        files
        |> Driver.withMember v3InvoiceTypeFile "def" (definition.ToJsonString())

    /// <summary>The JSON location of the member that <see cref="v3TypeFileWithANestedUnknownMember"/> adds.</summary>
    let v3NestedUnknownTypeMember = "$.def.value[2][2][0].metadata"

    /// <summary>
    /// <see cref="v3LibraryWithDependency"/> with a member <c>metadata</c> in the first field of the record that the
    /// type <c>invoice</c> aliases, beside the field's <c>name</c> and <c>tpe</c>: deep in the classic payload, where
    /// the classic model has no place for it.
    /// </summary>
    let v3TypeFileWithANestedUnknownMember () : VFileTree =
        let files = written v3LibraryWithDependency
        let definition = (nonNull (Driver.jsonAt v3InvoiceTypeFile files).["def"]).AsObject()
        let field = (nonNull (nonNull (nonNull (nonNull definition["value"]).[2]).[2]).[0]).AsObject()
        field["metadata"] <- Nodes.JsonValue.Create "do-not-drop"

        files
        |> Driver.withMember v3InvoiceTypeFile "def" (definition.ToJsonString())

    /// <summary>The JSON location of the member that <see cref="v3ValueFileWithANestedUnknownMember"/> adds.</summary>
    let v3NestedUnknownValueMember = "$.def.value.metadata"

    /// <summary>
    /// <see cref="v3LibraryWithDependency"/> with a member <c>metadata</c> in the value definition of
    /// <c>default-currency</c>, beside its <c>inputTypes</c>, <c>outputType</c> and <c>body</c>, where the classic model
    /// has no place for it. <c>FixtureCatalog.Trees.WrittenV3WithANestedUnknownMember</c> pins its files.
    /// </summary>
    let v3ValueFileWithANestedUnknownMember () : VFileTree =
        let files = written v3LibraryWithDependency
        let definition = (nonNull (Driver.jsonAt v3DefaultCurrencyValueFile files).["def"]).AsObject()
        (nonNull definition["value"]).AsObject().["metadata"] <- Nodes.JsonValue.Create "do-not-drop"

        files
        |> Driver.withMember v3DefaultCurrencyValueFile "def" (definition.ToJsonString())

    /// <summary>
    /// <see cref="v3Specs"/> with a member <c>metadata</c> in the specification of the value <c>default-currency</c>,
    /// beside its <c>inputs</c> and <c>output</c>: refused at <c>$.spec.metadata</c>.
    /// </summary>
    let v3SpecWithANestedUnknownMember () : VFileTree =
        let files = written v3Specs
        let spec = (nonNull (Driver.jsonAt v3DefaultCurrencyValueFile files).["spec"]).AsObject()
        spec["metadata"] <- Nodes.JsonValue.Create "do-not-drop"

        files
        |> Driver.withMember v3DefaultCurrencyValueFile "spec" (spec.ToJsonString())

    /// <summary><see cref="v3Library"/> with no file for the type <c>invoice</c>, which its module file lists.</summary>
    let v3ModuleListingAMissingType () : VFileTree =
        v3Files ()
        |> Driver.withoutFile v3InvoiceTypeFile

    /// <summary><see cref="v3Library"/> with a copy of the type file of <c>invoice</c> at the root of the tree.</summary>
    let v3TypeFileOutsideTheTree () : VFileTree =
        v3Files ()
        |> Driver.copied v3InvoiceTypeFile "invoice.type.json"
