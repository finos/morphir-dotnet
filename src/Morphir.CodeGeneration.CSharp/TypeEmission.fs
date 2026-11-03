// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open System
open System.Threading
open Morphir.IR
open Morphir.IR.Model

/// Private target types. Neither model family is converted into the other.
module internal TypeEmission =
    type Shape =
        | Parameter of Name
        | Named of FQName * Shape list
        | Product of Shape list
        | Fields of (Name * Shape) list
        | Arrow of Shape * Shape
        | Unit

    type Definition =
        | Alias of Shape
        | Cases of bool * (Name * (Name * Shape) list) list

    type Declaration = {
        Name: FQName
        Parameters: Name list
        Public: bool
        Synthetic: bool
        Definition: Definition
        Location: string
    }

    let validateName location (Name words as name) =
        if
            List.isEmpty words
            || words
               |> List.exists (fun word ->
                   String.IsNullOrEmpty word
                   || word
                      |> Seq.exists (fun c -> not (Char.IsAsciiLetterOrDigit c))
               )
        then
            Admission.fail
                "invalid-name"
                location
                ("Invalid Morphir name "
                 + sprintf "%A" name)

    let uniqueNames location names =
        names
        |> List.iter (fun (name, _) -> validateName location name)

        names
        |> List.groupBy snd
        |> List.iter (fun (target, owners) ->
            if owners.Length > 1 then
                Admission.fail
                    "name-collision"
                    location
                    (target
                     + " is shared by "
                     + (owners
                        |> List.map (fun (name, _) -> sprintf "%A" name)
                        |> String.concat " and "))
        )

    let protocolNames = [
        "MorphirEquals"
        "MorphirHashCode"
        "Equals"
        "GetHashCode"
        "GetType"
        "ToString"
        "ReferenceEquals"
        "MemberwiseClone"
        "Finalize"
    ]

    let title (Name words) =
        let text =
            words
            |> List.map (fun word ->
                if word = "" then
                    ""
                else
                    word.Substring(0, 1).ToUpperInvariant()
                    + word.Substring(1)
            )
            |> String.concat ""

        if
            text.Length > 0
            && Char.IsAsciiDigit text[0]
        then
            "_" + text
        else
            text

    let identifier name =
        "@"
        + title name

    let generic name =
        "T"
        + title name

    let packageName (PackageName(Path names)) =
        names
        |> List.map identifier
        |> String.concat "."

    let moduleName (ModulePath(Path names)) =
        "@"
        + (names
           |> List.map title
           |> String.concat "_")
        + "Module"

    let sourcePath pkg path =
        "p_"
        + (packageName pkg).Replace("@", "")
        + "/m_"
        + (moduleName path).TrimStart('@')
        + ".g.cs"

    let validateModules pkg modules =
        let (PackageName(Path package)) = pkg

        if List.isEmpty package then
            Admission.fail "invalid-name" "$package" "The package path is empty."

        package
        |> List.iter (validateName "$package")

        let paths =
            Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        for path, values in modules do
            let (ModulePath(Path parts)) = path

            let loc =
                "$modules/"
                + sprintf "%A" path

            if List.isEmpty parts then
                Admission.fail "invalid-name" loc "The module path is empty."

            parts
            |> List.iter (validateName loc)

            uniqueNames
                loc
                (values
                 |> List.map (fun name -> name, identifier name))

            for name in values do
                if
                    identifier name = moduleName path
                    || List.contains (title name) protocolNames
                then
                    Admission.fail
                        "name-collision"
                        loc
                        (title name
                         + " shares the generated module class or inherited CLR member identity.")

            let relative = sourcePath pkg path

            if
                relative.Split('/')
                |> Array.exists (fun part -> part.Length > 255)
            then
                Admission.fail "path-length" loc "A portable path component exceeds 255 characters."

            match paths.TryGetValue relative with
            | true, previous ->
                Admission.fail
                    "path-collision"
                    loc
                    (previous
                     + " and "
                     + relative
                     + " share a portable path identity.")
            | _ -> paths.Add(relative, relative)

    let qualified name =
        "global::"
        + packageName name.PackagePath
        + "."
        + moduleName name.ModulePath
        + "."
        + identifier name.LocalName

    let sdk (name: FQName) =
        let compact (Name words) =
            String.concat "" words
            |> fun s -> s.ToLowerInvariant()

        let (PackageName(Path package)) = name.PackagePath
        let (ModulePath(Path modul)) = name.ModulePath

        if List.map compact package = [ "morphir"; "sdk" ] then
            Some(String.concat "." (List.map compact modul), compact name.LocalName)
        else
            None

    let parameters names =
        if List.isEmpty names then
            ""
        else
            "<"
            + String.concat ", " (List.map generic names)
            + ">"

    let constraints names =
        names
        |> List.map (fun name ->
            " where "
            + generic name
            + " : notnull"
        )
        |> String.concat ""

    let arguments render args =
        if List.isEmpty args then
            ""
        else
            "<"
            + String.concat ", " (List.map render args)
            + ">"

    let rec classicType (ct: CancellationToken) location value =
        ct.ThrowIfCancellationRequested()
        let next = classicType ct location

        match value with
        | Morphir.IR.Classic.Type.Variable(_, name) -> Parameter name
        | Morphir.IR.Classic.Type.Reference(_, name, args) -> Named(name, List.map next args)
        | Morphir.IR.Classic.Type.Tuple(_, items) -> Product(List.map next items)
        | Morphir.IR.Classic.Type.Record(_, fields) ->
            Fields(
                fields
                |> List.map (fun f -> f.Name, next f.Type)
            )
        | Morphir.IR.Classic.Type.ExtensibleRecord _ ->
            Admission.fail "open-record" location "Open records have no closed C# representation."
        | Morphir.IR.Classic.Type.Function(_, input, output) -> Arrow(next input, next output)
        | Morphir.IR.Classic.Type.Unit _ -> Unit

    let rec v4Type (ct: CancellationToken) location value =
        ct.ThrowIfCancellationRequested()

        let attributes =
            match value with
            | Morphir.IR.V4.Type.Variable(a, _)
            | Morphir.IR.V4.Type.Reference(a, _, _)
            | Morphir.IR.V4.Type.Tuple(a, _)
            | Morphir.IR.V4.Type.Record(a, _)
            | Morphir.IR.V4.Type.ExtensibleRecord(a, _, _)
            | Morphir.IR.V4.Type.Function(a, _, _)
            | Morphir.IR.V4.Type.Unit a -> a

        match attributes.Constraints with
        | None -> ()
        | Some(Morphir.IR.V4.DocumentObject fields) when Morphir.IR.V4.OrderedMap.count fields = 0 ->
            ()
        | _ ->
            Admission.fail
                "unsupported-constraints"
                location
                "Type constraints have no admitted C# mapping."

        let next = v4Type ct location

        match value with
        | Morphir.IR.V4.Type.Variable(_, name) -> Parameter name
        | Morphir.IR.V4.Type.Reference(_, name, args) -> Named(name, List.map next args)
        | Morphir.IR.V4.Type.Tuple(_, items) -> Product(List.map next items)
        | Morphir.IR.V4.Type.Record(_, fields) ->
            Fields(
                fields
                |> List.map (fun (name, t) -> name, next t)
            )
        | Morphir.IR.V4.Type.ExtensibleRecord _ ->
            Admission.fail "open-record" location "Open records have no closed C# representation."
        | Morphir.IR.V4.Type.Function(_, input, output) -> Arrow(next input, next output)
        | Morphir.IR.V4.Type.Unit _ -> Unit

    let private declarations (model: VersionedModel) ct =
        let location path name =
            "$modules/"
            + moduleName path
            + "/types/"
            + title name

        match model.Distribution with
        | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, _, definition)) -> [
            for path, modul in Map.toList definition.Modules do
                for name, controlled in Map.toList modul.Value.Types do
                    let loc = location path name
                    let typ = classicType ct loc

                    let publicType =
                        modul.Access = Morphir.IR.Classic.AccessControlled.Public
                        && controlled.Access = Morphir.IR.Classic.AccessControlled.Public

                    let ps, body =
                        match Morphir.IR.Classic.Documented.value controlled.Value with
                        | Morphir.IR.Classic.Type.TypeAliasDefinition(ps, body) ->
                            ps, Alias(typ body)
                        | Morphir.IR.Classic.Type.CustomTypeDefinition(ps, cases) ->
                            ps,
                            Cases(
                                cases.Access = Morphir.IR.Classic.AccessControlled.Public,
                                Map.toList cases.Value
                                |> List.map (fun (name, fields) ->
                                    name,
                                    fields
                                    |> List.map (fun (name, t) -> name, typ t)
                                )
                            )

                    yield {
                        Name = FQName.fqName pkg path name
                        Parameters = ps
                        Public = publicType
                        Synthetic = false
                        Definition = body
                        Location = loc
                    }
          ]
        | DistributionModel.V4(Morphir.IR.V4.Library(pkg, _, definition)) -> [
            for path, modul in
                Morphir.IR.V4.OrderedMap.toList definition.Modules
                |> List.sortBy fst do
                for name, controlled in
                    Morphir.IR.V4.OrderedMap.toList modul.Value.Types
                    |> List.sortBy fst do
                    let loc = location path name
                    let typ = v4Type ct loc

                    let publicType =
                        modul.Access = Morphir.IR.V4.Public
                        && controlled.Access = Morphir.IR.V4.Public

                    let ps, body =
                        match controlled.Value.Value with
                        | Morphir.IR.V4.TypeAliasDefinition(ps, body) -> ps, Alias(typ body)
                        | Morphir.IR.V4.CustomTypeDefinition(ps, access, cases) ->
                            ps,
                            Cases(
                                (access = Morphir.IR.V4.Public),
                                Morphir.IR.V4.OrderedMap.toList cases
                                |> List.sortBy fst
                                |> List.map (fun (name, fields) ->
                                    name,
                                    fields
                                    |> List.map (fun (name, t) -> name, typ t)
                                )
                            )
                        | Morphir.IR.V4.IncompleteTypeDefinition _ ->
                            Admission.fail
                                "unsupported-body"
                                loc
                                "An incomplete type has no implementation."

                    yield {
                        Name = FQName.fqName pkg path name
                        Parameters = ps
                        Public = publicType
                        Synthetic = false
                        Definition = body
                        Location = loc
                    }
          ]
        | _ -> []

    let rec substitute mapping =
        function
        | Parameter name as original ->
            Map.tryFind name mapping
            |> Option.defaultValue original
        | Named(name, args) -> Named(name, List.map (substitute mapping) args)
        | Product items -> Product(List.map (substitute mapping) items)
        | Fields fields ->
            Fields(
                fields
                |> List.map (fun (name, t) -> name, substitute mapping t)
            )
        | Arrow(input, output) -> Arrow(substitute mapping input, substitute mapping output)
        | Unit -> Unit

    let rec expand catalog visiting location shape =
        let next = expand catalog visiting location

        match shape with
        | Parameter _ -> shape
        | Named(name, args) ->
            match Map.tryFind name catalog with
            | Some {
                       Definition = Alias body
                       Parameters = ps
                   } ->
                if
                    ps.Length
                    <> args.Length
                then
                    Admission.fail
                        "type-arity"
                        location
                        ("Incorrect type argument count for "
                         + qualified name)

                if Set.contains name visiting then
                    Admission.fail
                        "recursive-alias"
                        location
                        ("Cyclic alias "
                         + qualified name)

                expand
                    catalog
                    (Set.add name visiting)
                    location
                    (substitute
                        (List.zip ps args
                         |> Map.ofList)
                        body)
            | _ -> Named(name, List.map next args)
        | Fields fields ->
            Fields(
                fields
                |> List.map (fun (name, t) -> name, next t)
            )
        | Product items -> Product(List.map next items)
        | Arrow(input, output) -> Arrow(next input, next output)
        | Unit -> Unit

    let rec freeParameters =
        function
        | Parameter name -> Set.singleton name
        | Named(_, args)
        | Product args ->
            args
            |> List.map freeParameters
            |> Set.unionMany
        | Fields fields ->
            fields
            |> List.map (
                snd
                >> freeParameters
            )
            |> Set.unionMany
        | Arrow(input, output) -> Set.union (freeParameters input) (freeParameters output)
        | Unit -> Set.empty

    let private recordParameters catalog declaration body =
        let used =
            expand catalog (Set.singleton declaration.Name) declaration.Location body
            |> freeParameters

        if not (Set.isSubset used (Set.ofList declaration.Parameters)) then
            Admission.fail
                "unbound-type"
                declaration.Location
                "A record contains an unbound type parameter."

        declaration.Parameters
        |> List.filter (fun name -> Set.contains name used)

    let private matchShape parameters template actual =
        let rec matched bindings template actual =
            let many bindings left right =
                if
                    List.length left
                    <> List.length right
                then
                    None
                else
                    List.zip left right
                    |> List.fold
                        (fun state (left, right) ->
                            Option.bind (fun bindings -> matched bindings left right) state
                        )
                        (Some bindings)

            match template, actual with
            | Parameter name, _ when List.contains name parameters ->
                match Map.tryFind name bindings with
                | Some previous when
                    previous
                    <> actual
                    ->
                    None
                | _ -> Some(Map.add name actual bindings)
            | Parameter a, Parameter b when a = b -> Some bindings
            | Named(a, left), Named(b, right) when a = b -> many bindings left right
            | Product left, Product right -> many bindings left right
            | Fields left, Fields right when List.map fst left = List.map fst right ->
                many bindings (List.map snd left) (List.map snd right)
            | Arrow(a, b), Arrow(c, d) ->
                matched bindings a c
                |> Option.bind (fun bindings -> matched bindings b d)
            | Unit, Unit -> Some bindings
            | _ -> None

        matched Map.empty template actual

    let recordTarget (catalog: Map<FQName, Declaration>) location shape =
        let actual = expand catalog Set.empty location shape

        catalog
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun candidate -> not candidate.Public, candidate.Synthetic, candidate.Name)
        |> List.tryPick (fun candidate ->
            match candidate.Definition with
            | Alias(Fields _ as body) ->
                let parameters = recordParameters catalog candidate body

                let template =
                    expand catalog (Set.singleton candidate.Name) candidate.Location body

                matchShape parameters template actual
                |> Option.map (fun bindings ->
                    candidate,
                    parameters
                    |> List.map (fun name -> Map.find name bindings)
                )
            | _ -> None
        )
        |> Option.defaultWith (fun () ->
            Admission.fail
                "anonymous-record"
                location
                "This closed record requires a named shape allocation."
        )

    let rec render
        (catalog: Map<FQName, Declaration>)
        (ct: CancellationToken)
        location
        bound
        visiting
        shape
        =
        ct.ThrowIfCancellationRequested()
        let next = render catalog ct location bound visiting
        let runtime = "global::Morphir.SDK.CSharp."

        match shape with
        | Parameter name ->
            if not (List.contains name bound) then
                Admission.fail
                    "unbound-type"
                    location
                    ("Unbound type parameter "
                     + title name)

            generic name
        | Unit ->
            runtime
            + "Unit"
        | Arrow(input, output) ->
            "global::System.Func<"
            + next input
            + ", "
            + next output
            + ">"
        | Product items when
            items.Length
            >= 2
            ->
            "("
            + String.concat ", " (List.map next items)
            + ")"
        | Product _ ->
            Admission.fail "tuple-arity" location "A C# tuple requires at least two elements."
        | Fields _ ->
            let owner, args = recordTarget catalog location shape

            qualified owner.Name
            + arguments next args
        | Named(name, args) ->
            match sdk name with
            | Some(modul, local) ->
                let target, arity =
                    match modul, local with
                    | "basics", "int" -> "int", 0
                    | "basics", "float" -> "double", 0
                    | "basics", "bool" -> "bool", 0
                    | "string", "string" -> "string", 0
                    | "char", "char" -> "char", 0
                    | "decimal", "decimal" -> "decimal", 0
                    | "basics", "order" ->
                        runtime
                        + "Order",
                        0
                    | "list", "list" ->
                        runtime
                        + "List",
                        1
                    | "maybe", "maybe" ->
                        runtime
                        + "Maybe",
                        1
                    | "result", "result" ->
                        runtime
                        + "Result",
                        2
                    | "dict", "dict" ->
                        runtime
                        + "Dict",
                        2
                    | "set", "set" ->
                        runtime
                        + "Set",
                        1
                    | _ ->
                        Admission.fail
                            "unmapped-sdk-type"
                            location
                            ("No admitted SDK type: "
                             + FQName.toString name)

                if
                    args.Length
                    <> arity
                then
                    Admission.fail
                        "type-arity"
                        location
                        ("Incorrect type argument count for "
                         + FQName.toString name)

                target
                + arguments next args
            | None ->
                match Map.tryFind name catalog with
                | None ->
                    Admission.fail
                        "unresolved-type"
                        location
                        ("No implementation for "
                         + FQName.toString name)
                | Some declaration ->
                    if
                        declaration.Parameters.Length
                        <> args.Length
                    then
                        Admission.fail
                            "type-arity"
                            location
                            ("Incorrect type argument count for "
                             + FQName.toString name)

                    match declaration.Definition with
                    | Alias(Fields _ as body) ->
                        let concrete =
                            substitute
                                (List.zip declaration.Parameters args
                                 |> Map.ofList)
                                body

                        let owner, args = recordTarget catalog location concrete

                        qualified owner.Name
                        + arguments next args
                    | Cases _ ->
                        qualified name
                        + arguments next args
                    | Alias body ->
                        if Set.contains name visiting then
                            Admission.fail
                                "recursive-alias"
                                location
                                ("Cyclic alias "
                                 + FQName.toString name)

                        let body =
                            substitute
                                (List.zip declaration.Parameters args
                                 |> Map.ofList)
                                body

                        render catalog ct location bound (Set.add name visiting) body

    let private protocol name fields isOverride =
        let modifier = if isOverride then "override " else ""

        let equality =
            fields
            |> List.map (fun field ->
                " && global::Morphir.SDK.CSharp.Basics.Equal("
                + field
                + ", otherValue."
                + field
                + ")"
            )
            |> String.concat ""

        let payload =
            match fields with
            | [] -> "global::Morphir.SDK.CSharp.Unit.Value"
            | [ field ] -> field
            | _ ->
                "("
                + String.concat ", " fields
                + ")"

        "public "
        + modifier
        + "bool MorphirEquals(global::Morphir.SDK.CSharp.IMorphirValue other) => other is "
        + name
        + " otherValue"
        + equality
        + ";\n"
        + "public "
        + modifier
        + "int MorphirHashCode() => global::Morphir.SDK.CSharp.MorphirValue.Hash("
        + payload
        + ");\n"
        + "public bool Equals("
        + name
        + "? other) => other is not null && MorphirEquals(other);\n"
        + "public override bool Equals(object? other) => other is "
        + name
        + " otherValue && MorphirEquals(otherValue);\n"
        + "public override int GetHashCode() => MorphirHashCode();\n"
        + "public static bool operator ==("
        + name
        + "? left, "
        + name
        + "? right) => left is null ? right is null : right is not null && left.MorphirEquals(right);\n"
        + "public static bool operator !=("
        + name
        + "? left, "
        + name
        + "? right) => !(left == right);\n"

    let private valueClass catalog ct declaration name baseType access fields =
        let typ = render catalog ct declaration.Location declaration.Parameters Set.empty

        let fieldsText =
            fields
            |> List.map (fun (field, t) ->
                "public "
                + typ t
                + " "
                + identifier field
                + " { get; }\n"
            )
            |> String.concat ""

        let inputs =
            fields
            |> List.mapi (fun index (_, t) ->
                typ t
                + " p"
                + string index
            )
            |> String.concat ", "

        let assignments =
            fields
            |> List.mapi (fun index (field, _) ->
                "global::System.ArgumentNullException.ThrowIfNull(p"
                + string index
                + "); "
                + identifier field
                + " = p"
                + string index
                + ";\n"
            )
            |> String.concat ""

        let inheritance =
            if baseType = "" then
                "global::Morphir.SDK.CSharp.IMorphirValue"
            else
                baseType

        let effectiveParameters =
            match declaration.Definition with
            | Alias(Fields _ as body) -> recordParameters catalog declaration body
            | _ -> declaration.Parameters

        let className =
            name
            + (if baseType = "" then parameters effectiveParameters else "")

        access
        + " sealed class "
        + className
        + " : "
        + inheritance
        + ", global::System.IEquatable<"
        + className
        + ">"
        + (if baseType = "" then
               constraints effectiveParameters
           else
               "")
        + "\n{\n"
        + fieldsText
        + "public "
        + name
        + "("
        + inputs
        + ")\n{\n"
        + assignments
        + "}\n"
        + protocol
            className
            (fields
             |> List.map (
                 fst
                 >> identifier
             ))
            (baseType
             <> "")
        + "}\n"

    let private nestedRecords (ct: CancellationToken) declaration =
        let rec collect path shape =
            ct.ThrowIfCancellationRequested()

            let descend segment value =
                collect
                    (path
                     @ [ segment ])
                    value

            match shape with
            | Fields fields ->
                let local =
                    Name [
                        "__MorphirRecord_"
                        + title declaration.Name.LocalName
                        + "_"
                        + String.concat "_" path
                    ]

                { declaration with
                    Name =
                        { declaration.Name with
                            LocalName = local
                        }
                    Definition = Alias shape
                    Synthetic = true
                    Location =
                        declaration.Location
                        + "/"
                        + String.concat "/" path
                }
                :: (fields
                    |> List.collect (fun (name, t) -> descend (title name) t))
            | Named(_, args)
            | Product args ->
                args
                |> List.mapi (fun index t ->
                    descend
                        ("Item"
                         + string index)
                        t
                )
                |> List.concat
            | Arrow(input, output) ->
                descend "Input" input
                @ descend "Output" output
            | Parameter _
            | Unit -> []

        match declaration.Definition with
        | Alias(Fields fields) ->
            fields
            |> List.collect (fun (name, t) -> collect [ title name ] t)
        | Alias body -> collect [ "Value" ] body
        | Cases(publicCases, cases) ->
            cases
            |> List.collect (fun (case, fields) ->
                fields
                |> List.collect (fun (field, t) -> collect [ title case; title field ] t)
            )
            |> List.map (fun helper ->
                { helper with
                    Public =
                        helper.Public
                        && publicCases
                }
            )

    let rec private validatePublicType catalog location =
        function
        | Parameter _
        | Unit -> ()
        | Arrow(input, output) ->
            validatePublicType catalog location input
            validatePublicType catalog location output
        | Fields fields ->
            fields
            |> List.iter (
                snd
                >> validatePublicType catalog location
            )
        | Product items ->
            items
            |> List.iter (validatePublicType catalog location)
        | Named(name, args) ->
            match Map.tryFind name catalog with
            | Some declaration when not declaration.Public ->
                Admission.fail
                    "private-type-exposure"
                    location
                    ("Public payload exposes private type "
                     + qualified name)
            | _ -> ()

            args
            |> List.iter (validatePublicType catalog location)

    let prepare model ct =
        let raw = declarations model ct

        let checkFields location className fields =
            uniqueNames
                location
                (fields
                 |> List.map (fun (name, _) -> name, identifier name))

            for name, _ in fields do
                if
                    List.contains (title name) [
                        className
                        "MorphirEquals"
                        "MorphirHashCode"
                        "Equals"
                        "GetHashCode"
                        "GetType"
                        "ToString"
                    ]
                then
                    Admission.fail
                        "name-collision"
                        location
                        (title name
                         + " collides with the generated "
                         + className
                         + " protocol or constructor.")

        for declaration in raw do
            validateName declaration.Location declaration.Name.LocalName

            uniqueNames
                declaration.Location
                (declaration.Parameters
                 |> List.map (fun name -> name, generic name))

            match declaration.Definition with
            | Alias(Fields fields) ->
                checkFields declaration.Location (title declaration.Name.LocalName) fields
            | Alias _ -> ()
            | Cases(_, cases) ->
                uniqueNames
                    declaration.Location
                    (cases
                     |> List.map (fun (name, _) -> name, identifier name))

                for name, fields in cases do
                    if
                        List.contains
                            (title name)
                            ([
                                title declaration.Name.LocalName
                                "MorphirEquals"
                                "MorphirHashCode"
                                "Equals"
                                "GetHashCode"
                                "GetType"
                                "ToString"
                             ]
                             @ (List.map generic declaration.Parameters))
                    then
                        Admission.fail
                            "name-collision"
                            declaration.Location
                            "A case shares a generated protocol, generic parameter or enclosing type identity."

                    checkFields declaration.Location (title name) fields

        let rawCatalog =
            raw
            |> List.map (fun declaration -> declaration.Name, declaration)
            |> Map.ofList

        let original =
            raw
            |> List.map (fun declaration ->
                let expanded =
                    expand rawCatalog (Set.singleton declaration.Name) declaration.Location

                let body =
                    match declaration.Definition with
                    | Alias body -> Alias(expanded body)
                    | Cases(access, cases) ->
                        Cases(
                            access,
                            cases
                            |> List.map (fun (name, fields) ->
                                name,
                                fields
                                |> List.map (fun (name, t) -> name, expanded t)
                            )
                        )

                { declaration with Definition = body }
            )

        let declarations =
            original
            @ (original
               |> List.collect (nestedRecords ct))

        declarations
        |> List.groupBy (fun d -> d.Name.ModulePath, identifier d.Name.LocalName)
        |> List.iter (fun (_, owners) ->
            if owners.Length > 1 then
                Admission.fail
                    "name-collision"
                    owners.Head.Location
                    ("C# type identity is shared by "
                     + (owners
                        |> List.map (fun d -> sprintf "%A" d.Name.LocalName)
                        |> String.concat " and "))
        )

        let catalog =
            declarations
            |> List.map (fun d -> d.Name, d)
            |> Map.ofList

        for declaration in declarations do
            let emitted =
                match declaration.Definition with
                | Cases _ -> true
                | Alias(Fields _ as body) ->
                    let owner, _ = recordTarget catalog declaration.Location body in
                    owner.Name = declaration.Name
                | _ -> false

            let genericNames =
                declaration.Parameters
                |> List.map generic

            if
                emitted
                && (identifier declaration.Name.LocalName = moduleName declaration.Name.ModulePath
                    || List.contains
                        (title declaration.Name.LocalName)
                        (genericNames
                         @ protocolNames))
            then
                Admission.fail
                    "name-collision"
                    declaration.Location
                    "A generated type shares its module or generic parameter identity."

            let genericFieldNames fields =
                for name, _ in fields do
                    if List.contains (title name) genericNames then
                        Admission.fail
                            "name-collision"
                            declaration.Location
                            (title name
                             + " shares a generic parameter and payload identity.")

            match declaration.Definition with
            | Alias(Fields fields) ->
                genericFieldNames fields
                checkFields declaration.Location (title declaration.Name.LocalName) fields
            | Cases(_, cases) ->
                cases
                |> List.iter (fun (name, fields) ->
                    genericFieldNames fields
                    checkFields declaration.Location (title name) fields
                )
            | _ -> ()

            if declaration.Public then
                match declaration.Definition with
                | Alias body -> validatePublicType catalog declaration.Location body
                | Cases(true, cases) ->
                    cases
                    |> List.iter (fun (_, fields) ->
                        fields
                        |> List.iter (
                            snd
                            >> validatePublicType catalog declaration.Location
                        )
                    )
                | Cases(false, _) -> ()

        let modules =
            declarations
            |> List.groupBy (fun d -> d.Name.ModulePath)
            |> List.map (fun (path, items) ->
                let text =
                    items
                    |> List.map (fun declaration ->
                        ct.ThrowIfCancellationRequested()
                        let access = if declaration.Public then "public" else "internal"
                        let name = identifier declaration.Name.LocalName

                        match declaration.Definition with
                        | Alias(Fields fields) ->
                            let owner, _ =
                                recordTarget catalog declaration.Location (Fields fields)

                            if owner.Name = declaration.Name then
                                valueClass catalog ct declaration name "" access fields
                            else
                                ""
                        | Alias body ->
                            render
                                catalog
                                ct
                                declaration.Location
                                declaration.Parameters
                                Set.empty
                                body
                            |> ignore

                            ""
                        | Cases(publicCases, cases) ->
                            let fullName =
                                name
                                + parameters declaration.Parameters

                            let caseAccess = if publicCases then "public" else "internal"

                            access
                            + " abstract class "
                            + fullName
                            + " : global::Morphir.SDK.CSharp.IMorphirValue"
                            + ", global::System.IEquatable<"
                            + fullName
                            + ">"
                            + constraints declaration.Parameters
                            + "\n{\nprivate "
                            + name
                            + "() {}\n"
                            + "public abstract bool MorphirEquals(global::Morphir.SDK.CSharp.IMorphirValue other);\npublic abstract int MorphirHashCode();\n"
                            + "public bool Equals("
                            + fullName
                            + "? other) => other is not null && MorphirEquals(other);\n"
                            + "public override bool Equals(object? other) => other is "
                            + fullName
                            + " otherValue && MorphirEquals(otherValue);\n"
                            + "public override int GetHashCode() => MorphirHashCode();\n"
                            + "public static bool operator ==("
                            + fullName
                            + "? left, "
                            + fullName
                            + "? right) => left is null ? right is null : right is not null && left.MorphirEquals(right);\n"
                            + "public static bool operator !=("
                            + fullName
                            + "? left, "
                            + fullName
                            + "? right) => !(left == right);\n"
                            + (cases
                               |> List.map (fun (caseName, fields) ->
                                   valueClass
                                       catalog
                                       ct
                                       declaration
                                       (identifier caseName)
                                       fullName
                                       caseAccess
                                       fields
                               )
                               |> String.concat "")
                            + "}\n"
                    )
                    |> String.concat ""

                path, text
            )
            |> Map.ofList


        catalog, modules
