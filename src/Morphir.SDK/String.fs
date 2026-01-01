namespace Morphir.SDK

/// <summary>
/// String functions that complement F# String module.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/String.elm
/// </summary>
module String =
    
    /// <summary>Check if a string is empty</summary>
    let inline isEmpty (s: string) = System.String.IsNullOrEmpty(s)

    /// <summary>Get the length of a string</summary>
    let inline length (s: string) = s.Length

    /// <summary>Reverse a string</summary>
    let reverse (s: string) =
        s.ToCharArray() |> Array.rev |> System.String

    /// <summary>Repeat a string n times</summary>
    let repeat n (s: string) =
        System.String.Concat(System.Linq.Enumerable.Repeat(s, n))

    /// <summary>Convert to uppercase</summary>
    let inline toUpper (s: string) = s.ToUpperInvariant()

    /// <summary>Convert to lowercase</summary>
    let inline toLower (s: string) = s.ToLowerInvariant()

    /// <summary>Check if string starts with a prefix</summary>
    let inline startsWith (prefix: string) (s: string) = 
        s.StartsWith(prefix, System.StringComparison.Ordinal)

    /// <summary>Check if string ends with a suffix</summary>
    let inline endsWith (suffix: string) (s: string) = 
        s.EndsWith(suffix, System.StringComparison.Ordinal)

    /// <summary>Check if string contains a substring</summary>
    let inline contains (substring: string) (s: string) = 
        s.Contains(substring, System.StringComparison.Ordinal)

    /// <summary>Split a string by a separator</summary>
    let split (separator: string) (s: string) =
        s.Split([| separator |], System.StringSplitOptions.None) |> Array.toList

    /// <summary>Join a list of strings with a separator</summary>
    let join (separator: string) (strings: string list) =
        System.String.Join(separator, strings)

    /// <summary>Trim whitespace from both ends</summary>
    let inline trim (s: string) = s.Trim()

    /// <summary>Trim whitespace from the left</summary>
    let inline trimLeft (s: string) = s.TrimStart()

    /// <summary>Trim whitespace from the right</summary>
    let inline trimRight (s: string) = s.TrimEnd()

    /// <summary>Pad left to a certain length with a character</summary>
    let padLeft totalLength (padChar: char) (s: string) =
        s.PadLeft(totalLength, padChar)

    /// <summary>Pad right to a certain length with a character</summary>
    let padRight totalLength (padChar: char) (s: string) =
        s.PadRight(totalLength, padChar)

    /// <summary>Get substring from start index with given length</summary>
    let slice start end' (s: string) =
        if start < 0 || end' > s.Length || start > end' then
            ""
        else
            s.Substring(start, end' - start)

    /// <summary>Get character at index</summary>
    let getAt index (s: string) =
        if index >= 0 && index < s.Length then
            Some s.[index]
        else
            None

    /// <summary>Convert string to list of characters</summary>
    let toList (s: string) = s.ToCharArray() |> Array.toList

    /// <summary>Convert list of characters to string</summary>
    let fromList (chars: char list) = 
        chars |> List.toArray |> System.String

    /// <summary>Convert integer to string</summary>
    let inline fromInt (n: int) = n.ToString()

    /// <summary>Convert float to string</summary>
    let inline fromFloat (f: float) = f.ToString()

    /// <summary>Parse string to integer</summary>
    let toInt (s: string) =
        match System.Int32.TryParse(s) with
        | true, n -> Some n
        | false, _ -> None

    /// <summary>Parse string to float</summary>
    let toFloat (s: string) =
        match System.Double.TryParse(s) with
        | true, f -> Some f
        | false, _ -> None

    /// <summary>Append two strings</summary>
    let inline append (s1: string) (s2: string) = s1 + s2

    /// <summary>Concatenate a list of strings</summary>
    let inline concat (strings: string list) = System.String.Concat(strings)

    /// <summary>Map a function over each character</summary>
    let map (f: char -> char) (s: string) =
        s.ToCharArray() |> Array.map f |> System.String

    /// <summary>Filter characters by predicate</summary>
    let filter (predicate: char -> bool) (s: string) =
        s.ToCharArray() |> Array.filter predicate |> System.String

    /// <summary>Fold over characters from the left</summary>
    let foldl folder state (s: string) =
        s.ToCharArray() |> Array.fold folder state

    /// <summary>Fold over characters from the right</summary>
    let foldr folder state (s: string) =
        s.ToCharArray() |> Array.foldBack folder state

    /// <summary>Check if any character satisfies predicate</summary>
    let any (predicate: char -> bool) (s: string) =
        s.ToCharArray() |> Array.exists predicate

    /// <summary>Check if all characters satisfy predicate</summary>
    let all (predicate: char -> bool) (s: string) =
        s.ToCharArray() |> Array.forall predicate

    /// <summary>Replace all occurrences of a substring</summary>
    let replace (search: string) (replacement: string) (s: string) =
        s.Replace(search, replacement)

    /// <summary>Drop first n characters</summary>
    let dropLeft n (s: string) =
        if n >= s.Length then ""
        elif n <= 0 then s
        else s.Substring(n)

    /// <summary>Drop last n characters</summary>
    let dropRight n (s: string) =
        if n >= s.Length then ""
        elif n <= 0 then s
        else s.Substring(0, s.Length - n)

    /// <summary>Take first n characters</summary>
    let left n (s: string) =
        if n >= s.Length then s
        elif n <= 0 then ""
        else s.Substring(0, n)

    /// <summary>Take last n characters</summary>
    let right n (s: string) =
        if n >= s.Length then s
        elif n <= 0 then ""
        else s.Substring(s.Length - n)
