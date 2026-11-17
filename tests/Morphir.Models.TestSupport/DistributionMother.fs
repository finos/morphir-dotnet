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
    withType (name "invoice") (Alias(RecordOf [ name "amount", Sdk.int; name "currency", Sdk.string ]))
    >> withType (name "status") (Custom [ name "open", []; name "paid", [ name "amount", Sdk.int ] ])
    >> withValue (name "default-currency") (Constant(TextValue "USD"))
    >> withValue (name "same-invoice") (Identity(name "invoice", invoiceType))

/// The one module of each dependency in these cases.
let private sdkBasics =
    withType (name "money") (Alias Sdk.int)
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

/// <summary>A classic library with the module of <c>v4LibraryWithOneModule</c>.</summary>
let v3Library: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    buildV3 financeV3

/// <summary>A classic Specs distribution with one module and one dependency. The classic writer refuses it.</summary>
let v3Specs: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    financeV3
    |> withDependency Sdk.package sdkBasics
    |> buildV3Specs

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
    |> withModule (modulePath "ledger") (withType Names.repeated (Alias Sdk.string))
    |> buildV4

/// <summary>
/// A v4 library that spells <see cref="Names.repeated"/> twelve times, as a field of each of its twelve record
/// types.
/// </summary>
let nameUsedOften: Distribution =
    let record =
        Alias(RecordOf [ Names.repeated, Sdk.string; name "amount", Sdk.int ])

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
    |> withModule (modulePath "drinks") (withType Names.nonAscii (Alias Sdk.string))
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
        (withType Names.invoiceLine (Alias Sdk.int)
         >> withType Names.invoiceLineAsOneWord (Alias Sdk.string))
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
         |> List.map (fun device -> withType device (Alias Sdk.string))
         |> List.reduce (>>))
    |> buildV4
