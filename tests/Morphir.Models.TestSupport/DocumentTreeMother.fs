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
open Morphir.IR.Files
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.Models.TestSupport.DistributionBuilder
open Morphir.TestSupport.Fixtures

module Driver = DocumentTreeStoreDriver

/// The frozen bounded orders example and its named reader test variants.
module BoundedOrders =
    let manifestLocation = "manifest.json"
    let domainLocation = "pkg/acme/orders/domain/module.json"
    let orderLocation = "pkg/acme/orders/domain/order.value.json"
    let totalLocation = "pkg/acme/orders/domain/total-in-_usd.value.json"

    let catalogDocuments version profile =
        let prefix = $"{FixtureCatalog.BoundedOrders.Directory}{version}/{profile}/"

        FixtureCatalog.BoundedOrders.Files
        |> Seq.filter (fun entry ->
            entry.RelativePath.StartsWith(prefix, System.StringComparison.Ordinal)
        )
        |> Seq.map (fun entry -> entry.RelativePath.Substring(prefix.Length), entry)
        |> Map.ofSeq

    /// For C# callers: exposes <see cref="catalogDocuments"/> as a .NET read-only dictionary.
    [<CompiledName("CatalogDocuments")>]
    let catalogDocumentEntries version profile : System.Collections.Generic.IReadOnlyDictionary<string, FixtureEntry> =
        let entries = System.Collections.Generic.Dictionary<string, FixtureEntry>(System.StringComparer.Ordinal)

        for KeyValue(path, entry) in catalogDocuments version profile do
            entries[path] <- entry

        System.Collections.ObjectModel.ReadOnlyDictionary<string, FixtureEntry>(entries)
        :> System.Collections.Generic.IReadOnlyDictionary<string, FixtureEntry>

    let private plainJson version locations =
        let documents = catalogDocuments version "plain-json"

        locations
        |> List.map (fun location -> location, FixtureCatalog.ReadText(documents[location]))
        |> Driver.ofTexts

    let private metadataLocations = [ manifestLocation; domainLocation ]

    let metadata version = plainJson version metadataLocations

    let moduleMetadata version =
        plainJson
            version
            (metadataLocations
             @ [ "pkg/acme/orders/audit/module.json"; "deps/morphir/_sdk/@/basics/module.json" ])

    let ambiguousModuleProfile version =
        moduleMetadata version
        |> Driver.copied "pkg/acme/orders/audit/module.json" "pkg/acme/orders/audit/module.yaml"

    let selectedValues version =
        plainJson
            version
            (metadataLocations
             @ [ orderLocation; totalLocation ])

    let private withDomainValues (values: string) files =
        let root = Driver.jsonAt domainLocation files
        root["values"] <- Nodes.JsonNode.Parse values
        Driver.withFile domainLocation (root.ToJsonString()) files

    let duplicateListedValue version =
        metadata version
        |> withDomainValues """["order","order"]"""

    let malformedInlineValue version =
        metadata version
        |> withDomainValues """{"order":false}"""

    let selectedValueWithWrongName version =
        selectedValues version
        |> Driver.copied totalLocation orderLocation

    let private filesWithOrder version =
        plainJson
            version
            (metadataLocations
             @ [ orderLocation ])

    let private orderDefinition files =
        let definition = (Driver.jsonAt orderLocation files)["def"]
        (nonNull definition).ToJsonString()

    let alternateInlineValueWithMalformedSibling version =
        let files = filesWithOrder version
        let definition = orderDefinition files

        files
        |> Driver.withoutFile orderLocation
        |> withDomainValues $"""{{"a-b":{definition},"total-in-USD":false}}"""

    let inlineAndSplitValue version =
        let files = filesWithOrder version
        let definition = orderDefinition files

        files
        |> withDomainValues $"""{{"order":{definition}}}"""

    let oversizedSelectedValue version byteCount =
        metadata version
        |> Driver.withFile orderLocation (String.replicate byteCount "x")


    let private contextualText (text: string) =
        text.Replace("SCHEMA", MorphirFile.Draft1Schema)

    let manifestWithContextLast () =
        let text =
            """{"document":{"formatVersion":4,"distribution":"Library","package":{"$name":"p"},"pathBudget":256},"context":{"packagePaths":{"p":"acme/orders"},"prefixes":{"unused":"data"}},"$schema":"SCHEMA"}"""

        Driver.ofTexts [ manifestLocation, contextualText text ]

    let yamlManifestWithDuplicateContextEntry () =
        let text =
            """context:
  names:
    n: order
    n: other
document:
  formatVersion: 4
  distribution: Library
  package: acme/orders
  pathBudget: 256
$schema: SCHEMA
"""

        Driver.ofTexts [ "manifest.yaml", contextualText text ]

    let private v4Manifest =
        """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":256}"""

    let private v4SplitOrderModule =
        """{"formatVersion":4,"path":"domain","values":["order"]}"""

    let contextualInlineWithUnknownSiblingAlias () =
        let text =
            """{"$schema":"SCHEMA","context":{"modulePaths":{"m":"domain"},"fqNames":{"i":"morphir/SDK:basics#int"}},"document":{"formatVersion":4,"path":{"$name":"m"},"values":{"order":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":{"$name":"i"},"body":{"Literal":{"IntegerLiteral":73}}}}},"broken":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":{"$name":"i"},"body":{"Reference":{"$name":"missing"}}}}}}}}"""

        Driver.ofTexts [ manifestLocation, v4Manifest; domainLocation, contextualText text ]

    let private withSplitOrder text =
        Driver.ofTexts [
            manifestLocation, v4Manifest
            domainLocation, v4SplitOrderModule
            orderLocation, text
        ]

    let contextualOrderWithLiteralAndOptionalPrefixes () =
        let text =
            """{"$schema":"SCHEMA","context":{"prefixes":{"f":"ignored"},"fqNames":{"i":"morphir/SDK:basics#int"}},"document":{"formatVersion":4,"name":"order","def":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":{"$name":"i"},"body":{"Literal":{"DocumentLiteral":{"ratio":1.0,"$name":"i","id":9007199254740993}}}}}}}}"""

        let original =
            contextualText text
            + "\n"

        withSplitOrder original, original

    let contextualOrderWithUnknownCriticalPrefixes () =
        let files, original = contextualOrderWithLiteralAndOptionalPrefixes ()

        files
        |> Driver.withFile
            orderLocation
            (original.Replace("\"context\":", "\"critical\":[\"context.prefixes\"],\"context\":"))

    let contextualOrderWithExpandingAliases () =
        let text =
            """{"$schema":"SCHEMA","context":{"fqNames":{"q":"acme/orders:domain#LONG"}},"document":{"formatVersion":4,"name":"order","def":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":{"Tuple":[{"$name":"q"},{"$name":"q"},{"$name":"q"}]},"body":{"Literal":{"IntegerLiteral":73}}}}}}}"""

        let selected = (contextualText text).Replace("LONG", String.replicate 500 "n")
        withSplitOrder selected, System.Text.Encoding.UTF8.GetByteCount selected

    let contextualInlineOrderWithExpandingAliases () =
        let files, _ = contextualOrderWithExpandingAliases ()
        let envelope = Driver.jsonAt orderLocation files
        let splitDocument = nonNull envelope["document"]
        let definition = (nonNull splitDocument["def"]).DeepClone()

        let document =
            Nodes.JsonNode.Parse("""{"formatVersion":4,"path":"domain","values":{}}""")

        let moduleDocument = nonNull document
        (nonNull moduleDocument["values"])["order"] <- definition
        envelope["document"] <- moduleDocument
        let text = envelope.ToJsonString()

        Driver.ofTexts [ manifestLocation, v4Manifest; domainLocation, text ],
        System.Text.Encoding.UTF8.GetByteCount text

    let contextualModuleWithSharedInventoryAliases () =
        let names =
            [ "a"; "b"; "c" ]
            |> List.map (fun suffix -> String.replicate 120 "n" + suffix)

        let table =
            names
            |> List.mapi (sprintf "\"n%d\":\"%s\"")
            |> String.concat ","

        let references = """{"$name":"n0"},{"$name":"n1"},{"$name":"n2"}"""

        let moduleText =
            "{\"$schema\":\"SCHEMA\",\"context\":{\"names\":{"
            + table
            + "}},\"document\":{\"formatVersion\":4,\"path\":\"domain\",\"types\":["
            + references
            + "],\"values\":["
            + references
            + "]}}"
            |> contextualText

        let members =
            names
            |> List.collect (fun name ->
                let directory = "pkg/acme/orders/domain/"
                let typeDocument =
                    """{"formatVersion":4,"name":"NAME","def":{"Public":{"TypeAliasDefinition":{"typeParams":[],"typeExp":"morphir/SDK:basics#int"}}}}"""

                let valueDocument =
                    """{"$schema":"SCHEMA","context":{"fqNames":{"i":"morphir/SDK:basics#int"}},"document":{"formatVersion":4,"name":"NAME","def":{"Public":{"ExpressionBody":{"inputTypes":{"rate":{"$name":"i"},"amount":{"$name":"i"}},"outputType":{"$name":"i"},"body":{"Literal":{"IntegerLiteral":73}}}}}}}"""
                    |> contextualText

                [ directory + name + ".type.json", typeDocument.Replace("NAME", name)
                  directory + name + ".value.json", valueDocument.Replace("NAME", name) ])

        Driver.ofTexts ([ manifestLocation, v4Manifest; domainLocation, moduleText ] @ members),
        moduleText

    let orderTypeLocation = "pkg/acme/orders/domain/order.type.json"

    let contextualTypeFileWithValuePayload () =
        let text =
            """{"$schema":"SCHEMA","context":{"fqNames":{"i":"morphir/SDK:basics#int"}},"document":{"formatVersion":4,"name":"order","def":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":{"$name":"i"},"body":{"Literal":{"IntegerLiteral":73}}}}}}}"""

        Driver.ofTexts [
            manifestLocation, v4Manifest
            domainLocation, """{"formatVersion":4,"path":"domain","types":["order"]}"""
            orderTypeLocation, contextualText text
        ]

    let private contextWithInvalidSpelling =
        """{"$schema":"SCHEMA","context":{"names":{"n":"\uD800"}},"document":{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":256}}"""

    let manifestWithInvalidContextSpelling () =
        Driver.ofTexts [ manifestLocation, contextualText contextWithInvalidSpelling ]

    let manifestWithInvalidContextKey () =
        let text =
            contextWithInvalidSpelling.Replace("\"n\":\"\\uD800\"", "\"\\uD800\":\"order\"")

        Driver.ofTexts [ manifestLocation, contextualText text ]

    let private manifestsWithContextTables jsonEntries yamlEntries =
        let json =
            """{"$schema":"SCHEMA","context":{"names":{ENTRIES}},"document":{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":256}}"""

        let yaml =
            """$schema: SCHEMA
context:
  names:
ENTRIES
document:
  formatVersion: 4
  distribution: Library
  package: acme/orders
  pathBudget: 256
"""

        let make location entries text =
            Driver.ofTexts [ location, (contextualText text).Replace("ENTRIES", entries) ], location

        [ make manifestLocation jsonEntries json; make "manifest.yaml" yamlEntries yaml ]

    let manifestsWithOversizedContextSpelling () =
        let long = String.replicate 1025 "n"
        manifestsWithContextTables $"\"n\":\"{long}\"" $"    n: {long}"

    let manifestsWithTooManyContextEntries () =
        let ids = [ 0..16_384 ]

        let jsonEntries =
            ids
            |> List.map (fun index -> $"\"n{index}\":\"order\"")
            |> String.concat ","

        let yamlEntries =
            ids
            |> List.map (fun index -> $"    n{index}: order")
            |> String.concat "\n"

        manifestsWithContextTables jsonEntries yamlEntries

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
        nameOfLength (
            stemLengthFor
                (defaultBudget
                 + 1)
                valueAddress
        )
        nameOfLength (
            stemLengthFor
                (defaultBudget
                 + 20)
                valueAddress
        )
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

    /// <summary><see cref="noModules"/> with a readable manifest whose stream fails on close.</summary>
    let manifestThatFailsOnClose () : VFileTree =
        written noModules
        |> Driver.withFailingClose "manifest.json"

    let private streamedManifestWithPadding padding =
        let location = "manifest.json"
        let files = written noModules
        let limit = System.Text.Encoding.UTF8.GetByteCount((Driver.texts files)[location])

        let streamed, bytesRead =
            files
            |> Driver.withText
                location
                (fun manifest ->
                    manifest
                    + System.String(' ', padding)
                )
            |> Driver.withReadCounter location

        streamed, limit, bytesRead

    /// <summary>A streamed manifest that fits exactly within the byte limit.</summary>
    let streamedManifestAtReadLimit () = streamedManifestWithPadding 0

    /// <summary>A streamed manifest with 64 whitespace bytes beyond the byte limit.</summary>
    let streamedManifestOverReadLimit () = streamedManifestWithPadding 64

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
        |> Driver.withText
            financeInvoiceTypeFile
            (fun text -> text.Replace("\"Record\"", "\"Struct\""))

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

        let definition =
            (nonNull (Driver.jsonAt v3InvoiceTypeFile files).["def"]).AsObject()

        let field =
            (nonNull (nonNull (nonNull (nonNull definition["value"]).[2]).[2]).[0])
                .AsObject()

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

        let definition =
            (nonNull (Driver.jsonAt v3DefaultCurrencyValueFile files).["def"]).AsObject()

        (nonNull definition["value"]).AsObject().["metadata"] <-
            Nodes.JsonValue.Create "do-not-drop"

        files
        |> Driver.withMember v3DefaultCurrencyValueFile "def" (definition.ToJsonString())

    /// <summary>
    /// <see cref="v3Specs"/> with a member <c>metadata</c> in the specification of the value <c>default-currency</c>,
    /// beside its <c>inputs</c> and <c>output</c>: refused at <c>$.spec.metadata</c>.
    /// </summary>
    let v3SpecWithANestedUnknownMember () : VFileTree =
        let files = written v3Specs

        let spec =
            (nonNull (Driver.jsonAt v3DefaultCurrencyValueFile files).["spec"]).AsObject()

        spec["metadata"] <- Nodes.JsonValue.Create "do-not-drop"

        files
        |> Driver.withMember v3DefaultCurrencyValueFile "spec" (spec.ToJsonString())

    /// <summary>A wrapper of a v3 tree file's payload, where <see cref="v3WithAWrapperMember"/> adds a member.</summary>
    [<RequireQualifiedAccess>]
    type V3Wrapper =
        /// <summary>The <c>{ "access", "value" }</c> of the <c>def</c> of the type <c>invoice</c>.</summary>
        | TypeDefinition
        /// <summary>The <c>{ "doc", "value" }</c> of that definition, documented.</summary>
        | DocumentedTypeDefinition
        /// <summary>The <c>{ "access", "value" }</c> of the <c>def</c> of the value <c>default-currency</c>.</summary>
        | ValueDefinition
        /// <summary>The <c>{ "doc", "value" }</c> of that definition, documented.</summary>
        | DocumentedValueDefinition
        /// <summary>The <c>{ "doc", "value" }</c> of the <c>spec</c> of the type <c>invoice</c> in a Specs tree.</summary>
        | DocumentedTypeSpecification
        /// <summary>The <c>{ "doc", "value" }</c> of the <c>spec</c> of the value <c>default-currency</c> in a Specs tree.</summary>
        | DocumentedValueSpecification

    /// <summary>Every wrapper of <see cref="V3Wrapper"/>.</summary>
    let v3Wrappers = [
        V3Wrapper.TypeDefinition
        V3Wrapper.DocumentedTypeDefinition
        V3Wrapper.ValueDefinition
        V3Wrapper.DocumentedValueDefinition
        V3Wrapper.DocumentedTypeSpecification
        V3Wrapper.DocumentedValueSpecification
    ]

    /// <summary>
    /// <see cref="v3LibraryWithDependency"/>, or <see cref="v3Specs"/> for a specification, with a member
    /// <c>metadata</c> beside the members of one wrapper, where the classic model has no place for it; a documented
    /// wrapper is first made by giving the entry the doc <c>""</c>. Gives the files, the file that holds the member and
    /// the member's JSON location: <c>$.def.metadata</c>, <c>$.def.value.metadata</c> or <c>$.spec.metadata</c>.
    /// </summary>
    let v3WithAWrapperMember (wrapper: V3Wrapper) : VFileTree * string * string =
        let file, payload, documented, source =
            match wrapper with
            | V3Wrapper.TypeDefinition -> v3InvoiceTypeFile, "def", false, v3LibraryWithDependency
            | V3Wrapper.DocumentedTypeDefinition ->
                v3InvoiceTypeFile, "def", true, v3LibraryWithDependency
            | V3Wrapper.ValueDefinition ->
                v3DefaultCurrencyValueFile, "def", false, v3LibraryWithDependency
            | V3Wrapper.DocumentedValueDefinition ->
                v3DefaultCurrencyValueFile, "def", true, v3LibraryWithDependency
            | V3Wrapper.DocumentedTypeSpecification -> v3InvoiceTypeFile, "spec", true, v3Specs
            | V3Wrapper.DocumentedValueSpecification ->
                v3DefaultCurrencyValueFile, "spec", true, v3Specs

        let files = written source
        let root = Driver.jsonAt file files
        let content = nonNull root[payload]

        let documentedOf (entry: Nodes.JsonNode) =
            let wrapped = Nodes.JsonObject()
            wrapped["doc"] <- Nodes.JsonValue.Create ""
            wrapped["value"] <- entry.DeepClone()
            wrapped

        let changed, location =
            match payload, documented with
            | "def", false ->
                let definition = content.AsObject()
                definition["metadata"] <- Nodes.JsonValue.Create "do-not-drop"
                definition :> Nodes.JsonNode, "$.def.metadata"
            | "def", true ->
                let definition = content.AsObject()
                let wrapped = documentedOf (nonNull definition["value"])
                wrapped["metadata"] <- Nodes.JsonValue.Create "do-not-drop"
                definition["value"] <- wrapped
                definition :> Nodes.JsonNode, "$.def.value.metadata"
            | _ ->
                let wrapped = documentedOf content
                wrapped["metadata"] <- Nodes.JsonValue.Create "do-not-drop"
                wrapped :> Nodes.JsonNode, "$.spec.metadata"

        files
        |> Driver.withMember file payload (changed.ToJsonString()),
        file,
        location

    /// <summary><see cref="v3Library"/> with no file for the type <c>invoice</c>, which its module file lists.</summary>
    let v3ModuleListingAMissingType () : VFileTree =
        v3Files ()
        |> Driver.withoutFile v3InvoiceTypeFile

    /// <summary><see cref="v3Library"/> with a copy of the type file of <c>invoice</c> at the root of the tree.</summary>
    let v3TypeFileOutsideTheTree () : VFileTree =
        v3Files ()
        |> Driver.copied v3InvoiceTypeFile "invoice.type.json"

