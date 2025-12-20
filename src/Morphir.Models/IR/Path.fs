namespace Morphir.IR

/// <summary>
/// Path represents a hierarchical location in the IR structure.
/// A Path is a list of Names that identifies packages and modules within the hierarchy.
/// </summary>
module Path =

    open System
    open System.Text.RegularExpressions
    open Name

    /// <summary>
    /// A Path represents a hierarchical location in the IR structure.
    /// </summary>
    type Path = Path of Name list

    /// <summary>
    /// Regular expression for matching non-word, non-space characters (path separators).
    /// </summary>
    let private separatorRegex = Regex("[^\\w\\s]+", RegexOptions.Compiled)

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
        separatorRegex.Split(input)
        |> Array.map Name.fromString
        |> Array.filter (not << Name.isEmpty)
        |> List.ofArray
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

