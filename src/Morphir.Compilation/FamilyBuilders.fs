// The native IR builders (MID-1). They are for F# frontends only, like the other builder contracts.
namespace Morphir.Compilation.Building

open System
open System.Numerics
open Morphir.IR
open Morphir.IR.Model
open Morphir.Compilation

/// <summary>The diagnostic codes of the native builders.</summary>
[<RequireQualifiedAccess>]
module BuilderCodes =
    /// <summary>Two definitions in one scope have the same name.</summary>
    let DuplicateName = DiagnosticCode.Create "compile.duplicate_name"

module private Refuse =
    let unrepresentable (location: Location) (message: string) : Checked<'T> =
        Checked.fail [
            Diagnostic.error WriterCodes.Unrepresentable message
            |> Diagnostic.at location
        ]

    let duplicate (location: Location) (what: string) (name: Name) : Checked<'T> =
        let (Name words) = name
        let text = String.Join("-", words)

        Checked.fail [
            Diagnostic.error
                BuilderCodes.DuplicateName
                $"The {what} '{text}' is defined more than once."
            |> Diagnostic.at location
        ]

    let duplicates (location: Location) (what: string) (names: Name list) =
        names
        |> List.countBy id
        |> List.tryFind (fun (_, count) -> count > 1)
        |> Option.map (
            fst
            >> duplicate location what
        )

/// <summary>The builder interface at the classic family's types.</summary>
type ClassicIrBuilder =
    IIrBuilder<Classic.Type<unit>, Classic.Pattern<Classic.Type<unit>>, Classic.Value<unit, Classic.Type<unit>>, Classic.ValueDefinition<unit, Classic.Type<unit>>, Classic.Type.TypeDefinition<unit>, ModulePlan *
    Classic.AccessControlled.AccessControlled<Classic.Module.ModuleDefinition<unit, Classic.Type<unit>>>, Classic.Distribution.Distribution<unit, Classic.Type<unit>>>

/// <summary>The builder interface at the v4 family's types.</summary>
type V4IrBuilder =
    IIrBuilder<V4.Type, V4.Pattern, V4.Value, V4.ValueDefinition, V4.TypeDefinition, ModulePlan *
    V4.AccessControlled<V4.ModuleDefinition>, V4.Distribution>

