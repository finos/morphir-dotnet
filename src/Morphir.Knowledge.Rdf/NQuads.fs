// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open System.Text
open Morphir.Knowledge

/// The kind of problem encountered while reading an N-Quads document.
[<RequireQualifiedAccess>]
type NQuadsReadErrorKind =
    | InvalidUtf8
    | InvalidSubject
    | InvalidPredicate
    | InvalidObject
    | InvalidGraph
    | InvalidIri
    | InvalidBlankNode
    | InvalidLiteral
    | InvalidEscape
    | MissingTerminator
    | UnexpectedContent
    /// An N-Triples statement has a fourth term.
    | GraphLabelNotAllowed

    /// A stable diagnostic code.
    member this.Code =
        match this with
        | NQuadsReadErrorKind.InvalidUtf8 -> "nquads.invalid-utf8"
        | NQuadsReadErrorKind.InvalidSubject -> "nquads.invalid-subject"
        | NQuadsReadErrorKind.InvalidPredicate -> "nquads.invalid-predicate"
        | NQuadsReadErrorKind.InvalidObject -> "nquads.invalid-object"
        | NQuadsReadErrorKind.InvalidGraph -> "nquads.invalid-graph"
        | NQuadsReadErrorKind.InvalidIri -> "nquads.invalid-iri"
        | NQuadsReadErrorKind.InvalidBlankNode -> "nquads.invalid-blank-node"
        | NQuadsReadErrorKind.InvalidLiteral -> "nquads.invalid-literal"
        | NQuadsReadErrorKind.InvalidEscape -> "nquads.invalid-escape"
        | NQuadsReadErrorKind.MissingTerminator -> "nquads.missing-terminator"
        | NQuadsReadErrorKind.UnexpectedContent -> "nquads.unexpected-content"
        | NQuadsReadErrorKind.GraphLabelNotAllowed -> "ntriples.graph-label"

    override this.ToString() = this.Code

/// An N-Quads or N-Triples diagnostic and its UTF-16 source range when decoding succeeded.
type NQuadsReadError =
    { /// The kind of problem.
      Kind: NQuadsReadErrorKind
      /// An explanation of the problem.
      Message: string
      /// A range in the original text, for F# callers.
      Span: SourceSpan voption }

    /// Gets the source range if present. For C# callers.
    member this.TryGetSpan([<Out>] span: byref<SourceSpan>) =
        match this.Span with
        | ValueSome range ->
            span <- range
            true
        | ValueNone ->
            span <- Unchecked.defaultof<_>
            false

    override this.ToString() = $"{this.Kind.Code}: {this.Message}"

/// How to handle an empty named graph during N-Quads interchange.
[<RequireQualifiedAccess>]
type EmptyGraphHandling =
    | Reject
    | Omit

    override this.ToString() =
        match this with
        | EmptyGraphHandling.Reject -> "reject empty named graphs"
        | EmptyGraphHandling.Omit -> "omit empty named graphs"

/// The dataset cannot be represented according to the requested write policy.
[<RequireQualifiedAccess>]
type NQuadsWriteError =
    | EmptyNamedGraphs of ImmutableArray<GraphName>

    /// The named graphs that cannot be represented.
    member this.Graphs =
        let (NQuadsWriteError.EmptyNamedGraphs graphs) = this
        graphs

    override this.ToString() = $"N-Quads cannot encode {this.Graphs.Length} empty named graph(s)."

/// A serialized N-Quads document and any empty named graphs explicitly omitted.
type NQuadsOutput =
    { /// The serialized UTF-8 bytes.
      Bytes: byte array
      /// Empty named graphs omitted under the selected policy.
      OmittedGraphs: ImmutableArray<GraphName> }

    override this.ToString() = $"N-Quads document ({this.Bytes.Length} bytes)"

exception internal ParseError of NQuadsReadError

