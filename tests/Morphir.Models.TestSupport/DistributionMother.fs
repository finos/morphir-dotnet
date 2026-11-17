/// <summary>
/// The named distributions that tests share. Each case is a built model named for the intent a test needs, and
/// each is made with <see cref="DistributionBuilder"/>. A test that needs a variation builds it from the builder
/// rather than adding a case here.
/// </summary>
/// <remarks>
/// Every case encodes with the existing writers except two, which stand for a refusal: <c>v3Specs</c> (the
/// classic writer refuses a Specs distribution until its format 3.1.0 writer exists) and <c>collidingStems</c>
/// (its two type names have one spelling, so the v4 writer refuses the one that has no canonical spelling).
/// </remarks>
module Morphir.Models.TestSupport.DistributionMother

open Morphir.IR
open Morphir.IR.V4
open Morphir.Models.TestSupport.DistributionBuilder

/// <summary>The names that cases are built around, for tests that look for them.</summary>
module Names =
    /// <summary>The name that <c>nameUsedOnce</c> writes once and <c>nameUsedOften</c> writes many times.</summary>
    let repeated = name "settlement-instruction"

    /// <summary>
    /// The type name of <c>nonAsciiName</c>. The canonical name grammar is lowercase ASCII, so only a classic
    /// model, whose writer spells words as they are, holds it.
    /// </summary>
    let nonAscii = Name.fromList [ "café" ]

    /// <summary>The two words <c>invoice</c> and <c>line</c>, spelled <c>invoice-line</c>.</summary>
    let invoiceLine = Name.fromList [ "invoice"; "line" ]

    /// <summary>One word that holds the separator, also spelled <c>invoice-line</c>.</summary>
    let invoiceLineAsOneWord = Name.fromList [ "invoice-line" ]

    /// <summary>The Windows device names that <c>windowsReservedStems</c> uses.</summary>
    let windowsDevices = [ name "con"; name "aux"; name "nul" ]

let private finance = packageName "my-org/finance"

let private pricing = modulePath "pricing/models"

let private invoiceType = Named(fqName "my-org/finance" "pricing/models" "invoice", [])

/// A module with an alias, a custom type, a constant and a function.
let private pricingModule =
    withType (name "invoice") (alias (RecordOf [ name "amount", Sdk.int; name "currency", Sdk.string ]))
    >> withType (name "status") (custom [ name "open", []; name "paid", [ name "amount", Sdk.int ] ])
    >> withValue (name "default-currency") (Constant(TextValue "USD"))
    >> withValue (name "same-invoice") (Identity(name "invoice", invoiceType))

/// The one module of each dependency in these cases.
let private sdkBasics =
    withType (name "money") (alias Sdk.int)
    >> withValue (name "zero") (Constant(WholeValue 0L))

let private financeV4 =
    aV4Library finance
    |> withModule pricing pricingModule

let private financeV3 =
    aV3Library finance
    |> withModule pricing pricingModule

/// <summary>A v4 library with one module of types and values, and no dependency.</summary>
let v4LibraryWithOneModule: Distribution = buildV4 financeV4

/// <summary>A v4 library with one module and one dependency, <c>morphir/SDK</c>.</summary>
let v4LibraryWithDependency: Distribution =
    financeV4
    |> withDependency Sdk.package sdkBasics
    |> buildV4

/// <summary>The specification of <c>v4LibraryWithDependency</c>.</summary>
let v4Specs: Distribution =
    financeV4
    |> withDependency Sdk.package sdkBasics
    |> buildV4Specs

/// <summary>A v4 application with a linked dependency and a <c>main</c> entry point.</summary>
let v4Application: Distribution =
    aV4Library (packageName "my-org/billing")
    |> withModule (modulePath "main") (withValue (name "run") (Identity(name "invoice", Sdk.string)))
    |> withDependency Sdk.package sdkBasics
    |> buildV4Application [
        "main",
        {
            Target = fqName "my-org/billing" "main" "run"
            Kind = Main
            Doc = None
        }
    ]

/// <summary>
/// <c>v4Specs</c> with an annotation on each module: <c>my-org/finance:pricing/models</c> and the
/// <c>morphir/SDK</c> dependency's module.
/// </summary>
let v4SpecsWithModuleAnnotations: Distribution =
    v4Specs
    |> withV4ModuleAnnotations [
        {
            Name = fqName "morphir/SDK" "annotations" "stable"
            Arguments = []
        }
    ]

/// <summary>
/// The worked example of the model-convert design: package <c>acme/demo</c>, module <c>billing</c> with the types
/// <c>invoice</c> and <see cref="Names.invoiceLine"/> and the value <c>total</c>, and dependency
/// <c>morphir/sdk</c> with module <c>basics</c>.
/// </summary>
let acmeDemo: Distribution =
    aV4Library (packageName "acme/demo")
    |> withModule
        (modulePath "billing")
        (withType (name "invoice") (alias (RecordOf [ name "amount", Sdk.int ]))
         >> withType Names.invoiceLine (alias Sdk.int)
         >> withValue (name "total") (Constant(WholeValue 0L)))
    |> withDependencyModule (packageName "morphir/sdk") (modulePath "basics") sdkBasics
    |> buildV4

/// <summary>
/// A v4 library with one module and two dependencies: <c>morphir/SDK</c> with one module, and
/// <c>acme/ledger</c> with none. A tree has no document of <c>acme/ledger</c>; only its manifest records it.
/// </summary>
let dependencyWithoutModules: Distribution =
    financeV4
    |> withDependency Sdk.package sdkBasics
    |> withDependencyWithoutModules (packageName "acme/ledger")
    |> buildV4

