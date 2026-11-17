/// <summary>
/// A test-data builder for Morphir distributions. A test states the modules, types, values and dependencies it
/// needs as shapes, and the builder makes the v4 or the classic (v3) model from them:
/// <c>aV4Library pkg |> withModule path (withType name shape) |> withDependency dep id |> buildV4</c>.
/// </summary>
/// <remarks>
/// The classic DSLs of Morphir.Models are computation expressions over one version, and no v4 DSL exists, so the
/// builder makes both models from the model constructors themselves. A shape is the same for both versions, which
/// is what lets one builder serve the v4 and the classic cases and the generators of both. Access, documentation
/// and type parameters are part of the shapes, so a test or a law can see a conversion that loses them.
/// </remarks>
module Morphir.Models.TestSupport.DistributionBuilder

open Morphir.IR

/// <summary>A type expression that both versions can spell.</summary>
type TypeExpr =
    /// <summary>The unit type.</summary>
    | UnitType
    /// <summary>A type variable, such as a type parameter of the type being defined.</summary>
    | VariableOf of Name
    /// <summary>A reference to a named type with its arguments.</summary>
    | Named of FQName * TypeExpr list
    /// <summary>A record, with its fields in order.</summary>
    | RecordOf of (Name * TypeExpr) list
    /// <summary>A tuple.</summary>
    | TupleOf of TypeExpr list
    /// <summary>A function from one type to another.</summary>
    | FunctionOf of TypeExpr * TypeExpr

/// <summary>Whether a definition is visible outside its package.</summary>
type Visibility =
    /// <summary>Visible outside the package.</summary>
    | PublicAccess
    /// <summary>Visible only inside the package.</summary>
    | PrivateAccess

/// <summary>A type definition.</summary>
type TypeShape =
    /// <summary>An alias of a type expression, with its type parameters.</summary>
    | Alias of parameters: Name list * aliased: TypeExpr
    /// <summary>
    /// A custom type with its type parameters, the visibility of its constructors, and its constructors in order,
    /// each with its named arguments. A custom type with private constructors is opaque in a specification.
    /// </summary>
    | Custom of
        parameters: Name list *
        constructorAccess: Visibility *
        constructors: (Name * (Name * TypeExpr) list) list

/// <summary>A literal that both versions can spell.</summary>
type LiteralShape =
    /// <summary>A boolean.</summary>
    | BoolValue of bool
    /// <summary>A string.</summary>
    | TextValue of string
    /// <summary>A whole number.</summary>
    | WholeValue of int64

/// <summary>A value definition.</summary>
type ValueShape =
    /// <summary>A constant: no inputs, a literal body, and the SDK type of the literal.</summary>
    | Constant of LiteralShape
    /// <summary>A one-input function that returns its input.</summary>
    | Identity of parameter: Name * parameterType: TypeExpr

/// <summary>A named type or value of a module, with its visibility and documentation.</summary>
type Entry<'shape> = {
    /// <summary>The name.</summary>
    Name: Name
    /// <summary>The definition.</summary>
    Shape: 'shape
    /// <summary>The visibility.</summary>
    Access: Visibility
    /// <summary>The documentation.</summary>
    Doc: string option
}

/// <summary>The types and values of one module, in the order they were added, with its visibility and doc.</summary>
type ModuleBuilder = {
    /// <summary>The types in order.</summary>
    Types: Entry<TypeShape> list
    /// <summary>The values in order.</summary>
    Values: Entry<ValueShape> list
    /// <summary>The visibility of the module.</summary>
    Access: Visibility
    /// <summary>The documentation of the module.</summary>
    Doc: string option
}

/// <summary>Marks a library builder for the v4 model.</summary>
type V4Model = private | V4Model

/// <summary>Marks a library builder for the classic model.</summary>
type V3Model = private | V3Model

