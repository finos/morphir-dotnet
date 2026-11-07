namespace Morphir.Extensions

open Morphir.IR

/// <summary>Why a text value was refused. The output edge turns it into a diagnostic.</summary>
[<RequireQualifiedAccess>]
type ParseFailure =
    /// <summary>The text is null or empty.</summary>
    | Empty of field: string
    /// <summary>The text has more characters than the limit.</summary>
    | TooLong of field: string * limit: int
    /// <summary>The text has white space at its start or end, or a control character, at the index.</summary>
    | InvalidCharacter of field: string * index: int
    /// <summary>The dot-separated segment at the index is not a lowercase portable token.</summary>
    | InvalidSegment of field: string * segment: int
    /// <summary>The text is not a canonical SemVer 2.0.0 version.</summary>
    | InvalidVersion of field: string * error: Versioning.ParseError

    /// <summary>The name of the value that was refused, for example "extension id".</summary>
    member this.Field =
        match this with
        | Empty field
        | TooLong(field, _)
        | InvalidCharacter(field, _)
        | InvalidSegment(field, _)
        | InvalidVersion(field, _) -> field

    /// <summary>The rule that the text breaks.</summary>
    override this.ToString() =
        match this with
        | Empty field -> $"{field} is empty"
        | TooLong(field, limit) -> $"{field} is longer than {limit} characters"
        | InvalidCharacter(field, index) ->
            $"{field} has white space at its start or end, or a control character, at index {index}"
        | InvalidSegment(field, segment) ->
            $"{field} segment {segment} is not a lowercase portable token ([a-z]([a-z0-9-]*[a-z0-9])?)"
        | InvalidVersion(field, error) ->
            let detail =
                match error with
                | Versioning.ParseError.InvalidFormat message
                | Versioning.ParseError.InvalidVersionNumber message
                | Versioning.ParseError.InvalidPreReleaseFormat message
                | Versioning.ParseError.InvalidBuildMetadataFormat message -> message

            $"{field} is not a canonical SemVer 2.0.0 version: {detail}"