/// Named single-file and contextual YAML cases for model-loader and store tests.
module YamlDocuments =
    /// The v4 daily-rate model from <see cref="FixtureCatalog.Models.V4DailyRate"/> encoded as YAML
    /// with the requested <paramref name="formatVersion"/> header.
    [<CompiledName("V4DailyRateYaml")>]
    let v4DailyRateYaml (formatVersion: string) : string =
        use parsed = JsonDocument.Parse(FixtureCatalog.ReadText FixtureCatalog.Models.V4DailyRate)
        let distributionJson = parsed.RootElement.GetProperty("distribution").GetRawText()
        let header =
            if formatVersion.Contains('.') then $"\"{formatVersion}\"" else formatVersion
        $"formatVersion: {header}\ndistribution: {distributionJson}\n"

    /// A minimal valid plain YAML document carrying only formatVersion and an empty distribution mapping.
    [<CompiledName("MinimalV4PlainYaml")>]
    let minimalV4PlainYaml = "formatVersion: 4\ndistribution: {}\n"

    /// A malformed YAML document with an unclosed flow sequence.
    [<CompiledName("MalformedFlowYaml")>]
    let malformedFlowYaml = "formatVersion: [broken\n"

    /// A malformed YAML document starting with <c>formatVersion: 4</c> and an unclosed <c>distribution</c> sequence.
    [<CompiledName("MalformedDistributionSequenceYaml")>]
    let malformedDistributionSequenceYaml = "formatVersion: 4\ndistribution: [broken\n"

    /// A YAML document carrying an undeclared <c>$schema</c> URI without any MEP marker.
    [<CompiledName("UndeclaredSchemaYaml")>]
    let undeclaredSchemaYaml =
        "$schema: \"https://example.com/unknown.schema.json\"\ncontext: {}\ndocument: {}\n"

    /// A block YAML document whose first plain key contains <c>::</c>, followed by an undeclared <c>$schema</c>
    /// member, verifying Auto routes it to YAML rather than stopping at <c>::</c>.
    [<CompiledName("BlockColonColonKeyWithUndeclaredSchemaYaml")>]
    let blockColonColonKeyWithUndeclaredSchemaYaml =
        "custom::key: ignored\n$schema: \"https://example.com/unknown.schema.json\"\ncontext: {}\ndocument: {}\n"

    /// A flow YAML mapping whose first plain key contains <c>::</c>, followed by an undeclared <c>$schema</c>
    /// member, verifying Auto routes it to YAML rather than stopping at <c>::</c>.
    [<CompiledName("FlowColonColonKeyWithUndeclaredSchemaYaml")>]
    let flowColonColonKeyWithUndeclaredSchemaYaml =
        "{custom::key: ignored, \"$schema\": \"https://example.com/unknown.schema.json\", context: {}, document: {}}\n"

    /// A block YAML document whose first plain key contains <c>::(key)</c>, followed by an undeclared <c>$schema</c>.
    [<CompiledName("BlockColonColonParenKeyWithUndeclaredSchemaYaml")>]
    let blockColonColonParenKeyWithUndeclaredSchemaYaml =
        "custom::(key): ignored\n$schema: \"https://example.com/unknown.schema.json\"\ncontext: {}\ndocument: {}\n"

    /// A flow YAML mapping whose first plain key contains <c>::(key)</c>, followed by an undeclared <c>$schema</c>.
    [<CompiledName("FlowColonColonParenKeyWithUndeclaredSchemaYaml")>]
    let flowColonColonParenKeyWithUndeclaredSchemaYaml =
        "{custom::(key): ignored, \"$schema\": \"https://example.com/unknown.schema.json\", context: {}, document: {}}\n"

    /// A block YAML document whose first plain key starts with <c>morphir::key:</c>, followed by an undeclared <c>$schema</c>.
    [<CompiledName("BlockMorphirColonColonKeyWithUndeclaredSchemaYaml")>]
    let blockMorphirColonColonKeyWithUndeclaredSchemaYaml =
        "morphir::key: ignored\n$schema: \"https://example.com/unknown.schema.json\"\ncontext: {}\ndocument: {}\n"

    /// An Ion-annotated string literal containing <c>: </c> inside quotes, which Auto must NOT classify as YAML.
    [<CompiledName("IonAnnotatedQuotedStringWithColonSpace")>]
    let ionAnnotatedQuotedStringWithColonSpace = "custom::\"hello: world\"\n"

    /// An Ion document using stacked <c>morphir::v4::</c> annotations.
    [<CompiledName("IonStackedMorphirAnnotationStub")>]
    let ionStackedMorphirAnnotationStub = "morphir::v4::{}\n"

    /// An Ion document using a bare <c>morphir::bare</c> symbol annotation.
    [<CompiledName("IonBareMorphirAnnotationStub")>]
    let ionBareMorphirAnnotationStub = "morphir::bare\n"

    /// A single-line YAML mapping whose key ends with a colon (<c>formatVersion:</c>) separated by <c>:: </c>.
    [<CompiledName("FormatVersionDoubleColonYaml")>]
    let formatVersionDoubleColonYaml = "formatVersion:: ignored\n"

    /// The <see cref="FixtureCatalog.Models.V4DailyRate"/> JSON document with an invalid trailing comma
    /// before the closing brace, which JSON must refuse as <c>malformed-json</c> and never silently decode as YAML.
    [<CompiledName("TrailingCommaV4DailyRateJson")>]
    let trailingCommaV4DailyRateJson =
        let text = (FixtureCatalog.ReadText FixtureCatalog.Models.V4DailyRate).TrimEnd()
        let lastBrace = text.LastIndexOf('}')
        text.Substring(0, lastBrace).TrimEnd() + ",\n}\n"

    /// A truncated malformed JSON object payload.
    [<CompiledName("MalformedJsonBrace")>]
    let malformedJsonBrace = "{broken"

    /// An empty JSON object payload used to construct mixed-profile tree fixtures.
    [<CompiledName("EmptyJsonObject")>]
    let emptyJsonObject = "{}"

    /// A plain YAML manifest carrying a disallowed YAML anchor on <c>formatVersion</c>.
    [<CompiledName("AnchoredManifestYaml")>]
    let anchoredManifestYaml =
        "formatVersion: &v 4\ndistribution: Library\npackage: acme/orders\npathBudget: 4000\n"

    /// A package metadata YAML payload (<c>package.yaml</c>) ignored by document-tree reads with a notice.
    [<CompiledName("PackageMetadataYaml")>]
    let packageMetadataYaml = "formatVersion: 4\nversion: 1.0.0\n"

    /// A MEP model-envelope document written in YAML, which bare-IR loading must refuse.
    [<CompiledName("MepEnvelopeYaml")>]
    let mepEnvelopeYaml = "envelopeVersion: \"0.1.0\"\npayload: {}\n"

    /// A MEP model-envelope document written in YAML that also carries a <c>$schema</c> member;
    /// bare-IR loading must classify it as <c>unsupported-profile</c> before IR-context admission.
    [<CompiledName("MepEnvelopeWithSchemaYaml")>]
    let mepEnvelopeWithSchemaYaml =
        "$schema: \"https://morphir.finos.org/schemas/mep/0.2.0/model-envelope.schema.json\"\nenvelopeVersion: \"0.1.0\"\npayload: {}\n"

    /// A MEP model-envelope YAML document whose first key is <c>envelopeVersion:</c> followed by <c>$schema:</c>,
    /// testing extensionless <c>Auto</c> YAML detection and MEP classification.
    [<CompiledName("MepEnvelopeFirstWithSchemaYaml")>]
    let mepEnvelopeFirstWithSchemaYaml =
        "envelopeVersion: \"0.1.0\"\n$schema: \"https://morphir.finos.org/schemas/mep/0.2.0/model-envelope.schema.json\"\npayload: {}\n"

    /// A YAML document starting with the <c>morphir_model:</c> MEP marker key.
    [<CompiledName("MepMorphirModelYaml")>]
    let mepMorphirModelYaml = "morphir_model:\n  formatVersion: 4\n"

    /// A YAML document starting with the <c>jsonrpc:</c> MEP marker key.
    [<CompiledName("MepJsonRpcYaml")>]
    let mepJsonRpcYaml = "jsonrpc: \"2.0\"\nmethod: \"morphir/model\"\n"

    /// A MEP model-envelope YAML document preceded by a leading YAML comment.
    [<CompiledName("MepCommentedEnvelopeYaml")>]
    let mepCommentedEnvelopeYaml = "# MEP envelope comment\nenvelopeVersion: \"0.1.0\"\npayload: {}\n"

    /// A MEP model-envelope YAML document with a double-quoted <c>envelopeVersion</c> key.
    [<CompiledName("MepDoubleQuotedEnvelopeYaml")>]
    let mepDoubleQuotedEnvelopeYaml = "\"envelopeVersion\": \"0.1.0\"\npayload: {}\n"

    /// A MEP model-envelope YAML document with a single-quoted <c>envelopeVersion</c> key.
    [<CompiledName("MepSingleQuotedEnvelopeYaml")>]
    let mepSingleQuotedEnvelopeYaml = "'envelopeVersion': \"0.1.0\"\npayload: {}\n"

    /// A MEP <c>morphir_model</c> YAML document with a leading comment and a double-quoted marker key.
    [<CompiledName("MepCommentedQuotedMorphirModelYaml")>]
    let mepCommentedQuotedMorphirModelYaml = "# MEP model wrapper\n\"morphir_model\":\n  formatVersion: 4\n"

    /// A MEP <c>jsonrpc</c> YAML document with a single-quoted marker key.
    [<CompiledName("MepSingleQuotedJsonRpcYaml")>]
    let mepSingleQuotedJsonRpcYaml = "'jsonrpc': \"2.0\"\nmethod: \"morphir/model\"\n"

    /// A MEP model-envelope YAML document with a document-start marker, comment, and quoted key.
    [<CompiledName("MepDocumentStartCommentedYaml")>]
    let mepDocumentStartCommentedYaml = "---\n# MEP envelope\n\"envelopeVersion\": \"0.1.0\"\npayload: {}\n"

    /// A MEP model-envelope YAML document with a CR-only leading comment line break.
    [<CompiledName("MepCrOnlyCommentEnvelopeYaml")>]
    let mepCrOnlyCommentEnvelopeYaml = "# CR-only comment\renvelopeVersion: \"0.1.0\"\rpayload: {}\r"

    /// A MEP model-envelope YAML document written as a YAML flow mapping (unquoted keys).
    [<CompiledName("MepFlowMappingEnvelopeYaml")>]
    let mepFlowMappingEnvelopeYaml = "{envelopeVersion: \"0.1.0\", payload: {}}\n"

    /// A MEP model-envelope YAML document where <c>payload</c> precedes <c>envelopeVersion</c>.
    [<CompiledName("MepPayloadFirstEnvelopeYaml")>]
    let mepPayloadFirstEnvelopeYaml = "payload: {}\nenvelopeVersion: \"0.1.0\"\n"

    /// A MEP model-envelope YAML document with a double-quoted Unicode-escaped <c>envelopeVersion</c> key.
    [<CompiledName("MepEscapedDoubleQuotedEnvelopeYaml")>]
    let mepEscapedDoubleQuotedEnvelopeYaml = "\"envelope\\u0056ersion\": \"0.1.0\"\npayload: {}\n"

    /// A non-Morphir plain YAML mapping used to ensure extensionless Auto mode keeps non-Morphir inputs as malformed-json.
    [<CompiledName("NonMorphirPlainYaml")>]
    let nonMorphirPlainYaml = "foo: bar\n"

    /// A minimal v4 manifest-only JSON tree with scalar manifest members, used to test depth parity with YAML.
    let manifestOnlyV4JsonTree () : VFileTree =
        Driver.ofTexts [
            "manifest.json",
            """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":4000}"""
        ]

    /// A minimal v4 manifest-only YAML tree with scalar manifest members, used to test depth parity with JSON.
    let manifestOnlyV4YamlTree () : VFileTree =
        Driver.ofTexts [
            "manifest.yaml",
            "formatVersion: 4\ndistribution: Library\npackage: acme/orders\npathBudget: 4000\n"
        ]

    /// A malformed Ion manifest payload used to test failed Ion tree decoding under cancellation.
    [<CompiledName("MalformedIonManifest")>]
    let malformedIonManifest = "morphir::{broken"

    /// A deeply nested YAML mapping of <paramref name="depth"/> flow levels used to verify iterative
    /// YAML decoding does not exhaust the process stack when <c>MaxDepth = Int32.MaxValue</c>.
    let deepNestedYaml (depth: int) : string =
        let opens = String.replicate depth "{\"a\":"
        let closes = String.replicate depth "}"
        opens + "1" + closes + "\n"

    /// A valid contextual YAML manifest envelope using the draft.1 name-compaction schema.
    let contextualManifestYaml =
        $"$schema: \"{MorphirFile.Draft1Schema}\"\ncontext:\n  packagePaths:\n    p0: \"acme/orders\"\ndocument:\n  formatVersion: 4\n  distribution: Library\n  package:\n    $name: p0\n  pathBudget: 4000\n"

    /// A small compacted YAML manifest whose expanded output grows via repeated alias references
    /// to a long canonical package path, staying well under a small raw input size while exceeding
    /// tight expansion byte or node budgets.
    [<CompiledName("CompactedExpandingManifestYaml")>]
    let compactedExpandingManifestYaml (segmentCount: int) (dependencyCount: int) : string =
        let longPath =
            [ for i in 1..segmentCount -> $"segment-{i}" ]
            |> String.concat "/"

        let deps =
            [ for _ in 1..dependencyCount -> "    - $name: p0" ]
            |> String.concat "\n"

        $"$schema: \"{MorphirFile.Draft1Schema}\"\ncontext:\n  packagePaths:\n    p0: \"{longPath}\"\ndocument:\n  formatVersion: 4\n  distribution: Library\n  package: \"acme/orders\"\n  pathBudget: 4000\n  dependencies:\n{deps}\n"

    /// A single classic v3.1.0 contextual YAML type document whose raw node count is modest while expanding
    /// its FQName references into nested classic word arrays pushes the expanded node count well past raw nodes.
    let compactedClassicExpandingTypeYaml =
        $"$schema: \"{MorphirFile.Draft1Schema}\"\ncontext:\n  fqNames:\n    q: \"acme-corp/billing-core:invoice-line/rate-table#daily-discount-rate\"\ndocument:\n  formatVersion: \"3.1.0\"\n  name: \"sample\"\n  def:\n    access: \"Public\"\n    value:\n      doc: null\n      value:\n        - \"TypeAliasDefinition\"\n        - []\n        - - \"Tuple\"\n          - {{}}\n          - - - \"Reference\"\n              - {{}}\n              - $name: q\n              - []\n            - - \"Reference\"\n              - {{}}\n              - $name: q\n              - []\n            - - \"Reference\"\n              - {{}}\n              - $name: q\n              - []\n"

    /// A YAML document with a nested key containing ": " and a disallowed anchor value to test structured
    /// JSON Pointer preservation without splitting on ": ".
    [<CompiledName("NestedYamlKeyWithColonSpace")>]
    let nestedYamlKeyWithColonSpace =
        "formatVersion: 4\nouter:\n  \"note: detail\": &bad 1\n"

    /// A minimal Ion manifest stub used to construct ambiguous multi-manifest tree fixtures.
    [<CompiledName("MinimalIonManifest")>]
    let minimalIonManifest = "morphir::{}"

    /// A contextual YAML manifest whose ignored context field contains quotes that double in size when
    /// transcoded to JSON, so the transcoded envelope exceeds a tight byte allowance while both raw YAML
    /// bytes and the expanded plain document fit.
    let contextualYamlWithEscapedEnvelopeOverhead (quoteCount: int) : string =
        let quotes = System.String('"', quoteCount)
        $"$schema: \"{MorphirFile.Draft1Schema}\"\ncontext:\n  ignoredNote: |\n    {quotes}\ndocument:\n  formatVersion: 4\n  distribution: Library\n  package: \"acme/orders\"\n  pathBudget: 4000\n"

    /// A contextual YAML manifest with an invalid alias entry under <c>context.names.bad</c> to test
    /// structured nested context validation locations.
    [<CompiledName("ContextualYamlWithInvalidAliasEntry")>]
    let contextualYamlWithInvalidAliasEntry =
        $"$schema: \"{MorphirFile.Draft1Schema}\"\ncontext:\n  names:\n    bad: \"Not_A_Canonical_Name\"\ndocument:\n  formatVersion: 4\n  distribution: Library\n  package: \"acme/orders\"\n  pathBudget: 4000\n"

    /// For C# callers: serializes a <see cref="VersionedModel"/> as canonical single-file JSON bytes.
    [<CompiledName("WriteSingleFileJson")>]
    let writeSingleFileJson (model: Morphir.IR.Model.VersionedModel) : byte array =
        match model.Distribution with
        | Morphir.IR.Model.DistributionModel.Classic dist ->
            match Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes Morphir.Json.Classic.ClassicWireProfile.v3 true dist with
            | Ok bytes -> bytes
            | Result.Error err -> failwith $"Failed to write classic single-file JSON: {err}"
        | Morphir.IR.Model.DistributionModel.V4 dist ->
            match Morphir.Json.V4.V4JsonWriter.writeDistribution true dist with
            | Ok bytes -> bytes
            | Result.Error err -> failwith $"Failed to write v4 single-file JSON: {err}"

    /// Builds a v4 bounded Orders tree for <paramref name="profile"/> with its manifest <c>formatVersion</c>
    /// rewritten to the explicit quoted string <paramref name="explicitVersion"/>.
    let v4OrdersTreeWithExplicitVersion (profile: string) (explicitVersion: string) : VFileTree =
        let manifestName =
            if profile.EndsWith("-yaml", System.StringComparison.Ordinal) then "manifest.yaml" else "manifest.json"

        BoundedOrders.catalogDocuments "v4" profile
        |> Map.map (fun location fixture ->
            if location = manifestName then
                let original = FixtureCatalog.ReadText fixture

                let updated =
                    if manifestName = "manifest.yaml" then
                        original.Replace("formatVersion: 4", $"formatVersion: \"{explicitVersion}\"")
                    else
                        original
                            .Replace("\"formatVersion\": 4", $"\"formatVersion\": \"{explicitVersion}\"")
                            .Replace("\"formatVersion\":4", $"\"formatVersion\":\"{explicitVersion}\"")

                VFile.ofText location updated
            else
                VFile.ofBytes location (System.ReadOnlyMemory(FixtureCatalog.ReadBytes fixture))
        )
        |> VFileTree.fromFileMap

    /// For C# callers: returns the files (relative path -> UTF-8 bytes) of the v4 bounded Orders tree for
    /// <paramref name="profile"/> with its manifest <c>formatVersion</c> set to <paramref name="explicitVersion"/>.
    [<CompiledName("OrdersTreeWithExplicitVersion")>]
    let ordersTreeWithExplicitVersion (profile: string) (explicitVersion: string) : System.Collections.Generic.IReadOnlyDictionary<string, byte array> =
        let tree = v4OrdersTreeWithExplicitVersion profile explicitVersion
        let entries = System.Collections.Generic.Dictionary<string, byte array>(System.StringComparer.Ordinal)

        for KeyValue(location, file) in VFileTree.toFileMap tree do
            match VFile.readText file with
            | Ok text -> entries[location] <- System.Text.Encoding.UTF8.GetBytes text
            | Result.Error err -> failwith $"Failed to read mother tree entry '{location}': {err}"

        System.Collections.ObjectModel.ReadOnlyDictionary<string, byte array>(entries)
        :> System.Collections.Generic.IReadOnlyDictionary<string, byte array>

    /// Builds a v4 plain YAML Orders tree with both <c>order.value.yaml</c> and <c>order.value.yml</c> backed
    /// by guarded streams that invoke <paramref name="onOpen"/> and fail if either ambiguous sibling is opened.
    let v4YamlOrdersTreeWithGuardedAmbiguousSiblings (onOpen: string -> unit) : VFileTree * string * string =
        let yamlPath = "pkg/acme/orders/domain/order.value.yaml"
        let ymlPath = "pkg/acme/orders/domain/order.value.yml"

        let guardedFile path =
            VFile.ofStream
                path
                (StreamSource(fun () ->
                    onOpen path
                    failwith $"Should not open {path} when ambiguous siblings exist."
                ))

        let tree =
            BoundedOrders.catalogDocuments "v4" "plain-yaml"
            |> Map.map (fun location fixture -> VFile.ofBytes location (System.ReadOnlyMemory(FixtureCatalog.ReadBytes fixture)))
            |> Map.add yamlPath (guardedFile yamlPath)
            |> Map.add ymlPath (guardedFile ymlPath)
            |> VFileTree.fromFileMap

        tree, yamlPath, ymlPath

    /// Builds a v4 plain YAML Orders tree whose <c>pkg/acme/orders/domain/module.yaml</c> file invokes
    /// <paramref name="onOpenModule"/> before returning its bytes, used to test mid-decode cancellation.
    let v4YamlOrdersTreeWithOnOpenModule (onOpenModule: unit -> unit) : VFileTree =
        let moduleLocation = "pkg/acme/orders/domain/module.yaml"
        let docs = BoundedOrders.catalogDocuments "v4" "plain-yaml"
        let moduleBytes = FixtureCatalog.ReadBytes docs[moduleLocation]

        docs
        |> Map.map (fun location fixture ->
            if location = moduleLocation then
                VFile.ofStream
                    location
                    (StreamSource(fun () ->
                        onOpenModule ()
                        new System.IO.MemoryStream(moduleBytes, writable = false) :> System.IO.Stream
                    ))
            else
                VFile.ofBytes location (System.ReadOnlyMemory(FixtureCatalog.ReadBytes fixture))
        )
        |> VFileTree.fromFileMap

    /// A valid v4 JSON document tree whose single compacted <c>pkg/acme/orders/domain/module.json</c> envelope
    /// holds two inline value definitions (<c>alpha</c> and <c>beta</c>), used to verify physical path deduplication
    /// in <see cref="LoadedTreeModel.CompactedFiles"/> while preserving all logical entries in <c>ReadReport</c>.
    let compactedInlineTwoValuesModuleTree () : VFileTree =
        let manifest =
            """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":4000}"""

        let valueDef =
            """{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":{"$name":"i"},"body":{"Literal":{"IntegerLiteral":1}}}}}"""

        let compactedModule =
            $"{{\"$schema\":\"{MorphirFile.Draft1Schema}\",\"context\":{{\"fqNames\":{{\"i\":\"morphir/SDK:basics#int\"}}}},\"document\":{{\"formatVersion\":4,\"path\":\"domain\",\"values\":{{\"alpha\":{valueDef},\"beta\":{valueDef}}}}}}}"

        Driver.ofTexts [
            "manifest.json", manifest
            "pkg/acme/orders/domain/module.json", compactedModule
        ]

    /// For C# callers: returns the files (relative path -> UTF-8 bytes) of the v4 bounded Orders tree for
    /// <paramref name="profile"/> with an unknown <c>UnknownBodyTag</c> inside <c>order.value</c>.
    [<CompiledName("OrdersTreeWithUnknownBodyTag")>]
    let ordersTreeWithUnknownBodyTag (profile: string) : System.Collections.Generic.IReadOnlyDictionary<string, byte array> =
        let ext = if profile.EndsWith("-yaml", System.StringComparison.Ordinal) then ".yaml" else ".json"
        let memberPath = $"pkg/acme/orders/domain/order.value{ext}"
        let entries = System.Collections.Generic.Dictionary<string, byte array>(System.StringComparer.Ordinal)

        for KeyValue(location, fixture) in BoundedOrders.catalogDocuments "v4" profile do
            if location = memberPath then
                let mutated = (FixtureCatalog.ReadText fixture).Replace("ExpressionBody", "UnknownBodyTag")
                entries[location] <- System.Text.Encoding.UTF8.GetBytes mutated
            else
                entries[location] <- FixtureCatalog.ReadBytes fixture

        System.Collections.ObjectModel.ReadOnlyDictionary<string, byte array>(entries)
        :> System.Collections.Generic.IReadOnlyDictionary<string, byte array>

    /// A valid v4 JSON document tree with two compacted value members whose aliases each expand to
    /// ~3 * longWordLength bytes so each member fits individually under a tight budget while their
    /// aggregate expanded bytes exceed it.
    let compactedTwoMemberExpandingTree (longWordLength: int) : Map<string, string> =
        let longName = String.replicate longWordLength "n"
        let manifest =
            """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":4000}"""

        let moduleJson =
            """{"formatVersion":4,"path":"domain","values":["alpha","beta"]}"""

        let valueJson name =
            $"{{\"$schema\":\"{MorphirFile.Draft1Schema}\",\"context\":{{\"fqNames\":{{\"q\":\"acme/orders:domain#{longName}\"}}}},\"document\":{{\"formatVersion\":4,\"name\":\"{name}\",\"def\":{{\"Public\":{{\"ExpressionBody\":{{\"inputTypes\":{{}},\"outputType\":{{\"Tuple\":[{{\"$name\":\"q\"}},{{\"$name\":\"q\"}},{{\"$name\":\"q\"}}]}},\"body\":{{\"Literal\":{{\"IntegerLiteral\":1}}}}}}}}}}}}}}"

        Map.ofList [
            "manifest.json", manifest
            "pkg/acme/orders/domain/module.json", moduleJson
            "pkg/acme/orders/domain/alpha.value.json", valueJson "alpha"
            "pkg/acme/orders/domain/beta.value.json", valueJson "beta"
        ]

    /// For C# callers: exposes <see cref="compactedTwoMemberExpandingTree"/> as a .NET read-only dictionary.
    [<CompiledName("CompactedTwoMemberExpandingTree")>]
    let compactedTwoMemberExpandingTreeEntries (longWordLength: int) : System.Collections.Generic.IReadOnlyDictionary<string, string> =
        let entries = System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal)

        for KeyValue(path, content) in compactedTwoMemberExpandingTree longWordLength do
            entries[path] <- content

        System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(entries)
        :> System.Collections.Generic.IReadOnlyDictionary<string, string>

    /// For C# callers: projects a classic v3 single-file JSON distribution to classic wire profile v1 or v2.
    [<CompiledName("ProjectClassicJsonToVersion")>]
    let projectClassicJsonToVersion (version: string) (v3Json: string) : string =
        use parsed = JsonDocument.Parse v3Json

        match Morphir.Json.Classic.ClassicJson.readDistribution parsed.RootElement with
        | Result.Error err -> failwith $"Failed to decode classic fixture: {err}"
        | Ok loaded ->
            let profile =
                if version = "1" then Morphir.Json.Classic.ClassicWireProfile.v1
                else Morphir.Json.Classic.ClassicWireProfile.v2

            match Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes profile true loaded.Distribution with
            | Result.Error err -> failwith $"Failed to project classic fixture to v{version}: {err}"
            | Ok bytes -> System.Text.Encoding.UTF8.GetString bytes

    /// For C# callers: splits a v3.1.0 or v4 single-file JSON distribution into its document-tree files
    /// (relative path -> JSON text) as a .NET read-only dictionary, keeping F# list/Result handling inside F#.
    [<CompiledName("SplitJsonModelToTreeFiles")>]
    let splitJsonModelToTreeFiles (version: string) (json: string) : System.Collections.Generic.IReadOnlyDictionary<string, string> =
        use parsed = JsonDocument.Parse json

        let storedTree =
            if version.StartsWith("4", System.StringComparison.Ordinal) then
                match Morphir.Json.V4.V4Json.readDistribution parsed.RootElement with
                | Ok loaded -> V4Tree(V4DocumentTree.split loaded.Distribution)
                | Result.Error err -> failwith $"Failed to read v4 fixture: {err}"
            else
                match Morphir.Json.Classic.ClassicJson.readDistribution parsed.RootElement with
                | Ok loaded -> ClassicTree(ClassicDocumentTree.split loaded.Distribution)
                | Result.Error err -> failwith $"Failed to read classic fixture: {err}"

        match DocumentTreeStore.write WriteOptions.defaults storedTree with
        | Result.Error errors ->
            let joined = errors |> List.map string |> String.concat "; "
            failwith $"Failed to write document tree fixture: {joined}"
        | Ok tree ->
            let entries = System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal)

            for KeyValue(location, file) in VFileTree.toFileMap tree do
                let text =
                    match VFile.readText file with
                    | Ok value -> value
                    | Result.Error err -> failwith $"Failed to read written tree file '{location}': {err}"

                let adjusted =
                    if location = "manifest.json" && version = "4.0.0" then
                        text
                            .Replace("\"formatVersion\":4", "\"formatVersion\":\"4.0.0\"")
                            .Replace("\"formatVersion\": 4", "\"formatVersion\": \"4.0.0\"")
                    else
                        text

                entries[location] <- adjusted

            System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(entries)
            :> System.Collections.Generic.IReadOnlyDictionary<string, string>
