// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open System.Text.RegularExpressions
open System.Threading
open Morphir.Knowledge

/// The reason a requested OKF version cannot be selected.
[<RequireQualifiedAccess>]
type OkfVersionError =
    /// No version spelling was supplied.
    | Missing
    /// This validator does not support the supplied spelling.
    | Unsupported of string

    override this.ToString() =
        match this with
        | OkfVersionError.Missing -> "An OKF version is required. Use 0.2 or 0.3."
        | OkfVersionError.Unsupported version -> $"OKF version '{version}' is unsupported. Use 0.2 or 0.3."

/// The names of OKF versions this validator can check for conformance.
[<RequireQualifiedAccess>]
type OkfVersion =
    | V0_2
    | V0_3

    /// The declaration and CLI spelling.
    member this.WireValue =
        match this with
        | V0_2 -> "0.2"
        | V0_3 -> "0.3"

    /// Parses an OKF version for F# callers.
    static member Parse(value: string | null) =
        match value with
        | "0.2" -> Ok OkfVersion.V0_2
        | "0.3" -> Ok OkfVersion.V0_3
        | null
        | "" -> Error OkfVersionError.Missing
        | unsupported -> Error(OkfVersionError.Unsupported unsupported)

    /// Parses an OKF version for C# callers.
    static member TryParse
        (
            value: string | null,
            [<Out; MaybeNullWhen(false)>] version: byref<OkfVersion>,
            [<Out; MaybeNullWhen(true)>] failure: byref<OkfVersionError>
        ) =
        match OkfVersion.Parse value with
        | Ok recognized ->
            version <- recognized
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            version <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Creates a version from its literal spelling.
    static member Create(literal: string) =
        match OkfVersion.Parse literal with
        | Ok version -> version
        | Error failure -> invalidArg (nameof literal) (failure.ToString())

    override this.ToString() = this.WireValue

/// Optional conventions applied alongside the OKF standard.
[<RequireQualifiedAccess>]
type BundlePolicy =
    /// Check OKF, with advisory findings for unresolved links.
    | Standard
    /// Also check the conventions of the Morphir knowledge base.
    | Morphir

    /// The report spelling.
    member this.WireValue =
        match this with
        | Standard -> "standard"
        | Morphir -> "morphir"

    override this.ToString() = this.WireValue

/// The result of checking the selected standard independently of local policy.
[<RequireQualifiedAccess>]
type BundleConformance =
    | Conformant
    | NonConformant
    | Unverified
    | NotChecked

    /// The report spelling.
    member this.WireValue =
        match this with
        | Conformant -> "conformant"
        | NonConformant -> "non-conformant"
        | Unverified -> "unverified"
        | NotChecked -> "not-checked"

    override this.ToString() = this.WireValue

/// The category of a bundle finding.
[<RequireQualifiedAccess>]
type BundleFindingOrigin =
    | Conformance
    | Advisory
    | MorphirPolicy
    | Input

    /// The report spelling.
    member this.WireValue =
        match this with
        | Conformance -> "conformance"
        | Advisory -> "advisory"
        | MorphirPolicy -> "morphir"
        | Input -> "input"

    override this.ToString() = this.WireValue

/// A zero-based line and UTF-16 character position.
[<Struct>]
type BundlePosition = {
    /// The zero-based line.
    Line: int
    /// The number of UTF-16 code units from the beginning of that line.
    Character: int
} with
    override this.ToString() = $"{this.Line + 1}:{this.Character + 1}"

/// A half-open range in a member document.
[<Sealed>]
type BundleRange internal (startPosition: BundlePosition, endPosition: BundlePosition) =
    /// The inclusive start.
    member _.Start = startPosition
    /// The exclusive end.
    member _.End = endPosition

    override _.ToString() = $"{startPosition}-{endPosition}"

/// A finding in a knowledge bundle or a single document.
[<Sealed>]
type BundleFinding internal
    (origin: BundleFindingOrigin, diagnostic: Diagnostic, path: string | null, range: BundleRange | null) =
    /// Which layer produced the finding.
    member _.Origin = origin
    /// The finding's severity.
    member _.Severity = diagnostic.Severity
    /// Stable identifier for the finding.
    member _.Code = diagnostic.Code
    /// Explanation of the problem.
    member _.Message = diagnostic.Message
    /// Offset range in the document, when available.
    member _.Span = diagnostic.Span
    /// The member path, if applicable.
    member _.PathOrNull = path
    /// The range as zero-based line and character pairs, if applicable.
    member _.RangeOrNull = range

    override _.ToString() = diagnostic.ToString()