module internal NQuadsGrammar =
    let whitespace ch = ch = ' ' || ch = '\t'

    let notIriCharacter ch =
        ch <= ' ' || ch = '<' || ch = '>' || ch = '"' || ch = '{' || ch = '}' || ch = '|'
        || ch = '^' || ch = '`' || ch = '\\'

    let blankNodeBase scalar =
        scalar >= int 'A' && scalar <= int 'Z'
        || scalar >= int 'a' && scalar <= int 'z'
        || scalar >= 0x00C0 && scalar <= 0x00D6
        || scalar >= 0x00D8 && scalar <= 0x00F6
        || scalar >= 0x00F8 && scalar <= 0x02FF
        || scalar >= 0x0370 && scalar <= 0x037D
        || scalar >= 0x037F && scalar <= 0x1FFF
        || scalar >= 0x200C && scalar <= 0x200D
        || scalar >= 0x2070 && scalar <= 0x218F
        || scalar >= 0x2C00 && scalar <= 0x2FEF
        || scalar >= 0x3001 && scalar <= 0xD7FF
        || scalar >= 0xF900 && scalar <= 0xFDCF
        || scalar >= 0xFDF0 && scalar <= 0xFFFD
        || scalar >= 0x10000 && scalar <= 0xEFFFF

    let blankNodeStart scalar =
        // The RDF 1.1 PN_CHARS_U erratum removes ':' from blank-node labels.
        blankNodeBase scalar || scalar = int '_'
        || (scalar >= int '0' && scalar <= int '9')

    let blankNodeRest scalar =
        blankNodeStart scalar || scalar = int '-' || scalar = 0x00B7
        || scalar >= 0x0300 && scalar <= 0x036F
        || scalar >= 0x203F && scalar <= 0x2040

    let strictUtf8 = UTF8Encoding(false, true)

