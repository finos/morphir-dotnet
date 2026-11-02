/// JsonElement has no value equality, so classic distributions are compared after their attributes become
/// canonical strings.
module Morphir.Ion.Tests.ClassicComparison

open System
open System.Text.Json
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented

/// A JSON value as text, with object members sorted and numbers as written.
let rec canonical (element: JsonElement) : string =
    match element.ValueKind with
    | JsonValueKind.Object ->
        element.EnumerateObject()
        |> Seq.map (fun property -> property.Name, canonical property.Value)
        |> Seq.sortWith (fun (left, _) (right, _) -> String.CompareOrdinal(left, right))
        |> Seq.map (fun (name, value) -> $"{JsonSerializer.Serialize name}:{value}")
        |> String.concat ","
        |> fun members ->
            "{"
            + members
            + "}"
    | JsonValueKind.Array ->
        element.EnumerateArray()
        |> Seq.map canonical
        |> String.concat ","
        |> fun items ->
            "["
            + items
            + "]"
    | _ -> element.GetRawText()

let rec private mapType (t: Type<JsonElement>) : Type<string> =
    let fields (values: Field<JsonElement> list) : Field<string> list =
        values
        |> List.map (fun field -> {
            Name = field.Name
            Type = mapType field.Type
        })

    match t with
    | Type.Variable(a, name) -> Type.Variable(canonical a, name)
    | Type.Reference(a, name, args) -> Type.Reference(canonical a, name, List.map mapType args)
    | Type.Tuple(a, items) -> Type.Tuple(canonical a, List.map mapType items)
    | Type.Record(a, values) -> Type.Record(canonical a, fields values)
    | Type.ExtensibleRecord(a, name, values) ->
        Type.ExtensibleRecord(canonical a, name, fields values)
    | Type.Function(a, input, output) -> Type.Function(canonical a, mapType input, mapType output)
    | Type.Unit a -> Type.Unit(canonical a)

let rec private mapPattern (p: Pattern<JsonElement>) : Pattern<string> =
    match p with
    | WildcardPattern a -> WildcardPattern(canonical a)
    | AsPattern(a, inner, name) -> AsPattern(canonical a, mapPattern inner, name)
    | TuplePattern(a, items) -> TuplePattern(canonical a, List.map mapPattern items)
    | ConstructorPattern(a, name, items) ->
        ConstructorPattern(canonical a, name, List.map mapPattern items)
    | EmptyListPattern a -> EmptyListPattern(canonical a)
    | HeadTailPattern(a, head, tail) ->
        HeadTailPattern(canonical a, mapPattern head, mapPattern tail)
    | LiteralPattern(a, literal) -> LiteralPattern(canonical a, literal)
    | UnitPattern a -> UnitPattern(canonical a)

let rec private mapValue (v: Value<JsonElement, JsonElement>) : Value<string, string> =
    let fields = Map.map (fun _ item -> mapValue item)

    match v with
    | Value.Literal(a, literal) -> Value.Literal(canonical a, literal)
    | Value.Constructor(a, name) -> Value.Constructor(canonical a, name)
    | Value.Tuple(a, items) -> Value.Tuple(canonical a, List.map mapValue items)
    | Value.List(a, items) -> Value.List(canonical a, List.map mapValue items)
    | Value.Record(a, values) -> Value.Record(canonical a, fields values)
    | Value.Variable(a, name) -> Value.Variable(canonical a, name)
    | Value.Reference(a, name) -> Value.Reference(canonical a, name)
    | Value.Field(a, target, name) -> Value.Field(canonical a, mapValue target, name)
    | Value.FieldFunction(a, name) -> Value.FieldFunction(canonical a, name)
    | Value.Apply(a, f, x) -> Value.Apply(canonical a, mapValue f, mapValue x)
    | Value.Lambda(a, p, body) -> Value.Lambda(canonical a, mapPattern p, mapValue body)
    | Value.LetDefinition(a, name, definition, body) ->
        Value.LetDefinition(canonical a, name, mapDefinition definition, mapValue body)
    | Value.LetRecursion(a, definitions, body) ->
        Value.LetRecursion(
            canonical a,
            Map.map (fun _ definition -> mapDefinition definition) definitions,
            mapValue body
        )
    | Value.Destructure(a, p, value, body) ->
        Value.Destructure(canonical a, mapPattern p, mapValue value, mapValue body)
    | Value.IfThenElse(a, c, t, e) ->
        Value.IfThenElse(canonical a, mapValue c, mapValue t, mapValue e)
    | Value.PatternMatch(a, value, cases) ->
        Value.PatternMatch(
            canonical a,
            mapValue value,
            cases
            |> List.map (fun (p, body) -> mapPattern p, mapValue body)
        )
    | Value.UpdateRecord(a, target, values) ->
        Value.UpdateRecord(canonical a, mapValue target, fields values)
    | Value.Unit a -> Value.Unit(canonical a)

