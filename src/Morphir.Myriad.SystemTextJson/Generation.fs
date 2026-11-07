// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

/// The pure generation step: settings and source text in, F# source text or errors out.
module Generation =
    let private hasAttribute (name: string) (attributes: AttributeUse list) =
        attributes |> List.exists (fun a -> a.Name = name)

    /// The names in JsonName or JsonPropertyName on a field: the first is written, all are read.
    let private jsonNames (attributes: AttributeUse list) =
        attributes
        |> List.filter (fun a -> a.Name = "JsonName" || a.Name = "JsonPropertyName")
        |> List.collect (fun a ->
            a.Positional
            |> List.choose (function
                | AttributeValue.Text text -> Some text
                | _ -> None))

    /// The last part of a dotted name, such as CamelCase in JsonNaming.CamelCase.
    let private lastName (value: AttributeValue) =
        match value with
        | AttributeValue.Names [ name ] -> Some(name.Split('.') |> Array.last)
        | _ -> None

    /// The record rules after the JsonRecord attribute of one record, or the errors in its arguments.
    let private recordRules (defaults: RecordRules) (attributes: AttributeUse list) : Result<RecordRules, (Location * string) list> =
        match attributes |> List.tryFind (fun a -> a.Name = "JsonRecord") with
        | None -> Ok defaults
        | Some attribute ->
            let step (rules: Result<RecordRules, (Location * string) list>) (name: string, value: AttributeValue) =
                let invalid reason = Error [ attribute.Location, $"the JsonRecord argument {name} {reason}" ]

                let next =
                    match name, value with
                    | "Naming", value ->
                        match lastName value |> Option.map Naming.Parse with
                        | Some(Ok naming) -> Ok(fun (r: RecordRules) -> { r with Naming = naming })
                        | _ -> invalid "must be a JsonNaming value."
                    | "SkippableOptionFields", value ->
                        match lastName value |> Option.map ContextSettings.parseSkippable with
                        | Some(Ok skip) -> Ok(fun r -> { r with SkipAbsentOptions = skip })
                        | _ -> invalid "must be a JsonSkippableOptionFields value."
                    | "UnknownMembers", value ->
                        match lastName value |> Option.map ContextSettings.parseUnknownMembers with
                        | Some(Ok refuse) -> Ok(fun r -> { r with RefuseUnknownMembers = refuse })
                        | _ -> invalid "must be a JsonUnknownMembers value."
                    | "AllowNullFields", AttributeValue.Bool allow -> Ok(fun r -> { r with AllowNullFields = allow })
                    | "AllowNullFields", _ -> invalid "must be true or false."
                    | _ -> invalid "is not known."

                match rules, next with
                | Ok rules, Ok change -> Ok(change rules)
                | Error errors, Error more -> Error(errors @ more)
                | Error errors, Ok _ -> Error errors
                | Ok _, Error more -> Error more

            attribute.Named |> List.fold step (Ok defaults)

    /// True when the field type accepts null, so AllowNullFields can apply to it.
    let private acceptsNull (t: TypeRef) =
        match t with
        | TypeRef.Primitive Primitive.String
        | TypeRef.Sequence(SequenceKind.Array, _)
        | TypeRef.Sequence(SequenceKind.ResizeArray, _)
        | TypeRef.Sequence(SequenceKind.ReadOnlyList, _)
        | TypeRef.Sequence(SequenceKind.ReadOnlyCollection, _)
        | TypeRef.Map(MapKind.ReadOnlyDictionary, _, _)
        | TypeRef.External _ -> true
        | _ -> false

    /// True when a JsonNumberHandling attribute on the field allows reading numbers from strings.
    let private readsNumbersFromStrings (attributes: AttributeUse list) =
        attributes
        |> List.filter (fun a -> a.Name = "JsonNumberHandling")
        |> List.exists (fun a ->
            a.Positional
            |> List.exists (function
                | AttributeValue.Names names -> names |> List.exists (fun n -> n.EndsWith "AllowReadingFromString")
                | _ -> false))

    /// True for a number, or an option of a number: the shapes where a member can read numbers from strings.
    let private isNumberField (t: TypeRef) =
        match t with
        | TypeRef.Primitive p
        | TypeRef.Option(TypeRef.Primitive p)
        | TypeRef.ValueOption(TypeRef.Primitive p) -> p.NumberTypeName.IsSome
        | _ -> false

    /// The union rules after the JsonUnion attribute of one union, or the errors in its arguments.
    let private unionRules (defaults: UnionRules) (attributes: AttributeUse list) : Result<UnionRules, (Location * string) list> =
        match attributes |> List.tryFind (fun a -> a.Name = "JsonUnion") with
        | None -> Ok defaults
        | Some attribute ->
            let step (rules: Result<UnionRules, (Location * string) list>) (name: string, value: AttributeValue) =
                let invalid reason = Error [ attribute.Location, $"the JsonUnion argument {name} {reason}" ]

                let next =
                    match name, value with
                    | "BaseEncoding", AttributeValue.Names names ->
                        match UnionEncoding.ParseNames(names |> List.map (fun n -> n.Split('.') |> Array.last)) with
                        | Ok encoding -> Ok(fun (r: UnionRules) -> { r with Encoding = encoding })
                        | Error reason -> invalid reason
                    | "BaseEncoding", _ -> invalid "must be JsonUnionEncoding values joined by |||."
                    | "TagName", AttributeValue.Text text when text.Length > 0 -> Ok(fun r -> { r with TagName = text })
                    | "FieldsName", AttributeValue.Text text when text.Length > 0 -> Ok(fun r -> { r with FieldsName = text })
                    | ("TagName" | "FieldsName"), _ -> invalid "must be a text that is not empty."
                    | "TagNaming", value ->
                        match lastName value |> Option.map Naming.Parse with
                        | Some(Ok naming) -> Ok(fun r -> { r with TagNaming = naming })
                        | _ -> invalid "must be a JsonNaming value."
                    | "FieldNaming", value ->
                        match lastName value |> Option.map Naming.Parse with
                        | Some(Ok naming) -> Ok(fun r -> { r with FieldNaming = Some naming })
                        | _ -> invalid "must be a JsonNaming value."
                    | "UnknownMembers", value ->
                        match lastName value |> Option.map ContextSettings.parseUnknownMembers with
                        | Some(Ok refuse) -> Ok(fun r -> { r with RefuseUnknownMembers = refuse })
                        | _ -> invalid "must be a JsonUnknownMembers value."
                    | "TagCaseInsensitive", AttributeValue.Bool value -> Ok(fun r -> { r with TagCaseInsensitive = value })
                    | "TagCaseInsensitive", _ -> invalid "must be true or false."
                    | _ -> invalid "is not known."

                match rules, next with
                | Ok rules, Ok change -> Ok(change rules)
                | Error errors, Error more -> Error(errors @ more)
                | Error errors, Ok _ -> Error errors
                | Ok _, Error more -> Error more

            attribute.Named |> List.fold step (Ok defaults)

    /// The JSON token kind of a value of the type, for SelectByTokenKind.
    let private tokenKind (format: MapFormat) (plan: Map<string, TypeShape>) (t: TypeRef) : TokenKind option =
        match t with
        | TypeRef.Primitive Primitive.Bool -> Some TokenKind.Boolean
        | TypeRef.Primitive Primitive.JsonElement -> None
        | TypeRef.Primitive p when p.NumberTypeName.IsSome -> Some TokenKind.Number
        | TypeRef.Primitive _
        | TypeRef.Parsed _ -> Some TokenKind.String
        | TypeRef.Sequence _
        | TypeRef.Tuple _ -> Some TokenKind.Array
        | TypeRef.Map(_, key, _) -> Some(if Emit.mapAsObject format key then TokenKind.Object else TokenKind.Array)
        | TypeRef.Local declaration ->
            match plan.TryFind declaration.Name with
            | Some(TypeShape.Record _) -> Some TokenKind.Object
            | Some(TypeShape.Enum _) -> Some TokenKind.Number
            | _ -> None
        | _ -> None

    /// The JsonParsedValue settings of a type, when it has the attribute.
    let private parsedSpec (attributes: AttributeUse list) : ParsedSpec option =
        attributes
        |> List.tryFind (fun a -> a.Name = "JsonParsedValue")
        |> Option.map (fun a ->
            let text name fallback =
                a.Named
                |> List.tryPick (function
                    | n, AttributeValue.Text t when n = name -> Some t
                    | _ -> None)
                |> Option.defaultValue fallback

            let flag name =
                a.Named
                |> List.exists (function
                    | n, AttributeValue.Bool true when n = name -> true
                    | _ -> false)

            { ParseMember = text "ParseMember" "Parse"; UsesTryParse = flag "UsesTryParse"; TextMember = text "TextMember" "ToString" })

    /// The converter type in a System.Text.Json JsonConverter attribute, when there is one.
    let private converterOf (attributes: AttributeUse list) : string option =
        attributes
        |> List.tryFind (fun a -> a.Name = "JsonConverter")
        |> Option.bind (fun a ->
            match a.Positional with
            | [ AttributeValue.TypeOf name ] -> Some name
            | _ -> None)

    /// Names that the context type already uses.
    let private reservedMembers =
        set [ "Default"; "Options"; "GetTypeInfo"; "Verify"; "GeneratedSerializerOptions"; "Equals"; "GetHashCode"; "ToString"; "GetType" ]

    let internal plan (settings: ContextSettings) (source: SourceFile) : Result<Plan, GenerationError list> =
        let errors = ResizeArray<GenerationError>()

        let unsupported (declaration: TypeDeclaration) (location: Location) (memberName: string option) (reason: string) =
            errors.Add(GenerationError.Unsupported(source.File, location, declaration.Name, memberName, reason))

        let included =
            source.Types
            |> List.filter (fun declaration ->
                not declaration.IsPrivate
                && not (hasAttribute "JsonIgnoreType" declaration.Attributes)
                && (match declaration.Shape with
                    | TypeShape.Other -> false
                    | _ -> true))

        // Parsed values and user converters, from attributes on local types and on abbreviations of outside types.
        let parsedLocal =
            included
            |> List.choose (fun d ->
                match d.Shape, parsedSpec d.Attributes with
                | TypeShape.Abbreviation _, _ -> None
                | _, Some spec -> Some(d.Name, (d.QualifiedName, spec))
                | _ -> None)
            |> Map.ofList

        let parsedOutside =
            included
            |> List.choose (fun d ->
                match d.Shape, parsedSpec d.Attributes with
                | TypeShape.Abbreviation target, Some spec -> Some(Resolve.describe target, (d.QualifiedName, spec))
                | _ -> None)

        let customLocal =
            included
            |> List.choose (fun d -> converterOf d.Attributes |> Option.map (fun c -> d.Name, (d.QualifiedName, c)))
            |> Map.ofList

        let rec rewrite (t: TypeRef) : TypeRef =
            match t with
            | TypeRef.Local d when parsedLocal.ContainsKey d.Name -> TypeRef.Parsed parsedLocal[d.Name]
            | TypeRef.Local d when customLocal.ContainsKey d.Name -> TypeRef.Custom customLocal[d.Name]
            | TypeRef.External name ->
                parsedOutside
                |> List.tryFind (fun (target, _) -> target = name || target.EndsWith("." + name) || name.EndsWith("." + target))
                |> Option.map (fun (_, parsed) -> TypeRef.Parsed parsed)
                |> Option.defaultValue t
            | TypeRef.Option inner -> TypeRef.Option(rewrite inner)
            | TypeRef.ValueOption inner -> TypeRef.ValueOption(rewrite inner)
            | TypeRef.Sequence(kind, inner) -> TypeRef.Sequence(kind, rewrite inner)
            | TypeRef.Map(kind, key, value) -> TypeRef.Map(kind, rewrite key, rewrite value)
            | TypeRef.Tuple items -> TypeRef.Tuple(items |> List.map rewrite)
            | _ -> t

        /// A field type with a JsonConverter attribute on the field uses that converter for the whole field type.
        let withFieldConverter (attributes: AttributeUse list) (t: TypeRef) =
            match converterOf attributes with
            | Some converter -> TypeRef.Custom(t.FSharpName, converter)
            | None -> t

        let resolve (declaration: TypeDeclaration) (location: Location) (memberName: string option) (syntax: TypeSyntax) =
            match Resolve.resolve included [] syntax |> Result.map rewrite with
            | Ok resolved -> Some resolved
            | Error reason ->
                unsupported declaration location memberName reason
                None

        let planned =
            included
            |> List.choose (fun declaration ->
                if Resolve.reservedNames.Contains declaration.Name then
                    unsupported declaration declaration.Location None $"'{declaration.Name}' is the name of a built-in type that the generator reads by its name; rename the type."
                    None
                elif not declaration.Parameters.IsEmpty then
                    unsupported declaration declaration.Location None "generic types are not supported; declare an abbreviation of a closed type instead."
                    None
                elif (parsedSpec declaration.Attributes).IsSome then
                    let spec = (parsedSpec declaration.Attributes).Value
                    Some { Declaration = declaration; Shape = PlannedShape.Abbreviation(TypeRef.Parsed(declaration.QualifiedName, spec)) }
                elif (converterOf declaration.Attributes).IsSome then
                    let converter = (converterOf declaration.Attributes).Value
                    Some { Declaration = declaration; Shape = PlannedShape.Abbreviation(TypeRef.Custom(declaration.QualifiedName, converter)) }
                else
                    match declaration.Shape with
                    | TypeShape.Record(_, true) ->
                        unsupported declaration declaration.Location None "the record representation is private, so generated code cannot build it."
                        None
                    | TypeShape.Record(fields, false) ->
                        let rules =
                            match recordRules settings.Records declaration.Attributes with
                            | Ok rules -> rules
                            | Error problems ->
                                for location, reason in problems do
                                    unsupported declaration location None reason

                                settings.Records

                        let resolvedFields =
                            fields
                            |> List.choose (fun field ->
                                let name = defaultArg field.Name ""

                                let hasJsonName = hasAttribute "JsonName" field.Attributes
                                let hasPropertyName = hasAttribute "JsonPropertyName" field.Attributes

                                if hasJsonName && hasPropertyName then
                                    unsupported declaration field.Location field.Name "JsonName and JsonPropertyName both set the member name; use one of them."

                                resolve declaration field.Location field.Name field.Type
                                |> Option.filter (fun _ -> not (hasJsonName && hasPropertyName))
                                |> Option.bind (fun resolved ->
                                    let lenient = readsNumbersFromStrings field.Attributes

                                    if lenient && not (isNumberField resolved) then
                                        unsupported declaration field.Location field.Name "JsonNumberHandling applies only to a number field or an option of a number; set AllowNumbersFromStrings on the context for collections."
                                        None
                                    else
                                        Some(withFieldConverter field.Attributes resolved, lenient))
                                |> Option.map (fun (resolved, lenient) ->
                                    let names = jsonNames field.Attributes
                                    let written = List.tryHead names |> Option.defaultValue (rules.Naming.Apply name)

                                    {
                                        Name = name
                                        JsonName = written
                                        ReadNames = (if names.IsEmpty then [ written ] else names)
                                        Type = resolved
                                        Skippable = rules.SkipAbsentOptions
                                        AllowNull = rules.AllowNullFields && acceptsNull resolved
                                        NumbersFromStrings = lenient
                                    }))

                        if resolvedFields.Length = fields.Length then
                            Some { Declaration = declaration; Shape = PlannedShape.Record(resolvedFields, rules.RefuseUnknownMembers) }
                        else
                            None
                    | TypeShape.Union(_, true) ->
                        unsupported declaration declaration.Location None "the union cases are private, so generated code cannot build them."
                        None
                    | TypeShape.Union(cases, false) ->
                        let rules =
                            match unionRules settings.Unions declaration.Attributes with
                            | Ok rules -> rules
                            | Error problems ->
                                for location, reason in problems do
                                    unsupported declaration location None reason

                                settings.Unions

                        let fieldNaming = defaultArg rules.FieldNaming settings.Records.Naming
                        let e = rules.Encoding

                        let planCase (case: CaseSyntax) =
                            let tags = jsonNames (case.Attributes |> List.filter (fun a -> a.Named |> List.forall (fun (n, _) -> n <> "Field")))
                            let tag = List.tryHead tags |> Option.defaultValue (rules.TagNaming.Apply case.Name)

                            // JsonName(..., Field = "x") on the case names the field x.
                            let fieldNames =
                                case.Attributes
                                |> List.filter (fun a -> a.Name = "JsonName")
                                |> List.choose (fun a ->
                                    match a.Named |> List.tryFind (fun (n, _) -> n = "Field") with
                                    | Some(_, AttributeValue.Text field) ->
                                        Some(field, a.Positional |> List.choose (function AttributeValue.Text t -> Some t | _ -> None))
                                    | _ -> None)
                                |> Map.ofList

                            let fields =
                                case.Fields
                                |> List.mapi (fun index field ->
                                    let memberName = $"{case.Name}.{defaultArg field.Name (string index)}"

                                    match Resolve.resolve included [] field.Type |> Result.map (rewrite >> withFieldConverter field.Attributes) with
                                    | Error reason ->
                                        unsupported declaration field.Location (Some memberName) reason
                                        None
                                    | Ok resolved ->
                                        let fsharpName =
                                            match field.Name with
                                            | Some name -> name
                                            | None when case.Fields.Length = 1 -> "Item"
                                            | None -> $"Item{index + 1}"

                                        let baseName =
                                            match field.Name with
                                            | None when e.FieldNamesFromTypes ->
                                                match Resolve.clrName resolved with
                                                | Some name -> name
                                                | None ->
                                                    unsupported declaration field.Location (Some memberName) "UnionFieldNamesFromTypes needs a field type with a name."
                                                    fsharpName
                                            | _ -> fsharpName

                                        let names =
                                            match fieldNames.TryFind fsharpName with
                                            | Some names when not names.IsEmpty -> names
                                            | _ -> jsonNames field.Attributes

                                        let written = List.tryHead names |> Option.defaultValue (fieldNaming.Apply baseName)

                                        Some
                                            {
                                                Name = fsharpName
                                                JsonName = written
                                                ReadNames = (if names.IsEmpty then [ written ] else names)
                                                Type = resolved
                                                Skippable = settings.Records.SkipAbsentOptions
                                                AllowNull = false
                                                NumbersFromStrings = readsNumbersFromStrings field.Attributes
                                            })

                            if fields |> List.forall Option.isSome then
                                let fields = fields |> List.choose id

                                for group in fields |> List.groupBy (fun f -> f.JsonName) do
                                    if (snd group).Length > 1 then
                                        unsupported declaration case.Location (Some case.Name) $"more than one field has the JSON name '{fst group}'."

                                Some { Name = case.Name; Tag = tag; ReadTags = (if tags.IsEmpty then [ tag ] else tags); Fields = fields }
                            else
                                None

                        let planned = cases |> List.map planCase

                        if planned |> List.forall Option.isSome then
                            let cases = planned |> List.choose id

                            for group in cases |> List.collect (fun c -> c.ReadTags |> List.map (fun t -> t, c)) |> List.groupBy fst do
                                if (snd group).Length > 1 then
                                    unsupported declaration declaration.Location None $"more than one case has the tag '{fst group}'."

                            let shapes = included |> List.map (fun d -> d.Name, d.Shape) |> Map.ofList

                            let kinds =
                                if e.SelectByTokenKind then
                                    cases
                                    |> List.map (fun case ->
                                        match case.Fields with
                                        | [ field ] ->
                                            match tokenKind settings.MapFormat shapes field.Type with
                                            | Some kind -> Some kind
                                            | None ->
                                                unsupported declaration declaration.Location (Some case.Name) $"SelectByTokenKind needs a field whose JSON token kind is known at build time; {field.Type.FSharpName} has none."
                                                None
                                        | _ ->
                                            unsupported declaration declaration.Location (Some case.Name) "SelectByTokenKind needs exactly one field in each case."
                                            None)
                                    |> List.choose id
                                else
                                    []

                            if e.SelectByTokenKind then
                                for group in kinds |> List.countBy id do
                                    if snd group > 1 then
                                        unsupported declaration declaration.Location None $"more than one case has the JSON token kind {fst group}, so SelectByTokenKind cannot choose between them."

                            let singleCaseUnwrap = e.UnwrapSingleCaseUnions && cases.Length = 1 && cases.Head.Fields.Length = 1

                            if e.Untagged && not e.SelectByTokenKind && not singleCaseUnwrap then
                                let fieldless = cases |> List.filter (fun c -> c.Fields.IsEmpty)

                                if fieldless.Length > 1 && not e.UnwrapFieldlessTags then
                                    unsupported declaration declaration.Location None $"""the cases {fieldless |> List.map (fun c -> c.Name) |> String.concat " and "} have no field, so an untagged reader cannot tell them apart; add UnwrapFieldlessTags."""

                                let memberOwners =
                                    cases
                                    |> List.collect (fun c -> c.Fields |> List.collect (fun f -> f.ReadNames) |> List.distinct |> List.map (fun n -> n, c.Name))
                                    |> List.groupBy fst

                                for name, owners in memberOwners do
                                    if owners.Length > 1 then
                                        unsupported declaration declaration.Location None $"""the cases {owners |> List.map snd |> String.concat " and "} both have the member '{name}', so an untagged reader cannot tell them apart."""

                            Some { Declaration = declaration; Shape = PlannedShape.Union(cases, rules, kinds) }
                        else
                            None
                    | TypeShape.Enum cases -> Some { Declaration = declaration; Shape = PlannedShape.Enum cases }
                    | TypeShape.Abbreviation target ->
                        resolve declaration declaration.Location None (TypeSyntax.Named([ declaration.Name ], []))
                        |> Option.map (fun _ ->
                            match Resolve.resolve included [ declaration.Name ] target |> Result.map rewrite with
                            | Ok resolved -> { Declaration = declaration; Shape = PlannedShape.Abbreviation resolved }
                            | Error _ -> { Declaration = declaration; Shape = PlannedShape.Abbreviation(TypeRef.External declaration.QualifiedName) })
                    | TypeShape.Other -> None)

        // The Object map format needs keys that are JSON member names.
        let rec maps (t: TypeRef) : TypeRef list =
            match t with
            | TypeRef.Map(_, key, value) -> t :: (maps key @ maps value)
            | TypeRef.Option inner
            | TypeRef.ValueOption inner
            | TypeRef.Sequence(_, inner) -> maps inner
            | TypeRef.Tuple items -> items |> List.collect maps
            | _ -> []

        if settings.MapFormat = MapFormat.Object then
            for m in planned do
                let types =
                    match m.Shape with
                    | PlannedShape.Record(fields, _) -> fields |> List.map (fun f -> f.Type)
                    | PlannedShape.Abbreviation target -> [ target ]
                    | PlannedShape.Union(cases, _, _) -> cases |> List.collect (fun c -> c.Fields |> List.map (fun f -> f.Type))
                    | PlannedShape.Enum _ -> []

                for map in types |> List.collect maps do
                    match map with
                    | TypeRef.Map(_, key, _) when not (Emit.mapAsObject MapFormat.Object key) ->
                        unsupported m.Declaration m.Declaration.Location None $"the Object map format needs a string key, but {map.FSharpName} has the key {key.FSharpName}; use ObjectOrArrayOfPairs or ArrayOfPairs."
                    | _ -> ()

        for declaration in planned |> List.map (fun m -> m.Declaration) do
            if reservedMembers.Contains declaration.Name then
                unsupported declaration declaration.Location None $"the context type already has a member named '{declaration.Name}'; rename the type or leave it out with JsonIgnoreType."

        for group in planned |> List.groupBy (fun m -> m.Declaration.Name) do
            match snd group with
            | _ :: second :: _ ->
                unsupported second.Declaration second.Declaration.Location None $"more than one type of the context is named '{fst group}'; the context needs one member for each name."
            | _ -> ()

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok
                {
                    Settings = settings
                    Source = source
                    Members = planned
                    Records =
                        planned
                        |> List.choose (fun m ->
                            match m.Shape with
                            | PlannedShape.Record(fields, refuse) -> Some((TypeRef.Local m.Declaration).Slug, (m.Declaration, fields, refuse))
                            | _ -> None)
                        |> Map.ofList
                    Unions =
                        planned
                        |> List.choose (fun m ->
                            match m.Shape with
                            | PlannedShape.Union(cases, rules, kinds) -> Some((TypeRef.Local m.Declaration).Slug, (m.Declaration, cases, rules, kinds))
                            | _ -> None)
                        |> Map.ofList
                    Enums =
                        planned
                        |> List.choose (fun m ->
                            match m.Shape with
                            | PlannedShape.Enum cases -> Some((TypeRef.Local m.Declaration).Slug, (m.Declaration, cases))
                            | _ -> None)
                        |> Map.ofList
                }

    /// Generates the context file for one input file.
    let generate (settings: ContextSettings) (file: string) (source: string) : Result<string, GenerationError list> =
        Syntax.read file source |> Result.bind (plan settings) |> Result.map Emit.file
