namespace Morphir.IR.Classic

open Morphir.IR
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented
open Morphir.IR.Classic.Module
open Morphir.IR.Classic.Package
open Morphir.IR.Classic.Distribution

/// <summary>
/// Maps the type attributes and the value attributes of a classic model, and keeps every other part. A reader that
/// keeps attributes as JSON uses it to build a typed model.
/// </summary>
[<RequireQualifiedAccess>]
module Attributes =

    let rec mapType (f: 'a -> 'b) (t: Type<'a>) : Type<'b> =
        let field (item: Field<'a>) : Field<'b> = { Name = item.Name; Type = mapType f item.Type }

        match t with
        | Type.Variable(a, name) -> Type.Variable(f a, name)
        | Type.Reference(a, name, arguments) -> Type.Reference(f a, name, List.map (mapType f) arguments)
        | Type.Tuple(a, items) -> Type.Tuple(f a, List.map (mapType f) items)
        | Type.Record(a, fields) -> Type.Record(f a, List.map field fields)
        | Type.ExtensibleRecord(a, name, fields) -> Type.ExtensibleRecord(f a, name, List.map field fields)
        | Type.Function(a, input, output) -> Type.Function(f a, mapType f input, mapType f output)
        | Type.Unit a -> Type.Unit(f a)

    let rec mapPattern (f: 'a -> 'b) (p: Pattern<'a>) : Pattern<'b> =
        match p with
        | WildcardPattern a -> WildcardPattern(f a)
        | AsPattern(a, inner, name) -> AsPattern(f a, mapPattern f inner, name)
        | TuplePattern(a, items) -> TuplePattern(f a, List.map (mapPattern f) items)
        | ConstructorPattern(a, name, items) -> ConstructorPattern(f a, name, List.map (mapPattern f) items)
        | EmptyListPattern a -> EmptyListPattern(f a)
        | HeadTailPattern(a, head, tail) -> HeadTailPattern(f a, mapPattern f head, mapPattern f tail)
        | LiteralPattern(a, literal) -> LiteralPattern(f a, literal)
        | UnitPattern a -> UnitPattern(f a)

    let private constructors (f: 'a -> 'b) (items: Type.Constructors<'a>) : Type.Constructors<'b> =
        items |> Map.map (fun _ arguments -> arguments |> List.map (fun (name, t) -> name, mapType f t))

    let mapTypeSpecification (f: 'a -> 'b) (specification: Type.TypeSpecification<'a>) : Type.TypeSpecification<'b> =
        match specification with
        | Type.TypeAliasSpecification(parameters, t) -> Type.TypeAliasSpecification(parameters, mapType f t)
        | Type.OpaqueTypeSpecification parameters -> Type.OpaqueTypeSpecification parameters
        | Type.CustomTypeSpecification(parameters, items) -> Type.CustomTypeSpecification(parameters, constructors f items)
        | Type.DerivedTypeSpecification(parameters, details) ->
            Type.DerivedTypeSpecification(
                parameters,
                { BaseType = mapType f details.BaseType
                  FromBaseType = details.FromBaseType
                  ToBaseType = details.ToBaseType }
            )

    let mapTypeDefinition (f: 'a -> 'b) (definition: Type.TypeDefinition<'a>) : Type.TypeDefinition<'b> =
        match definition with
        | Type.TypeAliasDefinition(parameters, t) -> Type.TypeAliasDefinition(parameters, mapType f t)
        | Type.CustomTypeDefinition(parameters, items) ->
            Type.CustomTypeDefinition(parameters, AccessControlled.map (constructors f) items)

    let mapValueSpecification (f: 'a -> 'b) (specification: ValueSpecification<'a>) : ValueSpecification<'b> =
        { Inputs = specification.Inputs |> List.map (fun (name, t) -> name, mapType f t)
          Output = mapType f specification.Output }

    let rec mapValue (ft: 'ta -> 'tb) (fv: 'va -> 'vb) (v: Value<'ta, 'va>) : Value<'tb, 'vb> =
        let value = mapValue ft fv
        let pattern = mapPattern fv

        match v with
        | Literal(a, literal) -> Literal(fv a, literal)
        | Constructor(a, name) -> Constructor(fv a, name)
        | Value.Tuple(a, items) -> Value.Tuple(fv a, List.map value items)
        | List(a, items) -> List(fv a, List.map value items)
        | Value.Record(a, fields) -> Value.Record(fv a, Map.map (fun _ item -> value item) fields)
        | Value.Variable(a, name) -> Value.Variable(fv a, name)
        | Value.Reference(a, name) -> Value.Reference(fv a, name)
        | Field(a, subject, name) -> Field(fv a, value subject, name)
        | FieldFunction(a, name) -> FieldFunction(fv a, name)
        | Apply(a, target, argument) -> Apply(fv a, value target, value argument)
        | Lambda(a, argument, body) -> Lambda(fv a, pattern argument, value body)
        | LetDefinition(a, name, definition, body) -> LetDefinition(fv a, name, mapValueDefinition ft fv definition, value body)
        | LetRecursion(a, definitions, body) ->
            LetRecursion(fv a, Map.map (fun _ item -> mapValueDefinition ft fv item) definitions, value body)
        | Destructure(a, target, subject, body) -> Destructure(fv a, pattern target, value subject, value body)
        | IfThenElse(a, condition, whenTrue, whenFalse) -> IfThenElse(fv a, value condition, value whenTrue, value whenFalse)
        | PatternMatch(a, subject, cases) ->
            PatternMatch(fv a, value subject, cases |> List.map (fun (target, body) -> pattern target, value body))
        | UpdateRecord(a, subject, fields) -> UpdateRecord(fv a, value subject, Map.map (fun _ item -> value item) fields)
        | Value.Unit a -> Value.Unit(fv a)

    and mapValueDefinition
        (ft: 'ta -> 'tb)
        (fv: 'va -> 'vb)
        (definition: ValueDefinition<'ta, 'va>)
        : ValueDefinition<'tb, 'vb> =
        { InputTypes = definition.InputTypes |> List.map (fun (name, a, t) -> name, fv a, mapType ft t)
          OutputType = mapType ft definition.OutputType
          Body = mapValue ft fv definition.Body }

    let private documented (f: 'a -> 'b) (item: Documented<'a>) : Documented<'b> = Documented.map f item

    let mapModuleSpecification (f: 'a -> 'b) (specification: ModuleSpecification<'a>) : ModuleSpecification<'b> =
        { Types = specification.Types |> Map.map (fun _ item -> documented (mapTypeSpecification f) item)
          Values = specification.Values |> Map.map (fun _ item -> documented (mapValueSpecification f) item)
          Doc = specification.Doc }

    let mapModuleDefinition
        (ft: 'ta -> 'tb)
        (fv: 'va -> 'vb)
        (definition: ModuleDefinition<'ta, 'va>)
        : ModuleDefinition<'tb, 'vb> =
        { Types =
            definition.Types
            |> Map.map (fun _ item -> AccessControlled.map (documented (mapTypeDefinition ft)) item)
          Values =
            definition.Values
            |> Map.map (fun _ item -> AccessControlled.map (documented (mapValueDefinition ft fv)) item)
          Doc = definition.Doc }

    let mapPackageSpecification (f: 'a -> 'b) (specification: PackageSpecification<'a>) : PackageSpecification<'b> =
        { Modules = specification.Modules |> Map.map (fun _ item -> mapModuleSpecification f item) }

    let mapPackageDefinition
        (ft: 'ta -> 'tb)
        (fv: 'va -> 'vb)
        (definition: PackageDefinition<'ta, 'va>)
        : PackageDefinition<'tb, 'vb> =
        { Modules =
            definition.Modules
            |> Map.map (fun _ item -> AccessControlled.map (mapModuleDefinition ft fv) item) }

    /// <summary>Maps every type attribute with <paramref name="ft"/> and every value attribute with <paramref name="fv"/>.</summary>
    let mapDistribution
        (ft: 'ta -> 'tb)
        (fv: 'va -> 'vb)
        (distribution: Distribution<'ta, 'va>)
        : Distribution<'tb, 'vb> =
        let dependencies =
            Map.map (fun _ specification -> mapPackageSpecification ft specification)

        match distribution with
        | Library(name, dependencyMap, definition) ->
            Library(name, dependencies dependencyMap, mapPackageDefinition ft fv definition)
        | Specs(name, dependencyMap, specification) ->
            Specs(name, dependencies dependencyMap, mapPackageSpecification ft specification)
