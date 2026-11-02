namespace Morphir.Ion

open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented
open Morphir.Ion.IonNodes
open Morphir.Ion.ClassicIonTypes
open Morphir.Ion.ClassicIonValues

/// Decodes v3 definitions, specifications, modules and distributions from a normalized document.
module internal ClassicIonDistribution =
    /// A member's documentation. The classic model holds an absent doc as the empty one, as JSON writes it.
    let private documented path (values: Members) item =
        WithDocumentation(
            optional path "doc" text values
            |> Option.defaultValue "",
            item
        )

    let private accessOf path (names: string list) =
        match names with
        | access :: _ -> if accessText path access then Public else Private
        | [] -> fail "invalid_node" path "A definition starts with public or private."

    let private annotationsAreRefused path (values: Members) =
        if hasMember "annotations" values then
            fail
                "unsupported_node"
                (memberAt path "annotations")
                "Morphir annotations are a v4 node, and this is a v3 document."

    let private constructorAccess path (value: IonValue) =
        if accessText path (symbolOrText path value) then
            Public
        else
            Private

    let private typeDefinition (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        annotationsAreRefused path values
        let parameters = typeParams path values

        let definition =
            match item.MemberAnnotations with
            | [ _; "def"; "alias"; "type" ] ->
                closed path [ "typeExp"; "typeParams"; "doc" ] values
                Type.TypeAliasDefinition(parameters, required path "typeExp" typ values)
            | [ _; "def"; "custom"; "type" ] ->
                closed path [ "access"; "constructors"; "typeParams"; "doc" ] values

                Type.CustomTypeDefinition(
                    parameters,
                    {
                        Access = required path "access" constructorAccess values
                        Value = constructors path values
                    }
                )
            | names -> fail "unsupported_node" path $"{spell names} is not a v3 type definition."

        item.MemberName,
        {
            Access = accessOf path item.MemberAnnotations
            Value = documented path values definition
        }

    let private valueDefinition (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        annotationsAreRefused path values

        match item.MemberAnnotations with
        | [ _; "def"; "value" ] ->
            closed path [ "inputTypes"; "outputType"; "body"; "doc" ] values

            item.MemberName,
            {
                Access = accessOf path item.MemberAnnotations
                Value = documented path values (valueDefinitionMembers path values)
            }
        | names -> fail "unsupported_node" path $"{spell names} is not a v3 value definition."

    let private typeSpecification (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        annotationsAreRefused path values
        let parameters = typeParams path values

        let specification =
            match item.MemberAnnotations with
            | [ "public"; "spec"; "opaque"; "type" ] ->
                closed path [ "typeParams"; "doc" ] values
                Type.OpaqueTypeSpecification parameters
            | [ "public"; "spec"; "alias"; "type" ] ->
                closed path [ "typeExp"; "typeParams"; "doc" ] values
                Type.TypeAliasSpecification(parameters, required path "typeExp" typ values)
            | [ "public"; "spec"; "custom"; "type" ] ->
                closed path [ "constructors"; "typeParams"; "doc" ] values
                Type.CustomTypeSpecification(parameters, constructors path values)
            | [ "public"; "spec"; "derived"; "type" ] ->
                closed path [ "baseType"; "fromBaseType"; "toBaseType"; "typeParams"; "doc" ] values

                Type.DerivedTypeSpecification(
                    parameters,
                    {
                        BaseType = required path "baseType" typ values
                        FromBaseType = required path "fromBaseType" fqNameString values
                        ToBaseType = required path "toBaseType" fqNameString values
                    }
                )
            | names -> fail "invalid_node" path $"{spell names} is not a type specification."

        item.MemberName, documented path values specification

    let private valueSpecification (item: NormalizedMember) =
        let path = item.MemberPath
        let values = item.MemberFields
        annotationsAreRefused path values
        closed path [ "inputs"; "output"; "doc" ] values

        let specification: ValueSpecification<JsonElement> = {
            Inputs =
                optionalItems path "inputs" values
                |> List.map (fun (itemPath, input) ->
                    let parameter = members itemPath input
                    closed itemPath [ "name"; "type" ] parameter

                    required itemPath "name" nameString parameter,
                    required itemPath "type" typ parameter
                )
            Output = required path "output" typ values
        }

        item.MemberName, documented path values specification

    let private moduleDoc (item: NormalizedModule) =
        annotationsAreRefused item.ModulePath item.ModuleFields
        closed item.ModulePath [ "doc" ] item.ModuleFields
        optional item.ModulePath "doc" text item.ModuleFields

    let private moduleDefinition (item: NormalizedModule) =
        let definition: Module.ModuleDefinition<JsonElement, JsonElement> = {
            Types =
                item.ModuleTypes
                |> List.map typeDefinition
                |> Map.ofList
            Values =
                item.ModuleValues
                |> List.map valueDefinition
                |> Map.ofList
            Doc = moduleDoc item
        }

        item.ModuleName,
        {
            Access = accessOf item.ModulePath item.ModuleAnnotations
            Value = definition
        }

    let private moduleSpecification (item: NormalizedModule) =
        let specification: Module.ModuleSpecification<JsonElement> = {
            Types =
                item.ModuleTypes
                |> List.map typeSpecification
                |> Map.ofList
            Values =
                item.ModuleValues
                |> List.map valueSpecification
                |> Map.ofList
            Doc = moduleDoc item
        }

        item.ModuleName, specification

    let private packageSpecification
        (modules: NormalizedModule list)
        : Package.PackageSpecification<JsonElement> =
        {
            Modules =
                modules
                |> List.map moduleSpecification
                |> Map.ofList
        }

    let decode (document: NormalizedDocument) : Distribution<JsonElement, JsonElement> =
        let dependencies =
            document.Dependencies
            |> List.map (fun dependency ->
                dependency.PackageName, packageSpecification dependency.PackageModules
            )
            |> Map.ofList

        match document.Header.Kind with
        | LibraryKind ->
            Library(
                document.Header.Package,
                dependencies,
                {
                    Modules =
                        document.Modules
                        |> List.map moduleDefinition
                        |> Map.ofList
                }
            )
        | SpecsKind ->
            Specs(document.Header.Package, dependencies, packageSpecification document.Modules)
        | ApplicationKind ->
            fail
                "unsupported_node"
                (memberAt (itemAt Root 0) "kind")
                "A v3 distribution is a library or specs."
