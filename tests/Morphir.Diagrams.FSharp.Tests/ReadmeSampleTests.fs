// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The F# samples of src/Morphir.Diagrams/README.md, repeated here, each test naming its README section.
/// A test counts the README's F# blocks, so a new sample needs a test here.
module Morphir.Diagrams.FSharp.Tests.ReadmeSampleTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Morphir.Diagrams
open Morphir.Diagrams.FSharp.Tests
open Morphir.Knowledge

let private readme =
    lazy
        use stream =
            Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Morphir.Diagrams.README.md")
            |> Option.ofObj
            |> Option.defaultWith (fun () -> failwith "The README is not embedded.")

        use reader = new StreamReader(stream)
        reader.ReadToEnd()

[<Tests>]
let tests =
    testList "Diagrams README" [
        testCase "the README has the three F# samples that this project repeats" <| fun () ->
            Expect.equal (Regex.Matches(readme.Value, "```fsharp").Count) 3 "add a test here for each new F# sample"

        testCase "the F# sample of 'F#' runs" <| fun () ->
            let source = "flowchart LR\n  Read[Read request] --> Valid{Valid?}"

            match Mermaid.Parse source with
            | Ok document ->
                let rendered = SvgRenderer.Render document.Diagram
                Expect.isTrue (rendered.Output |> Option.ofObj |> Option.isSome) "the sample renders SVG"
            | Error diagnostics -> failtest (String.Join("\n", diagnostics))

        testCase "the F# sample of 'Themes' runs" <| fun () ->
            let source = "flowchart LR\n  Cart --> Billing"
            let options = SvgRenderOptions.Create(DiagramThemes.Cobalt, ThemeMode.Dark)
            let svg = Mermaid.RenderSvg(source, options)
            Expect.stringContains (svg.Output |> Option.ofObj |> Option.defaultValue "") "#193549" "the fixed dark Cobalt palette is written"

        testCase "the F# sample of 'Selections, comments and links' runs" <| fun () ->
            let source = DiagramMother.checkout
            let checkout = DiagramMother.checkoutDocument

            let snapshot =
                match Mermaid.Parse source |> Result.mapError (fun _ -> "parse") |> Result.bind (fun document ->
                    DiagramSnapshot.TryFromSource(checkout, document) |> Result.mapError (fun error -> error.ToString())) with
                | Ok snapshot -> snapshot
                | Error error -> failwith error

            let edge =
                match DiagramSelection.TryCreate(snapshot, [ DiagramElementRef.Create "edge=Cart/Billing/0" ]) with
                | Ok selection -> selection
                | Error error -> failwith (error.ToString())

            let comment: Annotation<DiagramSelection, string> =
                { Id = AnnotationId.Create(Iri.Create "urn:review:1")
                  Target = edge
                  Body = "Is this call idempotent?"
                  Provenance = { Author = Iri.Create "urn:reviewer:one"; CreatedAt = DateTimeOffset.UtcNow } }

            Expect.equal (snapshot.ElementIri comment.Target.Elements[0]).Value "https://example.org/diagrams/checkout#edge=Cart/Billing/0" "the comment targets the first edge"

        testCase "a selection of the same elements in another order is equal" <| fun () ->
            let snapshot = DiagramMother.snapshot DiagramMother.pair
            Expect.equal (DiagramMother.select snapshot [ "node=B"; "node=A" ]) (DiagramMother.select snapshot [ "node=A"; "node=B"; "node=A" ]) "F# structural equality uses the selection's elements"
    ]
