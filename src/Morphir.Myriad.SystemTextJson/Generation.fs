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

    /// Checks primitive token kinds and numeric bounds without running consumer code.
    let rec private scalarDefaultFits (skippable: bool) (value: System.Text.Json.JsonElement) (target: TypeRef) =
        let stringToken = value.ValueKind = System.Text.Json.JsonValueKind.String
        let numberToken = value.ValueKind = System.Text.Json.JsonValueKind.Number
        match target with
        | TypeRef.Option inner | TypeRef.ValueOption inner when skippable -> scalarDefaultFits skippable value inner
        | TypeRef.Primitive Primitive.JsonElement -> true
        | TypeRef.Primitive Primitive.Bool -> value.ValueKind = System.Text.Json.JsonValueKind.True || value.ValueKind = System.Text.Json.JsonValueKind.False
        | TypeRef.Primitive primitive when primitive.NumberTypeName.IsSome ->
            numberToken &&
                (match primitive with
                 | Primitive.Byte -> value.TryGetByte() |> fst
                 | Primitive.SByte -> value.TryGetSByte() |> fst
                 | Primitive.Int16 -> value.TryGetInt16() |> fst
                 | Primitive.UInt16 -> value.TryGetUInt16() |> fst
                 | Primitive.Int32 -> value.TryGetInt32() |> fst
                 | Primitive.UInt32 -> value.TryGetUInt32() |> fst
                 | Primitive.Int64 -> value.TryGetInt64() |> fst
                 | Primitive.UInt64 -> value.TryGetUInt64() |> fst
                 | Primitive.Single -> let valid, number = value.TryGetSingle() in valid && System.Single.IsFinite number
                 | Primitive.Double -> let valid, number = value.TryGetDouble() in valid && System.Double.IsFinite number
                 | Primitive.Decimal -> value.TryGetDecimal() |> fst
                 | _ -> false)
        | TypeRef.Primitive _ | TypeRef.Parsed _ -> stringToken
        | TypeRef.Sequence _ | TypeRef.Map _ | TypeRef.Tuple _ -> false
        | _ -> true

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

        // Provider types in this input are resolved from the record's lexical scope, before
        // considering the opens of the generated context. This also handles nested modules.
        let providerTypeName (declaration: TypeDeclaration) (name: string) =
            let rec find path =
                let candidate = String.concat "." (path @ [ name ])
                match source.Types |> List.tryFind (fun t -> String.concat "." (t.Path @ [ t.Name ]) = candidate) with
                | Some provider -> provider.QualifiedName
                | None when not path.IsEmpty -> find (List.take (path.Length - 1) path)
                | None -> name
            find declaration.Path

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
                                    let knownPaths =
                                        match field.Attributes |> List.tryFind (fun a -> a.Name = "JsonKnownPaths") with
                                        | None -> None
                                        | Some attribute ->
                                            let paths = attribute.Positional |> List.choose (function AttributeValue.Text path -> Some path | _ -> None)
                                            match resolved with
                                            | TypeRef.Sequence(_, TypeRef.Primitive Primitive.String) when attribute.Named.IsEmpty && paths.Length = attribute.Positional.Length ->
                                                Some(List.distinct paths)
                                            | _ ->
                                                unsupported declaration field.Location field.Name "JsonKnownPaths needs string literal paths on a string collection."
                                                None
                                    let versionAlias =
                                        match field.Attributes |> List.tryFind (fun a -> a.Name = "JsonVersionAlias") with
                                        | None -> None
                                        | Some attribute ->
                                            let critical = attribute.Named |> List.tryPick (function "CriticalMember", AttributeValue.Text name -> Some name | _ -> None)
                                            let validNamed = attribute.Named |> List.forall (function "CriticalMember", AttributeValue.Text name when name <> "" -> true | _ -> false)
                                            let validType =
                                                match resolved with
                                                | TypeRef.Primitive Primitive.String | TypeRef.Parsed _ -> true
                                                | _ -> false
                                            match attribute.Positional with
                                            | [ AttributeValue.Text legacy; AttributeValue.Text oldVersion; AttributeValue.Text currentVersion ] when validNamed && validType && legacy <> "" && legacy <> written && oldVersion <> "" && currentVersion <> "" && oldVersion <> currentVersion && names.Length <= 1 ->
                                                Some { LegacyName = legacy; LegacyVersion = oldVersion; CurrentVersion = currentVersion; CriticalMember = critical }
                                            | _ ->
                                                unsupported declaration field.Location field.Name "JsonVersionAlias needs a string or parsed-value field, distinct nonempty legacy/current names and versions, and an optional CriticalMember name."
                                                None
                                    let omitEmpty = hasAttribute "JsonOmitEmpty" field.Attributes
                                    let extensionData = hasAttribute "JsonExtensionData" field.Attributes
                                    if extensionData then
                                        match resolved with
                                        | TypeRef.Map(_, TypeRef.Primitive Primitive.String, TypeRef.Primitive Primitive.JsonElement) -> ()
                                        | _ -> unsupported declaration field.Location field.Name "JsonExtensionData needs Map<string, JsonElement> or IReadOnlyDictionary<string, JsonElement>."
                                        if rules.RefuseUnknownMembers then
                                            unsupported declaration field.Location field.Name "JsonExtensionData cannot be combined with UnknownMembers.Refuse."
                                        if omitEmpty || field.Attributes |> List.exists (fun a -> List.contains a.Name [ "JsonDefaultValue"; "JsonDefaultJson"; "JsonDefaultEmpty"; "JsonDefaultValueProvider" ]) then
                                            unsupported declaration field.Location field.Name "JsonExtensionData is already empty when absent and flattened on writing; do not combine it with JsonOmitEmpty or a missing-member default."
                                    if omitEmpty then
                                        match resolved with
                                        | TypeRef.Sequence _ | TypeRef.Map _ -> ()
                                        | _ -> unsupported declaration field.Location field.Name "JsonOmitEmpty applies only to a collection field."
                                    let defaultAttributes = field.Attributes |> List.filter (fun a -> List.contains a.Name [ "JsonDefaultValue"; "JsonDefaultJson"; "JsonDefaultEmpty"; "JsonDefaultValueProvider" ])
                                    if defaultAttributes.Length > 1 then
                                        unsupported declaration field.Location field.Name "Declare only one missing-member default."
                                    let missingDefault =
                                        match defaultAttributes |> List.tryHead with
                                        | None -> None
                                        | Some attribute when attribute.Name = "JsonDefaultValueProvider" ->
                                            match attribute.Positional, attribute.Named with
                                            | [ AttributeValue.TypeOf provider; AttributeValue.Text methodName ], [] when System.Text.RegularExpressions.Regex.IsMatch(methodName, "^[A-Za-z_][A-Za-z0-9_']*$") ->
                                                Some(DefaultValueSpec.Provider(providerTypeName declaration provider + ".``" + methodName + "``()"))
                                            | _ ->
                                                unsupported declaration field.Location field.Name "JsonDefaultValueProvider needs typeof<Provider> and a method name literal or nameof Provider.Method. The method must be accessible, static, take no arguments, and return the field type."
                                                None
                                        | Some attribute ->
                                            let json =
                                                match attribute.Name, attribute.Positional, attribute.Named with
                                                | "JsonDefaultEmpty", [], [] ->
                                                    match resolved with
                                                    | TypeRef.Sequence _ -> Some "[]"
                                                    | TypeRef.Map(_, key, _) -> Some(if Emit.mapAsObject settings.MapFormat key then "{}" else "[]")
                                                    | _ ->
                                                        unsupported declaration field.Location field.Name "JsonDefaultEmpty applies only to a collection field."
                                                        None
                                                | "JsonDefaultJson", [ AttributeValue.Text json ], [] -> Some json
                                                | "JsonDefaultValue", [ AttributeValue.Text text ], [] -> Some("\"" + System.Text.Json.JsonEncodedText.Encode(text).ToString() + "\"")
                                                | "JsonDefaultValue", [ AttributeValue.Bool value ], [] -> Some(if value then "true" else "false")
                                                | "JsonDefaultValue", [ AttributeValue.Int value ], [] -> Some(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                                | "JsonDefaultValue", [ AttributeValue.Number value ], [] -> Some value
                                                | _ ->
                                                    unsupported declaration field.Location field.Name $"{attribute.Name} needs one literal value of its supported kind."
                                                    None
                                            json |> Option.bind (fun json ->
                                                try
                                                    use document = System.Text.Json.JsonDocument.Parse json
                                                    let incompatibleVersion =
                                                        versionAlias |> Option.exists (fun alias ->
                                                            document.RootElement.ValueKind <> System.Text.Json.JsonValueKind.String
                                                            || not (System.String.Equals(document.RootElement.GetString(), alias.CurrentVersion, System.StringComparison.Ordinal)))
                                                    if incompatibleVersion then
                                                        unsupported declaration field.Location field.Name $"{attribute.Name} on a JsonVersionAlias field must contain its exact current version string."
                                                        None
                                                    elif attribute.Name = "JsonDefaultValue" && not (scalarDefaultFits rules.SkipAbsentOptions document.RootElement resolved) then
                                                        unsupported declaration field.Location field.Name "JsonDefaultValue has an incompatible scalar kind or numeric value outside the field's range; use a typed provider for non-scalar values."
                                                        None
                                                    else
                                                        Some(DefaultValueSpec.Json json)
                                                with :? System.Text.Json.JsonException ->
                                                    unsupported declaration field.Location field.Name $"{attribute.Name} must contain one complete JSON value."
                                                    None)

                                    let omitDefault =
                                        if hasAttribute "JsonOmitDefault" field.Attributes then
                                            match resolved, missingDefault with
                                            | TypeRef.Primitive Primitive.Bool, Some(DefaultValueSpec.Json("true" | "false" as literal)) -> Some literal
                                            | _ ->
                                                unsupported declaration field.Location field.Name "JsonOmitDefault needs a bool field with JsonDefaultValue(true) or JsonDefaultValue(false)."
                                                None
                                        else
                                            None

                                    {
                                        Name = name
                                        JsonName = written
                                        ReadNames = (if names.IsEmpty then [ written ] else names) @ (versionAlias |> Option.map (fun alias -> [ alias.LegacyName ]) |> Option.defaultValue [])
                                        Type = resolved
                                        Skippable = rules.SkipAbsentOptions
                                        AllowNull = rules.AllowNullFields && acceptsNull resolved
                                        NumbersFromStrings = lenient
                                        Default = missingDefault
                                        OmitEmpty = omitEmpty
                                        OmitDefault = omitDefault
                                        ExtensionData = extensionData
                                        VersionAlias = versionAlias
                                        KnownPaths = knownPaths
                                    }))

                        if resolvedFields |> List.filter (fun field -> field.ExtensionData) |> List.length > 1 then
                            unsupported declaration declaration.Location None "Only one JsonExtensionData field is allowed in a record."

                        if resolvedFields |> List.filter (fun field -> field.VersionAlias.IsSome) |> List.length > 1 then
                            unsupported declaration declaration.Location None "Only one JsonVersionAlias field is allowed in a record."

                        for field in resolvedFields do
                            match field.VersionAlias with
                            | Some alias ->
                                match alias.CriticalMember with
                                | Some name ->
                                    match resolvedFields |> List.tryFind (fun candidate -> candidate.JsonName = name) with
                                    | Some critical when critical.KnownPaths |> Option.exists (fun paths -> List.contains field.JsonName paths && not (List.contains alias.LegacyName paths)) -> ()
                                    | _ -> unsupported declaration declaration.Location (Some field.Name) "JsonVersionAlias.CriticalMember must name a JsonKnownPaths collection allowing the current version member, not its legacy name."
                                | None -> ()
                            | None -> ()

                        for name, occurrences in resolvedFields |> List.filter (fun field -> not field.ExtensionData) |> List.collect (fun field -> field.ReadNames) |> List.countBy id do
                            if occurrences > 1 then
                                unsupported declaration declaration.Location None $"More than one record field or alias has the JSON name '{name}'."

                        if resolvedFields.Length = fields.Length then
                            Some { Declaration = declaration; Shape = PlannedShape.Record(resolvedFields, rules.RefuseUnknownMembers) }
                        else
                            None
                    | TypeShape.Union(_, true) ->
                        unsupported declaration declaration.Location None "the union cases are private, so generated code cannot build them."
                        None
                    | TypeShape.Union(cases, false) ->
                        for case in cases do
                            for field in case.Fields do
                                if field.Attributes |> List.exists (fun a -> List.contains a.Name [ "JsonDefaultValue"; "JsonDefaultJson"; "JsonDefaultEmpty"; "JsonDefaultValueProvider"; "JsonOmitEmpty"; "JsonOmitDefault"; "JsonExtensionData"; "JsonVersionAlias"; "JsonKnownPaths" ]) then
                                    unsupported declaration field.Location field.Name "Wire member annotations apply only to record fields."
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
                                                Default = None
                                                OmitEmpty = false
                                                OmitDefault = None
                                                ExtensionData = false
                                                VersionAlias = None
                                                KnownPaths = None
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
                                // Cases written as {}: no field (without UnwrapFieldlessTags), or only options that are left out.
                                let empty =
                                    cases
                                    |> List.filter (fun c ->
                                        if c.Fields.IsEmpty then
                                            not e.UnwrapFieldlessTags
                                        else
                                            settings.Records.SkipAbsentOptions
                                            && c.Fields
                                               |> List.forall (fun f ->
                                                   match f.Type with
                                                   | TypeRef.Option _
                                                   | TypeRef.ValueOption _ -> true
                                                   | _ -> false))

                                if empty.Length > 1 then
                                    unsupported declaration declaration.Location None $"""the cases {empty |> List.map (fun c -> c.Name) |> String.concat " and "} can all be written as {{}}, so an untagged reader cannot tell them apart; add UnwrapFieldlessTags for cases with no field, or make an option field required."""

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
