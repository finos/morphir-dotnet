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

        let resolve (declaration: TypeDeclaration) (location: Location) (memberName: string option) (syntax: TypeSyntax) =
            match Resolve.resolve included [] syntax with
            | Ok resolved -> Some resolved
            | Error reason ->
                unsupported declaration location memberName reason
                None

        let planned =
            included
            |> List.choose (fun declaration ->
                if not declaration.Parameters.IsEmpty then
                    unsupported declaration declaration.Location None "generic types are not supported; declare an abbreviation of a closed type instead."
                    None
                else
                    match declaration.Shape with
                    | TypeShape.Record(_, true) ->
                        unsupported declaration declaration.Location None "the record representation is private, so generated code cannot build it."
                        None
                    | TypeShape.Record(fields, false) ->
                        let resolvedFields =
                            fields
                            |> List.choose (fun field ->
                                let name = defaultArg field.Name ""

                                resolve declaration field.Location field.Name field.Type
                                |> Option.map (fun resolved ->
                                    let names = jsonNames field.Attributes
                                    let written = List.tryHead names |> Option.defaultValue name

                                    {
                                        Name = name
                                        JsonName = written
                                        ReadNames = (if names.IsEmpty then [ written ] else names)
                                        Type = resolved
                                    }))

                        if resolvedFields.Length = fields.Length then
                            Some { Declaration = declaration; Shape = PlannedShape.Record resolvedFields }
                        else
                            None
                    | TypeShape.Union _ ->
                        unsupported declaration declaration.Location None "unions are not supported yet."
                        None
                    | TypeShape.Enum cases -> Some { Declaration = declaration; Shape = PlannedShape.Enum cases }
                    | TypeShape.Abbreviation target ->
                        resolve declaration declaration.Location None (TypeSyntax.Named([ declaration.Name ], []))
                        |> Option.map (fun _ ->
                            match Resolve.resolve included [ declaration.Name ] target with
                            | Ok resolved -> { Declaration = declaration; Shape = PlannedShape.Abbreviation resolved }
                            | Error _ -> { Declaration = declaration; Shape = PlannedShape.Abbreviation(TypeRef.External declaration.QualifiedName) })
                    | TypeShape.Other -> None)

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
                            | PlannedShape.Record fields -> Some((TypeRef.Local m.Declaration).Slug, (m.Declaration, fields))
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
