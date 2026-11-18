// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Okf.Tests.DocumentTests

open System
open System.Text
open System.Text.Json
open Expecto
open Morphir.Knowledge.Okf
open Morphir.TestSupport.Fixtures

module private DocumentMother =
    let withEditorialCommentsAndCitations =
        "---\r\n# editorial\ntype: 'Metric' # keep the quote style\r\nx-example: {owner: \"Finance\"}\n---\r\n"
        + "# Revenue 💰\r\n\nA claim.[^used]\r\n\n"
        + "| Name | Value |\r\n| :--- | ---: |\n| a\\|b | 42 |\r\n\n"
        + "[^unused]: Keep this definition even though it is unused.\r\n\n"
        + "[^used]: A source.\n\n## After the footnotes"

    let withMalformedYaml = "---\ninvalid: [\r\n---\n# Body survives\r\n"
    let withUnknownAlias = "---\nmissing: *anchor\n---\n# Body"
    let withRecursiveAlias = "---\nvalue: &anchor [*anchor]\n---\n# Body"
    let withHeaderOnlyTable = "---\ntype: Metric\n---\n| Name | Value |\n| :--- | ---: |"
    let withExtendedFence = "---\ntype: Metric\n--- \n# Still Markdown"
    let withBom = "\uFEFF---\rtitle: 'Metric'\r---\r# Body"
    let withReferenceLink = "[revenue][dictionary]\n\n[dictionary]: <revenue.md> 'Dictionary'\n"
    let withUnresolvedCitation = "A claim.[^missing] and \\[^escaped] and `[^code]`.\n"
    let withFencedCode = "Some ``a`b`` and code:\n\n~~~bash -e\nls\n~~~\n"

let private parse source =
    match OkfDocument.Parse(source: string) with
    | OkfParseResult.ParsedDocument(document, diagnostics) -> document, diagnostics
    | OkfParseResult.UnreadableSource diagnostic -> failtest diagnostic.Message

let private collect visitor root =
    let collected = ResizeArray<_>()
    Cursor.walk (fun node -> collected.Add(visitor node); VisitControl.Continue) root
    List.ofSeq collected

