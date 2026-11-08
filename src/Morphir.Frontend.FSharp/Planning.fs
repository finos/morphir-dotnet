// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System
open System.Collections.Generic
open FSharp.Compiler.Symbols
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.Compiler.Xml
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Frontend.FSharp
open Morphir.IR

/// <summary>What the caller decides about the package (FE-1).</summary>
type internal PlanningRequest = {
    Package: PackageName
    /// <summary>None exposes every module. An empty list exposes no module.</summary>
    ExposedModules: string list option
}

/// <summary>One planned type definition, in source order.</summary>
type internal PlannedType = {
    Name: Name
    Access: Access
    Documentation: string option
    Location: Location
    Syntax: SynTypeDefn
    Entity: FSharpEntity
}

/// <summary>One planned module-level value, in source order.</summary>
type internal PlannedValue = {
    Name: Name
    Access: Access
    Documentation: string option
    Location: Location
    Syntax: SynBinding
    Symbol: FSharpMemberOrFunctionOrValue
}

/// <summary>One planned module: its family-neutral plan and its declarations in source order.</summary>
type internal PlannedModule = {
    Plan: ModulePlan
    /// <summary>The F# name of the module, for messages.</summary>
    FSharpName: string
    Types: PlannedType list
    Values: PlannedValue list
}

/// <summary>Keys that identify the package's own symbols across lookups in one checked project.</summary>
[<RequireQualifiedAccess>]
module internal PlanKeys =

    let private fullName (entity: FSharpEntity) =
        match entity.TryFullName with
        | Some name -> name
        | None -> $"{entity.AccessPath}.{entity.CompiledName}"

    /// <summary>The key of a type definition.</summary>
    let entity (entity: FSharpEntity) = $"type:{fullName entity}"

    /// <summary>The key of a module-level value.</summary>
    let value (value: FSharpMemberOrFunctionOrValue) =
        match value.DeclaringEntity with
        // The F# logical name: unique in its module, unlike a CompiledName that overloads can share.
        | Some owner -> $"value:{fullName owner}.{value.LogicalName}"
        | None -> $"value:{value.LogicalName}"

    /// <summary>The key of a record field.</summary>
    let field (record: FSharpEntity) (field: FSharpField) = $"field:{fullName record}.{field.Name}"

    /// <summary>The key of a union case.</summary>
    let case (case: FSharpUnionCase) =
        let owner = case.ReturnType.StripAbbreviations()

        if owner.HasTypeDefinition then
            $"case:{fullName owner.TypeDefinition}.{case.Name}"
        else
            $"case:{case.Name}"

/// <summary>
/// The family-neutral result of planning (FSF-1): the package plan, the modules in document and source order,
/// and the Morphir names of the package's own types, values and constructors. Lowering reads it for every
/// IR family, so whole-program decisions are made once.
/// </summary>
type internal FrontendPlan =
    {
        Package: PackagePlan
        Modules: PlannedModule list
        Names: IReadOnlyDictionary<string, FQName>
    }

    member private this.Find key =
        match this.Names.TryGetValue key with
        | true, name -> Some name
        | _ -> None

    /// <summary>The Morphir name of a type of this package.</summary>
    member this.TryType(entity: FSharpEntity) = this.Find(PlanKeys.entity entity)

    /// <summary>The Morphir name of a module-level value of this package.</summary>
    member this.TryValue(value: FSharpMemberOrFunctionOrValue) = this.Find(PlanKeys.value value)

    /// <summary>The Morphir name of a record field of this package (the local name of the result).</summary>
    member this.TryField(record: FSharpEntity, field: FSharpField) =
        this.Find(PlanKeys.field record field)
        |> Option.map (fun name -> name.LocalName)

    /// <summary>The Morphir name of a union case of this package.</summary>
    member this.TryConstructor(case: FSharpUnionCase) = this.Find(PlanKeys.case case)

