/// <summary>
/// FsCheck generators of distributions, built on <see cref="DistributionBuilder"/>: a generator chooses the
/// shapes, and the builder makes the model. Every generated name has a canonical spelling, and names are distinct
/// where the model needs them to be, so every generated distribution is one the writers encode.
/// </summary>
module Morphir.Models.TestSupport.Generators

open FsCheck
open Morphir.IR
open Morphir.Models.TestSupport.DistributionBuilder

/// Segments of generated names: words, initialisms and digits, so that spellings such as value-in-USD occur.
let private segments = [
    "invoice"
    "line"
    "amount"
    "account"
    "value"
    "in"
    "order"
    "status"
    "v2"
    "2024"
    "USD"
    "ID"
]

/// <summary>A name with a canonical spelling, of one to three segments.</summary>
let canonicalName: Gen<Name> = gen {
    let! count = Gen.choose (1, 3)
    let! parts = Gen.listOfLength count (Gen.elements segments)
    return name (String.concat "-" parts)
}

/// Up to `most` distinct names. Two spellings can read as one name (USD-ID and USDID), so distinctness is by name.
let private distinctNames (most: int) : Gen<Name list> = gen {
    let! count = Gen.choose (0, most)
    let! names = Gen.listOfLength count canonicalName
    return List.distinct names
}

/// At least one distinct name, for a record or a set of constructors that needs one.
let private someDistinctNames (most: int) : Gen<Name list> = gen {
    let! first = canonicalName
    let! rest = distinctNames (most - 1)
    return List.distinct (first :: rest)
}

let private packages = [
    "my-org/finance"
    "acme/ledger"
    "acme/ledger/core"
    "finos/morphir-examples"
    "morphir/SDK"
    "my-org/USD-rates"
]

let private modulePaths = [
    "main"
    "pricing"
    "pricing/models"
    "domain/users"
    "IO/v2"
    "ledger/accounts/2024"
]

let private leafTypes = [ UnitType; Sdk.bool; Sdk.int; Sdk.string ]

/// A type expression no deeper than `depth`.
let rec private typeExpr (depth: int) : Gen<TypeExpr> =
    if depth <= 0 then
        Gen.elements leafTypes
    else
        let inner = typeExpr (depth - 1)

        Gen.frequency [
            3, Gen.elements leafTypes
            1, Gen.map Sdk.list inner
            1,
            gen {
                let! fields = someDistinctNames 3
                let! types = Gen.listOfLength fields.Length inner
                return RecordOf(List.zip fields types)
            }
            1,
            gen {
                let! count = Gen.choose (2, 3)
                let! elements = Gen.listOfLength count inner
                return TupleOf elements
            }
            1, Gen.map2 (fun input output -> FunctionOf(input, output)) inner inner
        ]

let private typeShape: Gen<TypeShape> =
    Gen.oneof [
        Gen.map Alias (typeExpr 2)
        gen {
            let! constructors = someDistinctNames 3

            let! cases =
                constructors
                |> List.map (fun constructor -> gen {
                    let! arguments = distinctNames 2
                    let! types = Gen.listOfLength arguments.Length (typeExpr 1)
                    return constructor, List.zip arguments types
                })
                |> Gen.sequence

            return Custom cases
        }
    ]

let private literalShape: Gen<LiteralShape> =
    Gen.oneof [
        Gen.map BoolValue Arb.generate<bool>
        Gen.map TextValue (Gen.elements [ ""; "USD"; "café"; "line\nbreak"; "quote \" and \\ slash"; "€ 10" ])
        Gen.map WholeValue Arb.generate<int64>
    ]

let private valueShape: Gen<ValueShape> =
    Gen.oneof [
        Gen.map Constant literalShape
        Gen.map2 (fun parameter parameterType -> Identity(parameter, parameterType)) canonicalName (typeExpr 2)
    ]

let private moduleBuilder: Gen<ModuleBuilder -> ModuleBuilder> = gen {
    let! typeNames = distinctNames 4
    let! types = Gen.listOfLength typeNames.Length typeShape
    let! valueNames = distinctNames 4
    let! values = Gen.listOfLength valueNames.Length valueShape

    let addTypes =
        List.zip typeNames types
        |> List.fold (fun build (n, shape) -> build >> withType n shape) id

    let addValues =
        List.zip valueNames values
        |> List.fold (fun build (n, shape) -> build >> withValue n shape) id

    return addTypes >> addValues
}

/// Up to `most` distinct items of a pool.
let private distinctOf (most: int) (pool: string list) : Gen<string list> = gen {
    let! picked = Gen.subListOf pool
    return List.truncate most picked
}

/// A library builder: up to three modules and up to two dependencies, none of them the package itself.
let private library (start: PackageName -> LibraryBuilder<'model>) : Gen<LibraryBuilder<'model>> = gen {
    let! package = Gen.elements packages
    let! paths = distinctOf 3 modulePaths
    let! modules = Gen.listOfLength paths.Length moduleBuilder

    let! dependencyPackages =
        packages
        |> List.filter ((<>) package)
        |> distinctOf 2

    let! dependencies = Gen.listOfLength dependencyPackages.Length moduleBuilder

    let withModules builder =
        List.zip paths modules
        |> List.fold (fun b (p, build) -> b |> withModule (modulePath p) build) builder

    let withDependencies builder =
        List.zip dependencyPackages dependencies
        |> List.fold (fun b (p, build) -> b |> withDependency (packageName p) build) builder

    return
        start (packageName package)
        |> withModules
        |> withDependencies
}

/// <summary>A v4 library builder, for a test that varies a generated library before it builds it.</summary>
let v4LibraryBuilder: Gen<V4LibraryBuilder> = library aV4Library

/// <summary>A v4 <c>Library</c>.</summary>
let v4Library: Gen<Morphir.IR.V4.Distribution> = Gen.map buildV4 v4LibraryBuilder

let private entryPoint (package: PackageName) : Gen<Morphir.IR.V4.EntryPoint> = gen {
    let! kind =
        Gen.elements [
            Morphir.IR.V4.Main
            Morphir.IR.V4.Command
            Morphir.IR.V4.Handler
            Morphir.IR.V4.Job
            Morphir.IR.V4.Policy
        ]

    let! target = canonicalName
    let! doc = Gen.elements [ None; Some "Runs the application." ]

    return {
        Target = FQName.fqName package (modulePath "main") target
        Kind = kind
        Doc = doc
    }
}

/// A v4 Application with up to two entry points, whose names are arbitrary text.
let private v4Application: Gen<Morphir.IR.V4.Distribution> = gen {
    let! builder = v4LibraryBuilder
    let! names = distinctOf 2 [ "main"; "nightly batch"; "règle" ]
    let! entryPoints = Gen.listOfLength names.Length (entryPoint builder.Package)
    return buildV4Application (List.zip names entryPoints) builder
}

/// <summary>A v4 distribution: mostly a <c>Library</c>, and also <c>Specs</c> and <c>Application</c>.</summary>
let v4Distribution: Gen<Morphir.IR.V4.Distribution> =
    Gen.frequency [
        3, v4Library
        1, Gen.map buildV4Specs v4LibraryBuilder
        1, v4Application
    ]

/// <summary>A classic library builder.</summary>
let v3LibraryBuilder: Gen<V3LibraryBuilder> = library aV3Library

/// <summary>
/// A classic <c>Library</c>. Only libraries are generated, because the classic writer refuses a Specs
/// distribution until its format 3.1.0 writer exists.
/// </summary>
let v3Distribution: Gen<Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>> =
    Gen.map buildV3 v3LibraryBuilder
