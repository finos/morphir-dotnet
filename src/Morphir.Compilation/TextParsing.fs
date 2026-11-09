namespace Morphir.Compilation

open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// A package name parsed from untrusted text, with that text. C# callers hold this type, because the name
/// <c>Morphir.IR.PackageName</c> is defined in more than one assembly that C# projects reference.
/// </summary>
[<Sealed>]
type ParsedPackageName private (text: string, name: PackageName) =

    /// <summary>The text that was parsed, for example <c>acme/car-rental</c>.</summary>
    member _.Text = text

    /// <summary>The package name.</summary>
    member _.Name = name

    override _.ToString() = text

    override _.Equals(other: obj | null) =
        match other with
        | :? ParsedPackageName as parsed -> parsed.Name = name
        | _ -> false

    override _.GetHashCode() = name.GetHashCode()

    /// <summary>
    /// Parses <paramref name="text"/> with the canonical path parser, for example <c>acme/car-rental</c>.
    /// For C# callers.
    /// </summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<ParsedPackageName>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<string>)
        : bool =
        match text with
        | null ->
            result <- Unchecked.defaultof<_>
            failure <- "A package name is empty."
            false
        | text ->
            match CanonicalNames.parsePath text with
            | Ok path ->
                result <- ParsedPackageName(text, PackageName path)
                failure <- Unchecked.defaultof<_>
                true
            | Error message ->
                result <- Unchecked.defaultof<_>
                failure <- message
                false

/// <summary>Parses IR format versions from untrusted text, keeping the exact spelling (VER-1). For C# callers.</summary>
[<AbstractClass; Sealed>]
type SourceFormatVersions =

    /// <summary>
    /// Parses <paramref name="text"/> as an integer classic version or a SemVer version with optional
    /// pre-release and build parts. The result keeps the spelling exactly.
    /// </summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<SourceFormatVersion>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<string>)
        : bool =
        match text with
        | null ->
            result <- Unchecked.defaultof<_>
            failure <- "A format version is empty."
            false
        | text ->
            match SourceFormatVersion.parse text with
            | Ok version ->
                result <- version
                failure <- Unchecked.defaultof<_>
                true
            | Error error ->
                result <- Unchecked.defaultof<_>
                failure <- $"'{text}' is not a format version: {error}"
                false