/// <summary>
/// Builds classic IR natively (MID-1). Every value and pattern carries its inferred type as its attribute;
/// type attributes are unit. Classic IR has no slot for source locations. Content that classic IR cannot
/// hold exactly is refused with <c>morphir.unrepresentable</c>.
/// </summary>
[<Sealed>]
type ClassicBuilder() =
    static let access (value: Access) =
        match value with
        | Access.Public -> Classic.AccessControlled.Public
        | Access.Private -> Classic.AccessControlled.Private

    static let documented (doc: string option) (value: 'T) =
        match doc with
        | Some text -> Classic.Documented.WithDocumentation(text, value)
        | None -> Classic.Documented.WithoutDocumentation value

    static let literal (location: Location) (value: LiteralValue) : Checked<Classic.Literal> =
        match value with
        | LiteralValue.Bool b -> Checked.pass (Classic.BoolLiteral b)
        | LiteralValue.Char rune when rune.IsBmp ->
            Checked.pass (Classic.CharLiteral(char rune.Value))
        | LiteralValue.Char rune ->
            Refuse.unrepresentable
                location
                $"The character U+{rune.Value:X4} is outside the Basic Multilingual Plane, which a classic character literal cannot hold."
        | LiteralValue.String s -> Checked.pass (Classic.StringLiteral s)
        | LiteralValue.Integer i when
            i
            >= BigInteger Int64.MinValue
            && i
               <= BigInteger Int64.MaxValue
            ->
            Checked.pass (Classic.WholeNumberLiteral(int64 i))
        | LiteralValue.Integer i ->
            Refuse.unrepresentable
                location
                $"The integer {i} is outside the 64-bit range that a classic whole number literal holds."
        | LiteralValue.Float f when Double.IsFinite f -> Checked.pass (Classic.FloatLiteral f)
        | LiteralValue.Float _ ->
            Refuse.unrepresentable location "A classic float literal must be finite."
        | LiteralValue.Decimal d -> Checked.pass (Classic.DecimalLiteral d)

    interface ClassicIrBuilder with
        member _.Family = ModelFamily.Classic

        member _.TypeVariable(_, name) =
            Checked.pass (Classic.Type.Variable((), name))

        member _.TypeReference(_, fqName, arguments) =
            Checked.pass (Classic.Type.Reference((), fqName, arguments))

        member _.TypeTuple(_, elements) =
            Checked.pass (Classic.Type.Tuple((), elements))

        member _.TypeRecord(location, fields) =
            match Refuse.duplicates location "record field" (List.map fst fields) with
            | Some refusal -> refusal
            | None ->
                Checked.pass (
                    Classic.Type.Record(
                        (),
                        fields
                        |> List.map (fun (name, t) -> { Name = name; Type = t })
                    )
                )

        member _.TypeFunction(_, argument, result) =
            Checked.pass (Classic.Type.Function((), argument, result))

        member _.TypeUnit _ = Checked.pass (Classic.Type.Unit())

        member _.PatternWildcard(_, inferred) =
            Checked.pass (Classic.WildcardPattern inferred)

        member _.PatternAs(_, inferred, pattern, name) =
            Checked.pass (Classic.AsPattern(inferred, pattern, name))

        member _.PatternTuple(_, inferred, elements) =
            Checked.pass (Classic.TuplePattern(inferred, elements))

        member _.PatternConstructor(_, inferred, fqName, arguments) =
            Checked.pass (Classic.ConstructorPattern(inferred, fqName, arguments))

        member _.PatternEmptyList(_, inferred) =
            Checked.pass (Classic.EmptyListPattern inferred)

        member _.PatternHeadTail(_, inferred, head, tail) =
            Checked.pass (Classic.HeadTailPattern(inferred, head, tail))

        member _.PatternLiteral(location, inferred, value) =
            literal location value
            |> Checked.map (fun l -> Classic.LiteralPattern(inferred, l))

        member _.PatternUnit(_, inferred) =
            Checked.pass (Classic.UnitPattern inferred)

        member _.Literal(location, inferred, value) =
            literal location value
            |> Checked.map (fun l -> Classic.Value.Literal(inferred, l))

        member _.Constructor(_, inferred, fqName) =
            Checked.pass (Classic.Value.Constructor(inferred, fqName))

        member _.Tuple(_, inferred, elements) =
            Checked.pass (Classic.Value.Tuple(inferred, elements))

        member _.List(_, inferred, items) =
            Checked.pass (Classic.Value.List(inferred, items))

        member _.Record(location, inferred, fields) =
            match Refuse.duplicates location "record field" (List.map fst fields) with
            | Some refusal -> refusal
            | None -> Checked.pass (Classic.Value.Record(inferred, Map.ofList fields))

        member _.Variable(_, inferred, name) =
            Checked.pass (Classic.Value.Variable(inferred, name))

        member _.Reference(_, inferred, fqName) =
            Checked.pass (Classic.Value.Reference(inferred, fqName))

        member _.Field(_, inferred, target, name) =
            Checked.pass (Classic.Value.Field(inferred, target, name))

        member _.FieldFunction(_, inferred, name) =
            Checked.pass (Classic.Value.FieldFunction(inferred, name))

        member _.Apply(_, inferred, fn, argument) =
            Checked.pass (Classic.Value.Apply(inferred, fn, argument))

        member _.Lambda(_, inferred, argument, body) =
            Checked.pass (Classic.Value.Lambda(inferred, argument, body))

        member _.LetDefinition(_, inferred, name, definition, inValue) =
            Checked.pass (Classic.Value.LetDefinition(inferred, name, definition, inValue))

        member _.LetRecursion(location, inferred, definitions, inValue) =
            match Refuse.duplicates location "definition" (List.map fst definitions) with
            | Some refusal -> refusal
            | None ->
                Checked.pass (Classic.Value.LetRecursion(inferred, Map.ofList definitions, inValue))

        member _.Destructure(_, inferred, pattern, value, inValue) =
            Checked.pass (Classic.Value.Destructure(inferred, pattern, value, inValue))

        member _.IfThenElse(_, inferred, condition, thenBranch, elseBranch) =
            Checked.pass (Classic.Value.IfThenElse(inferred, condition, thenBranch, elseBranch))

        member _.PatternMatch(_, inferred, subject, cases) =
            Checked.pass (Classic.Value.PatternMatch(inferred, subject, cases))

        member _.UpdateRecord(location, inferred, target, fields) =
            match Refuse.duplicates location "record field" (List.map fst fields) with
            | Some refusal -> refusal
            | None -> Checked.pass (Classic.Value.UpdateRecord(inferred, target, Map.ofList fields))

        member _.Unit(_, inferred) =
            Checked.pass (Classic.Value.Unit inferred)

        member _.ValueDefinition(_, inputs, output, body) =
            Checked.pass
                {
                    InputTypes =
                        inputs
                        |> List.map (fun (name, t) -> name, t, t)
                    OutputType = output
                    Body = body
                }

        member _.TypeAlias(_, parameters, body) =
            Checked.pass (Classic.Type.TypeAliasDefinition(parameters, body))

        member _.CustomType(location, parameters, constructorAccess, constructors) =
            match
                Refuse.duplicates
                    location
                    "constructor"
                    (constructors
                     |> List.map (fun c -> c.Name))
            with
            | Some refusal -> refusal
            | None ->
                let cs =
                    constructors
                    |> List.map (fun c -> c.Name, c.Arguments)
                    |> Map.ofList

                Checked.pass (
                    Classic.Type.CustomTypeDefinition(
                        parameters,
                        ({
                            Access = access constructorAccess
                            Value = cs
                        }
                        : Classic.AccessControlled.AccessControlled<_>)
                    )
                )

        member _.Module(plan, types, values) =
            let duplicate what (members: Member<_> list) =
                members
                |> List.countBy (fun m -> m.Name)
                |> List.tryFind (fun (_, count) -> count > 1)
                |> Option.map (fun (name, _) ->
                    let second =
                        members
                        |> List.filter (fun m -> m.Name = name)
                        |> List.item 1

                    Refuse.duplicate second.Location what name
                )

            match duplicate "type" types, duplicate "value" values with
            | Some refusal, _
            | _, Some refusal -> refusal
            | None, None ->
                let entry (m: Member<_>) =
                    m.Name,
                    ({
                        Access = access m.Access
                        Value = documented m.Documentation m.Definition
                    }
                    : Classic.AccessControlled.AccessControlled<_>)

                let definition: Classic.Module.ModuleDefinition<unit, Classic.Type<unit>> = {
                    Types =
                        types
                        |> List.map entry
                        |> Map.ofList
                    Values =
                        values
                        |> List.map entry
                        |> Map.ofList
                    Doc = plan.Documentation
                }

                Checked.pass (
                    plan,
                    ({
                        Access = access plan.Access
                        Value = definition
                    }
                    : Classic.AccessControlled.AccessControlled<_>)
                )

        member _.Package(plan, modules) =
            let paths =
                modules
                |> List.map (fun (m: ModulePlan, _) -> m.Path)

            match
                paths
                |> List.countBy id
                |> List.tryFind (fun (_, count) -> count > 1)
            with
            | Some(path, _) ->
                let second =
                    modules
                    |> List.filter (fun (m, _) -> m.Path = path)
                    |> List.item 1
                    |> fst

                let (ModulePath(Path names)) = path

                Refuse.duplicate
                    second.Location
                    "module"
                    (Name(
                        names
                        |> List.collect (fun (Name w) -> w)
                    ))
            | None ->
                Checked.pass (
                    Classic.Distribution.Library(
                        plan.Package,
                        Map.empty,
                        {
                            Modules =
                                modules
                                |> List.map (fun (m, definition) -> m.Path, definition)
                                |> Map.ofList
                        }
                    )
                )

/// <summary>
/// Builds IR v4 natively (MID-1). Every value and pattern carries its inferred type and its source location;
/// type nodes carry their source location. Locations become 1-based v4 source locations with an exclusive end.
/// </summary>
[<Sealed>]
type V4Builder() =
    static let source (location: Location) : V4.SourceLocation = {
        StartLine =
            location.Range.Start.Line
            + 1
        StartColumn =
            location.Range.Start.Character
            + 1
        EndLine =
            location.Range.End.Line
            + 1
        EndColumn =
            location.Range.End.Character
            + 1
    }

    static let typeAttributes location : V4.TypeAttributes =
        { V4.TypeAttributes.empty with
            Source = Some(source location)
        }

    static let valueAttributes location (inferred: V4.Type) : V4.ValueAttributes = {
        Source = Some(source location)
        InferredType = Some inferred
        Extensions = None
    }

    static let access (value: Access) =
        match value with
        | Access.Public -> V4.Public
        | Access.Private -> V4.Private

    static let ordered
        (location: Location)
        (what: string)
        (entries: (Name * 'V) list)
        : Checked<V4.OrderedMap<Name, 'V>> =
        match V4.OrderedMap.ofList entries with
        | Ok map -> Checked.pass map
        | Error name -> Refuse.duplicate location what name

    static let literal (location: Location) (value: LiteralValue) : Checked<V4.Literal> =
        match value with
        | LiteralValue.Bool b -> Checked.pass (V4.BoolLiteral b)
        | LiteralValue.Char rune -> Checked.pass (V4.CharLiteral(rune.ToString()))
        | LiteralValue.String s -> Checked.pass (V4.StringLiteral s)
        | LiteralValue.Integer i -> Checked.pass (V4.IntegerLiteral i)
        | LiteralValue.Float f ->
            match V4.FloatValue.create f None with
            | Ok value -> Checked.pass (V4.FloatLiteral value)
            | Error message -> Refuse.unrepresentable location message
        | LiteralValue.Decimal d ->
            match
                V4.DecimalLexeme.create (
                    d.ToString System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | Ok lexeme -> Checked.pass (V4.DecimalLiteral lexeme)
            | Error message -> Refuse.unrepresentable location message

    interface V4IrBuilder with
        member _.Family = ModelFamily.V4

        member _.TypeVariable(location, name) =
            Checked.pass (V4.Type.Variable(typeAttributes location, name))

        member _.TypeReference(location, fqName, arguments) =
            Checked.pass (V4.Type.Reference(typeAttributes location, fqName, arguments))

        member _.TypeTuple(location, elements) =
            Checked.pass (V4.Type.Tuple(typeAttributes location, elements))

        member _.TypeRecord(location, fields) =
            match Refuse.duplicates location "record field" (List.map fst fields) with
            | Some refusal -> refusal
            | None -> Checked.pass (V4.Type.Record(typeAttributes location, fields))

        member _.TypeFunction(location, argument, result) =
            Checked.pass (V4.Type.Function(typeAttributes location, argument, result))

        member _.TypeUnit location =
            Checked.pass (V4.Type.Unit(typeAttributes location))

        member _.PatternWildcard(location, inferred) =
            Checked.pass (V4.WildcardPattern(valueAttributes location inferred))

        member _.PatternAs(location, inferred, pattern, name) =
            Checked.pass (V4.AsPattern(valueAttributes location inferred, pattern, name))

        member _.PatternTuple(location, inferred, elements) =
            Checked.pass (V4.TuplePattern(valueAttributes location inferred, elements))

        member _.PatternConstructor(location, inferred, fqName, arguments) =
            Checked.pass (
                V4.ConstructorPattern(valueAttributes location inferred, fqName, arguments)
            )

        member _.PatternEmptyList(location, inferred) =
            Checked.pass (V4.EmptyListPattern(valueAttributes location inferred))

        member _.PatternHeadTail(location, inferred, head, tail) =
            Checked.pass (V4.HeadTailPattern(valueAttributes location inferred, head, tail))

        member _.PatternLiteral(location, inferred, value) =
            literal location value
            |> Checked.map (fun l -> V4.LiteralPattern(valueAttributes location inferred, l))

        member _.PatternUnit(location, inferred) =
            Checked.pass (V4.UnitPattern(valueAttributes location inferred))

        member _.Literal(location, inferred, value) =
            literal location value
            |> Checked.map (fun l -> V4.Value.Literal(valueAttributes location inferred, l))

        member _.Constructor(location, inferred, fqName) =
            Checked.pass (V4.Value.Constructor(valueAttributes location inferred, fqName))

        member _.Tuple(location, inferred, elements) =
            Checked.pass (V4.Value.Tuple(valueAttributes location inferred, elements))

        member _.List(location, inferred, items) =
            Checked.pass (V4.Value.List(valueAttributes location inferred, items))

        member _.Record(location, inferred, fields) =
            match Refuse.duplicates location "record field" (List.map fst fields) with
            | Some refusal -> refusal
            | None -> Checked.pass (V4.Value.Record(valueAttributes location inferred, fields))

        member _.Variable(location, inferred, name) =
            Checked.pass (V4.Value.Variable(valueAttributes location inferred, name))

        member _.Reference(location, inferred, fqName) =
            Checked.pass (V4.Value.Reference(valueAttributes location inferred, fqName))

        member _.Field(location, inferred, target, name) =
            Checked.pass (V4.Value.Field(valueAttributes location inferred, target, name))

        member _.FieldFunction(location, inferred, name) =
            Checked.pass (V4.Value.FieldFunction(valueAttributes location inferred, name))

        member _.Apply(location, inferred, fn, argument) =
            Checked.pass (V4.Value.Apply(valueAttributes location inferred, fn, argument))

        member _.Lambda(location, inferred, argument, body) =
            Checked.pass (V4.Value.Lambda(valueAttributes location inferred, argument, body))

        member _.LetDefinition(location, inferred, name, definition, inValue) =
            Checked.pass (
                V4.Value.LetDefinition(
                    valueAttributes location inferred,
                    name,
                    { Doc = None; Value = definition },
                    inValue
                )
            )

        member _.LetRecursion(location, inferred, definitions, inValue) =
            ordered
                location
                "definition"
                (definitions
                 |> List.map (fun (name, d) -> name, ({ Doc = None; Value = d }: V4.Documented<_>)))
            |> Checked.map (fun map ->
                V4.Value.LetRecursion(valueAttributes location inferred, map, inValue)
            )

        member _.Destructure(location, inferred, pattern, value, inValue) =
            Checked.pass (
                V4.Value.Destructure(valueAttributes location inferred, pattern, value, inValue)
            )

        member _.IfThenElse(location, inferred, condition, thenBranch, elseBranch) =
            Checked.pass (
                V4.Value.IfThenElse(
                    valueAttributes location inferred,
                    condition,
                    thenBranch,
                    elseBranch
                )
            )

        member _.PatternMatch(location, inferred, subject, cases) =
            Checked.pass (V4.Value.PatternMatch(valueAttributes location inferred, subject, cases))

        member _.UpdateRecord(location, inferred, target, fields) =
            match Refuse.duplicates location "record field" (List.map fst fields) with
            | Some refusal -> refusal
            | None ->
                Checked.pass (
                    V4.Value.UpdateRecord(valueAttributes location inferred, target, fields)
                )

        member _.Unit(location, inferred) =
            Checked.pass (V4.Value.Unit(valueAttributes location inferred))

        member _.ValueDefinition(_, inputs, output, body) =
            Checked.pass
                {
                    InputTypes = inputs
                    Body = V4.ExpressionBody(output, body)
                }

        member _.TypeAlias(_, parameters, body) =
            Checked.pass (V4.TypeAliasDefinition(parameters, body))

        member _.CustomType(location, parameters, constructorAccess, constructors) =
            ordered
                location
                "constructor"
                (constructors
                 |> List.map (fun c -> c.Name, c.Arguments))
            |> Checked.map (fun cs ->
                V4.CustomTypeDefinition(parameters, access constructorAccess, cs)
            )

        member _.Module(plan, types, values) =
            let entries (members: Member<'D> list) =
                members
                |> List.map (fun m ->
                    m.Name,
                    ({
                        Access = access m.Access
                        Value = {
                            Doc = m.Documentation
                            Value = m.Definition
                        }
                    }
                    : V4.AccessControlled<V4.Documented<'D>>)
                )

            check {
                let! typeMap = ordered plan.Location "type" (entries types)
                and! valueMap = ordered plan.Location "value" (entries values)

                let definition: V4.ModuleDefinition = {
                    Types = typeMap
                    Values = valueMap
                    Doc = plan.Documentation
                }

                return
                    plan,
                    ({
                        Access = access plan.Access
                        Value = definition
                    }
                    : V4.AccessControlled<_>)
            }

        member _.Package(plan, modules) =
            match
                V4.OrderedMap.ofList (
                    modules
                    |> List.map (fun (m: ModulePlan, definition) -> m.Path, definition)
                )
            with
            | Ok map ->
                Checked.pass (V4.Library(plan.Package, V4.OrderedMap.empty, { Modules = map }))
            | Error path ->
                let second =
                    modules
                    |> List.filter (fun (m, _) -> m.Path = path)
                    |> List.item 1
                    |> fst

                let (ModulePath(Path names)) = path

                Refuse.duplicate
                    second.Location
                    "module"
                    (Name(
                        names
                        |> List.collect (fun (Name w) -> w)
                    ))

/// <summary>The classic family builder, for the builder registry.</summary>
[<Sealed>]
type ClassicFamilyBuilder() =
    interface IFamilyBuilder with
        member _.Family = ModelFamily.Classic

        member _.Build program =
            program.Run(ClassicBuilder() :> IIrBuilder<_, _, _, _, _, _, _>)
            |> Checked.map FamilyModel.Classic

/// <summary>The v4 family builder, for the builder registry.</summary>
[<Sealed>]
type V4FamilyBuilder() =
    interface IFamilyBuilder with
        member _.Family = ModelFamily.V4

        member _.Build program =
            program.Run(V4Builder() :> IIrBuilder<_, _, _, _, _, _, _>)
            |> Checked.map FamilyModel.V4
