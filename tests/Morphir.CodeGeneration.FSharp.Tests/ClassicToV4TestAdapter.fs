/// Representation adapter for the shared classic fixture inventory. This is test code only.
/// Production v4 generation reads and emits native v4 nodes directly.
module Morphir.CodeGeneration.FSharp.Tests.ClassicToV4TestAdapter

open System.Globalization
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Model

let private ordered entries =
    match Morphir.IR.V4.OrderedMap.ofList entries with
    | Ok value -> value
    | Error error -> failwithf "Duplicate fixture key %A" error

let private access =
    function
    | AccessControlled.Public -> Morphir.IR.V4.Public
    | AccessControlled.Private -> Morphir.IR.V4.Private

let private controlled
    convert
    (value: AccessControlled.AccessControlled<_>)
    : Morphir.IR.V4.AccessControlled<_> =
    {
        Access = access value.Access
        Value = convert value.Value
    }

let private plain value : Morphir.IR.V4.Documented<_> = { Doc = None; Value = value }

let private documented convert source : Morphir.IR.V4.Documented<_> =
    match source with
    | Documented.WithDocumentation(doc, value) -> {
        Doc = Some doc
        Value = convert value
      }
    | Documented.WithoutDocumentation value -> { Doc = None; Value = convert value }

let rec private typ (value: Type<JsonElement>) =
    let attributes = Morphir.IR.V4.TypeAttributes.empty

    match value with
    | Type.Variable(_, name) -> Morphir.IR.V4.Type.Variable(attributes, name)
    | Type.Reference(_, name, args) ->
        Morphir.IR.V4.Type.Reference(attributes, name, List.map typ args)
    | Type.Tuple(_, elements) -> Morphir.IR.V4.Type.Tuple(attributes, List.map typ elements)
    | Type.Record(_, fields) ->
        Morphir.IR.V4.Type.Record(
            attributes,
            fields
            |> List.map (fun field -> field.Name, typ field.Type)
        )
    | Type.ExtensibleRecord(_, name, fields) ->
        Morphir.IR.V4.Type.ExtensibleRecord(
            attributes,
            name,
            fields
            |> List.map (fun field -> field.Name, typ field.Type)
        )
    | Type.Function(_, input, output) ->
        Morphir.IR.V4.Type.Function(attributes, typ input, typ output)
    | Type.Unit _ -> Morphir.IR.V4.Type.Unit attributes

let private attributes (value: JsonElement) =
    let mutable candidate = value

    if value.ValueKind = JsonValueKind.Object then
        if not (value.TryGetProperty("type", &candidate)) then
            candidate <- Unchecked.defaultof<JsonElement>

    let inferred =
        if candidate.ValueKind = JsonValueKind.Array then
            Some(typ (Morphir.CodeGeneration.FSharp.Decode.typ 3 "$" candidate))
        else
            None

    { Morphir.IR.V4.ValueAttributes.empty with
        InferredType = inferred
    }

let private literal =
    function
    | BoolLiteral value -> Morphir.IR.V4.BoolLiteral value
    | CharLiteral value -> Morphir.IR.V4.CharLiteral(string value)
    | StringLiteral value -> Morphir.IR.V4.StringLiteral value
    | WholeNumberLiteral value -> Morphir.IR.V4.IntegerLiteral(System.Numerics.BigInteger value)
    | FloatLiteral value ->
        match Morphir.IR.V4.FloatValue.create value None with
        | Ok value -> Morphir.IR.V4.FloatLiteral value
        | Error error -> failwith error
    | DecimalLiteral value ->
        match Morphir.IR.V4.DecimalLexeme.create (value.ToString(CultureInfo.InvariantCulture)) with
        | Ok value -> Morphir.IR.V4.DecimalLiteral value
        | Error error -> failwith error

let rec private pattern =
    function
    | WildcardPattern a -> Morphir.IR.V4.WildcardPattern(attributes a)
    | AsPattern(a, p, name) -> Morphir.IR.V4.AsPattern(attributes a, pattern p, name)
    | TuplePattern(a, patterns) ->
        Morphir.IR.V4.TuplePattern(attributes a, List.map pattern patterns)
    | ConstructorPattern(a, name, patterns) ->
        Morphir.IR.V4.ConstructorPattern(attributes a, name, List.map pattern patterns)
    | EmptyListPattern a -> Morphir.IR.V4.EmptyListPattern(attributes a)
    | HeadTailPattern(a, head, tail) ->
        Morphir.IR.V4.HeadTailPattern(attributes a, pattern head, pattern tail)
    | LiteralPattern(a, value) -> Morphir.IR.V4.LiteralPattern(attributes a, literal value)
    | UnitPattern a -> Morphir.IR.V4.UnitPattern(attributes a)

