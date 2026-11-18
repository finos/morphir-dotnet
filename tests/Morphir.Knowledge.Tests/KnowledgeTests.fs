// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Tests.KnowledgeTests

open System
open Expecto
open Morphir.Knowledge

let private span start finish =
    SourceSpan.Create(TextOffset.Create start, TextOffset.Create finish)

let private expectOk = function
    | Ok value -> value
    | Error reason -> failtestf "%O" reason

let private reviewedVersion =
    { Document = DocumentId.Create(Iri.Create "https://example.org/metrics/revenue")
      Revision = RevisionId.Create "commit-one" }

let private reviewedRevenue () =
    TextSnapshot.Create(reviewedVersion, "Net 🧮 revenue and gross revenue")

[<Tests>]
let tests =
    testList "Knowledge contracts" [
        testCase "IRI preserves Unicode and compares exact spelling"
        <| fun () ->
            let original = "https://例え.example/café/%41#résumé"
            let iri = Iri.Create original
            Expect.equal iri.Value original "preserves the original IRI"
            Expect.notEqual iri (Iri.Create(original.Replace("%41", "A"))) "no percent normalization"
            Expect.notEqual (Iri.Create "https://Example.org/x") (Iri.Create "https://example.org/x") "ordinal equality"

        testCase "IRIs require absolute, valid spellings"
        <| fun () ->
            for invalid in [ "metrics/revenue"; "https://example.org/a b"; "urn:example:%xx"; "https://[::gg]/"; "urn:x#one#two" ] do
                Expect.equal (Iri.Parse invalid) (Error IdentifierError.InvalidIri) invalid

            Expect.equal (RevisionId.Parse "") (Error IdentifierError.Empty) "empty revision"
            Expect.equal (RevisionId.Create "rev:1").Value "rev:1" "opaque revision"

        testCase "Offsets and endpoints have valid zero values"
        <| fun () ->
            Expect.equal sizeof<TextOffset> 4 "32-bit offset"
            Expect.equal sizeof<SourceSpan> 8 "two 32-bit endpoints"
            Expect.equal (Unchecked.defaultof<TextOffset>).Value 0 "default offset"
            Expect.equal (Unchecked.defaultof<SourceSpan>) (span 0 0) "default empty span"
            Expect.equal (TextOffset.TryCreate(-1)) ValueNone "negative offset"
            Expect.equal (SourceSpan.TryCreate(TextOffset.Create 3, TextOffset.Create 2)) ValueNone "reversed range"
            Expect.equal ((reviewedRevenue ()).TryRead(span 4 6)) (ValueSome "🧮") "UTF-16 coordinates"

        testCase "One selection holds disjoint ranges in source order"
        <| fun () ->
            let original = reviewedRevenue ()
            let selection = TextSelection.TryCreate(original, [ span 19 24; span 7 14 ]) |> expectOk
            Expect.sequenceEqual selection.Ranges [ span 7 14; span 19 24 ] "ranges are sorted"
            let text = selection.TryRead(original) |> expectOk
            Expect.sequenceEqual text [ "revenue"; "gross" ] "both passages"
            let newer = TextSnapshot.Create({ reviewedVersion with Revision = RevisionId.Create "commit-two" }, "Updated")
            Expect.equal (selection.TryRead(newer)) (Error SelectionError.VersionMismatch) "newer revision rejected"
            let movedSource = Map.ofList [ "archive/net-revenue.md", original ]
            let resolved = movedSource |> Map.values |> Seq.find (fun source -> source.Version = selection.Version)
            let movedText = selection.TryRead(resolved) |> expectOk
            Expect.sequenceEqual movedText text "a move preserves source identity"

        testCase "Selection errors distinguish invalid ranges"
        <| fun () ->
            let source = reviewedRevenue ()
            Expect.equal (TextSelection.TryCreate(source, [])) (Error SelectionError.EmptySelection) "empty"
            Expect.equal (TextSelection.TryCreate(source, [ span 2 2 ])) (Error(SelectionError.EmptyRange(span 2 2))) "zero length"
            Expect.equal (TextSelection.TryCreate(source, [ span 0 50 ])) (Error(SelectionError.OutOfBounds(span 0 50))) "past end"
            Expect.equal (TextSelection.TryCreate(source, [ span 0 2; span 1 3 ])) (Error(SelectionError.OverlappingRanges(span 0 2, span 1 3))) "overlap"

        testCase "A custom annotation and dictionary link share the original selection"
        <| fun () ->
            let source = reviewedRevenue ()
            let selection = TextSelection.TryCreate(source, [ span 7 14 ]) |> expectOk

            let annotation: Annotation<TextSelection, {| Comment: string |}> =
                { Id = AnnotationId.Create(Iri.Create "urn:reviews:one")
                  Target = selection
                  Body = {| Comment = "Clarify the definition" |}
                  Provenance =
                    { Author = Iri.Create "urn:reviewer:one"
                      CreatedAt = DateTimeOffset.UnixEpoch } }

            let link: KnowledgeLink<TextSelection, Iri> =
                { Id = LinkId.Create(Iri.Create "urn:links:one")
                  Source = annotation.Target
                  Predicate = Iri.Create "https://example.org/mentions"
                  Target = Iri.Create "https://example.org/dictionary/revenue" }

            Expect.equal annotation.Body.Comment "Clarify the definition" "typed body"
            Expect.equal link.Target.Value "https://example.org/dictionary/revenue" "dictionary identity"
            let text = link.Source.TryRead(source) |> expectOk
            Expect.sequenceEqual text [ "revenue" ] "source text is unchanged"
    ]
