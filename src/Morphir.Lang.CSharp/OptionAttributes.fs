// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open Microsoft.CodeAnalysis
open Morphir.IR
open Morphir.IR.V4

/// <summary>
/// The attribute carrier of the handling options (OPT-1): the attributes of <c>Morphir.Lang.CSharp.Attributes</c> on a
/// member, a type and the assembly. The frontend reads them from the compilation, so a project needs no configuration file
/// for them. A frontend option (loop lowering) is read where the frontend lowers. A backend option goes into the model as the
/// carrier of task 6.1, so <c>morphir generate</c> on the model file applies it.
/// </summary>
module internal OptionAttributes =
    /// <summary>Whether an attribute is the attribute of the package with the name, in the namespace <c>Morphir</c>.</summary>
    let private isMorphir (name: string) (attribute: AttributeData) =
        match attribute.AttributeClass with
        | null -> false
        | attributeClass ->
            attributeClass.Name = name
            && not (isNull attributeClass.ContainingNamespace)
            && attributeClass.ContainingNamespace.Name = "Morphir"
            && attributeClass.ContainingNamespace.ContainingNamespace.IsGlobalNamespace

    let private find (name: string) (symbol: ISymbol) =
        symbol.GetAttributes() |> Seq.tryFind (isMorphir name)

    /// <summary>The value of the one enum argument of the constructor.</summary>
    let private argument (attribute: AttributeData) =
        if attribute.ConstructorArguments.Length = 1 then
            match attribute.ConstructorArguments[0].Value with
            | :? int as value -> Some value
            | _ -> None
        else
            None

    /// <summary>The value of a named enum argument, when the attribute sets it.</summary>
    let private named (key: string) (attribute: AttributeData) =
        attribute.NamedArguments
        |> Seq.tryFind (fun pair -> pair.Key = key)
        |> Option.bind (fun pair ->
            match pair.Value.Value with
            | :? int as value -> Some value
            | _ -> None)

    let private pick (values: 'a list) (index: int) = List.tryItem index values

    /// <summary>The options that the attributes of one symbol set. The symbols around it are not read.</summary>
    let ofSymbol (symbol: ISymbol) : HandlingOptions =
        let equality =
            find "MorphirEqualityAttribute" symbol
            |> Option.bind argument
            |> Option.bind (pick [ FloatEquality.Clr; FloatEquality.Ieee ])

        let emit = find "MorphirEmitAttribute" symbol

        let traversal =
            emit
            |> Option.bind (named "Traversal")
            |> Option.bind (pick [ TraversalForm.Loop; TraversalForm.Functional ])

        let tailRecursion =
            emit
            |> Option.bind (named "TailRecursion")
            |> Option.bind (pick [ TailRecursionForm.Loop; TailRecursionForm.LocalFunction ])

        let loop =
            find "MorphirLoopAttribute" symbol
            |> Option.bind argument
            |> Option.bind (pick [ LoopLowering.Traversal; LoopLowering.Recursion ])

        {
            HandlingOptions.empty with
                Traversal = traversal
                TailRecursion = tailRecursion
                FloatEquality = equality
                LoopLowering = loop
        }

    /// <summary>The types that hold a symbol, nearest first.</summary>
    let rec private containers (symbol: ISymbol) : ISymbol list =
        match symbol.ContainingType with
        | null -> []
        | container -> (container :> ISymbol) :: containers container

    /// <summary>
    /// The scopes of a type, nearest first: the type, its closed base record (for a case record), the types that hold it.
    /// </summary>
    let rec private typeScopes (t: INamedTypeSymbol) : ISymbol list =
        let next =
            match t.BaseType with
            | null -> t.ContainingType
            | baseType when baseType.IsClosed -> baseType
            | _ -> t.ContainingType

        match next with
        | null -> [ t ]
        | parent -> (t :> ISymbol) :: typeScopes parent

    let private merged (scopes: ISymbol list) (assembly: IAssemblySymbol) =
        HandlingOptions.merge ((scopes |> List.map ofSymbol) @ [ ofSymbol assembly ])

    /// <summary>
    /// The options of a member, such as a method: the attributes of the member, of the types that hold it, nearest first,
    /// and of the assembly. The nearest attribute that sets an option wins. The loop lowering of a method comes from here.
    /// </summary>
    let ofMember (symbol: ISymbol) : HandlingOptions =
        merged (symbol :: containers symbol) symbol.ContainingAssembly

    /// <summary>The options of a type: the type, its closed base record, the types that hold it, and the assembly.</summary>
    let ofType (t: INamedTypeSymbol) : HandlingOptions = merged (typeScopes t) t.ContainingAssembly

    /// <summary>
    /// The options of a field of a type: the property and the positional parameter with the name of the field, then the
    /// options of the type.
    /// </summary>
    let ofField (owner: INamedTypeSymbol) (memberName: string) : HandlingOptions =
        let properties = owner.GetMembers memberName |> Seq.map ofSymbol |> List.ofSeq

        let parameters =
            owner.InstanceConstructors
            |> Seq.collect (fun constructor -> constructor.Parameters)
            |> Seq.filter (fun parameter -> parameter.Name = memberName)
            |> Seq.map ofSymbol
            |> List.ofSeq

        HandlingOptions.merge (properties @ parameters @ [ ofType owner ])

    /// <summary>The backend options of a value, which go into the model.</summary>
    let private backendOfValue (options: HandlingOptions) = {
        HandlingOptions.empty with
            Traversal = options.Traversal
            TailRecursion = options.TailRecursion
    }

    /// <summary>The carrier of the float equality of one field.</summary>
    let private fieldCarrier (owner: INamedTypeSymbol) (memberName: string) (t: Type) =
        HandlingCarrier.write { HandlingOptions.empty with FloatEquality = (ofField owner memberName).FloatEquality } t

    let private remap (map: OrderedMap<'k, 'v>) (f: 'k -> 'v -> 'v) : OrderedMap<'k, 'v> =
        match OrderedMap.ofList (OrderedMap.toList map |> List.map (fun (k, v) -> k, f k v)) with
        | Ok mapped -> mapped
        | Error _ -> map

    let private withOutput (write: Type -> Type) (body: ValueBody) =
        match body with
        | ExpressionBody(output, expression) -> ExpressionBody(write output, expression)
        | NativeBody(output, info) -> NativeBody(write output, info)
        | ExternalBody(output, info) -> ExternalBody(write output, info)
        | IncompleteBody _ -> body

    /// <summary>The field carriers of a type definition, from the planned type that it comes from.</summary>
    let private typeWithCarriers (planned: PlannedType) (definition: TypeDefinition) =
        let fieldsOf (owner: INamedTypeSymbol) (fields: (Name * string * ITypeSymbol) list) (items: (Name * Type) list) =
            items
            |> List.map (fun (name, t) ->
                match fields |> List.tryFind (fun (fieldName, _, _) -> fieldName = name) with
                | Some(_, memberName, _) -> name, fieldCarrier owner memberName t
                | None -> name, t)

        match planned.Kind, definition with
        | RecordAlias(fields, _), TypeAliasDefinition(parameters, Type.Record(attributes, items)) ->
            TypeAliasDefinition(parameters, Type.Record(attributes, fieldsOf planned.Symbol fields items))
        | CustomType cases, CustomTypeDefinition(parameters, access, constructors) ->
            let constructors =
                remap constructors (fun caseName items ->
                    match cases |> List.tryFind (fun case' -> case'.Name = caseName) with
                    | Some({ Symbol = (:? INamedTypeSymbol as owner) } as case') -> fieldsOf owner case'.Fields items
                    | _ -> items)

            CustomTypeDefinition(parameters, access, constructors)
        | _ -> definition

    /// <summary>
    /// Writes the backend options of the attributes into the model as the carrier of task 6.1: the traversal and tail
    /// recursion forms on the output type of each value, and the float equality on the type of each field. A declaration
    /// that no attribute reaches gets no carrier.
    /// </summary>
    let apply (plan: Plan) (distribution: Distribution) : Distribution =
        match distribution with
        | Library(pkg, dependencies, definition) ->
            let byModule = plan.Modules |> List.map (fun planned -> planned.Path, planned) |> Map.ofList

            let modules =
                remap definition.Modules (fun path access ->
                    match Map.tryFind path byModule with
                    | None -> access
                    | Some planned ->
                        let values =
                            remap access.Value.Values (fun name value ->
                                match planned.Values |> List.tryFind (fun v -> v.Name = name) with
                                | None -> value
                                | Some plannedValue ->
                                    let options = backendOfValue (ofMember plannedValue.Symbol)
                                    let definition = value.Value.Value
                                    let carried = { definition with Body = withOutput (HandlingCarrier.write options) definition.Body }
                                    { value with Value = { value.Value with Value = carried } })

                        let types =
                            remap access.Value.Types (fun name typeDefinition ->
                                match planned.Types |> List.tryFind (fun t -> t.Name = name) with
                                | None -> typeDefinition
                                | Some plannedType ->
                                    let carried = typeWithCarriers plannedType typeDefinition.Value.Value
                                    { typeDefinition with Value = { typeDefinition.Value with Value = carried } })

                        { access with Value = { access.Value with Values = values; Types = types } })

            Library(pkg, dependencies, { definition with Modules = modules })
        | other -> other

/// <summary>
/// The configuration carrier of the frontend options (OPT-1): the extra options of the compile request, which
/// <c>morphir compile</c> fills from the <c>[frontend.csharp]</c> keys of <c>morphir.toml</c> with <c>-</c> for <c>_</c>
/// (<c>loop_lowering</c> is <c>loop-lowering</c>). The C# frontend reads them below the attributes: an attribute on the
/// member, a type or the assembly wins over a key.
/// </summary>
module internal ConfiguredOptions =
    /// <summary>The options of the C# frontend that a key sets. A backend option is a key of <c>[codegen.csharp]</c>.</summary>
    let keys = [ HandlingOptions.LoopLoweringKey ]

    let private accepted (key: string) =
        HandlingOptions.words
        |> List.tryFind (fun (k, _) -> k = key)
        |> Option.map (fun (_, words) -> String.concat ", " words)
        |> Option.defaultValue ""

    /// <summary>
    /// Parses the extra options into the option record. A name that is not an option of the C# frontend, or a value that
    /// is not a word of its option, gives an error message.
    /// </summary>
    let parse (extra: System.Collections.Generic.IReadOnlyDictionary<string, System.Text.Json.JsonElement>) : Result<HandlingOptions, string> =
        extra
        |> Seq.fold
            (fun state pair ->
                state
                |> Result.bind (fun options ->
                    if not (List.contains pair.Key keys) then
                        Error $"'{pair.Key}' is not an option of the C# frontend. The C# frontend accepts {HandlingOptions.describe keys}."
                    else
                        match pair.Value.ValueKind with
                        | System.Text.Json.JsonValueKind.String ->
                            match pair.Value.GetString() with
                            | null -> Error $"The option '{pair.Key}' accepts {accepted pair.Key}; received null."
                            | word -> HandlingOptions.trySet pair.Key word options
                        | _ -> Error $"The option '{pair.Key}' accepts {accepted pair.Key}; received {pair.Value.GetRawText()}."))
            (Ok HandlingOptions.empty)
