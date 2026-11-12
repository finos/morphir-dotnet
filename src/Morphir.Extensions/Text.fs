/// Text rules shared by the parsed value types.
module internal Morphir.Extensions.Text

open System

/// A lowercase portable token: [a-z]([a-z0-9-]*[a-z0-9])?, the upstream extension id rule.
let isToken (segment: string) =
    segment.Length > 0
    && Char.IsAsciiLetterLower segment[0]
    && segment[segment.Length
               - 1]
       <> '-'
    && segment
       |> Seq.forall (fun c ->
           Char.IsAsciiLetterLower c
           || Char.IsAsciiDigit c
           || c = '-'
       )

/// One or more lowercase portable tokens joined by dots, with a length limit.
let dotted (field: string) (limit: int) (text: string | null) : Result<string, ParseFailure> =
    match text with
    | null -> Error(ParseFailure.Empty field)
    | value when value.Length = 0 -> Error(ParseFailure.Empty field)
    | value ->
        match
            value.Split '.'
            |> Array.tryFindIndex (
                isToken
                >> not
            )
        with
        | Some segment -> Error(ParseFailure.InvalidSegment(field, segment))
        | None when value.Length > limit -> Error(ParseFailure.TooLong(field, limit))
        | None -> Ok value

/// The extension contract checks grammar, then length, then the first segment's device name.
let extensionId (text: string | null) =
    dotted "extension id" 238 text
    |> Result.bind (fun value ->
        let first = value.Split('.')[0]

        let device =
            List.contains first [ "con"; "prn"; "aux"; "nul" ]
            || (first.Length = 4
                && (first.StartsWith("com", StringComparison.Ordinal)
                    || first.StartsWith("lpt", StringComparison.Ordinal))
                && first[3]
                   >= '1'
                && first[3]
                   <= '9')

        if device then
            Error(ParseFailure.DeviceName "extension id")
        else
            Ok value
    )

/// Display text: not empty, within the limit, no white space at the ends and no control character.
let display (field: string) (limit: int) (text: string | null) : Result<string, ParseFailure> =
    match text with
    | null -> Error(ParseFailure.Empty field)
    | value when value.Length = 0 -> Error(ParseFailure.Empty field)
    | value when value.Length > limit -> Error(ParseFailure.TooLong(field, limit))
    | value when Char.IsWhiteSpace value[0] -> Error(ParseFailure.InvalidCharacter(field, 0))
    | value when
        Char.IsWhiteSpace
            value[value.Length
                  - 1]
        ->
        Error(
            ParseFailure.InvalidCharacter(
                field,
                value.Length
                - 1
            )
        )
    | value ->
        match
            value
            |> Seq.tryFindIndex Char.IsControl
        with
        | Some index -> Error(ParseFailure.InvalidCharacter(field, index))
        | None -> Ok value

/// The detail text of a SemVer parse error.
let versionError (error: Morphir.IR.Versioning.ParseError) =
    match error with
    | Morphir.IR.Versioning.ParseError.InvalidFormat message
    | Morphir.IR.Versioning.ParseError.InvalidVersionNumber message
    | Morphir.IR.Versioning.ParseError.InvalidPreReleaseFormat message
    | Morphir.IR.Versioning.ParseError.InvalidBuildMetadataFormat message -> message

/// The message of a refused literal in source code.
let refusedLiteral (field: string) (literal: string) (failure: ParseFailure) =
    $"Invalid {field} literal '{literal}': {failure}"
