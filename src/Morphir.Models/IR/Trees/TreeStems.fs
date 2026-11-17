namespace Morphir.IR.Trees

open System
open System.Security.Cryptography
open System.Text
open Morphir.IR
#if NETSTANDARD2_0
open Morphir.Compat
#endif

/// <summary>What a file or directory stem in a document tree stands for.</summary>
type Stem =
    /// <summary>A stem that spells a whole name.</summary>
    | WholeStem of Name
    /// <summary>
    /// A stem the path budget cut: the prefix of the escaped name and the first eight hex digits of the SHA-256 of
    /// the whole escaped name.
    /// </summary>
    | CutStem of prefix: string * digest: string

/// <summary>
/// The directory part of a dependency path: <c>deps/&lt;package path&gt;/@&lt;version&gt;/&lt;module path&gt;</c>.
/// </summary>
type DependencyDirectory = {
    /// <summary>The directories of the package path, before the <c>@</c> segment.</summary>
    PackageDirectories: string list
    /// <summary>
    /// What follows the <c>@</c>. It is always empty, since only the bare <c>@</c> is accepted while the model has
    /// no package version.
    /// </summary>
    Version: string
    /// <summary>The directories of the module path, after the <c>@</c> segment.</summary>
    ModuleDirectories: string list
}

/// <summary>
/// Stems in a document tree. A name is written to a file name through a reversible escape (decision 0001 of
/// finos/morphir): an initialism segment is lowercase with a <c>_</c> prefix, and a stem that is a Windows device
/// name takes a <c>_</c> suffix. A path budget can cut a stem (decision 0012): the stem keeps a prefix and ends
/// in <c>__</c> and the first eight hex digits of the SHA-256 of the whole escaped stem. A dependency's package
/// path ends at the segment that begins with <c>@</c> (decision 0015).
/// </summary>
[<RequireQualifiedAccess>]
module TreeStems =
    let private isLowerOrDigit (c: char) =
        (c >= 'a'
         && c <= 'z')
        || (c >= '0'
            && c <= '9')

    let private isLowerHex (c: char) =
        (c >= '0'
         && c <= '9')
        || (c >= 'a'
            && c <= 'f')

    let private isUpper (c: char) =
        c >= 'A'
        && c <= 'Z'

    /// The names Windows reserves, with any extension: con, prn, aux, nul, com0 to com9 and lpt0 to lpt9.
    let private isDeviceName (stem: string) =
        match stem with
        | "con"
        | "prn"
        | "aux"
        | "nul" -> true
        | _ ->
            stem.Length = 4
            && (stem.StartsWith("com", StringComparison.Ordinal)
                || stem.StartsWith("lpt", StringComparison.Ordinal))
            && Char.IsAsciiDigit stem[3]

    /// <summary>Writes a name as a file stem, before any cut.</summary>
    let escapeName (name: Name) : string =
        let escaped =
            (CanonicalNames.nameToString name).Split('-')
            |> Array.map (fun segment ->
                if
                    segment
                    |> Seq.exists isUpper
                then
                    "_"
                    + segment.ToLowerInvariant()
                else
                    segment
            )
            |> String.concat "-"

        if isDeviceName escaped then
            escaped
            + "_"
        else
            escaped

    /// Whether the text is a word segment of a stem: an optional underscore, then lowercase letters and digits.
    let private isSegment (segment: string) =
        let body =
            if segment.StartsWith('_') then
                segment.Substring 1
            else
                segment

        body <> ""
        && body
           |> Seq.forall isLowerOrDigit

    /// Whether the text is segments joined by single hyphens.
    let private isSegments (text: string) =
        text <> ""
        && text.Split('-')
           |> Array.forall isSegment

    /// Reads the stem of a name that is whole, with the trailing underscore of a device name removed.
    let private wholeName (stem: string) : Result<Name, string> =
        let unmarked =
            if stem.EndsWith('_') then
                let body =
                    stem.Substring(
                        0,
                        stem.Length
                        - 1
                    )

                if isDeviceName body then Some body else None
            else
                Some stem

        match unmarked with
        | Some body when isSegments body ->
            let canonical =
                body.Split('-')
                |> Array.map (fun segment ->
                    if segment.StartsWith('_') then
                        segment.Substring(1).ToUpperInvariant()
                    else
                        segment
                )
                |> String.concat "-"

            CanonicalNames.parseName canonical
        | _ -> Error $"'{stem}' is not a file stem that the escape writes."

    /// <summary>Reads a file or directory stem back. A stem the escape never writes is an error.</summary>
    let parseStem (stem: string) : Result<Stem, string> =
        let cutAt =
            stem.Length
            - 10

        if
            cutAt >= 0
            && stem.Substring(cutAt, 2) = "__"
            && stem.Substring(cutAt + 2)
               |> Seq.forall isLowerHex
        then
            let prefix = stem.Substring(0, cutAt)

            if isSegments prefix then
                Ok(CutStem(prefix, stem.Substring(cutAt + 2)))
            else
                Error $"'{stem}' is not a file stem that the escape writes."
        else
            wholeName stem
            |> Result.map WholeStem

    /// The first eight hex digits of the SHA-256 of a text's UTF-8 bytes.
    let private digestOf (text: string) =
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes text))
            .Substring(0, 8)
            .ToLowerInvariant()

    /// <summary>
    /// Whether a cut stem belongs to a name: the stem's prefix starts the escaped name, and the digest is the
    /// escaped name's.
    /// </summary>
    let matchesCut (prefix: string) (digest: string) (name: Name) : bool =
        let escaped = escapeName name

        escaped.StartsWith(prefix, StringComparison.Ordinal)
        && digestOf escaped = digest

    /// <summary>
    /// Splits the directories below <c>deps/</c> at the segment that begins with <c>@</c>. Decision 0015's
    /// current layout has only the bare <c>@</c>, and a version after it would let two versions of a package
    /// merge, so a segment such as <c>@1.0.0</c> is refused.
    /// </summary>
    let splitDependency (directories: string list) : Result<DependencyDirectory, string> =
        match
            directories
            |> List.tryFindIndex (fun segment -> segment.StartsWith('@'))
        with
        | None -> Error "A dependency path has an @ segment after its package path."
        | Some 0 -> Error "A dependency path starts with its package path, not with the @ segment."
        | Some index when
            directories[index]
            <> "@"
            ->
            Error
                $"The segment '{directories[index]}' carries a version, and this layout has only the bare @ segment."
        | Some index ->
            Ok
                {
                    PackageDirectories = List.take index directories
                    Version = ""
                    ModuleDirectories = List.skip (index + 1) directories
                }