/// The parser owns positions within one line and shares a blank-node table over the whole import.
type internal NQuadsLine(text: string, start: int, finish: int, blankNodes: Dictionary<string, BlankNodeId>, allowGraph: bool) =
    let mutable offset = start

    let fail kind message first last =
        let span = SourceSpan.Create(TextOffset.Create first, TextOffset.Create last)
        raise (ParseError { Kind = kind; Message = message; Span = ValueSome span })

    let failHere kind message =
        fail kind message offset (min finish (offset + 1))

    let skipWhitespace () =
        while offset < finish && NQuadsGrammar.whitespace text[offset] do
            offset <- offset + 1

    let take ch =
        if offset < finish && text[offset] = ch then
            offset <- offset + 1
            true
        else false

    let hex ch =
        if ch >= '0' && ch <= '9' then int ch - int '0'
        elif ch >= 'A' && ch <= 'F' then int ch - int 'A' + 10
        elif ch >= 'a' && ch <= 'f' then int ch - int 'a' + 10
        else -1

    let readUnicodeEscape (builder: StringBuilder) first digits =
        if finish - offset < digits then
            fail NQuadsReadErrorKind.InvalidEscape "Incomplete Unicode escape." first finish

        let mutable scalar = 0u
        for _ in 1..digits do
            let digit = hex text[offset]
            if digit < 0 then
                fail NQuadsReadErrorKind.InvalidEscape "Expected a hexadecimal digit." first (offset + 1)
            scalar <- (scalar <<< 4) ||| uint32 digit
            offset <- offset + 1

        if scalar > 0x10FFFFu || not (Rune.IsValid(int scalar)) then
            fail NQuadsReadErrorKind.InvalidEscape "The escape must identify a Unicode scalar." first offset

        builder.Append(Char.ConvertFromUtf32(int scalar)) |> ignore

    let readEscape (builder: StringBuilder) literal =
        let first = offset
        offset <- offset + 1
        if offset = finish then
            fail NQuadsReadErrorKind.InvalidEscape "Incomplete escape." first offset

        let marker = text[offset]
        offset <- offset + 1
        match marker with
        | 'u' -> readUnicodeEscape builder first 4
        | 'U' -> readUnicodeEscape builder first 8
        | 't' when literal -> builder.Append '\t' |> ignore
        | 'b' when literal -> builder.Append '\b' |> ignore
        | 'n' when literal -> builder.Append '\n' |> ignore
        | 'r' when literal -> builder.Append '\r' |> ignore
        | 'f' when literal -> builder.Append '\f' |> ignore
        | '"' | '\'' | '\\' when literal -> builder.Append marker |> ignore
        | _ -> fail NQuadsReadErrorKind.InvalidEscape "Unknown escape sequence." first offset

    let readIri kind (missing: string) =
        let first = offset
        if not (take '<') then failHere kind missing
        let builder = StringBuilder()

        while offset < finish && text[offset] <> '>' do
            let ch = text[offset]
            if ch = '\\' then readEscape builder false
            elif NQuadsGrammar.notIriCharacter ch then
                failHere NQuadsReadErrorKind.InvalidIri "Invalid character in an IRI token."
            else
                builder.Append ch |> ignore
                offset <- offset + 1

        if not (take '>') then
            fail NQuadsReadErrorKind.InvalidIri "Unterminated IRI token." first offset

        match Iri.Parse(builder.ToString()) with
        | Ok value -> value
        | Error _ -> fail NQuadsReadErrorKind.InvalidIri "Expected an absolute IRI." first offset

    let scalarAt position =
        if Char.IsHighSurrogate text[position] then
            Char.ConvertToUtf32(text[position], text[position + 1]), 2
        else int text[position], 1

    let readBlankNode () =
        let first = offset
        offset <- offset + 1
        if not (take ':') || offset = finish then
            fail NQuadsReadErrorKind.InvalidBlankNode "Expected a blank node label after '_:'." first offset

        let labelStart = offset
        let scalar, count = scalarAt offset
        if not (NQuadsGrammar.blankNodeStart scalar) then
            fail NQuadsReadErrorKind.InvalidBlankNode "Invalid first character in a blank node label." first (offset + count)

        offset <- offset + count
        let mutable scan = offset
        let mutable scanning = true
        while scan < finish && scanning do
            let scalar, count = scalarAt scan
            if NQuadsGrammar.blankNodeRest scalar then
                scan <- scan + count
                offset <- scan
            elif scalar = int '.' then
                scan <- scan + count
            else
                scanning <- false

        let label = text.Substring(labelStart, offset - labelStart)
        match blankNodes.TryGetValue label with
        | true, value -> value
        | false, _ ->
            let value = BlankNodeId.New()
            blankNodes.Add(label, value)
            value

    let readLiteral () =
        let first = offset
        offset <- offset + 1
        let builder = StringBuilder()

        while offset < finish && text[offset] <> '"' do
            if text[offset] = '\\' then readEscape builder true
            else
                builder.Append text[offset] |> ignore
                offset <- offset + 1

        if not (take '"') then
            fail NQuadsReadErrorKind.InvalidLiteral "Unterminated literal." first offset

        let lexical = builder.ToString()
        let result =
            if take '@' then
                let languageStart = offset
                while offset < finish && (Char.IsAsciiLetterOrDigit text[offset] || text[offset] = '-') do
                    offset <- offset + 1
                RdfLiteral.LanguageTagged(lexical, text.Substring(languageStart, offset - languageStart))
            elif take '^' then
                if not (take '^') then
                    failHere NQuadsReadErrorKind.InvalidLiteral "Expected '^^' before the datatype."
                let datatype = readIri NQuadsReadErrorKind.InvalidLiteral "Expected a datatype IRI."
                RdfLiteral.Typed(lexical, datatype)
            else
                RdfLiteral.String lexical

        match result with
        | Ok value -> value
        | Error error -> fail NQuadsReadErrorKind.InvalidLiteral (error.ToString()) first offset

    let readTerm kind message iri blankNode =
        if offset = finish then failHere kind message
        match text[offset] with
        | '<' -> iri (readIri kind message)
        | '_' -> blankNode (readBlankNode ())
        | _ -> failHere kind message

    member _.Read() =
        skipWhitespace ()
        if offset = finish || text[offset] = '#' then
            ValueNone
        else
            let subject = readTerm NQuadsReadErrorKind.InvalidSubject "Expected an IRI or a blank node subject." RdfSubject.Iri RdfSubject.BlankNode
            skipWhitespace ()
            let predicate = readIri NQuadsReadErrorKind.InvalidPredicate "Expected a predicate IRI."
            skipWhitespace ()
            let objectValue =
                if offset < finish && text[offset] = '"' then RdfObject.Literal(readLiteral ())
                else readTerm NQuadsReadErrorKind.InvalidObject "Expected an IRI, blank node or literal object." RdfObject.Iri RdfObject.BlankNode

            skipWhitespace ()
            let graph =
                if offset = finish || text[offset] = '.' || text[offset] = '#' then GraphLocation.DefaultGraph
                elif not allowGraph then
                    let first = offset
                    while offset < finish && not (NQuadsGrammar.whitespace text[offset]) do
                        offset <- offset + 1
                    fail NQuadsReadErrorKind.GraphLabelNotAllowed "N-Triples statements have no graph label." first offset
                else
                    readTerm NQuadsReadErrorKind.InvalidGraph "Expected an IRI or a blank node graph label." GraphName.IriName GraphName.BlankNodeName
                    |> GraphLocation.NamedGraph

            skipWhitespace ()
            if not (take '.') then
                failHere NQuadsReadErrorKind.MissingTerminator "Expected a period at the end of the statement."

            skipWhitespace ()
            if offset < finish && text[offset] <> '#' then
                failHere NQuadsReadErrorKind.UnexpectedContent "Unexpected content after the statement."

            ValueSome { Subject = subject; Predicate = predicate; Object = objectValue; Graph = graph }