/// <summary>
/// A library in the making: its package, its modules and its dependencies in the order they were added. The type
/// parameter says which model it builds, so <c>buildV4</c> takes only a v4 builder.
/// </summary>
type LibraryBuilder<'model> = {
    /// <summary>The package.</summary>
    Package: PackageName
    /// <summary>The modules in order.</summary>
    Modules: (ModulePath * ModuleBuilder) list
    /// <summary>The dependencies in order, each with its modules in order.</summary>
    Dependencies: (PackageName * (ModulePath * ModuleBuilder) list) list
}

/// <summary>A builder of a v4 library.</summary>
type V4LibraryBuilder = LibraryBuilder<V4Model>

/// <summary>A builder of a classic library.</summary>
type V3LibraryBuilder = LibraryBuilder<V3Model>

/// <summary>Reads a canonical name, such as <c>value-in-USD</c>. Test data that does not read is a bug.</summary>
let name (text: string) : Name =
    match CanonicalNames.parseName text with
    | Ok parsed -> parsed
    | Error message -> invalidArg (nameof text) message

/// <summary>Reads a canonical path, such as <c>morphir/SDK</c>.</summary>
let path (text: string) : Path =
    match CanonicalNames.parsePath text with
    | Ok parsed -> parsed
    | Error message -> invalidArg (nameof text) message

/// <summary>Reads a canonical package name.</summary>
let packageName (text: string) : PackageName = PackageName(path text)

/// <summary>Reads a canonical module name.</summary>
let modulePath (text: string) : ModulePath = ModulePath(path text)

/// <summary>A fully-qualified name from canonical spellings of its three parts.</summary>
let fqName (package: string) (moduleName: string) (localName: string) : FQName =
    FQName.fqName (packageName package) (modulePath moduleName) (name localName)

/// <summary>The module that <c>withDependency</c> gives a dependency.</summary>
let dependencyModule: ModulePath = modulePath "api"

/// <summary>An alias with no type parameters.</summary>
let alias (aliased: TypeExpr) : TypeShape = Alias([], aliased)

/// <summary>A custom type with no type parameters and public constructors.</summary>
let custom (constructors: (Name * (Name * TypeExpr) list) list) : TypeShape =
    Custom([], PublicAccess, constructors)

/// <summary>The SDK types that constants have.</summary>
module Sdk =
    /// <summary>The SDK package, <c>morphir/SDK</c>.</summary>
    let package: PackageName = packageName "morphir/SDK"

    /// <summary>A type of the SDK with no arguments.</summary>
    let typeOf (moduleName: string) (localName: string) : TypeExpr =
        Named(fqName "morphir/SDK" moduleName localName, [])

    /// <summary><c>Bool</c>.</summary>
    let bool = typeOf "basics" "bool"

    /// <summary><c>Int</c>.</summary>
    let int = typeOf "basics" "int"

    /// <summary><c>String</c>.</summary>
    let string = typeOf "string" "string"

    /// <summary><c>List a</c>.</summary>
    let list (item: TypeExpr) : TypeExpr =
        Named(fqName "morphir/SDK" "list" "list", [ item ])

    /// <summary>The SDK type of a literal.</summary>
    let typeOfLiteral (literal: LiteralShape) : TypeExpr =
        match literal with
        | BoolValue _ -> bool
        | TextValue _ -> string
        | WholeValue _ -> int

/// <summary>A public module with no types, no values and no documentation.</summary>
let emptyModuleBuilder: ModuleBuilder = {
    Types = []
    Values = []
    Access = PublicAccess
    Doc = None
}

/// <summary>Starts a v4 library of a package, with no modules and no dependencies.</summary>
let aV4Library (package: PackageName) : V4LibraryBuilder = {
    Package = package
    Modules = []
    Dependencies = []
}

/// <summary>Starts a classic library of a package, with no modules and no dependencies.</summary>
let aV3Library (package: PackageName) : V3LibraryBuilder = {
    Package = package
    Modules = []
    Dependencies = []
}