and private mapDefinition
    (definition: ValueDefinition<JsonElement, JsonElement>)
    : ValueDefinition<string, string> =
    {
        InputTypes =
            definition.InputTypes
            |> List.map (fun (name, a, t) -> name, canonical a, mapType t)
        OutputType = mapType definition.OutputType
        Body = mapValue definition.Body
    }

let private mapDocumented map documented =
    match documented with
    | WithDocumentation(doc, value) -> WithDocumentation(doc, map value)
    | WithoutDocumentation value -> WithoutDocumentation(map value)

let private mapAccess map (controlled: AccessControlled<_>) : AccessControlled<_> = {
    Access = controlled.Access
    Value = map controlled.Value
}

let private mapConstructors
    (constructors: Type.Constructors<JsonElement>)
    : Type.Constructors<string> =
    constructors
    |> Map.map (fun _ args ->
        args
        |> List.map (fun (name, t) -> name, mapType t)
    )

let private mapTypeDefinition definition =
    match definition with
    | Type.TypeAliasDefinition(parameters, t) -> Type.TypeAliasDefinition(parameters, mapType t)
    | Type.CustomTypeDefinition(parameters, constructors) ->
        Type.CustomTypeDefinition(parameters, mapAccess mapConstructors constructors)

let private mapTypeSpecification specification =
    match specification with
    | Type.TypeAliasSpecification(parameters, t) ->
        Type.TypeAliasSpecification(parameters, mapType t)
    | Type.OpaqueTypeSpecification parameters -> Type.OpaqueTypeSpecification parameters
    | Type.CustomTypeSpecification(parameters, constructors) ->
        Type.CustomTypeSpecification(parameters, mapConstructors constructors)
    | Type.DerivedTypeSpecification(parameters, details) ->
        Type.DerivedTypeSpecification(
            parameters,
            {
                BaseType = mapType details.BaseType
                FromBaseType = details.FromBaseType
                ToBaseType = details.ToBaseType
            }
        )

let private mapValueSpecification
    (specification: ValueSpecification<JsonElement>)
    : ValueSpecification<string> =
    {
        Inputs =
            specification.Inputs
            |> List.map (fun (name, t) -> name, mapType t)
        Output = mapType specification.Output
    }

let private mapModuleSpecification
    (specification: Module.ModuleSpecification<JsonElement>)
    : Module.ModuleSpecification<string> =
    {
        Types =
            specification.Types
            |> Map.map (fun _ item -> mapDocumented mapTypeSpecification item)
        Values =
            specification.Values
            |> Map.map (fun _ item -> mapDocumented mapValueSpecification item)
        Doc = specification.Doc
    }

let private mapModuleDefinition
    (definition: Module.ModuleDefinition<JsonElement, JsonElement>)
    : Module.ModuleDefinition<string, string> =
    {
        Types =
            definition.Types
            |> Map.map (fun _ item -> mapAccess (mapDocumented mapTypeDefinition) item)
        Values =
            definition.Values
            |> Map.map (fun _ item -> mapAccess (mapDocumented mapDefinition) item)
        Doc = definition.Doc
    }

let private mapPackageSpecification
    (specification: Package.PackageSpecification<JsonElement>)
    : Package.PackageSpecification<string> =
    {
        Modules =
            specification.Modules
            |> Map.map (fun _ item -> mapModuleSpecification item)
    }

/// A distribution whose attributes are canonical strings, so that it has value equality.
let comparable
    (distribution: Distribution<JsonElement, JsonElement>)
    : Distribution<string, string> =
    match distribution with
    | Library(name, dependencies, definition) ->
        Library(
            name,
            dependencies
            |> Map.map (fun _ item -> mapPackageSpecification item),
            {
                Modules =
                    definition.Modules
                    |> Map.map (fun _ item -> mapAccess mapModuleDefinition item)
            }
        )
    | Specs(name, dependencies, specification) ->
        Specs(
            name,
            dependencies
            |> Map.map (fun _ item -> mapPackageSpecification item),
            mapPackageSpecification specification
        )
