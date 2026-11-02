namespace Morphir.Ion

open System.Collections.Generic
open Morphir.IR
open Morphir.IR.V4
open Morphir.Ion.IonNodes
open Morphir.Ion.V4IonTypes
open Morphir.Ion.V4IonValues

/// Decodes v4 definitions, specifications, annotations, modules and distributions from a normalized document.
module internal V4IonDistribution =
    let private refuseAnnotations path (values: Members) =
        if hasMember "annotations" values then
            fail
                "invalid_node"
                (memberAt path "annotations")
                "A definition carries no Morphir annotations; a specification does."

    let private annotationArgument path (element: IonValue) =
        if isPlainStruct element then
            let values = members path element
            closed path [ "name"; "value" ] values

            NamedArgument(
                required path "name" nameString values,
                required path "value" value values
            )
        else
            PositionalArgument(value path element)

    /// Reads a compact annotation. Its free text starts after the first colon that follows the hash, and it
    /// means one positional string argument, as the JSON reader holds it.
    let private compactAnnotation path (input: string) =
        let hash = input.IndexOf '#'
        let separator = if hash < 0 then -1 else input.IndexOf(':', hash)

        if separator < 0 then
            {
                Name = fqNameText path input
                Arguments = []
            }
        elif
            separator = input.Length
                        - 1
        then
            fail "invalid_name" path $"The annotation '{input}' has an empty value."
        else
            {
                Name = fqNameText path (input.Substring(0, separator))
                Arguments = [
                    PositionalArgument(
                        Value.Literal(
                            ValueAttributes.empty,
                            StringLiteral(
                                input.Substring(
                                    separator
                                    + 1
                                )
                            )
                        )
                    )
                ]
            }

    let private annotation path (element: IonValue) =
        match element.Annotations, element.Data with
        | [], IonData.String input -> compactAnnotation path input
        | [], IonData.Struct _ ->
            let values = members path element
            closed path [ "name"; "arguments" ] values

            {
                Name = required path "name" fqNameString values
                Arguments =
                    optionalItems path "arguments" values
                    |> List.map (fun (itemPath, item) -> annotationArgument itemPath item)
            }
        | _ ->
            fail "invalid_node" path "An annotation is a string or a struct with no Ion annotation."

    let private annotations path (values: Members) =
        optionalItems path "annotations" values
        |> List.map (fun (itemPath, item) -> annotation itemPath item)

    let private docOf path (values: Members) = optional path "doc" text values

    let private accessOf path (names: string list) =
        match names with
        | access :: _ -> if accessText path access then Public else Private
        | [] -> fail "invalid_node" path "A definition starts with public or private."

    let private toMap path (entries: (Name * 'v) list) =
        match OrderedMap.ofList entries with
        | Ok map -> map
        | Error key ->
            fail
                "duplicate_name"
                path
                $"The name '{CanonicalNames.nameToString key}' appears more than once."

    /// Reads <c>[ { name, args: [ { name, type } ] } ]</c>.
    let private constructors path (values: Members) : Constructors =
        optionalItems path "constructors" values
        |> List.map (fun (itemPath, item) ->
            let constructor = members itemPath item
            closed itemPath [ "name"; "args" ] constructor
            required itemPath "name" nameString constructor, namedTypes itemPath "args" constructor
        )
        |> toMap path

    let private constructorAccess path (element: IonValue) =
        if accessText path (symbolOrText path element) then
            Public
        else
            Private

    let private typeDefinition (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        refuseAnnotations path values
        let parameters = typeParams path values

        let definition =
            match item.MemberAnnotations with
            | [ _; "def"; "alias"; "type" ] ->
                closed path [ "typeExp"; "typeParams"; "doc" ] values
                TypeAliasDefinition(parameters, required path "typeExp" typ values)
            | [ _; "def"; "custom"; "type" ] ->
                closed path [ "access"; "constructors"; "typeParams"; "doc" ] values

                CustomTypeDefinition(
                    parameters,
                    required path "access" constructorAccess values,
                    constructors path values
                )
            | [ _; "def"; "incomplete"; "type" ] ->
                closed path [ "incompleteness"; "partialTypeExp"; "typeParams"; "doc" ] values

                IncompleteTypeDefinition(
                    parameters,
                    required path "incompleteness" incompleteness values,
                    optional path "partialTypeExp" typ values
                )
            | names -> fail "invalid_node" path $"{spell names} is not a type definition."

        item.MemberName,
        {
            Access = accessOf path item.MemberAnnotations
            Value = {
                Doc = docOf path values
                Value = definition
            }
        }

    let private nativeHint path (element: IonValue) =
        match annotationNames path element, element.Data with
        | [], IonData.Symbol(IonSymbol.Text "arithmetic") -> Arithmetic
        | [], IonData.Symbol(IonSymbol.Text "comparison") -> Comparison
        | [], IonData.Symbol(IonSymbol.Text "stringOp") -> StringOp
        | [], IonData.Symbol(IonSymbol.Text "collectionOp") -> CollectionOp
        | [ "platformSpecific" ], IonData.Struct _ ->
            let values = members path element
            closed path [ "platform" ] values
            PlatformSpecific(required path "platform" text values)
        | _ ->
            fail
                "invalid_node"
                path
                "hint is arithmetic, comparison, stringOp, collectionOp, or platformSpecific::{ platform }."

    /// At least one binding, and a platform appears once.
    let private externals path (element: IonValue) =
        let bindings =
            listItems path element
            |> List.map (fun (itemPath, item) ->
                let values = members itemPath item
                closed itemPath [ "targetPlatform"; "externalName" ] values

                {
                    TargetPlatform = required itemPath "targetPlatform" text values
                    ExternalName = required itemPath "externalName" text values
                }
            )

        if List.isEmpty bindings then
            fail "invalid_node" path "An external body needs at least one binding."

        let seen = HashSet<string>()

        for binding in bindings do
            if not (seen.Add binding.TargetPlatform) then
                fail
                    "duplicate_name"
                    path
                    $"The target platform '{binding.TargetPlatform}' appears more than once."

        bindings

    let private valueDefinition (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        refuseAnnotations path values

        /// Only the incomplete body may lack an output type.
        let output () = required path "outputType" typ values

        let body =
            match item.MemberAnnotations with
            | [ _; "def"; "value" ] ->
                closed path [ "inputTypes"; "outputType"; "body"; "doc" ] values
                ExpressionBody(output (), required path "body" value values)
            | [ _; "def"; "native"; "value" ] ->
                closed path [ "inputTypes"; "outputType"; "hint"; "description"; "doc" ] values

                NativeBody(
                    output (),
                    {
                        Hint = required path "hint" nativeHint values
                        Description = optional path "description" text values
                    }
                )
            | [ _; "def"; "external"; "value" ] ->
                closed path [ "inputTypes"; "outputType"; "externals"; "body"; "doc" ] values

                ExternalBody(
                    output (),
                    {
                        Externals = required path "externals" externals values
                        Fallback = optional path "body" value values
                    }
                )
            | [ _; "def"; "incomplete"; "value" ] ->
                closed path [ "inputTypes"; "outputType"; "incompleteness"; "body"; "doc" ] values

                IncompleteBody(
                    optional path "outputType" typ values,
                    required path "incompleteness" incompleteness values,
                    optional path "body" value values
                )
            | names -> fail "invalid_node" path $"{spell names} is not a value definition."

        item.MemberName,
        {
            Access = accessOf path item.MemberAnnotations
            Value = {
                Doc = docOf path values
                Value = {
                    InputTypes = inputs path "inputTypes" values
                    Body = body
                }
            }
        }

    let private typeSpecification (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        let stated = annotations path values
        let parameters = typeParams path values

        let specification =
            match item.MemberAnnotations with
            | [ "public"; "spec"; "opaque"; "type" ] ->
                closed path [ "annotations"; "typeParams"; "doc" ] values
                OpaqueTypeSpecification(stated, parameters)
            | [ "public"; "spec"; "alias"; "type" ] ->
                closed path [ "annotations"; "typeExp"; "typeParams"; "doc" ] values
                TypeAliasSpecification(stated, parameters, required path "typeExp" typ values)
            | [ "public"; "spec"; "custom"; "type" ] ->
                closed path [ "annotations"; "constructors"; "typeParams"; "doc" ] values
                CustomTypeSpecification(stated, parameters, constructors path values)
            | [ "public"; "spec"; "derived"; "type" ] ->
                closed
                    path
                    [ "annotations"; "baseType"; "fromBaseType"; "toBaseType"; "typeParams"; "doc" ]
                    values

                DerivedTypeSpecification(
                    stated,
                    parameters,
                    required path "baseType" typ values,
                    required path "fromBaseType" fqNameString values,
                    required path "toBaseType" fqNameString values
                )
            | names -> fail "invalid_node" path $"{spell names} is not a type specification."

        item.MemberName,
        {
            Doc = docOf path values
            Value = specification
        }

    let private valueSpecification (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        closed path [ "annotations"; "inputs"; "output"; "doc" ] values

        let specification: ValueSpecification = {
            Annotations = annotations path values
            Inputs = inputs path "inputs" values
            Output = required path "output" typ values
        }

        item.MemberName,
        {
            Doc = docOf path values
            Value = specification
        }

    let private moduleDefinition (item: NormalizedModule) =
        let path = item.ModulePath
        refuseAnnotations path item.ModuleFields
        closed path [ "doc" ] item.ModuleFields

        let definition: ModuleDefinition = {
            Types =
                item.ModuleTypes
                |> List.map typeDefinition
                |> toMap path
            Values =
                item.ModuleValues
                |> List.map valueDefinition
                |> toMap path
            Doc = docOf path item.ModuleFields
        }

        item.ModuleName,
        {
            Access = accessOf path item.ModuleAnnotations
            Value = definition
        }

    let private moduleSpecification (item: NormalizedModule) =
        let path = item.ModulePath
        closed path [ "annotations"; "doc" ] item.ModuleFields

        let specification: ModuleSpecification = {
            Annotations = annotations path item.ModuleFields
            Types =
                item.ModuleTypes
                |> List.map typeSpecification
                |> toMap path
            Values =
                item.ModuleValues
                |> List.map valueSpecification
                |> toMap path
            Doc = docOf path item.ModuleFields
        }

        item.ModuleName, specification

    /// Builds a map the layout step already made unique.
    let private uniqueMap (entries: ('k * 'v) list) =
        match OrderedMap.ofList entries with
        | Ok map -> map
        | Error _ -> fail "duplicate_name" Root "A module or package appears more than once."

    let private packageDefinition (modules: NormalizedModule list) : PackageDefinition = {
        Modules =
            modules
            |> List.map moduleDefinition
            |> uniqueMap
    }

    let private packageSpecification (modules: NormalizedModule list) : PackageSpecification = {
        Modules =
            modules
            |> List.map moduleSpecification
            |> uniqueMap
    }

    let private dependencies decode (document: NormalizedDocument) =
        document.Dependencies
        |> List.map (fun dependency -> dependency.PackageName, decode dependency.PackageModules)
        |> uniqueMap

    let private entryPointKind path (element: IonValue) =
        match symbolOrText path element with
        | "main" -> Main
        | "command" -> Command
        | "handler" -> Handler
        | "job" -> Job
        | "policy" -> Policy
        | other -> fail "invalid_type" path $"'{other}' is not an entry point kind."

    let private entryPoint path (element: IonValue) : EntryPoint =
        let values = members path element
        closed path [ "target"; "kind"; "doc" ] values

        {
            Target = required path "target" fqNameString values
            Kind = required path "kind" entryPointKind values
            Doc = docOf path values
        }

    let private entryPoints (entries: Node option) =
        match entries with
        | None -> []
        | Some(path, element) ->
            keyedMembers path element
            |> List.map (fun (key, item) -> key, entryPoint (memberAt path key) item)

    let decode (document: NormalizedDocument) : Distribution =
        let header = document.Header

        match header.Kind with
        | LibraryKind ->
            Library(
                header.Package,
                dependencies packageSpecification document,
                packageDefinition document.Modules
            )
        | SpecsKind ->
            Specs(
                header.Package,
                dependencies packageSpecification document,
                packageSpecification document.Modules
            )
        | ApplicationKind ->
            Application(
                header.Package,
                dependencies packageDefinition document,
                packageDefinition document.Modules,
                entryPoints header.EntryPoints
            )