let rec private expression =
    function
    | Value.Literal(a, value) -> Morphir.IR.V4.Value.Literal(attributes a, literal value)
    | Value.Constructor(a, name) -> Morphir.IR.V4.Value.Constructor(attributes a, name)
    | Value.Tuple(a, elements) ->
        Morphir.IR.V4.Value.Tuple(attributes a, List.map expression elements)
    | Value.List(a, elements) ->
        Morphir.IR.V4.Value.List(attributes a, List.map expression elements)
    | Value.Record(a, fields) ->
        Morphir.IR.V4.Value.Record(
            attributes a,
            Map.toList fields
            |> List.map (fun (name, value) -> name, expression value)
        )
    | Value.Variable(a, name) -> Morphir.IR.V4.Value.Variable(attributes a, name)
    | Value.Reference(a, name) -> Morphir.IR.V4.Value.Reference(attributes a, name)
    | Value.Field(a, target, name) ->
        Morphir.IR.V4.Value.Field(attributes a, expression target, name)
    | Value.FieldFunction(a, name) -> Morphir.IR.V4.Value.FieldFunction(attributes a, name)
    | Value.Apply(a, fn, arg) ->
        Morphir.IR.V4.Value.Apply(attributes a, expression fn, expression arg)
    | Value.Lambda(a, p, body) ->
        Morphir.IR.V4.Value.Lambda(attributes a, pattern p, expression body)
    | Value.LetDefinition(a, name, value, body) ->
        Morphir.IR.V4.Value.LetDefinition(
            attributes a,
            name,
            plain (definition value),
            expression body
        )
    | Value.LetRecursion(a, values, body) ->
        Morphir.IR.V4.Value.LetRecursion(
            attributes a,
            Map.toList values
            |> List.map (fun (name, value) -> name, plain (definition value))
            |> ordered,
            expression body
        )
    | Value.Destructure(a, p, value, body) ->
        Morphir.IR.V4.Value.Destructure(attributes a, pattern p, expression value, expression body)
    | Value.IfThenElse(a, condition, yes, no) ->
        Morphir.IR.V4.Value.IfThenElse(
            attributes a,
            expression condition,
            expression yes,
            expression no
        )
    | Value.PatternMatch(a, target, cases) ->
        Morphir.IR.V4.Value.PatternMatch(
            attributes a,
            expression target,
            cases
            |> List.map (fun (p, body) -> pattern p, expression body)
        )
    | Value.UpdateRecord(a, target, fields) ->
        Morphir.IR.V4.Value.UpdateRecord(
            attributes a,
            expression target,
            Map.toList fields
            |> List.map (fun (name, value) -> name, expression value)
        )
    | Value.Unit a -> Morphir.IR.V4.Value.Unit(attributes a)

and private definition value : Morphir.IR.V4.ValueDefinition = {
    InputTypes =
        value.InputTypes
        |> List.map (fun (name, _, t) -> name, typ t)
    Body = Morphir.IR.V4.ExpressionBody(typ value.OutputType, expression value.Body)
}

let private typeDefinition =
    function
    | Type.TypeAliasDefinition(parameters, body) ->
        Morphir.IR.V4.TypeAliasDefinition(parameters, typ body)
    | Type.CustomTypeDefinition(parameters, constructors) ->
        Morphir.IR.V4.CustomTypeDefinition(
            parameters,
            access constructors.Access,
            Map.toList constructors.Value
            |> List.map (fun (name, args) ->
                name,
                args
                |> List.map (fun (name, t) -> name, typ t)
            )
            |> ordered
        )

let promote (model: VersionedModel) =
    match model.Distribution with
    | DistributionModel.V4 _ -> model
    | DistributionModel.Classic(Distribution.Specs _) ->
        failwith "The fixture adapter expects implementations"
    | DistributionModel.Classic(Distribution.Library(package, dependencies, definition')) ->
        if not (Map.isEmpty dependencies) then
            failwith "The fixture adapter needs an explicit dependency-specification conversion"

        let convertModule
            (module': Module.ModuleDefinition<JsonElement, JsonElement>)
            : Morphir.IR.V4.ModuleDefinition =
            {
                Types =
                    Map.toList module'.Types
                    |> List.map (fun (name, value) ->
                        name, controlled (documented typeDefinition) value
                    )
                    |> ordered
                Values =
                    Map.toList module'.Values
                    |> List.map (fun (name, value) ->
                        name, controlled (documented definition) value
                    )
                    |> ordered
                Doc = module'.Doc
            }

        let source =
            match SourceFormatVersion.parse "4.0.0" with
            | Ok value -> value
            | Error error -> failwithf "%A" error

        {
            SourceVersion = source
            Distribution =
                DistributionModel.V4(
                    Morphir.IR.V4.Library(
                        package,
                        ordered [],
                        {
                            Modules =
                                Map.toList definition'.Modules
                                |> List.map (fun (path, value) ->
                                    path, controlled convertModule value
                                )
                                |> ordered
                        }
                    )
                )
        }