/// <summary>Adds a module, built from an empty public module by <paramref name="build"/>.</summary>
let withModule
    (path: ModulePath)
    (build: ModuleBuilder -> ModuleBuilder)
    (library: LibraryBuilder<'model>)
    : LibraryBuilder<'model> =
    {
        library with
            Modules =
                library.Modules
                @ [ path, build emptyModuleBuilder ]
    }

let private entry access entryName shape = {
    Name = entryName
    Shape = shape
    Access = access
    Doc = None
}

/// <summary>Adds a public type with no documentation.</summary>
let withType (typeName: Name) (shape: TypeShape) (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Types =
            moduleBuilder.Types
            @ [ entry PublicAccess typeName shape ]
}

/// <summary>Adds a private type with no documentation.</summary>
let withPrivateType (typeName: Name) (shape: TypeShape) (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Types =
            moduleBuilder.Types
            @ [ entry PrivateAccess typeName shape ]
}

/// <summary>Adds a public value with no documentation.</summary>
let withValue (valueName: Name) (shape: ValueShape) (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Values =
            moduleBuilder.Values
            @ [ entry PublicAccess valueName shape ]
}

/// <summary>Adds a private value with no documentation.</summary>
let withPrivateValue (valueName: Name) (shape: ValueShape) (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Values =
            moduleBuilder.Values
            @ [ entry PrivateAccess valueName shape ]
}

let private documentEntry (what: string) (entryName: Name) (doc: string) (entries: Entry<'shape> list) =
    if
        entries
        |> List.exists (fun e -> e.Name = entryName)
        |> not
    then
        invalidOp $"The module has no {what} %A{entryName} to document."

    entries
    |> List.map (fun e -> if e.Name = entryName then { e with Doc = Some doc } else e)

/// <summary>Documents a type that the module already has.</summary>
let withTypeDoc (typeName: Name) (doc: string) (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Types = documentEntry "type" typeName doc moduleBuilder.Types
}

/// <summary>Documents a value that the module already has.</summary>
let withValueDoc (valueName: Name) (doc: string) (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Values = documentEntry "value" valueName doc moduleBuilder.Values
}

/// <summary>Documents the module.</summary>
let withModuleDoc (doc: string) (moduleBuilder: ModuleBuilder) : ModuleBuilder = { moduleBuilder with Doc = Some doc }

/// <summary>Makes the module private to its package.</summary>
let asPrivateModule (moduleBuilder: ModuleBuilder) : ModuleBuilder = {
    moduleBuilder with
        Access = PrivateAccess
}

/// <summary>
/// Adds a module to a dependency, and adds the dependency if the library has none for that package yet. A
/// library depends on the specification of a dependency, so its private members and modules are left out; an
/// application links the whole definition.
/// </summary>
let withDependencyModule
    (package: PackageName)
    (path: ModulePath)
    (build: ModuleBuilder -> ModuleBuilder)
    (library: LibraryBuilder<'model>)
    : LibraryBuilder<'model> =
    let added = path, build emptyModuleBuilder

    let dependencies =
        if
            library.Dependencies
            |> List.exists (fun (p, _) -> p = package)
        then
            library.Dependencies
            |> List.map (fun (p, modules) -> if p = package then p, modules @ [ added ] else p, modules)
        else
            library.Dependencies
            @ [ package, [ added ] ]

    { library with Dependencies = dependencies }

/// <summary>Adds a dependency module at <see cref="dependencyModule"/>: the shorthand for a one-module dependency.</summary>
let withDependency
    (package: PackageName)
    (build: ModuleBuilder -> ModuleBuilder)
    (library: LibraryBuilder<'model>)
    : LibraryBuilder<'model> =
    withDependencyModule package dependencyModule build library

/// Builds a v4 OrderedMap, refusing a key that repeats: test data with a repeated name is a bug.
let private orderedMap (what: string) (entries: ('k * 'v) list) =
    match Morphir.IR.V4.OrderedMap.ofList entries with
    | Ok map -> map
    | Error key -> invalidOp $"The {what} %A{key} appears more than once."

/// Builds a classic Map, refusing a key that repeats, as the v4 builder does.
let private uniqueMap (what: string) (entries: ('k * 'v) list) =
    match
        entries
        |> List.countBy fst
        |> List.tryFind (fun (_, count) -> count > 1)
    with
    | Some(key, _) -> invalidOp $"The {what} %A{key} appears more than once."
    | None -> Map.ofList entries

/// The public entries, which are all that a specification has.
let private publicOnly (entries: Entry<'shape> list) =
    entries
    |> List.filter (fun e -> e.Access = PublicAccess)

let private publicModules (modules: (ModulePath * ModuleBuilder) list) =
    modules
    |> List.filter (fun (_, m) -> m.Access = PublicAccess)

/// The v4 model of the shapes.
module private ToV4 =
    open System.Numerics
    open Morphir.IR.V4

    let rec typ (expression: TypeExpr) : Type =
        let none = TypeAttributes.empty

        match expression with
        | UnitType -> Type.Unit none
        | VariableOf variable -> Type.Variable(none, variable)
        | Named(target, arguments) -> Type.Reference(none, target, List.map typ arguments)
        | RecordOf fields -> Type.Record(none, fields |> List.map (fun (n, t) -> n, typ t))
        | TupleOf elements -> Type.Tuple(none, List.map typ elements)
        | FunctionOf(input, output) -> Type.Function(none, typ input, typ output)

    let private access (visibility: Visibility) : Access =
        match visibility with
        | PublicAccess -> Public
        | PrivateAccess -> Private

    let private constructors (cases: (Name * (Name * TypeExpr) list) list) : Constructors =
        cases
        |> List.map (fun (n, arguments) -> n, arguments |> List.map (fun (a, t) -> a, typ t))
        |> orderedMap "constructor"

    let private literal (shape: LiteralShape) : Literal =
        match shape with
        | BoolValue flag -> BoolLiteral flag
        | TextValue text -> StringLiteral text
        | WholeValue number -> IntegerLiteral(BigInteger number)

    let private documented doc value : Documented<_> = { Doc = doc; Value = value }

    let private controlled visibility value : AccessControlled<_> = { Access = access visibility; Value = value }

    let typeDefinition (shape: TypeShape) : TypeDefinition =
        match shape with
        | Alias(parameters, aliased) -> TypeAliasDefinition(parameters, typ aliased)
        | Custom(parameters, constructorAccess, cases) ->
            CustomTypeDefinition(parameters, access constructorAccess, constructors cases)

    let typeSpecification (shape: TypeShape) : TypeSpecification =
        match shape with
        | Alias(parameters, aliased) -> TypeAliasSpecification([], parameters, typ aliased)
        | Custom(parameters, PublicAccess, cases) -> CustomTypeSpecification([], parameters, constructors cases)
        | Custom(parameters, PrivateAccess, _) -> OpaqueTypeSpecification([], parameters)

    let valueDefinition (shape: ValueShape) : ValueDefinition =
        match shape with
        | Constant value ->
            let output = typ (Sdk.typeOfLiteral value)

            {
                InputTypes = []
                Body = ExpressionBody(output, Value.Literal(ValueAttributes.empty, literal value))
            }
        | Identity(parameter, parameterType) ->
            let input = typ parameterType

            {
                InputTypes = [ parameter, input ]
                Body = ExpressionBody(input, Value.Variable(ValueAttributes.empty, parameter))
            }

    let valueSpecification (shape: ValueShape) : ValueSpecification =
        match shape with
        | Constant value -> {
            Annotations = []
            Inputs = []
            Output = typ (Sdk.typeOfLiteral value)
          }
        | Identity(parameter, parameterType) -> {
            Annotations = []
            Inputs = [ parameter, typ parameterType ]
            Output = typ parameterType
          }

    let moduleDefinition (m: ModuleBuilder) : ModuleDefinition = {
        Types =
            m.Types
            |> List.map (fun e -> e.Name, controlled e.Access (documented e.Doc (typeDefinition e.Shape)))
            |> orderedMap "type"
        Values =
            m.Values
            |> List.map (fun e -> e.Name, controlled e.Access (documented e.Doc (valueDefinition e.Shape)))
            |> orderedMap "value"
        Doc = m.Doc
    }

    let moduleSpecification (m: ModuleBuilder) : ModuleSpecification = {
        Annotations = []
        Types =
            publicOnly m.Types
            |> List.map (fun e -> e.Name, documented e.Doc (typeSpecification e.Shape))
            |> orderedMap "type"
        Values =
            publicOnly m.Values
            |> List.map (fun e -> e.Name, documented e.Doc (valueSpecification e.Shape))
            |> orderedMap "value"
        Doc = m.Doc
    }

    let packageDefinition (modules: (ModulePath * ModuleBuilder) list) : PackageDefinition = {
        Modules =
            modules
            |> List.map (fun (p, m) -> p, controlled m.Access (moduleDefinition m))
            |> orderedMap "module"
    }

    let packageSpecification (modules: (ModulePath * ModuleBuilder) list) : PackageSpecification = {
        Modules =
            publicModules modules
            |> List.map (fun (p, m) -> p, moduleSpecification m)
            |> orderedMap "module"
    }

    let dependencies (make: (ModulePath * ModuleBuilder) list -> 'd) (library: LibraryBuilder<_>) =
        library.Dependencies
        |> List.map (fun (package, modules) -> package, make modules)
        |> orderedMap "dependency"

/// The classic model of the shapes: type attributes are unit, and each value carries its type.
module private ToClassic =
    open Morphir.IR.Classic
    open Morphir.IR.Classic.AccessControlled
    open Morphir.IR.Classic.Documented

    let rec typ (expression: TypeExpr) : Type<unit> =
        match expression with
        | UnitType -> Type.Unit()
        | VariableOf variable -> Type.Variable((), variable)
        | Named(target, arguments) -> Type.Reference((), target, List.map typ arguments)
        | RecordOf fields -> Type.Record((), fields |> List.map (fun (n, t) -> { Name = n; Type = typ t }))
        | TupleOf elements -> Type.Tuple((), List.map typ elements)
        | FunctionOf(input, output) -> Type.Function((), typ input, typ output)

    let private controlled (visibility: Visibility) value =
        match visibility with
        | PublicAccess -> public' value
        | PrivateAccess -> private' value

    let private documented (doc: string option) value =
        match doc with
        | Some text -> withDocumentation text value
        | None -> withoutDocumentation value

    let private constructors (cases: (Name * (Name * TypeExpr) list) list) : Type.Constructors<unit> =
        cases
        |> List.map (fun (n, arguments) -> n, arguments |> List.map (fun (a, t) -> a, typ t))
        |> uniqueMap "constructor"

    let private literal (shape: LiteralShape) : Literal =
        match shape with
        | BoolValue flag -> BoolLiteral flag
        | TextValue text -> StringLiteral text
        | WholeValue number -> WholeNumberLiteral number

    let typeDefinition (shape: TypeShape) : Type.TypeDefinition<unit> =
        match shape with
        | Alias(parameters, aliased) -> Type.TypeAliasDefinition(parameters, typ aliased)
        | Custom(parameters, constructorAccess, cases) ->
            Type.CustomTypeDefinition(parameters, controlled constructorAccess (constructors cases))

    let typeSpecification (shape: TypeShape) : Type.TypeSpecification<unit> =
        match shape with
        | Alias(parameters, aliased) -> Type.TypeAliasSpecification(parameters, typ aliased)
        | Custom(parameters, PublicAccess, cases) -> Type.CustomTypeSpecification(parameters, constructors cases)
        | Custom(parameters, PrivateAccess, _) -> Type.OpaqueTypeSpecification parameters

    let valueDefinition (shape: ValueShape) : ValueDefinition<unit, Type<unit>> =
        match shape with
        | Constant value ->
            let output = typ (Sdk.typeOfLiteral value)

            {
                InputTypes = []
                OutputType = output
                Body = Value.Literal(output, literal value)
            }
        | Identity(parameter, parameterType) ->
            let input = typ parameterType

            {
                InputTypes = [ parameter, input, input ]
                OutputType = input
                Body = Value.Variable(input, parameter)
            }

    let valueSpecification (shape: ValueShape) : ValueSpecification<unit> =
        match shape with
        | Constant value -> {
            Inputs = []
            Output = typ (Sdk.typeOfLiteral value)
          }
        | Identity(parameter, parameterType) -> {
            Inputs = [ parameter, typ parameterType ]
            Output = typ parameterType
          }

    let moduleDefinition (m: ModuleBuilder) : Module.ModuleDefinition<unit, Type<unit>> =
        Module.moduleDefinition
            (m.Types
             |> List.map (fun e -> e.Name, controlled e.Access (documented e.Doc (typeDefinition e.Shape)))
             |> uniqueMap "type")
            (m.Values
             |> List.map (fun e -> e.Name, controlled e.Access (documented e.Doc (valueDefinition e.Shape)))
             |> uniqueMap "value")
            m.Doc

    let moduleSpecification (m: ModuleBuilder) : Module.ModuleSpecification<unit> =
        Module.moduleSpecification
            (publicOnly m.Types
             |> List.map (fun e -> e.Name, documented e.Doc (typeSpecification e.Shape))
             |> uniqueMap "type")
            (publicOnly m.Values
             |> List.map (fun e -> e.Name, documented e.Doc (valueSpecification e.Shape))
             |> uniqueMap "value")
            m.Doc

    let packageDefinition (modules: (ModulePath * ModuleBuilder) list) : Package.PackageDefinition<unit, Type<unit>> =
        Package.packageDefinition (
            modules
            |> List.map (fun (p, m) -> p, controlled m.Access (moduleDefinition m))
            |> uniqueMap "module"
        )

    let packageSpecification (modules: (ModulePath * ModuleBuilder) list) : Package.PackageSpecification<unit> =
        Package.packageSpecification (
            publicModules modules
            |> List.map (fun (p, m) -> p, moduleSpecification m)
            |> uniqueMap "module"
        )

    let dependencies (library: V3LibraryBuilder) =
        library.Dependencies
        |> List.map (fun (package, modules) -> package, packageSpecification modules)
        |> uniqueMap "dependency"

/// <summary>Builds a v4 <c>Library</c>.</summary>
let buildV4 (library: V4LibraryBuilder) : Morphir.IR.V4.Distribution =
    Morphir.IR.V4.Library(
        library.Package,
        ToV4.dependencies ToV4.packageSpecification library,
        ToV4.packageDefinition library.Modules
    )

/// <summary>
/// Builds a v4 <c>Specs</c>: the public interface of the library and its dependencies. Private members and
/// modules are left out, and a custom type with private constructors is opaque.
/// </summary>
let buildV4Specs (library: V4LibraryBuilder) : Morphir.IR.V4.Distribution =
    Morphir.IR.V4.Specs(
        library.Package,
        ToV4.dependencies ToV4.packageSpecification library,
        ToV4.packageSpecification library.Modules
    )

/// <summary>
/// Builds a v4 <c>Application</c> with its entry points in order. Its dependencies are linked, so they carry
/// their definitions.
/// </summary>
let buildV4Application
    (entryPoints: (string * Morphir.IR.V4.EntryPoint) list)
    (library: V4LibraryBuilder)
    : Morphir.IR.V4.Distribution =
    Morphir.IR.V4.Application(
        library.Package,
        ToV4.dependencies ToV4.packageDefinition library,
        ToV4.packageDefinition library.Modules,
        entryPoints
    )

/// <summary>Builds a classic <c>Library</c>, the shape that <c>ClassicJsonWriter.writeTyped</c> writes.</summary>
let buildV3 (library: V3LibraryBuilder) : Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    Morphir.IR.Classic.Distribution.Library(
        library.Package,
        ToClassic.dependencies library,
        ToClassic.packageDefinition library.Modules
    )

/// <summary>
/// Builds a classic <c>Specs</c>, the public interface as <c>buildV4Specs</c> makes it. The classic writer refuses
/// it today: a Specs distribution needs format 3.1.0.
/// </summary>
let buildV3Specs
    (library: V3LibraryBuilder)
    : Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>> =
    Morphir.IR.Classic.Distribution.Specs(
        library.Package,
        ToClassic.dependencies library,
        ToClassic.packageSpecification library.Modules
    )
