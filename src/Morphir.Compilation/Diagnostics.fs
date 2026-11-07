namespace Morphir.Compilation

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.Json

/// <summary>The severity of a diagnostic, with the MEP wire values.</summary>
[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    /// <summary>A problem that fails the run.</summary>
    | Error
    /// <summary>A problem that does not fail the run.</summary>
    | Warning
    /// <summary>Information.</summary>
    | Info
    /// <summary>A suggestion.</summary>
    | Hint

    /// <summary>Calls the function for this severity. For C# callers.</summary>
    member this.Match<'R>(error: Func<'R>, warning: Func<'R>, info: Func<'R>, hint: Func<'R>) : 'R =
        match this with
        | Error -> error.Invoke()
        | Warning -> warning.Invoke()
        | Info -> info.Invoke()
        | Hint -> hint.Invoke()

    /// <summary>The MEP wire value: <c>error</c>, <c>warning</c>, <c>info</c> or <c>hint</c>.</summary>
    member this.WireValue =
        match this with
        | Error -> "error"
        | Warning -> "warning"
        | Info -> "info"
        | Hint -> "hint"

    override this.ToString() = this.WireValue

/// <summary>Why a diagnostic code was refused.</summary>
[<RequireQualifiedAccess>]
type DiagnosticCodeFailure =
    /// <summary>The text is null or empty.</summary>
    | Empty
    /// <summary>The zero-based dot-separated segment is not a valid token.</summary>
    | InvalidSegment of segment: int

    override this.ToString() =
        match this with
        | Empty -> "A diagnostic code is empty."
        | InvalidSegment segment ->
            $"Diagnostic code segment {segment} is not a lowercase token of letters, digits, '_' or '-'."

/// <summary>
/// A stable diagnostic code, for example <c>fsharp.unsupported_mutable</c>: lowercase tokens joined by dots.
/// Build one with <c>Parse</c>, <c>TryParse</c> (for C# callers) or <c>Create</c> (for literals).
/// </summary>
[<CustomEquality; CustomComparison>]
type DiagnosticCode =
    private
    | DiagnosticCode of string

    /// <summary>The code text.</summary>
    member this.Value = let (DiagnosticCode value) = this in value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? DiagnosticCode as code ->
            String.Equals(this.Value, code.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? DiagnosticCode as code -> String.CompareOrdinal(this.Value, code.Value)
            | _ -> invalidArg (nameof other) "Not a DiagnosticCode."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<DiagnosticCode, DiagnosticCodeFailure> =
        match text with
        | null -> Error DiagnosticCodeFailure.Empty
        | value when value.Length = 0 -> Error DiagnosticCodeFailure.Empty
        | value ->
            let isToken (segment: string) =
                segment.Length > 0
                && Char.IsAsciiLetterLower segment[0]
                && segment
                   |> Seq.forall (fun c ->
                       Char.IsAsciiLetterLower c
                       || Char.IsAsciiDigit c
                       || c = '_'
                       || c = '-'
                   )

            match
                value.Split '.'
                |> Array.tryFindIndex (
                    isToken
                    >> not
                )
            with
            | Some index -> Error(DiagnosticCodeFailure.InvalidSegment index)
            | None -> Ok(DiagnosticCode value)

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<DiagnosticCode>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<DiagnosticCodeFailure>)
        : bool =
        match DiagnosticCode.Parse text with
        | Ok code ->
            result <- code
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds a code from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : DiagnosticCode =
        match DiagnosticCode.Parse literal with
        | Ok code -> code
        | Error error -> invalidArg (nameof literal) (error.ToString())

/// <summary>A zero-based position. <c>Character</c> counts UTF-16 code units, as in MEP and LSP.</summary>
type Position =
    private
        {
            LineValue: int
            CharacterValue: int
        }

    /// <summary>The zero-based line.</summary>
    member this.Line = this.LineValue

    /// <summary>The zero-based character, in UTF-16 code units.</summary>
    member this.Character = this.CharacterValue

    override this.ToString() =
        $"{this.LineValue}:{this.CharacterValue}"

    /// <summary>Builds a position. Throws <c>ArgumentOutOfRangeException</c> for a negative line or character.</summary>
    static member Create(line: int, character: int) : Position =
        if line < 0 then
            raise (
                ArgumentOutOfRangeException(
                    nameof line,
                    line,
                    "A line is zero-based and not negative."
                )
            )

        if character < 0 then
            raise (
                ArgumentOutOfRangeException(
                    nameof character,
                    character,
                    "A character is zero-based and not negative."
                )
            )

        {
            LineValue = line
            CharacterValue = character
        }

    /// <summary>
    /// The position of a UTF-16 offset in <paramref name="text"/>. <c>\r\n</c>, <c>\n</c> and <c>\r</c> end a line.
    /// Throws <c>ArgumentOutOfRangeException</c> when the offset is outside the text.
    /// </summary>
    static member FromOffset(text: string, offset: int) : Position =
        if
            offset < 0
            || offset > text.Length
        then
            raise (
                ArgumentOutOfRangeException(
                    nameof offset,
                    offset,
                    "The offset is outside the text."
                )
            )

        let mutable line = 0
        let mutable lineStart = 0
        let mutable index = 0

        while index < offset do
            match text[index] with
            | '\r' when
                index + 1 < text.Length
                && text[index + 1] = '\n'
                ->
                if index + 1 < offset then
                    line <- line + 1
                    lineStart <- index + 2

                index <- index + 2
            | '\r'
            | '\n' ->
                line <- line + 1
                lineStart <- index + 1
                index <- index + 1
            | _ -> index <- index + 1

        {
            LineValue = line
            CharacterValue =
                offset
                - lineStart
        }

/// <summary>A range with an exclusive end. The start is never after the end.</summary>
type Range =
    private
        {
            StartValue: Position
            EndValue: Position
        }

    /// <summary>The first position in the range.</summary>
    member this.Start = this.StartValue

    /// <summary>The position after the range (exclusive).</summary>
    member this.End = this.EndValue

    override this.ToString() = $"{this.StartValue}-{this.EndValue}"

    /// <summary>Builds a range. Throws <c>ArgumentException</c> when <paramref name="start"/> is after <paramref name="finish"/>.</summary>
    static member Create(start: Position, finish: Position) : Range =
        if (start.Line, start.Character) > (finish.Line, finish.Character) then
            invalidArg (nameof finish) $"The range end {finish} is before its start {start}."

        {
            StartValue = start
            EndValue = finish
        }

/// <summary>A range in one source document, named by its URI as the request gave it.</summary>
type Location =
    private
        {
            UriValue: string
            RangeValue: Range
        }

    /// <summary>The document URI.</summary>
    member this.Uri = this.UriValue

    /// <summary>The range in the document.</summary>
    member this.Range = this.RangeValue

    override this.ToString() = $"{this.UriValue}:{this.RangeValue}"

    /// <summary>Builds a location. Throws <c>ArgumentException</c> for a null or empty URI.</summary>
    static member Create(uri: string | null, range: Range) : Location =
        match uri with
        | null
        | "" -> invalidArg (nameof uri) "A location needs a document URI."
        | uri -> { UriValue = uri; RangeValue = range }

/// <summary>A related location and its message, for example "Declared here.".</summary>
type RelatedInformation =
    {
        /// <summary>The related location.</summary>
        Location: Location
        /// <summary>What the location has to do with the diagnostic.</summary>
        Message: string
    }

    override this.ToString() = $"{this.Location}: {this.Message}"

/// <summary>
/// A diagnostic in the MEP shape: severity, optional stable code, message, optional location, related
/// locations and optional structured data. F# callers use the <c>Diagnostic</c> module; C# callers use
/// <c>Create</c> and the nullable members.
/// </summary>
type Diagnostic =
    private
        {
            SeverityValue: DiagnosticSeverity
            CodeValue: DiagnosticCode option
            MessageValue: string
            LocationValue: Location option
            RelatedValue: RelatedInformation list
            // Raw JSON text, so equality compares content and not document identity.
            DataValue: string option
        }

    /// <summary>The severity.</summary>
    member this.Severity = this.SeverityValue

    /// <summary>The message.</summary>
    member this.Message = this.MessageValue

    /// <summary>True when the severity is <c>error</c>.</summary>
    member this.IsError = this.SeverityValue = DiagnosticSeverity.Error

    /// <summary>The code, or null when the diagnostic has none. For C# callers.</summary>
    member this.CodeOrNull = Option.toObj this.CodeValue

    /// <summary>The location, or null when the diagnostic has none. For C# callers.</summary>
    member this.LocationOrNull = Option.toObj this.LocationValue

    /// <summary>The related locations, in order.</summary>
    member this.Related: IReadOnlyList<RelatedInformation> =
        List.toArray this.RelatedValue :> IReadOnlyList<_>

    /// <summary>The structured data, or null when the diagnostic has none. For C# callers.</summary>
    member this.Data: Nullable<JsonElement> =
        match this.DataValue with
        | Some text ->
            use document = JsonDocument.Parse text
            Nullable(document.RootElement.Clone())
        | None -> Nullable()

    /// <summary>The same diagnostic at a location.</summary>
    member this.WithLocation(location: Location) =
        { this with
            LocationValue = Some location
        }

    /// <summary>The same diagnostic with one more related location, after the existing ones.</summary>
    member this.WithRelated(related: RelatedInformation) =
        { this with
            RelatedValue =
                this.RelatedValue
                @ [ related ]
        }

    /// <summary>The same diagnostic with structured data, kept as its JSON text.</summary>
    member this.WithData(data: JsonElement) =
        { this with
            DataValue = Some(data.GetRawText())
        }

    override this.ToString() =
        let code =
            this.CodeValue
            |> Option.map (fun c -> $" {c}")
            |> Option.defaultValue ""

        let location =
            this.LocationValue
            |> Option.map (fun l -> $"{l}: ")
            |> Option.defaultValue ""

        $"{location}{this.SeverityValue}{code}: {this.MessageValue}"

    /// <summary>
    /// Builds a diagnostic. For C# callers. Throws <c>ArgumentException</c> for an empty message.
    /// </summary>
    static member Create
        (severity: DiagnosticSeverity, code: DiagnosticCode | null,
                                                              message: string,
                                                              location: Location | null)
        : Diagnostic =
        if String.IsNullOrEmpty message then
            invalidArg (nameof message) "A diagnostic needs a message."

        {
            SeverityValue = severity
            CodeValue = Option.ofObj code
            MessageValue = message
            LocationValue = Option.ofObj location
            RelatedValue = []
            DataValue = None
        }

/// <summary>Builds and reads diagnostics. For F# callers.</summary>
[<RequireQualifiedAccess>]
module Diagnostic =

    /// <summary>A diagnostic with a severity, a code and a message.</summary>
    let make severity (code: DiagnosticCode) message =
        Diagnostic.Create(severity, code, message, null)

    /// <summary>An error diagnostic.</summary>
    let error code message =
        make DiagnosticSeverity.Error code message

    /// <summary>A warning diagnostic.</summary>
    let warning code message =
        make DiagnosticSeverity.Warning code message

    /// <summary>The same diagnostic at a location.</summary>
    let at (location: Location) (diagnostic: Diagnostic) = diagnostic.WithLocation location

    /// <summary>The same diagnostic with one more related location, after the existing ones.</summary>
    let withRelated (related: RelatedInformation) (diagnostic: Diagnostic) =
        diagnostic.WithRelated related

    /// <summary>The same diagnostic with structured data, kept as its JSON text.</summary>
    let withData (data: JsonElement) (diagnostic: Diagnostic) = diagnostic.WithData data

    /// <summary>The code, if any.</summary>
    let code (diagnostic: Diagnostic) = diagnostic.CodeValue

    /// <summary>The location, if any.</summary>
    let location (diagnostic: Diagnostic) = diagnostic.LocationValue

    /// <summary>The related locations, in order.</summary>
    let related (diagnostic: Diagnostic) = diagnostic.RelatedValue

    /// <summary>The structured data, if any.</summary>
    let data (diagnostic: Diagnostic) = Option.ofNullable diagnostic.Data
