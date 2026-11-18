// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>Source trees for fresh consumers of the individual knowledge packages.</summary>
public sealed record KnowledgePackageExample(string Package, string FSharp, string CSharp, string? Concept = null);

/// <summary>Named package examples shared by the two consumer languages.</summary>
public static class KnowledgePackageExamples
{
    public static KnowledgePackageExample For(string capability) => capability switch
    {
        "selection" => Selection,
        "bundle" => Bundle,
        "N-Quads" => Rdf,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown knowledge capability."),
    };

    private static readonly KnowledgePackageExample Selection = new("Morphir.Knowledge",
        """
        open Morphir.Knowledge

        let document = DocumentId.Create(Iri.Create "https://example.org/revenue")
        let version = { Document = document; Revision = RevisionId.Create "review-1" }
        let snapshot = TextSnapshot.Create(version, "net and gross revenue")
        let span start finish = SourceSpan.Create(TextOffset.Create start, TextOffset.Create finish)
        match TextSelection.TryCreate(snapshot, [ span 8 13; span 0 3 ]) with
        | Ok selection ->
            match selection.TryRead(snapshot) with
            | Ok passages when Seq.toList passages = [ "net"; "gross" ] -> printfn "knowledge example passed"
            | _ -> failwith "Wrong selection"
        | Error error -> failwith (error.ToString())
        """,
        """
        using Morphir.Knowledge;

        var version = new DocumentVersion(DocumentId.Create(Iri.Create("https://example.org/revenue")), RevisionId.Create("review-1"));
        var snapshot = TextSnapshot.Create(version, "net and gross revenue");
        SourceSpan Span(int start, int finish) => SourceSpan.Create(TextOffset.Create(start), TextOffset.Create(finish));
        if (!TextSelection.TryCreate(snapshot, [Span(8, 13), Span(0, 3)], out var selection, out var error))
            throw new InvalidOperationException(error.ToString());
        if (!selection.TryRead(snapshot, out var passages, out error))
            throw new InvalidOperationException(error.ToString());
        if (!passages.SequenceEqual(["net", "gross"])) throw new InvalidOperationException("Wrong selection.");
        Console.WriteLine("knowledge example passed");
        """);

    private static readonly KnowledgePackageExample Bundle = new("Morphir.Knowledge.Okf",
        """
        open System.Threading
        open Morphir.Knowledge.Okf

        [<EntryPoint>]
        let main args =
            let report = BundleValidation.validateDirectory None BundlePolicy.Standard args[0] CancellationToken.None
            if report.ExitCode <> 0 || report.Conformance.WireValue <> "conformant" then failwith "Bundle did not conform."
            match OkfDocument.Parse("# Revenue") with
            | OkfParseResult.ParsedDocument(document, _) ->
                let mutable markdown = 0
                document.AstCursor
                |> Cursor.walk (fun node ->
                    match node.Node with
                    | AstNode.Markdown _ -> markdown <- markdown + 1
                    | _ -> ()
                    VisitControl.Continue)
                if markdown < 2 then failwith "The AST walk missed the Markdown."
            | OkfParseResult.UnreadableSource error -> failwith (error.ToString())
            printfn "knowledge example passed"
            0
        """,
        """
        using Morphir.Knowledge.Okf;

        var report = BundleValidation.ValidateDirectory(args[0], null, BundlePolicy.Standard, CancellationToken.None);
        if (report.ExitCode != 0 || report.Conformance.WireValue != "conformant")
            throw new InvalidOperationException("Bundle did not conform.");
        var document = OkfDocument.Parse("# Revenue").Match((parsed, _) => parsed,
            error => throw new InvalidOperationException(error.ToString()));
        var markdown = 0;
        document.AstCursor.Walk(cursor =>
        {
            markdown += cursor.Node.Match(_ => 0, _ => 0, _ => 0, _ => 1);
            return VisitControl.Continue;
        });
        if (markdown < 2) throw new InvalidOperationException("The AST walk missed the Markdown.");
        Console.WriteLine("knowledge example passed");
        """,
        """
        ---
        type: Metric
        title: Revenue
        ---
        # Revenue

        Income earned during the reporting period.
        """);

    private static readonly KnowledgePackageExample Rdf = new("Morphir.Knowledge.Rdf",
        """
        open System
        open System.Text
        open Morphir.Knowledge.Rdf

        let bytes = Encoding.UTF8.GetBytes("<https://example.org/net> <https://example.org/means> \"Net revenue\" <https://example.org/approved> .\n")
        let read (bytes: byte array) =
            match NQuads.Read(ReadOnlyMemory<byte>(bytes)) with
            | Ok dataset -> dataset
            | Error error -> failwith (error.ToString())
        match NQuads.Write(read bytes) with
        | Ok output when (read output.Bytes).Quads.Length = 1 -> printfn "knowledge example passed"
        | _ -> failwith "Unexpected N-Quads result."
        """,
        """
        using System.Text;
        using Morphir.Knowledge.Rdf;

        var bytes = Encoding.UTF8.GetBytes("<https://example.org/net> <https://example.org/means> \"Net revenue\" <https://example.org/approved> .\n");
        if (!NQuads.TryRead(bytes, out var dataset, out var readError))
            throw new InvalidOperationException(readError.ToString());
        if (!NQuads.TryWrite(dataset, EmptyGraphHandling.Reject, out var output, out var writeError))
            throw new InvalidOperationException(writeError.ToString());
        if (!NQuads.TryRead(output.Bytes, out var parsed, out readError))
            throw new InvalidOperationException(readError.ToString());
        if (parsed.Quads.Length != 1) throw new InvalidOperationException("Unexpected quad count.");
        Console.WriteLine("knowledge example passed");
        """);
}
