// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Immutable
open System.Runtime.InteropServices
open Morphir.Knowledge

/// The severity of a parse or validation finding.
[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    | Info
    | Warning
    | Error

    /// The severity in structured reports.
    member this.WireValue =
        match this with
        | Info -> "info"
        | Warning -> "warning"
        | Error -> "error"

    override this.ToString() = this.WireValue

/// A finding tied to an original source span when a location is available.
type Diagnostic = {
    /// Stable identifier for this finding.
    Code: string
    /// Impact of the finding.
    Severity: DiagnosticSeverity
    /// Human-readable explanation.
    Message: string
    /// The affected UTF-16 range, if present.
    Span: SourceSpan voption
} with
    /// Writes the range when the finding has a source location. For C# callers.
    member this.TryGetSpan([<Out>] span: byref<SourceSpan>) =
        match this.Span with
        | ValueSome value ->
            span <- value
            true
        | ValueNone ->
            span <- Unchecked.defaultof<_>
            false

    override this.ToString() = $"{this.Code}: {this.Message}"

/// The original document, decoded as UTF-8 without changing its source units.
[<Sealed>]
type OkfSource internal (text: string, bytes: ImmutableArray<byte>) =
    /// Original text including line endings and any initial byte order mark.
    member _.Text = text

    /// Immutable original UTF-8 bytes.
    member _.Bytes = bytes

    /// Returns the original text in the supplied UTF-16 range.
    member _.Slice(span: SourceSpan) =
        text.Substring(span.Start.Value, span.EndExclusive.Value - span.Start.Value)

    override _.ToString() = text

/// The authored presentation of a YAML scalar.
[<RequireQualifiedAccess>]
type YamlScalarStyle =
    | Plain
    | SingleQuoted
    | DoubleQuoted
    | Literal
    | Folded

    override this.ToString() =
        match this with
        | Plain -> "plain"
        | SingleQuoted -> "single-quoted"
        | DoubleQuoted -> "double-quoted"
        | Literal -> "literal"
        | Folded -> "folded"

/// A decoded YAML scalar and its authored form.
type YamlScalar = {
    /// Scalar content decoded according to YAML syntax.
    Value: string
    /// The scalar's presentation in the source.
    Style: YamlScalarStyle
} with
    override this.ToString() = this.Value

/// The shape of a YAML node, before applying the OKF metadata schema.
[<RequireQualifiedAccess>]
type YamlKind =
    | Mapping of ImmutableArray<YamlEntry>
    | Sequence of ImmutableArray<YamlNode>
    | Scalar of YamlScalar
    | Alias of string

    override this.ToString() =
        match this with
        | Mapping _ -> "mapping"
        | Sequence _ -> "sequence"
        | Scalar value -> value.Value
        | Alias name -> $"*{name}"

/// A YAML node with a range in the original document.
and YamlNode = {
    /// Node shape and value.
    Kind: YamlKind
    /// Location in the original document.
    Span: SourceSpan
    /// The explicitly written tag, if any.
    Tag: string option
    /// The authored anchor name, if any.
    Anchor: string option
} with
    override this.ToString() = this.Kind.ToString()

/// A YAML mapping entry, with independently located key and value.
and YamlEntry = {
    /// The key as written, including arbitrary YAML key shapes.
    Key: YamlNode
    /// The associated value.
    Value: YamlNode
} with
    override this.ToString() = this.Key.ToString()

/// A link target recognized in Markdown.
type MarkdownLink = {
    /// Decoded link destination.
    Destination: string
    /// Exact destination range where available, which can be in a definition.
    DestinationSpan: SourceSpan voption
    /// The optional link title.
    Title: string option
    /// Whether the link is an image.
    IsImage: bool
} with
    override this.ToString() = this.Destination

/// Alignment declared by a GFM table's delimiter row.
[<RequireQualifiedAccess>]
type MarkdownTableAlignment =
    | Unspecified
    | Left
    | Center
    | Right

    override this.ToString() =
        match this with
        | Unspecified -> "unspecified"
        | Left -> "left"
        | Center -> "center"
        | Right -> "right"

/// Markdown semantic node kinds, shared by the AST and CST.
[<RequireQualifiedAccess>]
type MarkdownKind =
    | Body
    | Paragraph
    | Heading of int
    | Quote
    | List of ordered: bool * startNumber: int voption * tight: bool
    | ListItem
    | ThematicBreak
    | CodeBlock of info: string option * content: string
    | HtmlBlock of string
    | Table of columns: ImmutableArray<MarkdownTableAlignment>
    | TableRow of header: bool
    | TableCell
    | Text of string
    | Emphasis of delimiter: char * count: int
    | CodeSpan of string
    | HtmlInline of string
    | CharacterReference of string
    | LineBreak of hard: bool
    | Link of MarkdownLink
    | ReferenceDefinition of label: string * link: MarkdownLink
    | FootnoteReference of label: string
    | FootnoteDefinition of label: string

    override this.ToString() =
        match this with
        | Body -> "body"
        | Paragraph -> "paragraph"
        | Heading level -> $"heading {level}"
        | Quote -> "blockquote"
        | List(true, _, _) -> "ordered list"
        | List(false, _, _) -> "unordered list"
        | ListItem -> "list item"
        | ThematicBreak -> "thematic break"
        | CodeBlock _ -> "code block"
        | HtmlBlock _ -> "HTML block"
        | Table _ -> "table"
        | TableRow true -> "table header row"
        | TableRow false -> "table row"
        | TableCell -> "table cell"
        | Text _ -> "text"
        | Emphasis _ -> "emphasis"
        | CodeSpan _ -> "code span"
        | HtmlInline _ -> "inline HTML"
        | CharacterReference _ -> "character reference"
        | LineBreak _ -> "line break"
        | Link link -> $"link {link.Destination}"
        | ReferenceDefinition(label, _) -> $"reference definition {label}"
        | FootnoteReference label -> $"footnote reference {label}"
        | FootnoteDefinition label -> $"footnote definition {label}"

/// A Markdown node and its children beneath the original document.
[<Sealed>]
type MarkdownNode internal
    (kind: MarkdownKind, span: SourceSpan, children: ImmutableArray<MarkdownNode>, source: OkfSource) =
    /// Semantic shape and value.
    member _.Kind = kind
    /// Location in the original document.
    member _.Span = span
    /// The node's children in source order.
    member _.Children = children
    /// The original source text for this node.
    member _.Text = source.Slice span
    override _.ToString() = kind.ToString()

/// The lexical kind of a token in YAML source.
[<RequireQualifiedAccess>]
type YamlTokenKind =
    | Comment
    | Scalar
    | Anchor
    | Alias
    | Tag
    | Indicator
    | Directive

    override this.ToString() =
        match this with
        | Comment -> "comment"
        | Scalar -> "scalar"
        | Anchor -> "anchor"
        | Alias -> "alias"
        | Tag -> "tag"
        | Indicator -> "indicator"
        | Directive -> "directive"

/// The authored structure or token represented by a concrete syntax node.
[<RequireQualifiedAccess>]
type SyntaxKind =
    | Document
    | ByteOrderMark
    | Frontmatter
    | OpeningFence
    | ClosingFence
    | YamlMapping
    | YamlSequence
    | YamlPair
    | YamlToken of YamlTokenKind
    | Markdown of MarkdownKind
    | TableDelimiterRow
    | FootnoteLabel
    | LinkDestination
    | CodeDelimiter
    | Whitespace
    | Newline
    | Punctuation of char
    | Text
    | Error

    override this.ToString() =
        match this with
        | Document -> "document"
        | ByteOrderMark -> "byte order mark"
        | Frontmatter -> "frontmatter"
        | OpeningFence -> "opening fence"
        | ClosingFence -> "closing fence"
        | YamlMapping -> "YAML mapping"
        | YamlSequence -> "YAML sequence"
        | YamlPair -> "YAML pair"
        | YamlToken token -> $"YAML {token}"
        | Markdown node -> node.ToString()
        | TableDelimiterRow -> "table delimiter row"
        | FootnoteLabel -> "footnote label"
        | LinkDestination -> "link destination"
        | CodeDelimiter -> "code delimiter"
        | Whitespace -> "whitespace"
        | Newline -> "newline"
        | Punctuation character -> $"punctuation {character}"
        | Text -> "text"
        | Error -> "malformed source"

/// A source-preserving syntax node. Nonempty leaves partition the original source.
[<Sealed>]
type SyntaxNode internal (kind: SyntaxKind, span: SourceSpan, children: ImmutableArray<SyntaxNode>) =
    /// The authored structure or token.
    member _.Kind = kind
    /// Location in the original document.
    member _.Span = span
    /// Concrete children in source order, including trivia.
    member _.Children = children
    override _.ToString() = kind.ToString()

/// Frontmatter status in a parsed document.
[<RequireQualifiedAccess>]
type Frontmatter =
    | Absent
    | Parsed of YamlNode
    | Invalid of SourceSpan

    override this.ToString() =
        match this with
        | Absent -> "absent"
        | Parsed _ -> "parsed"
        | Invalid _ -> "invalid"

/// The semantic root of one Markdown document and its frontmatter.
type OkfDocumentAst = {
    /// The optional YAML metadata region.
    Frontmatter: Frontmatter
    /// The Markdown body including its blocks and inlines.
    Body: MarkdownNode
} with
    override _.ToString() = "OKF document"

/// A node encountered in a semantic traversal of a document.
[<RequireQualifiedAccess>]
type AstNode =
    | Document of OkfDocumentAst
    | Frontmatter of Frontmatter
    | Yaml of YamlNode
    | Markdown of MarkdownNode

    /// Handles every semantic node category. For C# callers.
    member this.Match<'Result>
        (
            document: Func<OkfDocumentAst, 'Result>,
            frontmatter: Func<Frontmatter, 'Result>,
            yaml: Func<YamlNode, 'Result>,
            markdown: Func<MarkdownNode, 'Result>
        ) =
        match this with
        | Document node -> document.Invoke node
        | Frontmatter node -> frontmatter.Invoke node
        | Yaml node -> yaml.Invoke node
        | Markdown node -> markdown.Invoke node

    override this.ToString() =
        match this with
        | Document node -> node.ToString()
        | Frontmatter node -> node.ToString()
        | Yaml node -> node.ToString()
        | Markdown node -> node.ToString()