/// <summary>
/// Planning (FSF-1): naming, module paths, access, exposed modules and declaration order. It runs once on the
/// checked program, after admission and evidence, and is the same for every IR family.
/// </summary>
[<RequireQualifiedAccess>]
module internal Planning =

    /// <summary>One F# module, or the namespace-level declarations of one namespace.</summary>
    type private Source = {
        Document: CheckedDocument
        Path: Ident list
        Range: range
        IsPrivate: bool
        Documentation: string option
        /// <summary>Each declaration with its document: namespace-level declarations can come from several.</summary>
        Declarations: (CheckedDocument * SynModuleDecl) list
        IsNamespace: bool
    }

    let private isPrivate (access: SynAccess option) =
        match access with
        | Some(SynAccess.Private _)
        | Some(SynAccess.Internal _) -> true
        | _ -> false

    let private documentation (xmlDoc: PreXmlDoc) =
        let lines =
            xmlDoc.ToXmlDoc(false, None).UnprocessedLines
            |> Array.map (fun line -> line.Trim())

        match (String.Join("\n", lines)).Trim() with
        | "" -> None
        | text -> Some text

    let private rangeOf (identifiers: Ident list) (fallback: range) =
        match identifiers with
        | [] -> fallback
        | first :: _ -> Range.unionRanges first.idRange (List.last identifiers).idRange

    let private hasMembers (declarations: SynModuleDecl list) =
        declarations
        |> List.exists (fun declaration ->
            match declaration with
            | SynModuleDecl.Types _
            | SynModuleDecl.Let _ -> true
            | _ -> false
        )

    let rec private nested
        (document: CheckedDocument)
        (parent: Ident list)
        (parentPrivate: bool)
        declarations
        =
        declarations
        |> List.collect (fun declaration ->
            match declaration with
            | SynModuleDecl.NestedModule(
                moduleInfo = SynComponentInfo(
                    longId = identifiers; accessibility = access; xmlDoc = xmlDoc)
                decls = inner
                range = range) ->
                let path =
                    parent
                    @ identifiers

                let hidden =
                    parentPrivate
                    || isPrivate access

                {
                    Document = document
                    Path = path
                    Range = rangeOf identifiers range
                    IsPrivate = hidden
                    Documentation = documentation xmlDoc
                    Declarations =
                        inner
                        |> List.map (fun declaration -> document, declaration)
                    IsNamespace = false
                }
                :: nested document path hidden inner
            | _ -> []
        )

    let private sources (document: CheckedDocument) =
        match document.Syntax with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = contents)) ->
            contents
            |> List.collect (fun
                                 (SynModuleOrNamespace(
                                     longId = identifiers
                                     kind = kind
                                     decls = declarations
                                     xmlDoc = xmlDoc
                                     accessibility = access
                                     range = range)) ->
                let hidden = isPrivate access

                let self =
                    match kind with
                    | SynModuleOrNamespaceKind.NamedModule
                    | SynModuleOrNamespaceKind.AnonModule -> [
                        {
                            Document = document
                            Path = identifiers
                            Range = rangeOf identifiers range
                            IsPrivate = hidden
                            Documentation = documentation xmlDoc
                            Declarations =
                                declarations
                                |> List.map (fun declaration -> document, declaration)
                            IsNamespace = false
                        }
                      ]
                    | SynModuleOrNamespaceKind.DeclaredNamespace
                    | SynModuleOrNamespaceKind.GlobalNamespace when hasMembers declarations -> [
                        {
                            Document = document
                            Path = identifiers
                            Range = rangeOf identifiers range
                            IsPrivate = hidden
                            Documentation = None
                            Declarations =
                                declarations
                                |> List.map (fun declaration -> document, declaration)
                            IsNamespace = true
                        }
                      ]
                    | _ -> []

                self
                @ nested document identifiers hidden declarations
            )
        | ParsedInput.SigFile _ -> []

    let private locate (document: CheckedDocument) (range: range) =
        Location.Create(document.Uri, CompilerEdge.rangeOf range)

    let private nameOf (identifiers: Ident list) =
        identifiers
        |> List.map (fun ident -> Name.fromString ident.idText)

    let private textOf (identifiers: Ident list) =
        identifiers
        |> List.map (fun ident -> ident.idText)
        |> String.concat "."

    let private hasWordCharacter (text: string) =
        text
        |> Seq.exists Char.IsLetterOrDigit

    let private morphirNameAttribute = "Morphir.MorphirNameAttribute"

    /// <summary>The value of a <c>[&lt;MorphirName&gt;]</c> attribute, if the declaration has one.</summary>
    let private declaredName (attributes: FSharpAttribute seq) =
        attributes
        |> Seq.tryPick (fun attribute ->
            if attribute.AttributeType.TryFullName = Some morphirNameAttribute then
                match List.ofSeq attribute.ConstructorArguments with
                | [ (_, (:? string as name)) ] -> Some name
                | _ -> Some ""
            else
                None
        )

    /// <summary>
    /// The Morphir name of a declaration: the <c>[&lt;MorphirName&gt;]</c> value in canonical syntax when the
    /// declaration has one, or else the words of the F# identifier. An error when neither gives a name.
    /// </summary>
    let private morphirName
        (document: CheckedDocument)
        (attributes: FSharpAttribute seq)
        (text: string)
        (range: range)
        =
        match declaredName attributes with
        | Some declared ->
            match CanonicalNames.parseName declared with
            | Ok name when not (Name.isEmpty name) -> Ok name
            | _ ->
                Error(
                    Diagnostic.error
                        FSharpCodes.invalidMorphirName
                        $"The Morphir name '{declared}' of '{text}' is not a canonical Morphir name. Use lowercase words joined with '-', for example 'triple-add'."
                    |> Diagnostic.at (locate document range)
                )
        | None ->

            let name = Name.fromString text

            if
                hasWordCharacter text
                && not (Name.isEmpty name)
            then
                Ok name
            else
                Error(
                    Diagnostic.error
                        FSharpCodes.unsupportedName
                        $"The F# name '{text}' gives no Morphir name. Morphir names are words: use a name with letters, or add [<Morphir.MorphirName(\"...\")>]."
                    |> Diagnostic.at (locate document range)
                )

    /// <summary>Reports each declaration whose Morphir name was already declared in the same scope.</summary>
    let private collisionsBy
        (words: 'Key -> string)
        (scope: string)
        (items: ('Key * Location * string) list)
        =
        let seen = Dictionary<'Key, Location * string>(HashIdentity.Structural)

        [
            for key, location, text in items do
                match seen.TryGetValue key with
                | true, (firstLocation, firstText) ->
                    Diagnostic.error
                        FSharpCodes.nameCollision
                        $"'{text}' and '{firstText}' have the same Morphir name ({words key}) in {scope}. Rename one of them."
                    |> Diagnostic.at location
                    |> Diagnostic.withRelated
                        {
                            Location = firstLocation
                            Message = $"'{firstText}' is declared here."
                        }
                | _ -> seen[key] <- (location, text)
        ]

    let private collisions scope (items: (Name * Location * string) list) =
        collisionsBy (fun name -> String.Join(" ", Name.toList name)) scope items

    let private definitionSymbol
        (evidence: CheckedEvidence)
        (document: CheckedDocument)
        (range: range)
        =
        evidence.SymbolsAt(document.Index, range)
        |> List.filter (fun symbolUse -> symbolUse.IsFromDefinition)
        |> List.map (fun symbolUse -> symbolUse.Symbol)

    let private accessOf (accessibility: FSharpAccessibility) =
        if
            accessibility.IsPrivate
            || accessibility.IsInternal
        then
            Access.Private
        else
            Access.Public

    let rec private headIdentifier (pattern: SynPat) =
        match pattern with
        | SynPat.Named(ident = SynIdent(ident, _)) -> Some ident
        | SynPat.LongIdent(longDotId = SynLongIdent(id = [ ident ])) -> Some ident
        | SynPat.Paren(pat = inner)
        | SynPat.Typed(pat = inner)
        | SynPat.Attrib(pat = inner) -> headIdentifier inner
        | _ -> None

    /// <summary>
    /// Reports generic parameters whose Morphir names collide, such as <c>'a</c> and <c>'A</c>. Morphir type
    /// variables are names, so the two would become one variable.
    /// </summary>
    let private parameterCollisions
        (document: CheckedDocument)
        (owner: string)
        (fallback: Location)
        (declared: SynTyparDecls option)
        (parameters: FSharpGenericParameter seq)
        =
        let ranges =
            match declared with
            | Some declarations ->
                declarations.TyparDecls
                |> List.map (fun (SynTyparDecl(typar = SynTypar(ident = ident))) ->
                    ident.idText, ident.idRange
                )
            | None -> []

        parameters
        |> Seq.map (fun parameter ->
            let location =
                match
                    ranges
                    |> List.tryFind (fun (text, _) -> text = parameter.Name)
                with
                | Some(_, range) -> locate document range
                | None -> fallback

            Name.fromString parameter.Name, location, $"'{parameter.Name}"
        )
        |> List.ofSeq
        |> collisions $"the type parameters of '{owner}'"

    type private ModuleResult = {
        Module: PlannedModule
        Names: (string * FQName) list
        Diagnostics: Diagnostic list
    }

    let private planModule
        (request: PlanningRequest)
        (evidence: CheckedEvidence)
        (modulePath: ModulePath)
        (access: Access)
        (source: Source)
        =
        let diagnostics = ResizeArray<Diagnostic>()
        let names = ResizeArray<string * FQName>()

        let fqName local =
            FQName.fqName request.Package modulePath local

        let constructorItems = ResizeArray<Name * Location * string>()

        let types = [
            for document, declaration in source.Declarations do
                match declaration with
                | SynModuleDecl.Types(typeDefns = definitions) ->
                    for definition in definitions do
                        let (SynTypeDefn(
                            typeInfo = SynComponentInfo(
                                longId = identifiers; xmlDoc = xmlDoc; typeParams = typeParameters)
                            typeRepr = representation)) =
                            definition

                        let ident = List.last identifiers

                        let entity =
                            definitionSymbol evidence document ident.idRange
                            |> List.tryPick (fun symbol ->
                                match symbol with
                                | :? FSharpEntity as entity -> Some entity
                                | _ -> None
                            )

                        let typeName =
                            entity
                            |> Option.map (fun entity ->
                                morphirName document entity.Attributes ident.idText ident.idRange
                            )
                            |> Option.defaultValue (Ok(Name.fromString ident.idText))

                        match typeName, entity with
                        | Error diagnostic, _ -> diagnostics.Add diagnostic
                        | Ok _, None ->
                            diagnostics.Add(
                                Diagnostic.error
                                    FSharpCodes.unsupportedName
                                    $"The type '{ident.idText}' has no checked symbol."
                                |> Diagnostic.at (locate document ident.idRange)
                            )
                        | Ok name, Some entity ->
                            names.Add(PlanKeys.entity entity, fqName name)

                            diagnostics.AddRange(
                                parameterCollisions
                                    document
                                    ident.idText
                                    (locate document ident.idRange)
                                    typeParameters
                                    entity.GenericParameters
                            )

                            match representation with
                            | SynTypeDefnRepr.Simple(
                                simpleRepr = SynTypeDefnSimpleRepr.Union(unionCases = cases)) ->
                                let checkedCases = List.ofSeq entity.UnionCases

                                for index, SynUnionCase(ident = SynIdent(caseIdent, _)) in
                                    List.indexed cases do
                                    match
                                        morphirName
                                            document
                                            (if index < checkedCases.Length then
                                                 checkedCases[index].Attributes :> seq<_>
                                             else
                                                 Seq.empty)
                                            caseIdent.idText
                                            caseIdent.idRange
                                    with
                                    | Error diagnostic -> diagnostics.Add diagnostic
                                    | Ok caseName ->
                                        constructorItems.Add(
                                            caseName,
                                            locate document caseIdent.idRange,
                                            caseIdent.idText
                                        )

                                        if index < checkedCases.Length then
                                            names.Add(
                                                PlanKeys.case checkedCases[index],
                                                fqName caseName
                                            )
                            | SynTypeDefnRepr.Simple(
                                simpleRepr = SynTypeDefnSimpleRepr.Record(recordFields = fields)) ->
                                let checkedFields = List.ofSeq entity.FSharpFields

                                let fieldItems = [
                                    for index, SynField(idOpt = fieldIdent) in List.indexed fields do
                                        match fieldIdent with
                                        | Some fieldIdent ->
                                            let checkedField = List.tryItem index checkedFields

                                            let attributes =
                                                match checkedField with
                                                | Some field ->
                                                    Seq.append
                                                        field.PropertyAttributes
                                                        field.FieldAttributes
                                                | None -> Seq.empty

                                            match
                                                morphirName
                                                    document
                                                    attributes
                                                    fieldIdent.idText
                                                    fieldIdent.idRange
                                            with
                                            | Ok fieldName ->
                                                match checkedField with
                                                | Some field ->
                                                    names.Add(
                                                        PlanKeys.field entity field,
                                                        fqName fieldName
                                                    )
                                                | None -> ()

                                                yield
                                                    fieldName,
                                                    locate document fieldIdent.idRange,
                                                    fieldIdent.idText
                                            | Error diagnostic -> diagnostics.Add diagnostic
                                        | None -> ()
                                ]

                                diagnostics.AddRange(
                                    collisions $"the record '{ident.idText}'" fieldItems
                                )
                            | _ -> ()

                            yield {
                                Name = name
                                Access = accessOf entity.Accessibility
                                Documentation = documentation xmlDoc
                                Location = locate document ident.idRange
                                Syntax = definition
                                Entity = entity
                            }
                | _ -> ()
        ]

        let values = [
            for document, declaration in source.Declarations do
                match declaration with
                | SynModuleDecl.Let(bindings = bindings) ->
                    for SynBinding(headPat = head; xmlDoc = xmlDoc) as binding in bindings do
                        match headIdentifier head with
                        | None ->
                            diagnostics.Add(
                                Diagnostic.error
                                    (DiagnosticCode.Create "fsharp.unsupported_declaration")
                                    "A module-level binding must name one value. Bind each value with its own 'let'."
                                |> Diagnostic.at (locate document head.Range)
                            )
                        | Some ident ->
                            let value =
                                definitionSymbol evidence document ident.idRange
                                |> List.tryPick (fun symbol ->
                                    match symbol with
                                    | :? FSharpMemberOrFunctionOrValue as value -> Some value
                                    | _ -> None
                                )

                            let text =
                                match value with
                                | Some value -> value.DisplayNameCore
                                | None -> ident.idText

                            let attributes =
                                match value with
                                | Some value -> value.Attributes :> seq<_>
                                | None -> Seq.empty

                            match morphirName document attributes text ident.idRange, value with
                            | Error diagnostic, _ -> diagnostics.Add diagnostic
                            | Ok _, None ->
                                diagnostics.Add(
                                    Diagnostic.error
                                        FSharpCodes.unsupportedName
                                        $"The value '{text}' has no checked symbol."
                                    |> Diagnostic.at (locate document ident.idRange)
                                )
                            | Ok name, Some value ->
                                names.Add(PlanKeys.value value, fqName name)

                                diagnostics.AddRange(
                                    parameterCollisions
                                        document
                                        value.DisplayNameCore
                                        (locate document ident.idRange)
                                        None
                                        value.GenericParameters
                                )

                                yield {
                                    Name = name
                                    Access = accessOf value.Accessibility
                                    Documentation = documentation xmlDoc
                                    Location = locate document ident.idRange
                                    Syntax = binding
                                    Symbol = value
                                }
                | _ -> ()
        ]

        let moduleName = textOf source.Path

        diagnostics.AddRange(
            collisions
                $"the types of module '{moduleName}'"
                (types
                 |> List.map (fun planned ->
                     planned.Name, planned.Location, planned.Entity.DisplayName
                 ))
        )

        diagnostics.AddRange(
            collisions
                $"the values of module '{moduleName}'"
                (values
                 |> List.map (fun planned ->
                     planned.Name, planned.Location, planned.Symbol.DisplayNameCore
                 ))
        )

        diagnostics.AddRange(
            collisions $"the constructors of module '{moduleName}'" (List.ofSeq constructorItems)
        )

        {
            Module = {
                Plan = {
                    Path = modulePath
                    Access = access
                    Documentation = source.Documentation
                    Location = locate source.Document source.Range
                }
                FSharpName = moduleName
                Types = types
                Values = values
            }
            Names = List.ofSeq names
            Diagnostics = List.ofSeq diagnostics
        }

    /// <summary>
    /// Plans the package: module paths with the package path removed, access from the exposed modules and F#
    /// visibility, Morphir names, collisions and declaration order. Every problem is reported together.
    /// </summary>
    let plan (request: PlanningRequest) (evidence: CheckedEvidence) : Checked<FrontendPlan> =
        let (PackageName packagePath) = request.Package
        let packageNames = Path.toList packagePath

        let packageText = CanonicalNames.pathToString packagePath

        // Namespace-level declarations of one namespace in several documents form one module.
        let all =
            evidence.Original.Documents
            |> List.collect sources
            |> List.fold
                (fun (merged: Source list) source ->
                    let earlier =
                        if source.IsNamespace then
                            merged
                            |> List.tryFindIndex (fun earlier ->
                                earlier.IsNamespace
                                // The same F# namespace (ordinal), not the same Morphir path: distinct
                                // namespaces with one Morphir path must reach the collision check.
                                && textOf earlier.Path = textOf source.Path
                            )
                        else
                            None

                    match earlier with
                    | Some index ->
                        merged
                        |> List.mapi (fun at earlier ->
                            if at = index then
                                { earlier with
                                    Declarations =
                                        earlier.Declarations
                                        @ source.Declarations
                                }
                            else
                                earlier
                        )
                    | None ->
                        merged
                        @ [ source ]
                )
                []

        // Each module segment takes its name from [<MorphirName>] on that module, when present.
        let segmentNames (source: Source) =
            source.Path
            |> List.map (fun ident ->
                let attributes =
                    definitionSymbol evidence source.Document ident.idRange
                    |> List.tryPick (fun symbol ->
                        match symbol with
                        | :? FSharpEntity as entity when entity.IsFSharpModule ->
                            Some entity.Attributes
                        | _ -> None
                    )

                match attributes with
                | Some attributes ->
                    morphirName source.Document attributes ident.idText ident.idRange
                | None -> Ok(Name.fromString ident.idText)
            )

        let named =
            all
            |> List.map (fun source ->
                let results = segmentNames source

                let errors =
                    results
                    |> List.choose (fun result ->
                        match result with
                        | Error diagnostic -> Some diagnostic
                        | Ok _ -> None
                    )

                source,
                (if errors.IsEmpty then
                     Ok(
                         results
                         |> List.choose (fun result ->
                             match result with
                             | Ok name -> Some name
                             | Error _ -> None
                         )
                     )
                 else
                     Error errors)
            )

        let invalidSegments =
            named
            |> List.collect (fun (_, result) ->
                match result with
                | Error errors -> errors
                | Ok _ -> []
            )

        let located =
            named
            |> List.choose (fun (source, result) ->
                match result with
                | Ok names -> Some(source, names)
                | Error _ -> None
            )
            |> List.map (fun (source, names) ->

                let relative =
                    if
                        names.Length > packageNames.Length
                        && List.take packageNames.Length names = packageNames
                    then
                        Some(Path(List.skip packageNames.Length names))
                    else
                        None

                source, relative
            )

        let outside =
            located
            |> List.choose (fun (source, relative) ->
                match relative with
                | Some _ -> None
                | None ->
                    Some(
                        Diagnostic.error
                            FSharpCodes.moduleOutsidePackage
                            $"The module '{textOf source.Path}' is not under the package '{packageText}'. Declare it in a namespace or module whose name starts with the package path."
                        |> Diagnostic.at (locate source.Document source.Range)
                    )
            )

        let inside =
            located
            |> List.choose (fun (source, relative) ->
                relative
                |> Option.map (fun path -> source, path)
            )

        let exposed =
            request.ExposedModules
            |> Option.map (List.map (fun text -> text, Path.fromString text))

        let unknown =
            match exposed with
            | None -> []
            | Some entries ->
                entries
                |> List.filter (fun (_, path) ->
                    not (
                        inside
                        |> List.exists (fun (_, modulePath) -> modulePath = path)
                    )
                )
                |> List.map (fun (text, _) ->
                    Diagnostic.error
                        CompileCodes.unknownExposedModule
                        $"The exposed module '{text}' matches no compiled module."
                )

        let results =
            inside
            |> List.map (fun (source, path) ->
                let access =
                    if source.IsPrivate then
                        Access.Private
                    else
                        match exposed with
                        | None -> Access.Public
                        | Some entries when
                            entries
                            |> List.exists (fun (_, exposedPath) -> exposedPath = path)
                            ->
                            Access.Public
                        | Some _ -> Access.Private

                planModule request evidence (ModulePath path) access source
            )

        let moduleCollisions =
            collisionsBy
                (fun (ModulePath path) -> CanonicalNames.pathToString path)
                $"the package '{packageText}'"
                (results
                 |> List.map (fun result ->
                     result.Module.Plan.Path, result.Module.Plan.Location, result.Module.FSharpName
                 ))

        let diagnostics =
            invalidSegments
            @ outside
            @ unknown
            @ moduleCollisions
            @ (results
               |> List.collect (fun result -> result.Diagnostics))
            |> CompilerEdge.order (
                evidence.Original.Documents
                |> List.map (fun document -> {
                    Uri = document.Uri
                    Text = document.Text
                })
            )

        let names = Dictionary<string, FQName>(StringComparer.Ordinal)

        for result in results do
            for key, name in result.Names do
                names[key] <- name

        Checked.ofDiagnostics
            {
                Package = {
                    Package = request.Package
                    Documentation = None
                }
                Modules =
                    results
                    |> List.map (fun result -> result.Module)
                Names = names
            }
            diagnostics