/// <summary>A classic library with the module of <c>v4LibraryWithOneModule</c>.</summary>
let v3Library: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    buildV3 financeV3

/// <summary>A classic Specs distribution with one module and one dependency. The classic writer refuses it.</summary>
let v3Specs: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    financeV3
    |> withDependency Sdk.package sdkBasics
    |> buildV3Specs

/// A public module and a private one. The public module has a documented generic alias, a private type, a custom
/// type whose constructors are private, a documented value and a private value.
let private withPrivateAndDocumentedMembers (start: PackageName -> LibraryBuilder<'model>) =
    let pair = Named(fqName "my-org/finance" "accounts" "pair", [ Sdk.int; Sdk.string ])

    start finance
    |> withModule
        (modulePath "accounts")
        (withModuleDoc "Accounts and their balances."
         >> withType (name "pair") (Alias([ name "a"; name "b" ], TupleOf [ VariableOf(name "a"); VariableOf(name "b") ]))
         >> withTypeDoc (name "pair") "Two values, of the types a and b."
         >> withPrivateType (name "ledger-key") (alias Sdk.string)
         >> withType
             (name "balance")
             (Custom([ name "currency" ], PrivateAccess, [ name "balance", [ name "amount", Sdk.int ] ]))
         >> withValue (name "opening-balance") (Constant(WholeValue 0L))
         >> withValueDoc (name "opening-balance") "The balance of a new account, in cents. Zéro."
         >> withPrivateValue (name "same-pair") (Identity(name "pair", pair)))
    |> withModule
        (modulePath "accounts/internal")
        (asPrivateModule
         >> withModuleDoc "Helpers that the package keeps to itself."
         >> withValue (name "audit") (Constant(BoolValue true)))
    |> withDependencyModule Sdk.package (modulePath "basics") sdkBasics
    |> withDependencyModule
        Sdk.package
        (modulePath "string/format")
        (withValue (name "pad") (Constant(TextValue " ")))

/// <summary>
/// A v4 library with private members, a private module, private constructors, a generic type and documentation on
/// a module, a type and a value, and a dependency with two modules, one of them nested.
/// </summary>
let privateAndDocumentedMembers: Distribution =
    withPrivateAndDocumentedMembers aV4Library
    |> buildV4

/// <summary>The classic library of <c>privateAndDocumentedMembers</c>.</summary>
let v3PrivateAndDocumentedMembers: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    withPrivateAndDocumentedMembers aV3Library
    |> buildV3

/// <summary>A v4 library with one module that has no types and no values.</summary>
let emptyModule: Distribution =
    aV4Library finance
    |> withModule (modulePath "empty") id
    |> buildV4

/// <summary>A v4 library with no modules.</summary>
let noModules: Distribution = aV4Library finance |> buildV4

/// <summary>A v4 library that spells <see cref="Names.repeated"/> once, as the name of its one type.</summary>
let nameUsedOnce: Distribution =
    aV4Library (packageName "my-org/settlement")
    |> withModule (modulePath "ledger") (withType Names.repeated (alias Sdk.string))
    |> buildV4

/// <summary>
/// A v4 library that spells <see cref="Names.repeated"/> twelve times, as a field of each of its twelve record
/// types.
/// </summary>
let nameUsedOften: Distribution =
    let record =
        alias (RecordOf [ Names.repeated, Sdk.string; name "amount", Sdk.int ])

    let types =
        [
            "trade"
            "order"
            "payment"
            "invoice"
            "refund"
            "transfer"
            "deposit"
            "withdrawal"
            "fee"
            "credit"
            "debit"
            "charge"
        ]
        |> List.map (fun typeName -> withType (name typeName) record)
        |> List.reduce (>>)

    aV4Library (packageName "my-org/settlement")
    |> withModule (modulePath "ledger") types
    |> buildV4

/// <summary>A classic library whose one type is named <see cref="Names.nonAscii"/>, a name with an <c>é</c>.</summary>
let nonAsciiName: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    aV3Library (packageName "my-org/menu")
    |> withModule (modulePath "drinks") (withType Names.nonAscii (alias Sdk.string))
    |> buildV3

/// <summary>
/// A v4 library with one module whose two types, <see cref="Names.invoiceLine"/> and
/// <see cref="Names.invoiceLineAsOneWord"/>, are different names with one spelling, <c>invoice-line</c>, and so
/// one escaped stem. A valid v4 name has one spelling, so the second name has no canonical spelling and the v4
/// writer refuses it; a tree writer must refuse the pair before two documents map to one file.
/// </summary>
let collidingStems: Distribution =
    aV4Library finance
    |> withModule
        pricing
        (withType Names.invoiceLine (alias Sdk.int)
         >> withType Names.invoiceLineAsOneWord (alias Sdk.string))
    |> buildV4

/// <summary>
/// A v4 library with a module named <c>aux</c> and types named <c>con</c>, <c>aux</c> and <c>nul</c>, which
/// Windows reserves as device names. A name that ends with a dot is not here: the canonical name grammar has no
/// dot, so no v4 name ends with one.
/// </summary>
let windowsReservedStems: Distribution =
    aV4Library (packageName "my-org/devices")
    |> withModule
        (modulePath "aux")
        (Names.windowsDevices
         |> List.map (fun device -> withType device (alias Sdk.string))
         |> List.reduce (>>))
    |> buildV4