/// The report of one bundle validation.
[<Sealed>]
type BundleValidationReport internal
    (
        version: OkfVersion option,
        declared: string option,
        policy: BundlePolicy,
        conformance: BundleConformance,
        exitCode: int,
        findings: ImmutableArray<BundleFinding>
    ) =
    /// The version used to apply rules, or null if rules could not run.
    member _.VersionOrNull : OkfVersion | null =
        match version with
        | Some checkedVersion -> checkedVersion
        | None -> null

    /// The version declared by the root index, or null when it has no declaration.
    member _.DeclaredVersionOrNull = Option.toObj declared
    /// The requested policy.
    member _.Policy = policy
    /// Conformance with the selected OKF version, independently of local policy.
    member _.Conformance = conformance
    /// Zero on success, one for error findings or two when the check cannot run.
    member _.ExitCode = exitCode
    /// Findings in deterministic order.
    member _.Findings = findings

    override _.ToString() = $"{conformance} ({findings.Length} findings)"

/// A concept's interpreted title, description, type and stable bundle-relative ID.
type BundleConcept = {
    /// The bundle-relative Markdown path without the .md suffix.
    Id: string
    /// The concept's declared OKF type.
    ConceptType: string
    /// The authored title, where present.
    Title: string option
    /// The authored description, where present.
    Description: string option
} with
    override this.ToString() = this.Id

/// The interpretation of a parsed Markdown member in the bundle layout.
[<RequireQualifiedAccess>]
type BundleDocumentView =
    | Concept of BundleConcept
    | Index
    | Log
    | Markdown

    override this.ToString() =
        match this with
        | Concept concept -> $"concept {concept.Id}"
        | Index -> "index"
        | Log -> "log"
        | Markdown -> "Markdown document"

/// The result of resolving a reference from a member.
[<RequireQualifiedAccess>]
type BundleTarget =
    | Concept of BundlePath
    | ReservedDocument of BundlePath
    | MarkdownDocument of BundlePath
    | Asset of BundlePath
    | Directory of BundlePath option
    | Unresolved
    | External

    override this.ToString() =
        match this with
        | Concept path
        | ReservedDocument path
        | MarkdownDocument path
        | Asset path
        | Directory(Some path) -> path.Value
        | Directory None -> "/"
        | Unresolved -> "unresolved"
        | External -> "external"