[<Tests>]
let tests =
    testList
        "OKF documents"
        [
            testCase "CST leaves tile source and emit identical UTF-8" (fun () ->
                let text = DocumentMother.withEditorialCommentsAndCitations
                let document, diagnostics = parse text
                Expect.isEmpty diagnostics "Well-formed source"
                let nodes = Cursor.syntax document |> collect (fun cursor -> cursor.Node)
                let has kind literal =
                    nodes |> List.exists (fun node -> node.Kind = kind && document.Source.Slice(node.Span) = literal)
                Expect.isTrue (has (SyntaxKind.YamlToken YamlTokenKind.Comment) "# keep the quote style") "Structured YAML comment"
                Expect.isTrue (has SyntaxKind.TableDelimiterRow "| :--- | ---: |") "Structured alignment row"
                Expect.isTrue (has SyntaxKind.FootnoteLabel "^unused") "Unused footnote captured"
                let leaves = nodes |> List.filter (fun node -> node.Children.IsEmpty)
                let mutable expected = 0
                for leaf in leaves do
                    Expect.equal leaf.Span.Start.Value expected "Leaf starts at the previous end"
                    expected <- leaf.Span.EndExclusive.Value
                Expect.equal expected text.Length "Leaves include the entire document"
                Expect.sequenceEqual (document.Reconstruct()) (Encoding.UTF8.GetBytes text) "Byte-identical emission")

            testCase "AST exposes nested extension fields and Markdown nodes" (fun () ->
                let document, _ = parse DocumentMother.withEditorialCommentsAndCitations
                match document.Ast.Frontmatter with
                | Frontmatter.Parsed { Kind = YamlKind.Mapping entries } ->
                    match entries[1].Key.Kind, entries[1].Value.Kind with
                    | YamlKind.Scalar field, YamlKind.Mapping nested ->
                        Expect.equal field.Value "x-example" "Extension remains visible"
                        match nested[0].Value.Kind with
                        | YamlKind.Scalar scalar -> Expect.equal scalar.Value "Finance" "Nested value decoded"
                        | _ -> failtest "Expected a scalar owner"
                    | _ -> failtest "Expected the extension mapping"
                | _ -> failtest "Expected a mapping"
                let ast = Cursor.ast document |> collect (fun cursor -> cursor.Node)
                let kinds = ast |> List.choose (function AstNode.Markdown node -> Some node.Kind | _ -> None)
                let alignments =
                    kinds |> List.pick (function MarkdownKind.Table columns -> Some columns | _ -> None)
                Expect.sequenceEqual alignments [ MarkdownTableAlignment.Left; MarkdownTableAlignment.Right ] "Typed column alignments"
                Expect.contains kinds (MarkdownKind.TableRow true) "Header row"
                Expect.contains kinds MarkdownKind.TableCell "Cell traversed"
                Expect.contains kinds (MarkdownKind.FootnoteReference "used") "Citation label"
                Expect.contains kinds (MarkdownKind.FootnoteDefinition "unused") "Unused label")

            testCase "Malformed YAML preserves CST error region and body" (fun () ->
                let document, diagnostics = parse DocumentMother.withMalformedYaml
                match document.Ast.Frontmatter with
                | Frontmatter.Invalid _ -> ()
                | _ -> failtest "Expected invalid frontmatter"
                Expect.equal diagnostics[0].Code "okf.invalid_yaml" "Located YAML diagnostic"
                Expect.isTrue diagnostics[0].Span.IsSome "Expected a source range"
                let nodes = Cursor.syntax document |> collect (fun cursor -> cursor.Node.Kind)
                Expect.contains nodes SyntaxKind.Error "Full malformed region"
                Expect.contains nodes (SyntaxKind.Markdown(MarkdownKind.Heading 1)) "Body survives")

            testCase "YAML aliases refer to anchors already declared in the document" (fun () ->
                let invalid, findings = parse DocumentMother.withUnknownAlias
                Expect.hasLength findings 1 "An undefined alias is malformed YAML"
                Expect.equal (invalid.Source.Slice(ValueOption.get findings[0].Span)) "*anchor" "Alias owns the finding"
                let valid, findings = parse DocumentMother.withRecursiveAlias
                Expect.isEmpty findings "A container can anchor a reference to itself"
                let nodes = Cursor.ast valid |> collect (fun cursor -> cursor.Node)
                Expect.isTrue
                    (nodes |> List.exists (function AstNode.Yaml { Kind = YamlKind.Alias "anchor" } -> true | _ -> false))
                    "The AST retains the authored reference without expansion")

            testCase "Invalid UTF-8 returns an unreadable diagnostic" (fun () ->
                match OkfDocument.Parse(ReadOnlyMemory<byte>([| 0xC0uy; 0x80uy |])) with
                | OkfParseResult.UnreadableSource diagnostic -> Expect.equal diagnostic.Code "okf.invalid_utf8" "Encoding fault"
                | _ -> failtest "Expected typed failure")

            testCase "A closing fence must be exactly three dashes" (fun () ->
                let document, diagnostics = parse DocumentMother.withExtendedFence
                match document.Ast.Frontmatter with
                | Frontmatter.Absent -> Expect.isEmpty diagnostics "Ordinary Markdown"
                | _ -> failtest "A trailing space must not close frontmatter")

            testCase "Header-only table retains its alignment row at EOF" (fun () ->
                let document, _ = parse DocumentMother.withHeaderOnlyTable
                let nodes = Cursor.syntax document |> collect (fun cursor -> cursor.Node)
                let row = nodes |> List.find (fun node -> node.Kind = SyntaxKind.TableDelimiterRow)
                Expect.equal row.Span.EndExclusive.Value document.Source.Text.Length "Delimiter ends at EOF"
                Expect.sequenceEqual (document.Reconstruct()) (Encoding.UTF8.GetBytes DocumentMother.withHeaderOnlyTable) "No newline appended")

            testCase "BOM and standalone CR line endings retain whole-document coordinates" (fun () ->
                let document, diagnostics = parse DocumentMother.withBom
                Expect.isEmpty diagnostics "Valid source"
                let nodes = Cursor.syntax document |> collect (fun cursor -> cursor.Node)
                Expect.isTrue (nodes |> List.exists (fun node -> node.Kind = SyntaxKind.OpeningFence && node.Span.Start.Value = 1)) "BOM is a source unit"
                Expect.sequenceEqual (document.Reconstruct()) (Encoding.UTF8.GetBytes DocumentMother.withBom) "Original bytes emitted")

            testCase "Visitors skip descendants, stop, and stay inside the starting subtree" (fun () ->
                let document, _ = parse DocumentMother.withEditorialCommentsAndCitations
                let cursor = Cursor.ast document
                let frontmatter = cursor.FirstChild |> Option.get
                let body = frontmatter.NextSibling |> Option.get
                Expect.equal (body.PreviousSibling |> Option.map (fun c -> c.Node)) (Some frontmatter.Node) "Adjacent siblings"
                Expect.equal (body.Parent |> Option.map (fun c -> c.Node)) (Some cursor.Node) "Parent retained"
                let nodes = ResizeArray<_>()
                cursor |> Cursor.walk (fun current ->
                    nodes.Add current.Node
                    match current.Node with
                    | AstNode.Frontmatter _ -> VisitControl.SkipChildren
                    | AstNode.Markdown node when node.Kind = MarkdownKind.Heading 1 -> VisitControl.Stop
                    | _ -> VisitControl.Continue)
                Expect.equal nodes.Count 4 "Root, frontmatter, body and first heading only"
                let frontMatterNodes = frontmatter |> collect (fun c -> c.Node)
                Expect.isFalse (frontMatterNodes |> List.exists (function AstNode.Markdown _ -> true | _ -> false)) "Walk cannot leave subtree")

            testCase "Reference definitions keep their own source location" (fun () ->
                let document, _ = parse DocumentMother.withReferenceLink
                let nodes = Cursor.ast document |> collect (fun cursor -> cursor.Node)
                let expected = nodes |> List.choose (function
                    | AstNode.Markdown node ->
                        match node.Kind with
                        | MarkdownKind.Link link
                        | MarkdownKind.ReferenceDefinition(_, link) -> Some link
                        | _ -> None
                    | _ -> None)
                Expect.hasLength expected 2 "The link and its definition"
                for link in expected do
                    Expect.equal link.Destination "revenue.md" "Decoded link destination"
                    let destination = link.DestinationSpan |> ValueOption.get |> document.Source.Slice
                    Expect.stringContains destination "revenue.md" "Exact document location of the destination")

            testCase "Unresolved citations are distinguishable from escaped and code examples" (fun () ->
                let document, _ = parse DocumentMother.withUnresolvedCitation
                let nodes = Cursor.ast document |> collect (fun cursor -> cursor.Node)
                let references = nodes |> List.choose (function
                    | AstNode.Markdown node ->
                        match node.Kind with
                        | MarkdownKind.FootnoteReference label -> Some(label, node.Text)
                        | _ -> None
                    | _ -> None)
                Expect.equal references [ "missing", "[^missing]" ] "Only the unescaped citation is recognized")

            testCase "Code retains semantic fence information and concrete delimiters" (fun () ->
                let document, _ = parse DocumentMother.withFencedCode
                let syntax = Cursor.syntax document |> collect (fun cursor -> cursor.Node)
                let delimiters =
                    syntax
                    |> List.filter (fun node -> node.Kind = SyntaxKind.CodeDelimiter)
                    |> List.map (fun node -> document.Source.Slice node.Span)
                Expect.equal delimiters [ "``"; "``"; "~~~"; "~~~" ] "Each code delimiter is independently navigable"
                let ast = Cursor.ast document |> collect (fun cursor -> cursor.Node)
                let code =
                    ast |> List.pick (function
                        | AstNode.Markdown node ->
                            match node.Kind with MarkdownKind.CodeBlock(info, _) -> Some info | _ -> None
                        | _ -> None)
                Expect.equal code (Some "bash -e") "Decoded fence info includes arguments")

            testCase "CommonMark corpus parses as a lossless Markdown body" (fun () ->
                use corpus = JsonDocument.Parse(FixtureCatalog.ReadText(FixtureCatalog.OkfDocuments.CommonMark))
                Expect.equal (corpus.RootElement.GetArrayLength()) 652 "The complete pinned corpus"
                for example in corpus.RootElement.EnumerateArray() do
                    let number = example.GetProperty("example").GetInt32()
                    let markdown = example.GetProperty("markdown").GetString()
                    let input = "---\ntype: Reference\n---\n" + markdown
                    let document, diagnostics = parse input
                    Expect.isEmpty diagnostics $"Example {number} parsed"
                    Expect.equal document.Ast.Body.Text markdown $"Example {number} AST body range"
                    Expect.sequenceEqual (document.Reconstruct()) (Encoding.UTF8.GetBytes input) $"Example {number} reconstruction"
                    match number with
                    | 62 ->
                        let kinds = document.Ast.Body.Children |> Seq.map _.Kind |> Seq.toList
                        Expect.equal kinds (List.map MarkdownKind.Heading [1..6]) "All six CommonMark ATX heading levels"
                    | 302 ->
                        let kinds = document.Ast.Body.Children |> Seq.map _.Kind |> Seq.toList
                        Expect.equal kinds
                            [ MarkdownKind.List(true, ValueSome 1, true); MarkdownKind.List(true, ValueSome 3, true) ]
                            "Each ordered list retains its start and tightness"
                    | _ -> ())
        ]
