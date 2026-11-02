namespace Morphir.CodeGeneration.FSharp

open System
open Morphir.IR
open Morphir.IR.V4

/// Direct v4 type and expression emission. No classic IR projection or JSON bridge.
module internal V4Emit =
    let private distinct location names =
        if
            (Set.ofList names).Count
            <> List.length names
        then
            Decode.fail "name-collision" location "Generated names collide."

    let private internalAccess =
        function
        | Public -> ""
        | Private -> "internal "

    let generate (_options: GenerationOptions) (distribution: Distribution) : GeneratedModule list =
        V4Support.validate distribution
        V4Check.validate distribution

        let package, definition =
            match distribution with
            | Library(package, _, definition) -> package, definition
            | Specs _
            | Application _ ->
                Decode.fail
                    "unsupported-distribution"
                    "$"
                    "F# generation requires Library implementations."

        Emit.packageNames package
        |> ignore

        let modules =
            OrderedMap.toList definition.Modules
            |> List.sortBy fst

        modules
        |> List.map (fun (path, _) -> (Emit.modulePath path).ToUpperInvariant())
        |> distinct "$"

        let types =
            modules
            |> List.collect (fun (path, module') ->
                OrderedMap.toList module'.Value.Types
                |> List.map (fun (name, definition) ->
                    {
                        PackagePath = package
                        ModulePath = path
                        LocalName = name
                    },
                    definition.Value.Value
                )
            )
            |> Map.ofList

        let values =
            modules
            |> List.collect (fun (path, module') ->
                OrderedMap.toList module'.Value.Values
                |> List.map (fun (name, value) ->
                    {
                        PackagePath = package
                        ModulePath = path
                        LocalName = name
                    },
                    value.Value.Value
                )
            )
            |> Map.ofList

        let emitted =
            modules
            |> List.map (fun (path, controlled) ->
                let moduleName = Emit.modulePath path
                let mutable dependencies = Set.empty

                let qualify q local =
                    if q.ModulePath = path then
                        local
                    else
                        dependencies <- Set.add q.ModulePath dependencies

                        Emit.packagePath q.PackagePath
                        + "."
                        + Emit.modulePath q.ModulePath
                        + "."
                        + local

                let rec typ value =
                    let renderFields fields =
                        fields
                        |> List.map (fun (name, t) ->
                            Emit.ident (Emit.camel name)
                            + " : "
                            + typ t
                        )
                        |> String.concat "; "

                    match value with
                    | Type.Variable(_, name) ->
                        "'"
                        + Emit.camel name
                    | Type.Unit _ -> "unit"
                    | Type.Tuple(_, []) -> "unit"
                    | Type.Tuple(_, elements) ->
                        "("
                        + (elements
                           |> List.map typ
                           |> String.concat " * ")
                        + ")"
                    | Type.Function(_, input, output) ->
                        "("
                        + typ input
                        + " -> "
                        + typ output
                        + ")"
                    | Type.Record(_, fields) ->
                        "{| "
                        + renderFields fields
                        + " |}"
                    | Type.ExtensibleRecord _ ->
                        Decode.fail
                            "unsupported-open-record"
                            moduleName
                            "Open extensible records cannot be represented faithfully."
                    | Type.Reference(_, q, arguments) ->
                        let rendered = List.map typ arguments

                        if Emit.sdk q then
                            match TargetSyntax.sdkType (Emit.sdkName q) rendered with
                            | Some target -> target
                            | None ->
                                Decode.fail
                                    "unsupported-sdk"
                                    moduleName
                                    $"Unmapped SDK type {FQName.toString q}."
                        else
                            match Map.tryFind q types with
                            | None ->
                                Decode.fail
                                    "unresolved-reference"
                                    moduleName
                                    $"Unknown type {FQName.toString q}."
                            | Some definition ->
                                let parameters =
                                    match definition with
                                    | TypeAliasDefinition(parameters, _)
                                    | CustomTypeDefinition(parameters, _, _)
                                    | IncompleteTypeDefinition(parameters, _, _) -> parameters

                                if
                                    parameters.Length
                                    <> arguments.Length
                                then
                                    Decode.fail
                                        "type-arity"
                                        moduleName
                                        $"Wrong type argument count for {FQName.toString q}."

                                let target = qualify q (Emit.ident (Emit.pascal q.LocalName))

                                if List.isEmpty rendered then
                                    target
                                else
                                    target
                                    + "<"
                                    + String.concat ", " rendered
                                    + ">"

                let entries =
                    OrderedMap.toList controlled.Value.Types
                    |> List.sortBy fst

                entries
                |> List.map (
                    fst
                    >> Emit.pascal
                )
                |> distinct moduleName

                let typeDeclarations =
                    entries
                    |> List.mapi (fun index (name, controlled) ->
                        let parameters, body =
                            match controlled.Value.Value with
                            | TypeAliasDefinition(parameters, Type.Record(_, fields)) ->
                                fields
                                |> List.map (
                                    fst
                                    >> Emit.camel
                                )
                                |> distinct moduleName

                                parameters,
                                "{ "
                                + (fields
                                   |> List.map (fun (name, t) ->
                                       Emit.ident (Emit.camel name)
                                       + " : "
                                       + typ t
                                   )
                                   |> String.concat "; ")
                                + " }"
                            | TypeAliasDefinition(parameters, body) -> parameters, typ body
                            | CustomTypeDefinition(parameters, access, constructors) ->
                                let cases = OrderedMap.toList constructors

                                cases
                                |> List.map (
                                    fst
                                    >> Emit.pascal
                                )
                                |> distinct moduleName

                                let cases =
                                    cases
                                    |> List.map (fun (name, arguments) ->
                                        "| "
                                        + Emit.ident (Emit.pascal name)
                                        + (if List.isEmpty arguments then
                                               ""
                                           else
                                               " of "
                                               + (arguments
                                                  |> List.map (snd >> typ)
                                                  |> String.concat " * "))
                                    )
                                    |> String.concat "\n    "

                                parameters,
                                "\n    "
                                + internalAccess access
                                + "\n    "
                                + cases
                            | IncompleteTypeDefinition _ ->
                                Decode.fail
                                    "unsupported-incomplete-type"
                                    moduleName
                                    "An incomplete type has no executable definition."

                        let generic =
                            if List.isEmpty parameters then
                                ""
                            else
                                "<"
                                + (parameters
                                   |> List.map (fun name ->
                                       "'"
                                       + Emit.camel name
                                   )
                                   |> String.concat ", ")
                                + ">"

                        (if index = 0 then "type " else "and ")
                        + internalAccess controlled.Access
                        + Emit.ident (Emit.pascal name)
                        + generic
                        + " = "
                        + body
                    )

                let entries =
                    OrderedMap.toList controlled.Value.Values
                    |> List.sortBy fst

                entries
                |> List.map (
                    fst
                    >> Emit.camel
                )
                |> distinct moduleName

                let valueDeclarations =
                    V4Expressions.declarations
                        {
                            Package = package
                            Module = path
                            Location = moduleName
                            Types = types
                            Values = values
                            Typ = typ
                            Qualify = qualify
                        }
                        entries

                let source =
                    "module "
                    + internalAccess controlled.Access
                    + Emit.packagePath package
                    + "."
                    + moduleName
                    + "\n\n"
                    + String.concat
                        "\n\n"
                        (typeDeclarations
                         @ valueDeclarations)
                    + "\n"

                path,
                dependencies,
                {
                    ModulePath = moduleName
                    RelativePath =
                        moduleName.Replace('.', '/')
                        + ".fs"
                    Source = Emit.format source
                }
            )

        DependencyOrder.modules Emit.modulePath emitted
