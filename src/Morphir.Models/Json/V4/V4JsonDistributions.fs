namespace Morphir.Json.V4

open System
open System.Numerics
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4.V4JsonDecoding
open Morphir.Json.V4.V4JsonValues

/// Hand-written decoding of v4 definitions, specifications, modules, packages and distributions.
module internal V4JsonDistributions =
    let private annotationArgument location element =
        let values =
            if (element: JsonElement).ValueKind = JsonValueKind.Object then
                members location element
            else
                []

        if hasMember "name" values then
            closed location [ "name"; "value" ] values

            NamedArgument(
                required location [ "name" ] name values,
                required location [ "value" ] value values
            )
        else
            PositionalArgument(value location element)

    /// Reads a compact annotation. Its free text starts after the first colon that follows the hash, and it
    /// means one positional string argument.
    let private compactAnnotation location (input: string) =
        let hash = input.IndexOf '#'
        let separator = if hash < 0 then -1 else input.IndexOf(':', hash)

        if separator < 0 then
            {
                Name = fqNameText location input
                Arguments = []
            }
        elif
            separator = input.Length
                        - 1
        then
            fail "invalid_name" location $"The annotation '{input}' has an empty value."
        else
            let argument =
                Value.Literal(
                    ValueAttributes.empty,
                    StringLiteral(
                        input.Substring(
                            separator
                            + 1
                        )
                    )
                )

            {
                Name = fqNameText location (input.Substring(0, separator))
                Arguments = [ PositionalArgument argument ]
            }

    let private annotation location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> compactAnnotation location (nameString location element)
        | JsonValueKind.Object ->
            let values = members location element
            closed location [ "name"; "arguments" ] values

            {
                Name = required location [ "name" ] fqName values
                Arguments =
                    optional location [ "arguments" ] (listOf annotationArgument) values
                    |> Option.defaultValue []
            }
        | _ -> fail "invalid_type" location "Expected an annotation."

    let private annotations location values =
        optional location [ "annotations" ] (listOf annotation) values
        |> Option.defaultValue []

    let private typeParams = listOf name

    let private namedType location element =
        match items location element with
        | [ parameter; parameterType ] ->
            name (itemAt location 0) parameter, typ (itemAt location 1) parameterType
        | _ -> fail "invalid_type" location "Expected a [name, type] pair."

    let private constructorEntry location element =
        match items location element with
        | [ key; parameters ] ->
            name (itemAt location 0) key, listOf namedType (itemAt location 1) parameters
        | _ -> fail "invalid_type" location "Expected a [name, parameters] pair."

    let private constructors location (element: JsonElement) : Constructors =
        match element.ValueKind with
        | JsonValueKind.Object -> orderedNameMap (listOf namedType) location element
        | JsonValueKind.Array ->
            listOf constructorEntry location element
            |> orderedMap location CanonicalNames.nameToString
        | _ -> fail "invalid_type" location "Expected constructors."

    /// A custom type body's access takes only the two canonical spellings, and defaults to public.
    let private constructorAccess location (element: JsonElement) =
        match text location element with
        | "Public" -> Public
        | "Private" -> Private
        | other -> fail "invalid_access" location $"'{other}' is not a constructor access level."

    let private typeDefinitionMembers location values =
        let tag, payload =
            nodeTag
                location
                [ "TypeAliasDefinition"; "CustomTypeDefinition"; "IncompleteTypeDefinition" ]
                values

        let here = memberAt location tag
        let body = members here payload

        match tag with
        | "TypeAliasDefinition" ->
            closed here [ "typeParams"; "typeExp" ] body

            TypeAliasDefinition(
                required here [ "typeParams" ] typeParams body,
                required here [ "typeExp" ] typ body
            )
        | "CustomTypeDefinition" ->
            closed here [ "typeParams"; "access"; "constructors" ] body

            CustomTypeDefinition(
                required here [ "typeParams" ] typeParams body,
                optional here [ "access" ] constructorAccess body
                |> Option.defaultValue Public,
                required here [ "constructors" ] constructors body
            )
        | "IncompleteTypeDefinition" ->
            closed here [ "typeParams"; "incompleteness"; "partialTypeExp" ] body

            IncompleteTypeDefinition(
                required here [ "typeParams" ] typeParams body,
                required here [ "incompleteness" ] incompleteness body,
                optional here [ "partialTypeExp" ] typ body
            )
        | other -> fail "unknown_node" location $"'{other}' is not a type definition."

    let private legacyTag location (element: JsonElement) =
        match items location element with
        | tag :: rest -> text (itemAt location 0) tag, rest
        | [] -> fail "invalid_type" location "A tagged array starts with its tag."

    /// Reads a legacy tagged array.
    let private typeDefinitionArray location element =
        let accessControlledConstructors =
            accessControlled
                (fun here values ->
                    closed here [ "value" ] values
                    required here [ "value" ] constructors values
                )
                constructors

        match legacyTag location element with
        | "TypeAliasDefinition", [ parameters; aliased ] ->
            TypeAliasDefinition(
                typeParams (itemAt location 1) parameters,
                typ (itemAt location 2) aliased
            )
        | "CustomTypeDefinition", [ parameters; controlled ] ->
            let controlled = accessControlledConstructors (itemAt location 2) controlled

            CustomTypeDefinition(
                typeParams (itemAt location 1) parameters,
                controlled.Access,
                controlled.Value
            )
        | ("TypeAliasDefinition" | "CustomTypeDefinition"), _ ->
            fail "invalid_type" location "The tagged array has the wrong number of items."
        | tag, _ -> fail "unknown_node" location $"'{tag}' is not a type definition."

    let private typeDefinition = documented typeDefinitionMembers typeDefinitionArray

    let private derivedMembers location values =
        required location [ "baseType" ] typ values,
        required location [ "fromBaseType" ] fqName values,
        required location [ "toBaseType" ] fqName values

    let private typeSpecificationMembers location values =
        let tag, payload =
            nodeTag
                location
                [
                    "TypeAliasSpecification"
                    "OpaqueTypeSpecification"
                    "CustomTypeSpecification"
                    "DerivedTypeSpecification"
                ]
                values

        let here = memberAt location tag
        let body = members here payload

        match tag with
        | "TypeAliasSpecification" ->
            closed here [ "annotations"; "typeParams"; "typeExp" ] body

            TypeAliasSpecification(
                annotations here body,
                required here [ "typeParams" ] typeParams body,
                required here [ "typeExp" ] typ body
            )
        | "OpaqueTypeSpecification" ->
            closed here [ "annotations"; "typeParams" ] body

            OpaqueTypeSpecification(
                annotations here body,
                optional here [ "typeParams" ] typeParams body
                |> Option.defaultValue []
            )
        | "CustomTypeSpecification" ->
            closed here [ "annotations"; "typeParams"; "constructors" ] body

            CustomTypeSpecification(
                annotations here body,
                required here [ "typeParams" ] typeParams body,
                required here [ "constructors" ] constructors body
            )
        | "DerivedTypeSpecification" ->
            closed
                here
                [ "annotations"; "typeParams"; "baseType"; "fromBaseType"; "toBaseType" ]
                body

            let baseType, fromBaseType, toBaseType = derivedMembers here body

            DerivedTypeSpecification(
                annotations here body,
                required here [ "typeParams" ] typeParams body,
                baseType,
                fromBaseType,
                toBaseType
            )
        | other -> fail "unknown_node" location $"'{other}' is not a type specification."

    /// Reads a legacy tagged array.
    let private typeSpecificationArray location element =
        match legacyTag location element with
        | "TypeAliasSpecification", [ parameters; aliased ] ->
            TypeAliasSpecification(
                [],
                typeParams (itemAt location 1) parameters,
                typ (itemAt location 2) aliased
            )
        | "OpaqueTypeSpecification", [ parameters ] ->
            OpaqueTypeSpecification([], typeParams (itemAt location 1) parameters)
        | "CustomTypeSpecification", [ parameters; declared ] ->
            CustomTypeSpecification(
                [],
                typeParams (itemAt location 1) parameters,
                constructors (itemAt location 2) declared
            )
        | "DerivedTypeSpecification", [ parameters; details ] ->
            let here = itemAt location 2
            let body = members here details
            closed here [ "baseType"; "fromBaseType"; "toBaseType" ] body
            let baseType, fromBaseType, toBaseType = derivedMembers here body

            DerivedTypeSpecification(
                [],
                typeParams (itemAt location 1) parameters,
                baseType,
                fromBaseType,
                toBaseType
            )
        | ("TypeAliasSpecification" | "OpaqueTypeSpecification" | "CustomTypeSpecification" | "DerivedTypeSpecification"),
          _ -> fail "invalid_type" location "The tagged array has the wrong number of items."
        | tag, _ -> fail "unknown_node" location $"'{tag}' is not a type specification."

    let private typeSpecification =
        documented typeSpecificationMembers typeSpecificationArray

    /// Reads inputs as an object keyed by name, or as the legacy array of pairs.
    let private inputs location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Array ->
            listOf namedType location element
            |> uniqueNames location
        | _ -> nameMap typ location element

    let private valueSpecificationMembers location values =
        closed location [ "annotations"; "inputs"; "output" ] values

        {
            Annotations = annotations location values
            Inputs =
                optional location [ "inputs" ] inputs values
                |> Option.defaultValue []
            Output = required location [ "output" ] typ values
        }

    let private valueSpecification = documented valueSpecificationMembers notAnObject

    let private optionalMap location key decode values =
        optional location [ key ] (orderedNameMap decode) values
        |> Option.defaultValue OrderedMap.empty

    let private controlledTypeDefinition =
        accessControlled
            (fun location values ->
                documentedMembers location typeDefinitionMembers typeDefinitionArray values
            )
            typeDefinition

    let private controlledValueDefinition =
        accessControlled
            (fun location values ->
                documentedMembers location valueDefinitionMembers notAnObject values
            )
            valueDefinition

    let private moduleDefinitionMembers location values =
        closed location [ "types"; "values" ] values

        optionalMap location "types" controlledTypeDefinition values,
        optionalMap location "values" controlledValueDefinition values

    let private toModuleDefinition (documentedModule: Documented<_>) : ModuleDefinition =
        let types, values = documentedModule.Value

        {
            Types = types
            Values = values
            Doc = documentedModule.Doc
        }

    let private controlledModuleDefinition =
        accessControlled
            (fun location values ->
                documentedMembers location moduleDefinitionMembers notAnObject values
                |> toModuleDefinition
            )
            (fun location element ->
                documented moduleDefinitionMembers notAnObject location element
                |> toModuleDefinition
            )

    let private moduleSpecificationMembers location values =
        closed location [ "annotations"; "types"; "values" ] values

        annotations location values,
        optionalMap location "types" typeSpecification values,
        optionalMap location "values" valueSpecification values

    let private moduleSpecification location element : ModuleSpecification =
        let specification =
            documented moduleSpecificationMembers notAnObject location element

        let annotations, types, values = specification.Value

        {
            Annotations = annotations
            Types = types
            Values = values
            Doc = specification.Doc
        }

    let private describeModule (ModulePath path) = CanonicalNames.pathToString path
    let private describePackage (PackageName path) = CanonicalNames.pathToString path

    let private modulesOf decode location element =
        let values = members location element
        closed location [ "modules" ] values

        optional location [ "modules" ] (orderedPathMap ModulePath describeModule decode) values
        |> Option.defaultValue OrderedMap.empty

    let private packageDefinition location element : PackageDefinition = {
        Modules = modulesOf controlledModuleDefinition location element
    }

    let private packageSpecification location element : PackageSpecification = {
        Modules = modulesOf moduleSpecification location element
    }

    let private dependenciesOf decode location values =
        optional
            location
            [ "dependencies" ]
            (orderedPathMap PackageName describePackage decode)
            values
        |> Option.defaultValue OrderedMap.empty

    let private entryPointKind location element =
        match text location element with
        | "main" -> Main
        | "command" -> Command
        | "handler" -> Handler
        | "job" -> Job
        | "policy" -> Policy
        | other -> fail "invalid_type" location $"'{other}' is not an entry point kind."

    let private entryPoint location element : EntryPoint =
        let values = members location element
        closed location [ "target"; "kind"; "doc" ] values

        {
            Target = required location [ "target" ] fqName values
            Kind = required location [ "kind" ] entryPointKind values
            Doc = optional location [ "doc" ] text values
        }

    let private entryPoints location element =
        members location element
        |> List.map (fun (key, item) -> key, entryPoint (memberAt location key) item)

    let private packageNameOf location values =
        PackageName(required location [ "packageName" ] path values)

    let private emptyDefinition: PackageDefinition = { Modules = OrderedMap.empty }
    let private emptySpecification: PackageSpecification = { Modules = OrderedMap.empty }

    let private distribution location (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.Object
        then
            fail
                "invalid_distribution_shape"
                location
                "A distribution is an object keyed by its kind."

        let tag, payload =
            tagWith
                "invalid_distribution_shape"
                location
                [ "Library"; "Specs"; "Application" ]
                (members location element)

        let here = memberAt location tag
        let body = members here payload

        let definition () =
            optional here [ "def" ] packageDefinition body
            |> Option.defaultValue emptyDefinition

        match tag with
        | "Library" ->
            closed here [ "packageName"; "dependencies"; "def" ] body

            Library(
                packageNameOf here body,
                dependenciesOf packageSpecification here body,
                definition ()
            )
        | "Specs" ->
            closed here [ "packageName"; "dependencies"; "spec" ] body

            Specs(
                packageNameOf here body,
                dependenciesOf packageSpecification here body,
                optional here [ "spec" ] packageSpecification body
                |> Option.defaultValue emptySpecification
            )
        | "Application" ->
            closed here [ "packageName"; "dependencies"; "def"; "entryPoints" ] body

            Application(
                packageNameOf here body,
                dependenciesOf packageDefinition here body,
                definition (),
                required here [ "entryPoints" ] entryPoints body
            )
        | other ->
            fail "invalid_distribution_shape" location $"'{other}' is not a distribution kind."

    let private maxComponent = BigInteger 4294967295L
    let private syntaxRule = "invalid_format_version_syntax"
    let private rangeRule = "format_version_out_of_range"
    let private unsupportedMajorRule = "unsupported_format_version_major"

    /// Reads one release component, which has no sign and no leading zero.
    let private releaseComponent location (input: string) =
        let isDigits =
            input
            <> ""
            && input
               |> Seq.forall Char.IsAsciiDigit

        if
            not isDigits
            || (input.Length > 1
                && input[0] = '0')
        then
            fail syntaxRule location $"'{input}' is not a release component."

        let number = BigInteger.Parse input

        if number > maxComponent then
            fail rangeRule location $"'{input}' exceeds the unsigned 32-bit range."

        number

    /// Normalizes a format version to its exact release, following the shared format version contract.
    let private formatVersionValue location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Number ->
            let lexeme = element.GetRawText()

            if
                not (
                    lexeme
                    |> Seq.forall Char.IsAsciiDigit
                )
            then
                fail
                    "invalid_format_version_type"
                    location
                    "A format version integer is unsigned and whole."

            match BigInteger.Parse lexeme with
            | number when number > maxComponent ->
                fail rangeRule location "The version exceeds the unsigned 32-bit range."
            | number when number.IsZero ->
                fail syntaxRule location "Integer 0 is not a format family."
            | number when number = BigInteger 4 -> "4.0.0"
            | _ -> fail unsupportedMajorRule location "This reader supports IR version 4."
        | JsonValueKind.String ->
            let release = stringOf syntaxRule location element

            match release.Split('.') with
            | [| major; minor; patch |] ->
                let majorNumber = releaseComponent location major
                let minorNumber = releaseComponent location minor

                releaseComponent location patch
                |> ignore

                if majorNumber < BigInteger 3 then
                    fail syntaxRule location "Release strings start at major version 3."

                if
                    majorNumber
                    <> BigInteger 4
                then
                    fail unsupportedMajorRule location "This reader supports IR version 4."

                if not minorNumber.IsZero then
                    fail
                        "unsupported_format_version_minor"
                        location
                        "This reader supports releases 4.0.x."

                release
            | _ -> fail syntaxRule location $"'{release}' is not an exact release."
        | _ ->
            fail
                "invalid_format_version_type"
                location
                "A format version is an integer or a release string."

    /// Checks the format version before any v4 decoding, so a document of another version gets a version
    /// diagnostic rather than an incidental one.
    let private formatVersion (values: Members) =
        match
            values
            |> List.filter (fun (key, _) -> key = "formatVersion")
        with
        | [] -> fail "missing_format_version" "$" "The document states no formatVersion."
        | [ _, element ] -> formatVersionValue (memberAt "$" "formatVersion") element
        | _ ->
            fail
                "duplicate_format_version"
                (memberAt "$" "formatVersion")
                "The member 'formatVersion' appears more than once."

    /// Reads a complete document to its normalized format version and distribution.
    let document (root: JsonElement) =
        let values = members "$" root
        let version = formatVersion values
        checkDuplicates "$" root
        closed "$" [ "formatVersion"; "distribution" ] values
        version, required "$" [ "distribution" ] distribution values
