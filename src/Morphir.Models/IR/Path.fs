namespace Morphir.IR

/// <summary>
/// A Path represents a hierarchical location in the IR structure.
/// </summary>
type Path = Path of Name list

/// <summary>
/// Path module provides functions for working with Path values.
/// Path represents a hierarchical location in the IR structure.
/// A Path is a list of Names that identifies packages and modules within the hierarchy.
/// </summary>
[<RequireQualifiedAccess>]
module Path =

    open System

    /// <summary>
    /// Checks if a character is a word character (letter, digit, or underscore).
    /// </summary>
    let private isWordChar (c: char) : bool =
        (c >= 'A' && c <= 'Z')
        || (c >= 'a' && c <= 'z')
        || (c >= '0' && c <= '9')
        || c = '_'

    /// <summary>
    /// Checks if a character is whitespace.
    /// </summary>
    let private isWhitespace (c: char) : bool =
        Char.IsWhiteSpace(c)

    /// <summary>
    /// Splits a string by non-word, non-space characters (path separators).
    /// Pattern: [^\w\s]+
    /// Uses direct character-by-character parsing instead of Regex for AOT/trimming compatibility.
    /// </summary>
    let private splitBySeparators (input: string) : string list =
        if String.IsNullOrEmpty(input) then
            []
        else
            let mutable parts = []
            let mutable currentPart = System.Text.StringBuilder()
            let mutable i = 0

            while i < input.Length do
                let c = input.[i]
                if isWordChar c || isWhitespace c then
                    currentPart.Append(c) |> ignore
                else
                    // Separator found - end current part if any
                    if currentPart.Length > 0 then
                        parts <- currentPart.ToString() :: parts
                        currentPart.Clear() |> ignore

                i <- i + 1

            // Add final part if any
            if currentPart.Length > 0 then
                parts <- currentPart.ToString() :: parts

            List.rev parts

    /// <summary>
    /// Creates a Path from a list of Names.
    /// </summary>
    let fromList (names: Name list) = Path names

    /// <summary>
    /// Creates an empty Path.
    /// </summary>
    let empty = Path []

    /// <summary>
    /// Translates a string into a path by splitting it into names along special characters.
    /// The algorithm will treat any non-word characters that are not spaces as a path separator.
    /// </summary>
    let fromString (input: string) =
        splitBySeparators input
        |> List.map Name.fromString
        |> List.filter (not << Name.isEmpty)
        |> Path

    /// <summary>
    /// Gets the Names in a Path as a list.
    /// </summary>
    let toList (Path names) = names

    /// <summary>
    /// Checks if a Path is empty.
    /// </summary>
    let isEmpty (Path names) = List.isEmpty names

    /// <summary>
    /// Gets the head (first Name) of a Path, if it exists.
    /// </summary>
    let head (Path names) = List.tryHead names

    /// <summary>
    /// Gets the tail (remaining Names) of a Path.
    /// </summary>
    let tail (Path names) =
        match names with
        | [] -> Path []
        | _ :: tail -> Path tail

    /// <summary>
    /// Converts a Path to a string using the provided name renderer and separator.
    /// </summary>
    let toString (renderName: Name -> string) (separator: string) (Path names) =
        names
        |> List.map renderName
        |> String.concat separator

    /// <summary>
    /// Converts a Path to its canonical string representation (kebab-case with '/' separator).
    /// </summary>
    let toCanonicalString path = toString Name.toKebabCase "/" path

    /// <summary>
    /// Checks if a Path is a prefix of another Path.
    /// An empty path is a prefix of any other path.
    /// </summary>
    let isPrefixOf (Path prefixNames) (Path pathNames) =
        let rec loop (prefix: Name list) (path: Name list) =
            match prefix, path with
            | [], _ -> true // Empty path is prefix of any path
            | _, [] -> false // Non-empty prefix cannot be prefix of empty path
            | prefixHead :: prefixTail, pathHead :: pathTail ->
                if prefixHead = pathHead then
                    loop prefixTail pathTail
                else
                    false

        loop prefixNames pathNames

    /// <summary>
    /// Checks if this Path is a prefix of the provided path.
    /// </summary>
    let isPrefixOfPath (path: Path) (prefix: Path) = isPrefixOf prefix path
