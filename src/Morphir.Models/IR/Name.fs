namespace Morphir.IR

/// <summary>
/// Name is an abstraction of human-readable identifiers made up of words.
/// This abstraction allows us to use the same identifiers across various naming conventions used by the different
/// frontend and backend languages Morphir integrates with.
/// </summary>
module Name =

    open System
    open System.Globalization

    /// <summary>
    /// A Name represents a human-readable identifier made up of one or more words.
    /// </summary>
    type Name = Name of string list

    /// <summary>
    /// Checks if a character is a letter (uppercase or lowercase).
    /// </summary>
    let private isLetter (c: char) : bool =
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')

    /// <summary>
    /// Checks if a character is a digit.
    /// </summary>
    let private isDigit (c: char) : bool =
        c >= '0' && c <= '9'

    /// <summary>
    /// Checks if a character is a lowercase letter.
    /// </summary>
    let private isLowercase (c: char) : bool =
        c >= 'a' && c <= 'z'

    /// <summary>
    /// Capitalizes the first character of a string.
    /// </summary>
    let private capitalize (str: string) =
        if String.IsNullOrEmpty(str) then
            str
        else
            CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str)

    /// <summary>
    /// Splits a string into Morphir words using direct parsing.
    /// Pattern: [a-zA-Z][a-z]*|[0-9]+
    /// Matches sequences of letters (starting with uppercase or lowercase) followed by zero or more lowercase letters,
    /// OR sequences of digits.
    /// Uses direct character-by-character parsing instead of Regex for AOT/trimming compatibility.
    ///
    /// The regex [a-zA-Z][a-z]* means:
    /// - One letter (any case) followed by zero or more lowercase letters
    /// So "SDK" matches as three words: "S", "D", "K" (each is one uppercase with zero lowercase)
    /// And "fooBar" matches as two words: "foo", "Bar" (Bar starts new word because of uppercase)
    /// </summary>
    let private toMorphirWords (input: string) : string list =
        if String.IsNullOrEmpty(input) then
            []
        else
            let mutable words = []
            let mutable currentWord = System.Text.StringBuilder()
            let mutable i = 0
            let mutable inWord = false
            let mutable wordType = 0 // 0 = none, 1 = letters, 2 = digits
            let mutable hasLowercase = false // Track if current word has lowercase letters

            while i < input.Length do
                let c = input.[i]
                let isLetterChar = isLetter c
                let isDigitChar = isDigit c
                let isLowercaseChar = isLowercase c

                if isLetterChar then
                    if not inWord then
                        // Start new word with letter
                        currentWord.Clear() |> ignore
                        currentWord.Append(c) |> ignore
                        inWord <- true
                        wordType <- 1
                        hasLowercase <- isLowercaseChar
                    else if wordType = 1 then
                        // Continue letter word
                        if isLowercaseChar then
                            // Lowercase can always be added to letter word
                            currentWord.Append(c) |> ignore
                            hasLowercase <- true
                        else
                            // Uppercase letter
                            if hasLowercase then
                                // We have lowercase in current word, uppercase starts new word
                                words <- currentWord.ToString() :: words
                                currentWord.Clear() |> ignore
                                currentWord.Append(c) |> ignore
                                hasLowercase <- false
                            else
                                // Current word is all uppercase (e.g., "SDK"), uppercase starts new word
                                words <- currentWord.ToString() :: words
                                currentWord.Clear() |> ignore
                                currentWord.Append(c) |> ignore
                                hasLowercase <- false
                    else
                        // Digit word ended, start letter word
                        words <- currentWord.ToString() :: words
                        currentWord.Clear() |> ignore
                        currentWord.Append(c) |> ignore
                        wordType <- 1
                        hasLowercase <- isLowercaseChar
                else if isDigitChar then
                    if not inWord then
                        // Start new word with digit
                        currentWord.Clear() |> ignore
                        currentWord.Append(c) |> ignore
                        inWord <- true
                        wordType <- 2
                        hasLowercase <- false
                    else if wordType = 2 then
                        // Continue digit word
                        currentWord.Append(c) |> ignore
                    else
                        // Letter word ended, start digit word
                        words <- currentWord.ToString() :: words
                        currentWord.Clear() |> ignore
                        currentWord.Append(c) |> ignore
                        wordType <- 2
                        hasLowercase <- false
                else
                    // Non-word character - end current word if any
                    if inWord then
                        words <- currentWord.ToString() :: words
                        currentWord.Clear() |> ignore
                        inWord <- false
                        wordType <- 0
                        hasLowercase <- false

                i <- i + 1

            // Add final word if any
            if inWord then
                words <- currentWord.ToString() :: words

            List.rev words

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