module internal NQuadsWriter =
    let write (dataset: RdfDataset) =
        let output = StringBuilder()
        let labels = Dictionary<BlankNodeId, string>()

        let append (text: string) = output.Append text |> ignore

        let escape (ch: char) =
            append "\\u"
            append ((int ch).ToString("X4", CultureInfo.InvariantCulture))

        let iri (value: Iri) =
            append "<"
            for ch in value.Value do
                if NQuadsGrammar.notIriCharacter ch then escape ch
                else output.Append ch |> ignore
            append ">"

        let blankNode value =
            match labels.TryGetValue value with
            | true, label -> append label
            | false, _ ->
                let label = "_:b" + labels.Count.ToString(CultureInfo.InvariantCulture)
                labels.Add(value, label)
                append label

        let literal (value: RdfLiteral) =
            append "\""
            for ch in value.LexicalForm do
                match ch with
                | '"' -> append "\\\""
                | '\\' -> append "\\\\"
                | '\n' -> append "\\n"
                | '\r' -> append "\\r"
                | ch when Char.IsControl ch -> escape ch
                | ch -> output.Append ch |> ignore
            append "\""
            match value.LanguageTag with
            | Some language ->
                append "@"
                append language
            | None when value.Datatype.Value <> "http://www.w3.org/2001/XMLSchema#string" ->
                append "^^"
                iri value.Datatype
            | None -> ()

        for quad in dataset.EnumerateQuads do
            match quad.Subject with
            | RdfSubject.Iri value -> iri value
            | RdfSubject.BlankNode value -> blankNode value
            append " "
            iri quad.Predicate
            append " "
            match quad.Object with
            | RdfObject.Iri value -> iri value
            | RdfObject.BlankNode value -> blankNode value
            | RdfObject.Literal value -> literal value
            match quad.Graph with
            | GraphLocation.DefaultGraph -> ()
            | GraphLocation.NamedGraph name ->
                append " "
                match name with
                | GraphName.IriName value -> iri value
                | GraphName.BlankNodeName value -> blankNode value
            append " .\n"

        NQuadsGrammar.strictUtf8.GetBytes(output.ToString())

module internal NQuadsReading =
    /// Reads statements line by line with one blank-node table for the whole input.
    let read allowGraph (source: ReadOnlyMemory<byte>) =
        try
            let text = NQuadsGrammar.strictUtf8.GetString source.Span
            let blankNodes = Dictionary<string, BlankNodeId>(StringComparer.Ordinal)
            let mutable dataset = RdfDataset.Empty
            let mutable start = 0

            while start < text.Length do
                let mutable finish = start
                while finish < text.Length && text[finish] <> '\n' && text[finish] <> '\r' do
                    finish <- finish + 1

                match NQuadsLine(text, start, finish, blankNodes, allowGraph).Read() with
                | ValueSome quad -> dataset <- dataset.Add quad
                | ValueNone -> ()

                start <- finish
                while start < text.Length && (text[start] = '\n' || text[start] = '\r') do
                    start <- start + 1

            Ok dataset
        with
        | ParseError error -> Error error
        | :? DecoderFallbackException ->
            Error { Kind = NQuadsReadErrorKind.InvalidUtf8; Message = "The input is not valid UTF-8."; Span = ValueNone }

/// A UTF-8 RDF 1.1 N-Quads reader and writer.
[<AbstractClass; Sealed>]
type NQuads private () =
    /// Reads UTF-8 bytes, with fresh blank-node identities for this input. For F# callers.
    static member Read(source: ReadOnlyMemory<byte>) = NQuadsReading.read true source

    /// Reads UTF-8 bytes, with fresh blank-node identities for this input. For C# callers.
    static member TryRead(
        source: ReadOnlyMemory<byte>,
        [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<NQuadsReadError>
    ) =
        match NQuads.Read source with
        | Ok value ->
            dataset <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            dataset <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Writes a dataset according to the caller's empty-graph policy. For F# callers.
    static member Write(dataset: RdfDataset, emptyGraphs: EmptyGraphHandling) =
        let omitted =
            dataset.NamedGraphs
            |> Seq.choose (fun pair -> if pair.Value.IsEmpty then Some pair.Key else None)
            |> ImmutableArray.CreateRange

        if not omitted.IsEmpty && emptyGraphs = EmptyGraphHandling.Reject then
            Error(NQuadsWriteError.EmptyNamedGraphs omitted)
        else
            Ok { Bytes = NQuadsWriter.write dataset; OmittedGraphs = omitted }

    /// Writes a dataset and rejects empty named graphs. For F# callers.
    static member Write(dataset: RdfDataset) =
        NQuads.Write(dataset, EmptyGraphHandling.Reject)

    /// Writes a dataset according to the caller's empty-graph policy. For C# callers.
    static member TryWrite(
        dataset: RdfDataset,
        emptyGraphs: EmptyGraphHandling,
        [<Out; MaybeNullWhen(false)>] output: byref<NQuadsOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<NQuadsWriteError>
    ) =
        match NQuads.Write(dataset, emptyGraphs) with
        | Ok value ->
            output <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            output <- Unchecked.defaultof<_>
            error <- failure
            false
