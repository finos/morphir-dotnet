namespace Morphir.IR

/// <summary>
/// Name is an abstraction of human-readable identifiers made up of words.
/// This abstraction allows us to use the same identifiers across various naming conventions used by the different
/// frontend and backend languages Morphir integrates with.
/// </summary>
module Name =

    open System
    open System.Globalization
    open System.Text.RegularExpressions

    /// <summary>
    /// A Name represents a human-readable identifier made up of one or more words.
    /// </summary>
    type Name = Name of string list

    /// <summary>
    /// Regular expression pattern for matching words in a string.
    /// Matches sequences of letters (starting with uppercase or lowercase) or sequences of digits.
    /// </summary>
    let private wordPattern = Regex("[a-zA-Z][a-z]*|[0-9]+", RegexOptions.Compiled)

    /// <summary>
    /// Capitalizes the first character of a string.
    /// </summary>
    let private capitalize (str: string) =
        if String.IsNullOrEmpty(str) then
            str
        else
            CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str)

    /// <summary>
    /// Splits a string into Morphir words using the word pattern.
    /// </summary>
    let private toMorphirWords (input: string) =
        wordPattern.Matches(input)
        |> Seq.map (fun m -> m.Value)
        |> List.ofSeq

    /// <summary>
    /// Creates a Name from a list of string segments.
    /// </summary>
    let fromList (segments: string list) = Name segments

    /// <summary>
    /// Creates a Name from an array of string segments.
    /// </summary>
    let fromArray (segments: string[]) = Name(List.ofArray segments)

    /// <summary>
    /// Creates a Name from a variable number of string segments.
    /// </summary>
    let create ([<ParamArray>] segments: string[]) = fromArray segments

    /// <summary>
    /// Creates an empty Name.
    /// </summary>
    let empty = Name []

    /// <summary>
    /// Translates a string into a name by splitting it into words.
    /// The algorithm is designed to work with the most well-known naming conventions or mixes of them.
    /// The general rule is that consecutive letters and numbers are treated as words,
    /// upper-case letters and non-alphanumeric characters start a new word.
    /// </summary>
    let fromString (input: string) =
        input
        |> toMorphirWords
        |> List.map (fun w -> w.ToLowerInvariant())
        |> Name

    /// <summary>
    /// Gets the segments of a Name as a list.
    /// </summary>
    let toList (Name segments) = segments

    /// <summary>
    /// Checks if a Name is empty.
    /// </summary>
    let isEmpty (Name segments) = List.isEmpty segments

    /// <summary>
    /// Turns a name into a camel-case string.
    /// </summary>
    let toCamelCase (Name segments) =
        match segments with
        | [] -> ""
        | head :: tail ->
            head :: (tail |> List.map capitalize)
            |> String.concat ""

    /// <summary>
    /// Turns a name into a title-case string (PascalCase).
    /// </summary>
    let toTitleCase (Name segments) =
        segments
        |> List.map capitalize
        |> String.concat ""

    /// <summary>
    /// Turns a name into a list of human-readable strings.
    /// The only difference compared to toList is how it handles abbreviations.
    /// They will be turned into a single upper-case word instead of separate single-character words.
    /// </summary>
    let toHumanWords (Name segments) =
        let rec loop (acc: string list) (abbrev: System.Text.StringBuilder) (remaining: string list) =
            match remaining with
            | [] ->
                if abbrev.Length > 0 then
                    abbrev.ToString().ToUpperInvariant() :: acc |> List.rev
                else
                    List.rev acc
            | head :: tail ->
                if head.Length = 1 then
                    abbrev.Append(head) |> ignore
                    loop acc abbrev tail
                else
                    if abbrev.Length > 0 then
                        let abbrevStr = abbrev.ToString().ToUpperInvariant()
                        abbrev.Clear() |> ignore
                        loop (abbrevStr :: head :: acc) abbrev tail
                    else
                        loop (head :: acc) abbrev tail

        loop [] (System.Text.StringBuilder()) segments

    /// <summary>
    /// Turns a name into a kebab-case string.
    /// </summary>
    let toKebabCase name =
        name
        |> toHumanWords
        |> String.concat "-"

    /// <summary>
    /// Turns a name into a snake-case string.
    /// </summary>
    let toSnakeCase name =
        name
        |> toHumanWords
        |> String.concat "_"

    /// <summary>
    /// Gets the head (first segment) of a Name, if it exists.
    /// </summary>
    let head (Name segments) = List.tryHead segments

    /// <summary>
    /// Gets the tail (remaining segments) of a Name.
    /// </summary>
    let tail (Name segments) = Name(List.tail segments)