module private BundleAnalysis =
    let private coreNumeric =
        Regex(
            @"^[+-]?(?:[0-9]+(?:\.[0-9]*)?(?:[eE][+-]?[0-9]+)?|\.[0-9]+(?:[eE][+-]?[0-9]+)?|\.(?:inf|nan))$|^0(?:o[0-7]+|x[0-9a-f]+)$",
            RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase ||| RegexOptions.NonBacktracking
        )

    let yamlString (node: YamlNode) =
        match node.Kind, node.Tag with
        | YamlKind.Scalar scalar, Some ("tag:yaml.org,2002:str" | "!!str") -> Some scalar.Value
        | YamlKind.Scalar scalar, None ->
            let isCoreValue =
                match scalar.Value with
                | "" | "~" | "null" | "Null" | "NULL"
                | "true" | "True" | "TRUE" | "false" | "False" | "FALSE" -> true
                | value -> coreNumeric.IsMatch(value)

            if scalar.Style = YamlScalarStyle.Plain && isCoreValue then None
            else Some scalar.Value
        | _ -> None

    let nonemptyText node =
        yamlString node |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))

    let isAbsentValue (node: YamlNode) =
        match node.Kind, node.Tag with
        | YamlKind.Sequence items, _ -> items.IsEmpty
        | YamlKind.Scalar _, Some ("tag:yaml.org,2002:null" | "!!null") -> true
        | YamlKind.Scalar scalar, None when scalar.Style = YamlScalarStyle.Plain ->
            match scalar.Value with
            | "" | "~" | "null" | "Null" | "NULL" -> true
            | _ -> false
        | _ -> yamlString node = Some ""

    let private yamlAccess (document: OkfDocument) =
        let anchors = Dictionary<string, ResizeArray<YamlNode>>(StringComparer.Ordinal)

        let rec index (node: YamlNode) =
            match node.Anchor with
            | Some name ->
                match anchors.TryGetValue name with
                | true, previous -> previous.Add node
                | _ -> anchors.Add(name, ResizeArray([ node ]))
            | None -> ()

            match node.Kind with
            | YamlKind.Mapping entries ->
                for entry in entries do
                    index entry.Key
                    index entry.Value
            | YamlKind.Sequence items ->
                for item in items do index item
            | _ -> ()

        let resolve (node: YamlNode) =
            match node.Kind with
            | YamlKind.Alias name ->
                match anchors.TryGetValue name with
                | true, definitions ->
                    definitions
                    |> Seq.tryFindBack (fun definition -> definition.Span.Start < node.Span.Start)
                    |> Option.map (fun definition -> { definition with Span = node.Span })
                    |> Option.defaultValue node
                | _ -> node
            | _ -> node

        let property name node =
            match (resolve node).Kind with
            | YamlKind.Mapping entries ->
                entries
                |> Seq.tryPick (fun entry ->
                    if yamlString (resolve entry.Key) = Some name then Some(resolve entry.Value)
                    else None)
            | _ -> None

        let root =
            match document.Ast.Frontmatter with
            | Frontmatter.Parsed node ->
                index node
                Some node
            | _ -> None

        let field name = root |> Option.bind (property name)
        field, property

    let fields (document: OkfDocument) = fst (yamlAccess document)

    let classify (path: BundlePath) document =
        match path.Name with
        | "index.md" -> BundleDocumentView.Index
        | "log.md" -> BundleDocumentView.Log
        | _ when not path.IsMarkdown -> BundleDocumentView.Markdown
        | _ ->
            let find = fields document
            let field name = find name |> Option.bind nonemptyText

            match field "type" with
            | Some conceptType ->
                BundleDocumentView.Concept {
                    Id = path.Value.Substring(0, path.Value.Length - ".md".Length)
                    ConceptType = conceptType
                    Title = field "title"
                    Description = field "description"
                }
            | None -> BundleDocumentView.Markdown

    let rec descendants (node: MarkdownNode) =
        seq {
            yield node
            for child in node.Children do
                yield! descendants child
        }

    let rec private contentText (node: MarkdownNode) =
        match node.Kind with
        | MarkdownKind.Text value | MarkdownKind.CodeSpan value | MarkdownKind.CharacterReference value -> value
        | MarkdownKind.LineBreak _ -> " "
        | _ -> String.Concat(node.Children |> Seq.map contentText)

    let headingText (node: MarkdownNode) = (contentText node).Trim()

    let links node =
        descendants node
        |> Seq.choose (fun child ->
            match child.Kind with
            | MarkdownKind.Link link -> Some(child, link)
            | _ -> None)

    let blockChildren (body: MarkdownNode) =
        body.Children
        |> Seq.filter (fun node ->
            match node.Kind with
            | MarkdownKind.ReferenceDefinition _
            | MarkdownKind.FootnoteDefinition _ -> false
            | _ -> true)

    let lineStarts (text: string) =
        let starts = ResizeArray<int>()
        starts.Add 0
        let mutable offset = 0

        while offset < text.Length do
            let ch = text[offset]
            offset <- offset + 1

            if ch = '\r' then
                if offset < text.Length && text[offset] = '\n' then offset <- offset + 1
                starts.Add offset
            elif ch = '\n' then
                starts.Add offset

        starts.ToArray()

    let private position (lines: int array) offset =
        let index = Array.BinarySearch(lines, offset)
        let line = if index >= 0 then index else (~~~index) - 1
        { Line = line; Character = offset - lines[line] }

    let finding origin path lines (diagnostic: Diagnostic) =
        let range: BundleRange | null =
            match lines, diagnostic.Span with
            | Some index, ValueSome span -> BundleRange(position index span.Start.Value, position index span.EndExclusive.Value)
            | _ -> null

        BundleFinding(origin, diagnostic, path, range)

    let diagnostic code severity message span : Diagnostic =
        { Code = code; Severity = severity; Message = message; Span = span }

    let isAbsolute (reference: string) =
        let colon = reference.IndexOf(':')
        reference.StartsWith("//", StringComparison.Ordinal)
        || (colon > 0 && Uri.CheckSchemeName(reference.Substring(0, colon)))

    let private validPercentEscapes (value: string) =
        let mutable index = value.IndexOf('%')
        let mutable valid = true

        while valid && index >= 0 do
            if index + 2 < value.Length && Char.IsAsciiHexDigit(value[index + 1]) && Char.IsAsciiHexDigit(value[index + 2]) then
                index <- value.IndexOf('%', index + 3)
            else
                valid <- false

        valid

    let private relativeSegments (value: string) =
        let pieces = value.TrimEnd('/').Split('/')

        if pieces |> Array.exists (fun piece -> piece = "" || not (validPercentEscapes piece)) then
            Error BundleTarget.Unresolved
        else
            let segments = pieces |> Array.map Uri.UnescapeDataString

            if segments |> Array.exists (fun piece -> piece.IndexOfAny([| '/'; '\\'; '\000' |]) >= 0) then
                Error BundleTarget.Unresolved
            else
                Ok segments

    let private tryAddress prefix (segments: string array) =
        let rec fold parents index =
            if index = segments.Length then
                Ok(String.Join("/", List.rev parents))
            else
                match segments[index], parents with
                | ".", _ -> fold parents (index + 1)
                | "..", _ :: previous -> fold previous (index + 1)
                | "..", [] -> Error BundleTarget.External
                | name, _ -> fold (name :: parents) (index + 1)

        fold prefix 0

    let private targetAt version (bundle: KnowledgeBundle) directoryOnly value cancellation =
        if value = "" then
            BundleTarget.Directory None
        else
            match BundlePath.Parse value with
            | Ok path when version = OkfVersion.V0_2 || not path.IsHidden ->
                match bundle.TryFind path with
                | Some _ when directoryOnly -> BundleTarget.Unresolved
                | Some memberEntry ->
                    match memberEntry.ReadEntry(cancellation) with
                    | BundleEntry.Asset _ -> BundleTarget.Asset path
                    | BundleEntry.Document(OkfParseResult.ParsedDocument(document, _)) ->
                        match classify path document with
                        | BundleDocumentView.Concept _ -> BundleTarget.Concept path
                        | BundleDocumentView.Index | BundleDocumentView.Log -> BundleTarget.ReservedDocument path
                        | BundleDocumentView.Markdown -> BundleTarget.MarkdownDocument path
                    | BundleEntry.Document(OkfParseResult.UnreadableSource _) ->
                        if path.Name = "index.md" || path.Name = "log.md" then BundleTarget.ReservedDocument path
                        else BundleTarget.MarkdownDocument path
                | None ->
                    if bundle.HasDirectory path then BundleTarget.Directory(Some path)
                    else BundleTarget.Unresolved
            | _ -> BundleTarget.Unresolved

    let resolve version (bundle: KnowledgeBundle) (source: BundlePath) (reference: string) allowRootFallback cancellation =
        if isAbsolute reference then
            BundleTarget.External
        else
            let suffix = reference.IndexOfAny([| '?'; '#' |])
            let address = if suffix < 0 then reference else reference.Substring(0, suffix)
            let directoryOnly = address.EndsWith("/", StringComparison.Ordinal)

            if address = "" then
                targetAt version bundle false source.Value cancellation
            elif address = "/" then
                BundleTarget.Directory None
            else
                let absolute = address.StartsWith("/", StringComparison.Ordinal)
                let segments = if absolute then address.Substring(1) else address

                match relativeSegments segments with
                | Error unresolved -> unresolved
                | Ok parts ->
                    let parents =
                        if absolute then [] else source.Value.Split('/') |> Array.rev |> Array.toList |> List.tail

                    match tryAddress parents parts with
                    | Error external -> external
                    | Ok local ->
                        match targetAt version bundle directoryOnly local cancellation with
                        | BundleTarget.Unresolved when allowRootFallback && not absolute ->
                            match tryAddress [] parts with
                            | Ok fallback -> targetAt version bundle directoryOnly fallback cancellation
                            | Error _ -> BundleTarget.Unresolved
                        | result -> result

    let private itemLink (item: MarkdownNode) =
        if item.Kind <> MarkdownKind.ListItem then
            None
        else
            item.Children
            |> Seq.tryHead
            |> Option.bind (fun paragraph ->
                paragraph.Children
                |> Seq.filter (fun child ->
                    match child.Kind with
                    | MarkdownKind.Text value -> not (String.IsNullOrWhiteSpace value)
                    | _ -> true)
                |> Seq.tryHead
                |> Option.bind (fun first ->
                    match first.Kind with
                    | MarkdownKind.Link link when not link.IsImage -> Some(paragraph, first, link)
                    | _ -> None))

    let indexEntries (body: MarkdownNode) =
        blockChildren body
        |> Seq.collect (fun node ->
            match node.Kind with
            | MarkdownKind.List _ -> node.Children |> Seq.choose itemLink
            | _ -> Seq.empty)

    let private isIsoDate (value: string) =
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None)

    let private validateReserved version
        (path: BundlePath)
        (document: OkfDocument)
        (add: string -> string -> SourceSpan voption -> unit) =
        let body = document.Ast.Body
        let requireBareFrontmatter () =
            match document.Ast.Frontmatter with
            | Frontmatter.Absent -> ()
            | Frontmatter.Parsed node -> add "okf.reserved-frontmatter" $"{path.Name} does not allow frontmatter here." (ValueSome node.Span)
            | Frontmatter.Invalid _ -> ()

        if path.Name = "index.md" then
            if path.Value <> "index.md" then requireBareFrontmatter ()
            let mutable sawHeading = false
            let mutable sawEntries = false
            let mutable invalid = false

            for block in blockChildren body do
                match block.Kind with
                | MarkdownKind.Heading _ -> sawHeading <- true
                | MarkdownKind.List _ when sawHeading && not block.Children.IsEmpty ->
                    if block.Children |> Seq.forall (itemLink >> Option.isSome) then sawEntries <- true
                    else invalid <- true
                | MarkdownKind.Paragraph when sawHeading -> ()
                | _ -> invalid <- true

            if invalid || not sawEntries then
                add "okf.invalid-index" "An index requires headed sections with Markdown link entries." (ValueSome body.Span)
        elif path.Name = "log.md" then
            if version = OkfVersion.V0_3 then requireBareFrontmatter ()
            let mutable first = true
            let mutable lastDate: DateOnly option = None
            let mutable hasEntries = false
            let mutable sawDateHeading = false

            for block in blockChildren body do
                match block.Kind with
                | MarkdownKind.Heading level ->
                    let text = headingText block

                    match isIsoDate text with
                    | true, date ->
                        if sawDateHeading && not hasEntries then
                            add "okf.invalid-log" "Log dates must have a list of entries." (ValueSome block.Span)
                        if lastDate |> Option.exists (fun previous -> date > previous) then
                            add "okf.log-order" "Log dates must be in reverse chronological order." (ValueSome block.Span)
                        lastDate <- Some date
                        sawDateHeading <- true
                        hasEntries <- false
                    | _ when first && level = 1 -> ()
                    | _ ->
                        add "okf.invalid-log-date" "Log date headings must use a valid YYYY-MM-DD date." (ValueSome block.Span)
                        sawDateHeading <- true
                        hasEntries <- false
                | MarkdownKind.List _ when sawDateHeading && not block.Children.IsEmpty ->
                    hasEntries <- true

                    if block.Children |> Seq.exists (fun item -> item.Children |> Seq.exists (fun nested -> match nested.Kind with | MarkdownKind.List _ -> true | _ -> false)) then
                        add "okf.invalid-log" "Log entries must be a flat list." (ValueSome block.Span)
                | _ -> add "okf.invalid-log" "Log entries must be grouped below date headings." (ValueSome block.Span)

                first <- false

            if not sawDateHeading || not hasEntries then
                add "okf.invalid-log" "A log requires a date group with a list of entries." (ValueSome body.Span)

    let documentFindings version path (document: OkfDocument) =
        let out = ResizeArray<Diagnostic>()
        let add code message span = out.Add(diagnostic code DiagnosticSeverity.Error message span)

        match document.Ast.Frontmatter with
        | Frontmatter.Parsed { Kind = YamlKind.Mapping _ }
        | Frontmatter.Absent
        | Frontmatter.Invalid _ -> ()
        | Frontmatter.Parsed node ->
            add "okf.frontmatter-mapping" "Frontmatter must be a YAML mapping." (ValueSome node.Span)

        let isReserved =
            path |> Option.exists (fun (memberPath: BundlePath) -> memberPath.Name = "index.md" || memberPath.Name = "log.md")

        if isReserved then
            path |> Option.iter (fun memberPath -> validateReserved version memberPath document add)
        elif version = OkfVersion.V0_2 then
            match document.Ast.Frontmatter with
            | Frontmatter.Absent ->
                add "okf.frontmatter-required" "OKF 0.2 concepts require a YAML frontmatter block." ValueNone
            | Frontmatter.Parsed { Kind = YamlKind.Mapping _; Span = span } ->
                let field = fields document "type"

                if field |> Option.bind nonemptyText |> Option.isNone then
                    let range = field |> Option.map _.Span |> Option.defaultValue span
                    add "okf.type-required" "OKF 0.2 concept frontmatter requires a nonempty type." (ValueSome range)
            | _ -> ()

        out

    let private values (node: YamlNode) =
        match node.Kind with
        | YamlKind.Sequence items -> items :> seq<_>
        | _ -> Seq.singleton node

    let private presentPath (node: YamlNode) =
        nonemptyText node |> Option.map (fun value -> node.Span, value)

    let private references version (document: OkfDocument) =
        let field, property = yamlAccess document
        let mappingPath container = field container |> Option.bind (property "resource")

        seq {
            for name in [ "resource"; "computation" ] do
                yield! field name |> Option.bind presentPath |> Option.toList

            if version = OkfVersion.V0_3 && (field "status" |> Option.bind nonemptyText) = Some "deprecated" then
                yield! field "superseded_by" |> Option.bind presentPath |> Option.toList

            for container in [ "executor"; "attester" ] do
                yield! mappingPath container |> Option.bind presentPath |> Option.toList

            for source in field "sources" |> Option.toList |> Seq.collect values do
                match property "resource" source |> Option.bind presentPath with
                | Some(_, value) as path when
                    isAbsolute value || (not (value |> Seq.exists Char.IsWhiteSpace) && value.IndexOfAny([| '/'; '.' |]) >= 0) ->
                    yield! Option.toList path
                | _ -> ()
        }

    let private citations (document: OkfDocument) =
        let field, property = yamlAccess document
        let known =
            field "sources"
            |> Option.toList
            |> Seq.collect values
            |> Seq.choose (fun node -> property "id" node |> Option.bind nonemptyText)
            |> Set.ofSeq

        descendants document.Ast.Body
        |> Seq.choose (fun node ->
            match node.Kind with
            | MarkdownKind.FootnoteReference label when not (known.Contains label) -> Some(node.Span, label)
            | _ -> None)

    let private authoredDescription (paragraph: MarkdownNode) (link: MarkdownNode) (document: OkfDocument) =
        let first = link.Span.EndExclusive.Value
        let last = paragraph.Span.EndExclusive.Value

        if first < last then
            document.Source.Text.Substring(first, last - first).Trim().TrimStart('-', '–', '—').Trim()
        else ""

    let relationshipFindings version allowRootFallback policy (bundle: KnowledgeBundle) path (document: OkfDocument) cancellation =
        let morphir = policy = BundlePolicy.Morphir
        let origin = if morphir then BundleFindingOrigin.MorphirPolicy else BundleFindingOrigin.Advisory
        let severity = if morphir then DiagnosticSeverity.Error else DiagnosticSeverity.Warning
        let prefix = if morphir then "morphir." else "okf."

        seq {
            for node, link in links document.Ast.Body do
                if resolve version bundle path link.Destination false cancellation = BundleTarget.Unresolved then
                    let span = link.DestinationSpan |> ValueOption.defaultValue node.Span
                    yield origin, diagnostic (prefix + "unresolved-link") severity $"Local link '{link.Destination}' does not resolve." (ValueSome span)

            for span, target in references version document do
                if resolve version bundle path target allowRootFallback cancellation = BundleTarget.Unresolved then
                    yield origin, diagnostic (prefix + "unresolved-path") severity $"Local resource '{target}' does not resolve." (ValueSome span)

            if morphir then
                match classify path document with
                | BundleDocumentView.Concept _ ->
                    for span, label in citations document do
                        yield origin, diagnostic "morphir.unknown-citation" severity $"Footnote '{label}' does not name a declared source." (ValueSome span)
                | BundleDocumentView.Index ->
                    for paragraph, node, link in indexEntries document.Ast.Body do
                        match resolve version bundle path link.Destination false cancellation with
                        | BundleTarget.Concept target ->
                            match bundle.TryFind target |> Option.map (fun memberEntry -> memberEntry.ReadEntry(cancellation)) with
                            | Some(BundleEntry.Document(OkfParseResult.ParsedDocument(concept, _))) ->
                                match fields concept "description" |> Option.bind nonemptyText with
                                | Some expected when (authoredDescription paragraph node document) <> expected ->
                                    yield origin, diagnostic "morphir.index-description" severity $"The index entry for '{target}' must use the concept description." (ValueSome paragraph.Span)
                                | _ -> ()
                            | _ -> ()
                        | _ -> ()
                | _ -> ()
        }

