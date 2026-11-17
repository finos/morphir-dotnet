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

/// A type expression no deeper than `depth`, which may use the type variables in scope.
let rec private typeExpr (variables: Name list) (depth: int) : Gen<TypeExpr> =
    let leaf =
        match variables with
        | [] -> Gen.elements leafTypes
        | _ ->
            Gen.frequency [
                2, Gen.elements leafTypes
                1, Gen.map VariableOf (Gen.elements variables)
            ]

    if depth <= 0 then
        leaf
    else
        let inner = typeExpr variables (depth - 1)

        Gen.frequency [
            3, leaf
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

/// Mostly public, sometimes private.
let private visibility: Gen<Visibility> =
    Gen.frequency [
        3, Gen.constant PublicAccess
        1, Gen.constant PrivateAccess
    ]

/// Mostly no documentation, sometimes a line, several lines or non-ASCII text.
let private doc: Gen<string option> =
    Gen.frequency [
        2, Gen.constant None
        1, Gen.elements [ Some "Documented."; Some "A first line.\nA second line."; Some "Prix en €, déjà vu." ]
    ]

/// Up to two type parameters.
let private typeParameters: Gen<Name list> =
    Gen.frequency [
        2, Gen.constant []
        1, Gen.elements [ [ name "a" ]; [ name "a"; name "b" ]; [ name "key"; name "value" ] ]
    ]

let private typeShape: Gen<TypeShape> = gen {
    let! parameters = typeParameters

    return!
        Gen.oneof [
            Gen.map (fun aliased -> Alias(parameters, aliased)) (typeExpr parameters 2)
            gen {
                let! constructorAccess = visibility
                let! constructors = someDistinctNames 3

                let! cases =
                    constructors
                    |> List.map (fun constructor -> gen {
                        let! arguments = distinctNames 2
                        let! types = Gen.listOfLength arguments.Length (typeExpr parameters 1)
                        return constructor, List.zip arguments types
                    })
                    |> Gen.sequence

                return Custom(parameters, constructorAccess, cases)
            }
        ]
}

let private literalShape: Gen<LiteralShape> =
    Gen.oneof [
        Gen.map BoolValue Arb.generate<bool>
        Gen.map TextValue (Gen.elements [ ""; "USD"; "café"; "line\nbreak"; "quote \" and \\ slash"; "€ 10" ])
        Gen.map WholeValue Arb.generate<int64>
    ]

let private valueShape: Gen<ValueShape> =
    Gen.oneof [
        Gen.map Constant literalShape
        Gen.map2 (fun parameter parameterType -> Identity(parameter, parameterType)) canonicalName (typeExpr [] 2)
    ]

/// Adds a generated entry: public or private, with or without documentation.
let private addEntry withPublic withPrivate withDoc (entryName: Name) shape access entryDoc =
    let added =
        match access with
        | PublicAccess -> withPublic entryName shape
        | PrivateAccess -> withPrivate entryName shape

    match entryDoc with
    | Some text -> added >> withDoc entryName text
    | None -> added

let private moduleBuilder: Gen<ModuleBuilder -> ModuleBuilder> = gen {
    let! moduleAccess = visibility
    let! moduleDoc = doc
    let! typeNames = distinctNames 4
    let! types = Gen.listOfLength typeNames.Length (Gen.zip3 typeShape visibility doc)
    let! valueNames = distinctNames 4
    let! values = Gen.listOfLength valueNames.Length (Gen.zip3 valueShape visibility doc)

    let addTypes =
        List.zip typeNames types
        |> List.fold
            (fun build (n, (shape, access, entryDoc)) ->
                build
                >> addEntry withType withPrivateType withTypeDoc n shape access entryDoc
            )
            id

    let addValues =
        List.zip valueNames values
        |> List.fold
            (fun build (n, (shape, access, entryDoc)) ->
                build
                >> addEntry withValue withPrivateValue withValueDoc n shape access entryDoc
            )
            id

    let setModule =
        (match moduleAccess with
         | PublicAccess -> id
         | PrivateAccess -> asPrivateModule)
        >> (match moduleDoc with
            | Some text -> withModuleDoc text
            | None -> id)

    return setModule >> addTypes >> addValues
}

/// Up to `most` distinct items of a pool.
let private distinctOf (most: int) (pool: string list) : Gen<string list> = gen {
    let! picked = Gen.subListOf pool
    return List.truncate most picked
}

/// One to three modules at distinct paths, nested ones among them.
let private modulesOfDependency: Gen<(string * (ModuleBuilder -> ModuleBuilder)) list> = gen {
    let! first = Gen.elements modulePaths
    let! others = distinctOf 2 (modulePaths |> List.filter ((<>) first))
    let paths = first :: others
    let! modules = Gen.listOfLength paths.Length moduleBuilder
    return List.zip paths modules
}

/// A library builder: up to three modules, and up to two dependencies of one to three modules each, none of them
/// the package itself.
let private library (start: PackageName -> LibraryBuilder<'model>) : Gen<LibraryBuilder<'model>> = gen {
    let! package = Gen.elements packages
    let! paths = distinctOf 3 modulePaths
    let! modules = Gen.listOfLength paths.Length moduleBuilder

    let! dependencyPackages =
        packages
        |> List.filter ((<>) package)
        |> distinctOf 2

    let! dependencies = Gen.listOfLength dependencyPackages.Length modulesOfDependency

    let withModules builder =
        List.zip paths modules
        |> List.fold (fun b (p, build) -> b |> withModule (modulePath p) build) builder

    let withDependencies builder =
        List.zip dependencyPackages dependencies
        |> List.fold
            (fun b (p, dependencyModules) ->
                dependencyModules
                |> List.fold
                    (fun inner (m, build) -> inner |> withDependencyModule (packageName p) (modulePath m) build)
                    b
            )
            builder

    return
        start (packageName package)
        |> withModules
        |> withDependencies
}

/// <summary>A v4 library builder, for a test that varies a generated library before it builds it.</summary>
let v4LibraryBuilder: Gen<V4LibraryBuilder> = library aV4Library

/// Mostly no module annotations, sometimes one: they reach the module specifications of a v4 model only.
let private moduleAnnotations: Gen<Morphir.IR.V4.Annotation list> =
    Gen.frequency [
        3, Gen.constant []
        1,
        Gen.constant [
            {
                Name = FQName.fqName (packageName "morphir/SDK") (modulePath "annotations") (name "stable")
                Arguments = []
            }
        ]
    ]

/// A built v4 distribution whose module specifications may carry annotations.
let private annotated (build: V4LibraryBuilder -> Morphir.IR.V4.Distribution) : Gen<Morphir.IR.V4.Distribution> =
    Gen.map2
        (fun builder annotations -> build builder |> withV4ModuleAnnotations annotations)
        v4LibraryBuilder
        moduleAnnotations

/// <summary>A v4 <c>Library</c>; the modules of its dependencies may carry annotations.</summary>
let v4Library: Gen<Morphir.IR.V4.Distribution> = annotated buildV4

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
        1, annotated buildV4Specs
        1, v4Application
    ]

/// <summary>A classic library builder.</summary>
let v3LibraryBuilder: Gen<V3LibraryBuilder> = library aV3Library

/// <summary>
/// A classic <c>Library</c>, the input of a property that writes classic JSON: the classic writer refuses a Specs
/// distribution until its format 3.1.0 writer exists.
/// </summary>
let v3Distribution: Gen<Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>> =
    Gen.map buildV3 v3LibraryBuilder

/// <summary>
/// A classic distribution: mostly a <c>Library</c>, and also <c>Specs</c>, for laws such as the 3.1.0 round trip
/// that must see both.
/// </summary>
let v3AnyDistribution: Gen<Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>> =
    Gen.frequency [
        3, v3Distribution
        1, Gen.map buildV3Specs v3LibraryBuilder
    ]
