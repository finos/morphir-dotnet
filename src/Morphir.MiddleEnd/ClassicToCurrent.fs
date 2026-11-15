// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open System
open System.Collections.Generic
open System.Globalization
open System.Numerics
open System.Text.Json
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Model

/// <summary>
/// The upgrade step from the classic IR (format versions 1 to 3) to the current IR (version 4). The repository had no
/// conversion between the two families before this step. The conversion keeps every node and every name. A classic node
/// attribute that carries data moves to the <c>extensions</c> of the node, under the key <c>classic</c>, so no data is
/// lost. A node with a value that the current IR cannot hold, such as a float literal that is not finite, is an error.
/// </summary>
module ClassicToCurrent =

    /// <summary>The id of the step.</summary>
    [<CompiledName("Id")>]
    let id = RuleId.Create "https://morphir.finos.org/ir/upgrades/classic-to-current"

    /// <summary>The format version that the step writes: the integer <c>4</c>.</summary>
    let target = Versioning.Classic 4

    type private Failure = string

    let private ( >>= ) (result: Result<'a, Failure>) (binder: 'a -> Result<'b, Failure>) = Result.bind binder result

    let rec private traverse (mapping: 'a -> Result<'b, Failure>) (items: 'a list) : Result<'b list, Failure> =
        match items with
        | [] -> Ok []
        | head :: tail -> mapping head >>= fun h -> traverse mapping tail >>= fun t -> Ok(h :: t)

    // ----- attributes -----

    let rec private document (element: JsonElement) : V4.DocumentValue =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> V4.DocumentNull
        | JsonValueKind.True -> V4.DocumentBool true
        | JsonValueKind.False -> V4.DocumentBool false
        | JsonValueKind.Number -> V4.DocumentNumber(element.GetRawText())
        | JsonValueKind.String -> V4.DocumentString(element.GetString() |> string)
        | JsonValueKind.Array -> V4.DocumentArray [ for item in element.EnumerateArray() -> document item ]
        | _ ->
            let members = Dictionary<string, V4.DocumentValue>()
            let order = ResizeArray<string>()

            for property in element.EnumerateObject() do
                if not (members.ContainsKey property.Name) then
                    order.Add property.Name

                members[property.Name] <- document property.Value

            match V4.OrderedMap.ofList [ for key in order -> key, members[key] ] with
            | Ok map -> V4.DocumentObject map
            | Error _ -> V4.DocumentNull

    /// <summary>The extensions that keep the data of a classic attribute. An empty attribute has none.</summary>
    let private extensions (attribute: JsonElement) : V4.DocumentValue option =
        let empty =
            match attribute.ValueKind with
            | JsonValueKind.Undefined
            | JsonValueKind.Null -> true
            | JsonValueKind.Object -> attribute.EnumerateObject() |> Seq.isEmpty
            | _ -> false

        if empty then
            None
        else
            match V4.OrderedMap.ofList [ "classic", document attribute ] with
            | Ok map -> Some(V4.DocumentObject map)
            | Error _ -> None

    let private typeAttributes (attribute: JsonElement) : V4.TypeAttributes =
        { V4.TypeAttributes.empty with Extensions = extensions attribute }

    let private valueAttributes (attribute: JsonElement) : V4.ValueAttributes =
        { V4.ValueAttributes.empty with Extensions = extensions attribute }

    // ----- types -----

    let rec private convertType (t: Type<JsonElement>) : Result<V4.Type, Failure> =
        match t with
        | Type.Variable(a, name) -> Ok(V4.Type.Variable(typeAttributes a, name))
        | Type.Reference(a, fq, arguments) ->
            arguments |> traverse convertType >>= fun lowered -> Ok(V4.Type.Reference(typeAttributes a, fq, lowered))
        | Type.Tuple(a, elements) ->
            elements |> traverse convertType >>= fun lowered -> Ok(V4.Type.Tuple(typeAttributes a, lowered))
        | Type.Record(a, fields) ->
            fields |> traverse convertField >>= fun lowered -> Ok(V4.Type.Record(typeAttributes a, lowered))
        | Type.ExtensibleRecord(a, variable, fields) ->
            fields |> traverse convertField >>= fun lowered -> Ok(V4.Type.ExtensibleRecord(typeAttributes a, variable, lowered))
        | Type.Function(a, parameter, result) ->
            convertType parameter >>= fun p -> convertType result >>= fun r -> Ok(V4.Type.Function(typeAttributes a, p, r))
        | Type.Unit a -> Ok(V4.Type.Unit(typeAttributes a))

    and private convertField (field: Field<JsonElement>) : Result<Name * V4.Type, Failure> =
        convertType field.Type >>= fun t -> Ok(field.Name, t)

    let private access (value: AccessControlled.Access) : V4.Access =
        match value with
        | AccessControlled.Access.Public -> V4.Public
        | AccessControlled.Access.Private -> V4.Private

    let private ordered (entries: (Name * 'v) list) : Result<V4.OrderedMap<Name, 'v>, Failure> =
        match V4.OrderedMap.ofList entries with
        | Ok map -> Ok map
        | Error name -> Error $"The name '{Name.toKebabCase name}' is used twice."

    let private convertConstructors (constructors: Type.Constructors<JsonElement>) : Result<V4.Constructors, Failure> =
        constructors
        |> Map.toList
        |> traverse (fun (name, arguments) ->
            arguments
            |> traverse (fun (argumentName, t) -> convertType t >>= fun lowered -> Ok(argumentName, lowered))
            >>= fun lowered -> Ok(name, lowered))
        >>= ordered

    let private convertTypeDefinition (definition: Type.TypeDefinition<JsonElement>) : Result<V4.TypeDefinition, Failure> =
        match definition with
        | Type.TypeAliasDefinition(parameters, body) ->
            convertType body >>= fun lowered -> Ok(V4.TypeAliasDefinition(parameters, lowered))
        | Type.CustomTypeDefinition(parameters, constructors) ->
            convertConstructors constructors.Value
            >>= fun lowered -> Ok(V4.CustomTypeDefinition(parameters, access constructors.Access, lowered))

    // ----- literals and patterns -----

    let private convertLiteral (literal: Literal) : Result<V4.Literal, Failure> =
        match literal with
        | BoolLiteral value -> Ok(V4.BoolLiteral value)
        | CharLiteral value -> Ok(V4.CharLiteral(string value))
        | StringLiteral value -> Ok(V4.StringLiteral value)
        | WholeNumberLiteral value -> Ok(V4.IntegerLiteral(BigInteger value))
        | FloatLiteral value ->
            match V4.FloatValue.create value None with
            | Ok lowered -> Ok(V4.FloatLiteral lowered)
            | Error message -> Error $"The float literal {value.ToString(CultureInfo.InvariantCulture)}: {message}"
        | DecimalLiteral value ->
            match V4.DecimalLexeme.create (value.ToString(CultureInfo.InvariantCulture)) with
            | Ok lowered -> Ok(V4.DecimalLiteral lowered)
            | Error message -> Error message

    let rec private convertPattern (pattern: Pattern<JsonElement>) : Result<V4.Pattern, Failure> =
        match pattern with
        | WildcardPattern a -> Ok(V4.WildcardPattern(valueAttributes a))
        | AsPattern(a, inner, name) -> convertPattern inner >>= fun p -> Ok(V4.AsPattern(valueAttributes a, p, name))
        | TuplePattern(a, items) -> items |> traverse convertPattern >>= fun ps -> Ok(V4.TuplePattern(valueAttributes a, ps))
        | ConstructorPattern(a, fq, items) ->
            items |> traverse convertPattern >>= fun ps -> Ok(V4.ConstructorPattern(valueAttributes a, fq, ps))
        | EmptyListPattern a -> Ok(V4.EmptyListPattern(valueAttributes a))
        | HeadTailPattern(a, head, tail) ->
            convertPattern head >>= fun h -> convertPattern tail >>= fun t -> Ok(V4.HeadTailPattern(valueAttributes a, h, t))
        | LiteralPattern(a, literal) -> convertLiteral literal >>= fun l -> Ok(V4.LiteralPattern(valueAttributes a, l))
        | UnitPattern a -> Ok(V4.UnitPattern(valueAttributes a))

    // ----- values -----

    let rec private convertValue (value: Value<JsonElement, JsonElement>) : Result<V4.Value, Failure> =
        let list (items: Value<JsonElement, JsonElement> list) = items |> traverse convertValue

        let fields (items: Map<Name, Value<JsonElement, JsonElement>>) =
            items
            |> Map.toList
            |> traverse (fun (name, v) -> convertValue v >>= fun lowered -> Ok(name, lowered))

        match value with
        | Value.Literal(a, literal) -> convertLiteral literal >>= fun l -> Ok(V4.Value.Literal(valueAttributes a, l))
        | Value.Constructor(a, fq) -> Ok(V4.Value.Constructor(valueAttributes a, fq))
        | Value.Tuple(a, items) -> list items >>= fun l -> Ok(V4.Value.Tuple(valueAttributes a, l))
        | Value.List(a, items) -> list items >>= fun l -> Ok(V4.Value.List(valueAttributes a, l))
        | Value.Record(a, items) -> fields items >>= fun f -> Ok(V4.Value.Record(valueAttributes a, f))
        | Value.Variable(a, name) -> Ok(V4.Value.Variable(valueAttributes a, name))
        | Value.Reference(a, fq) -> Ok(V4.Value.Reference(valueAttributes a, fq))
        | Value.Field(a, target, name) -> convertValue target >>= fun t -> Ok(V4.Value.Field(valueAttributes a, t, name))
        | Value.FieldFunction(a, name) -> Ok(V4.Value.FieldFunction(valueAttributes a, name))
        | Value.Apply(a, f, argument) ->
            convertValue f >>= fun fn -> convertValue argument >>= fun arg -> Ok(V4.Value.Apply(valueAttributes a, fn, arg))
        | Value.Lambda(a, pattern, body) ->
            convertPattern pattern >>= fun p -> convertValue body >>= fun b -> Ok(V4.Value.Lambda(valueAttributes a, p, b))
        | Value.LetDefinition(a, name, definition, inValue) ->
            convertDefinition definition
            >>= fun d -> convertValue inValue >>= fun v -> Ok(V4.Value.LetDefinition(valueAttributes a, name, { Doc = None; Value = d }, v))
        | Value.LetRecursion(a, definitions, inValue) ->
            definitions
            |> Map.toList
            |> traverse (fun (name, d) -> convertDefinition d >>= fun lowered -> Ok(name, ({ Doc = None; Value = lowered }: V4.Documented<V4.ValueDefinition>)))
            >>= ordered
            >>= fun map -> convertValue inValue >>= fun v -> Ok(V4.Value.LetRecursion(valueAttributes a, map, v))
        | Value.Destructure(a, pattern, subject, inValue) ->
            convertPattern pattern
            >>= fun p -> convertValue subject >>= fun s -> convertValue inValue >>= fun v -> Ok(V4.Value.Destructure(valueAttributes a, p, s, v))
        | Value.IfThenElse(a, condition, thenBranch, elseBranch) ->
            convertValue condition
            >>= fun c -> convertValue thenBranch >>= fun t -> convertValue elseBranch >>= fun e -> Ok(V4.Value.IfThenElse(valueAttributes a, c, t, e))
        | Value.PatternMatch(a, subject, cases) ->
            convertValue subject
            >>= fun s ->
                cases
                |> traverse (fun (p, b) -> convertPattern p >>= fun lp -> convertValue b >>= fun lb -> Ok(lp, lb))
                >>= fun cs -> Ok(V4.Value.PatternMatch(valueAttributes a, s, cs))
        | Value.UpdateRecord(a, target, items) ->
            convertValue target >>= fun t -> fields items >>= fun f -> Ok(V4.Value.UpdateRecord(valueAttributes a, t, f))
        | Value.Unit a -> Ok(V4.Value.Unit(valueAttributes a))

    and private convertDefinition (definition: ValueDefinition<JsonElement, JsonElement>) : Result<V4.ValueDefinition, Failure> =
        definition.InputTypes
        |> traverse (fun (name, _, t) -> convertType t >>= fun lowered -> Ok(name, lowered))
        >>= fun inputs ->
            convertType definition.OutputType
            >>= fun output ->
                convertValue definition.Body
                >>= fun body -> Ok({ InputTypes = inputs; Body = V4.ExpressionBody(output, body) }: V4.ValueDefinition)

    // ----- modules, packages and the distribution -----

    let private convertModuleDefinition (definition: Module.ModuleDefinition<JsonElement, JsonElement>) : Result<V4.ModuleDefinition, Failure> =
        definition.Types
        |> Map.toList
        |> traverse (fun (name, accessControlled) ->
            let documented = accessControlled.Value

            let documentation, inner =
                match documented with
                | Documented.WithDocumentation(doc, value) -> Some doc, value
                | Documented.WithoutDocumentation value -> None, value

            convertTypeDefinition inner
            >>= fun d -> Ok(name, ({ Access = access accessControlled.Access; Value = { Doc = documentation; Value = d } }: V4.AccessControlled<V4.Documented<V4.TypeDefinition>>)))
        >>= ordered
        >>= fun types ->
            definition.Values
            |> Map.toList
            |> traverse (fun (name, accessControlled) ->
                let documentation, inner =
                    match accessControlled.Value with
                    | Documented.WithDocumentation(doc, value) -> Some doc, value
                    | Documented.WithoutDocumentation value -> None, value

                convertDefinition inner
                >>= fun d -> Ok(name, ({ Access = access accessControlled.Access; Value = { Doc = documentation; Value = d } }: V4.AccessControlled<V4.Documented<V4.ValueDefinition>>)))
            >>= ordered
            >>= fun values -> Ok({ Types = types; Values = values; Doc = definition.Doc }: V4.ModuleDefinition)

    let private convertTypeSpecification (specification: Type.TypeSpecification<JsonElement>) : Result<V4.TypeSpecification, Failure> =
        match specification with
        | Type.TypeAliasSpecification(parameters, body) ->
            convertType body >>= fun t -> Ok(V4.TypeAliasSpecification([], parameters, t))
        | Type.OpaqueTypeSpecification parameters -> Ok(V4.OpaqueTypeSpecification([], parameters))
        | Type.CustomTypeSpecification(parameters, constructors) ->
            convertConstructors constructors >>= fun c -> Ok(V4.CustomTypeSpecification([], parameters, c))
        | Type.DerivedTypeSpecification(parameters, details) ->
            convertType details.BaseType
            >>= fun b -> Ok(V4.DerivedTypeSpecification([], parameters, b, details.FromBaseType, details.ToBaseType))

    let private convertModuleSpecification (specification: Module.ModuleSpecification<JsonElement>) : Result<V4.ModuleSpecification, Failure> =
        specification.Types
        |> Map.toList
        |> traverse (fun (name, documented) ->
            let documentation, inner =
                match documented with
                | Documented.WithDocumentation(doc, value) -> Some doc, value
                | Documented.WithoutDocumentation value -> None, value

            convertTypeSpecification inner
            >>= fun s -> Ok(name, ({ Doc = documentation; Value = s }: V4.Documented<V4.TypeSpecification>)))
        >>= ordered
        >>= fun types ->
            specification.Values
            |> Map.toList
            |> traverse (fun (name, documented) ->
                let documentation, inner =
                    match documented with
                    | Documented.WithDocumentation(doc, value) -> Some doc, value
                    | Documented.WithoutDocumentation value -> None, value

                inner.Inputs
                |> traverse (fun (inputName, t) -> convertType t >>= fun lowered -> Ok(inputName, lowered))
                >>= fun inputs ->
                    convertType inner.Output
                    >>= fun output ->
                        Ok(name, ({ Doc = documentation; Value = ({ Annotations = []; Inputs = inputs; Output = output }: V4.ValueSpecification) }: V4.Documented<V4.ValueSpecification>)))
            >>= ordered
            >>= fun values -> Ok({ Annotations = []; Types = types; Values = values; Doc = specification.Doc }: V4.ModuleSpecification)

    let private orderedModules (entries: (ModulePath * 'v) list) : Result<V4.OrderedMap<ModulePath, 'v>, Failure> =
        match V4.OrderedMap.ofList entries with
        | Ok map -> Ok map
        | Error(ModulePath path) -> Error $"The module '{CanonicalNames.pathToString path}' is used twice."

    let private convertPackageSpecification (specification: Package.PackageSpecification<JsonElement>) : Result<V4.PackageSpecification, Failure> =
        specification.Modules
        |> Map.toList
        |> traverse (fun (path, m) -> convertModuleSpecification m >>= fun lowered -> Ok(path, lowered))
        >>= orderedModules
        >>= fun modules -> Ok({ Modules = modules }: V4.PackageSpecification)

    let private convertPackageDefinition (definition: Package.PackageDefinition<JsonElement, JsonElement>) : Result<V4.PackageDefinition, Failure> =
        definition.Modules
        |> Map.toList
        |> traverse (fun (path, accessControlled) ->
            convertModuleDefinition accessControlled.Value
            >>= fun m -> Ok(path, ({ Access = access accessControlled.Access; Value = m }: V4.AccessControlled<V4.ModuleDefinition>)))
        >>= orderedModules
        >>= fun modules -> Ok({ Modules = modules }: V4.PackageDefinition)

    let private dependencies (map: Map<PackageName, Package.PackageSpecification<JsonElement>>) : Result<V4.OrderedMap<PackageName, V4.PackageSpecification>, Failure> =
        map
        |> Map.toList
        |> traverse (fun (package, specification) -> convertPackageSpecification specification >>= fun s -> Ok(package, s))
        >>= fun entries ->
            match V4.OrderedMap.ofList entries with
            | Ok converted -> Ok converted
            | Error(PackageName path) -> Error $"The package '{CanonicalNames.pathToString path}' is used twice."

    /// <summary>Converts a classic distribution to the current IR.</summary>
    let convert (distribution: Distribution.Distribution<JsonElement, JsonElement>) : Result<V4.Distribution, string> =
        match distribution with
        | Distribution.Library(package, deps, definition) ->
            dependencies deps
            >>= fun d -> convertPackageDefinition definition >>= fun p -> Ok(V4.Library(package, d, p))
        | Distribution.Specs(package, deps, specification) ->
            dependencies deps
            >>= fun d -> convertPackageSpecification specification >>= fun s -> Ok(V4.Specs(package, d, s))

    /// <summary>The upgrade step from the classic IR to the current IR.</summary>
    [<CompiledName("Step")>]
    let step: IIrUpgrade =
        { new IIrUpgrade with
            member _.Id = id
            member _.From = Versioning.Classic 3
            member _.To = target

            member _.Accepts(version) =
                match version with
                | Versioning.Classic major -> major >= 1 && major <= 3
                | Versioning.SemVer semantic -> semantic.Major = 3

            member _.Upgrade(input) =
                match input.Model.Distribution with
                | DistributionModel.V4 _ ->
                    Checked.fail [
                        Diagnostic.error UpgradeCodes.unrepresentable "The classic to current step reads a classic model, and this model is already a current model."
                    ]
                | DistributionModel.Classic classic ->
                    match convert classic with
                    | Error message ->
                        Checked.fail [
                            Diagnostic.error UpgradeCodes.unrepresentable $"The model has a node that the current IR cannot hold. {message}"
                        ]
                    | Ok converted ->
                        match Snapshot.ofDistribution converted with
                        | Error message ->
                            Checked.fail [
                                Diagnostic.error UpgradeCodes.unrepresentable $"The upgraded model cannot be written: {message}"
                            ]
                        | Ok snapshot ->
                            let model: VersionedModel =
                                {
                                    SourceVersion = SourceFormatVersion.Create(Versioning.version target)
                                    Distribution = DistributionModel.V4 converted
                                }

                            // The conversion keeps every node at its path, so it moves no node and has nothing to record.
                            Checked.pass { Model = model; Snapshot = snapshot; Applications = [] } }