/// Validation of document and bundle conformance, plus optional Morphir conventions.
[<RequireQualifiedAccess>]
module BundleValidation =
    /// Interprets the metadata of a parsed Markdown member.
    [<CompiledName("View")>]
    let view (path: BundlePath, document: OkfDocument) = BundleAnalysis.classify path document

    /// Resolves a Markdown link against a bundle without opening any external resource.
    [<CompiledName("ResolveLink")>]
    let resolveLink version bundle source reference =
        BundleAnalysis.resolve version bundle source reference false CancellationToken.None

    /// Resolves a frontmatter path field, including the OKF 0.2 root-relative fallback.
    [<CompiledName("ResolvePathField")>]
    let resolvePathField version bundle source reference =
        BundleAnalysis.resolve version bundle source reference (version = OkfVersion.V0_2) CancellationToken.None

    /// Checks one document parse outcome; relationship checks require a bundle.
    let validateDocument version path parsed =
        let relative = path |> Option.map (fun (value: BundlePath) -> value.Value) |> Option.toObj

        match parsed with
        | OkfParseResult.UnreadableSource diagnostic ->
            ImmutableArray.Create(BundleAnalysis.finding BundleFindingOrigin.Input relative None diagnostic)
        | OkfParseResult.ParsedDocument(document, diagnostics) ->
            let lines = Some(BundleAnalysis.lineStarts document.Source.Text)

            Seq.append diagnostics (BundleAnalysis.documentFindings version path document)
            |> Seq.map (BundleAnalysis.finding BundleFindingOrigin.Conformance relative lines)
            |> ImmutableArray.CreateRange

    /// Checks one document parse outcome for C# callers. Supply the bundle path to apply reserved-file rules.
    [<CompiledName("ValidateDocument")>]
    let validateDocumentInterop(version: OkfVersion, path: BundlePath | null, parsed: OkfParseResult) =
        validateDocument version (Option.ofObj path) parsed

    let private run version (policy: BundlePolicy) (bundle: KnowledgeBundle) (cancellation: CancellationToken) =
        cancellation.ThrowIfCancellationRequested()
        let findings = ResizeArray<BundleFinding>()

        let add origin path lines code severity message span =
            BundleAnalysis.diagnostic code severity message span
            |> BundleAnalysis.finding origin path lines
            |> findings.Add

        let root = BundlePath.Create("index.md")
        let index =
            match bundle.TryFind root |> Option.map (fun memberEntry -> memberEntry.ReadEntry(cancellation)) with
            | Some(BundleEntry.Document(OkfParseResult.ParsedDocument(document, _))) -> Some document
            | _ -> None

        let declaration =
            index
            |> Option.bind (fun document -> BundleAnalysis.fields document "okf_version")
            |> Option.filter (BundleAnalysis.isAbsentValue >> not)
        let declared = declaration |> Option.bind BundleAnalysis.nonemptyText

        let selected, unknown =
            match version, declared with
            | Some requested, _ -> requested, false
            | None, Some text ->
                match OkfVersion.Parse text with
                | Ok recognized -> recognized, false
                | Error _ ->
                    let range = declaration |> Option.map _.Span |> Option.toValueOption
                    let lines = index |> Option.map (fun document -> BundleAnalysis.lineStarts document.Source.Text)
                    add BundleFindingOrigin.Advisory root.Value lines "okf.unknown-version" DiagnosticSeverity.Warning $"OKF version '{text}' is unrecognized; the available checks use 0.3." range
                    OkfVersion.V0_3, true
            | None, None -> OkfVersion.V0_3, false

        let allowRootFallback = selected = OkfVersion.V0_2 || (version.IsNone && declaration.IsNone)

        if policy = BundlePolicy.Morphir then
            if (bundle.TryFind root).IsNone then
                add BundleFindingOrigin.MorphirPolicy root.Value None "morphir.index-required" DiagnosticSeverity.Error "The Morphir knowledge base requires a root index.md." ValueNone
            elif index.IsSome && declared.IsNone then
                add BundleFindingOrigin.MorphirPolicy root.Value None "morphir.version-required" DiagnosticSeverity.Error "The root index must declare okf_version." ValueNone

        if declaration.IsSome && declared.IsNone then
            let range = declaration |> Option.map _.Span |> Option.toValueOption
            let lines = index |> Option.map (fun document -> BundleAnalysis.lineStarts document.Source.Text)
            add BundleFindingOrigin.Advisory root.Value lines "okf.invalid-version" DiagnosticSeverity.Warning "The root index's okf_version must be a nonempty string." range

        for issue in bundle.LoadIssues do
            let hidden = issue.Path.Split('/') |> Array.exists (fun segment -> segment.StartsWith('.'))

            if selected = OkfVersion.V0_2 || not hidden then
                let code, severity =
                    if issue.IsExcluded then "okf.excluded-entry", DiagnosticSeverity.Warning
                    else "okf.unreadable-member", DiagnosticSeverity.Error

                add BundleFindingOrigin.Input issue.Path None code severity issue.Message ValueNone

        for memberEntry in bundle.Members do
            cancellation.ThrowIfCancellationRequested()
            let path = memberEntry.Path

            if selected = OkfVersion.V0_2 || not path.IsHidden then
                match memberEntry.ReadEntry(cancellation) with
                | BundleEntry.Asset _ -> ()
                | BundleEntry.Document(OkfParseResult.UnreadableSource diagnostic) ->
                    findings.Add(BundleAnalysis.finding BundleFindingOrigin.Input path.Value None diagnostic)
                | BundleEntry.Document(OkfParseResult.ParsedDocument(document, diagnostics)) ->
                    let lines = Some(BundleAnalysis.lineStarts document.Source.Text)

                    for diagnostic in diagnostics do
                        findings.Add(BundleAnalysis.finding BundleFindingOrigin.Conformance path.Value lines diagnostic)
                    for diagnostic in BundleAnalysis.documentFindings selected (Some path) document do
                        findings.Add(BundleAnalysis.finding BundleFindingOrigin.Conformance path.Value lines diagnostic)
                    for origin, diagnostic in BundleAnalysis.relationshipFindings selected allowRootFallback policy bundle path document cancellation do
                        findings.Add(BundleAnalysis.finding origin path.Value lines diagnostic)

        let ordered =
            findings
            |> Seq.sortBy (fun finding ->
                let offset = finding.Span |> ValueOption.map (fun span -> span.Start.Value) |> ValueOption.defaultValue -1
                finding.PathOrNull, offset, finding.Code)
            |> ImmutableArray.CreateRange

        let failed = ordered |> Seq.filter (fun finding -> finding.Severity = DiagnosticSeverity.Error)
        let conformance =
            if unknown then BundleConformance.Unverified
            elif failed |> Seq.exists (fun finding -> finding.Origin <> BundleFindingOrigin.MorphirPolicy) then BundleConformance.NonConformant
            else BundleConformance.Conformant

        let exitCode = if Seq.isEmpty failed then 0 else 1
        BundleValidationReport(Some selected, declared, policy, conformance, exitCode, ordered)

    /// Checks a supplied virtual or previously loaded bundle.
    let validate version policy bundle = run version policy bundle CancellationToken.None

    /// Checks a supplied bundle for C# callers, treating null as automatic version selection.
    [<CompiledName("Validate")>]
    let validateInterop(bundle: KnowledgeBundle, version: OkfVersion | null, policy: BundlePolicy, cancellation: CancellationToken) =
        run (Option.ofObj version) policy bundle cancellation

    /// Loads a physical bundle and checks it without following symbolic links.
    let validateDirectory version policy root cancellation =
        match KnowledgeBundle.LoadDirectory(root, cancellation) with
        | Ok bundle -> run version policy bundle cancellation
        | Error reason ->
            let diagnostic = BundleAnalysis.diagnostic "okf.bundle-unavailable" DiagnosticSeverity.Error reason.Message ValueNone
            let finding = BundleAnalysis.finding BundleFindingOrigin.Input null None diagnostic
            BundleValidationReport(None, None, policy, BundleConformance.NotChecked, 2, ImmutableArray.Create finding)

    /// Checks a physical bundle for C# callers, treating null as automatic version selection.
    [<CompiledName("ValidateDirectory")>]
    let validateDirectoryInterop(root: string, version: OkfVersion | null, policy: BundlePolicy, cancellation: CancellationToken) =
        validateDirectory (Option.ofObj version) policy root cancellation
