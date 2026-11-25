namespace Morphir.IR

/// <summary>
/// Strict parsers and writers for the canonical name strings of IR v4 and later, which follow Morphir's
/// decision 0001. A lowercase segment is a word, and an uppercase segment is an initialism held as
/// single-letter words, so <c>value-in-USD</c> equals the legacy array <c>["value","in","u","s","d"]</c>.
/// </summary>
/// <remarks>
/// <see cref="Name"/> has no initialism marker, so spellings that differ only in how they mark one parse
/// to one value: <c>USD</c> and <c>u-s-d</c>, <c>A</c> and <c>a</c>, <c>FR2052A</c> and <c>FR-2052-a</c>.
/// The writer gives the spelling a legacy array decodes to.
/// </remarks>
[<RequireQualifiedAccess>]
module CanonicalNames =

    open System

    let private isLower (c: char) =
        c >= 'a'
        && c <= 'z'

    let private isUpper (c: char) =
        c >= 'A'
        && c <= 'Z'

    let private isDigit (c: char) =
        c >= '0'
        && c <= '9'

    let private traverse parse values =
        let rec loop acc remaining =
            match remaining with
            | [] -> Ok(List.rev acc)
            | value :: rest ->
                match parse value with
                | Error message -> Error message
                | Ok parsed -> loop (parsed :: acc) rest

        loop [] values

    /// Splits an uppercase segment into one word per letter and one word per digit run.
    let private initialismWords (segment: string) =
        let rec loop acc (chars: char list) =
            match chars with
            | [] -> List.rev acc
            | c :: rest when isDigit c ->
                let digits =
                    c
                    :: List.takeWhile isDigit rest

                loop
                    (String(Array.ofList digits) :: acc)
                    (List.skip
                        (digits.Length
                         - 1)
                        rest)
            | c :: rest ->
                loop (string (Char.ToLowerInvariant c) :: acc) rest

        loop [] (List.ofSeq segment)

    let private segmentWords (name: string) (segment: string) =
        if String.IsNullOrEmpty segment then
            Error $"The name '{name}' has an empty segment."
        elif
            segment
            |> Seq.forall (fun c ->
                isLower c
                || isDigit c
            )
        then
            Ok [ segment ]
        elif
            segment
            |> Seq.forall (fun c ->
                isUpper c
                || isDigit c
            )
        then
            Ok(initialismWords segment)
        else
            Error
                $"The segment '{segment}' of the name '{name}' must be all lowercase or all uppercase."

    /// <summary>Parses a canonical name string such as <c>value-in-USD</c>.</summary>
    let parseName (input: string) : Result<Name, string> =
        if String.IsNullOrEmpty input then
            Error "A name cannot be empty."
        else
            input.Split('-')
            |> List.ofArray
            |> traverse (segmentWords input)
            |> Result.map (
                List.concat
                >> Name
            )

    /// <summary>Parses the words of a legacy name array, where each word matches <c>^[a-z0-9]+$</c>.</summary>
    let parseLegacyName (words: string list) : Result<Name, string> =
        let isWord (word: string) =
            not (String.IsNullOrEmpty word)
            && word
               |> Seq.forall (fun c ->
                   isLower c
                   || isDigit c
               )

        match
            words
            |> List.tryFind (
                isWord
                >> not
            )
        with
        | _ when List.isEmpty words -> Error "A legacy name needs at least one word."
        | Some word -> Error $"The legacy word '{word}' must be lowercase letters and digits."
        | None -> Ok(Name words)

    /// <summary>Parses a canonical path string such as <c>morphir/SDK</c>.</summary>
    let parsePath (input: string) : Result<Path, string> =
        if String.IsNullOrEmpty input then
            Error "A path cannot be empty."
        else
            input.Split('/')
            |> List.ofArray
            |> traverse parseName
            |> Result.map Path

    /// <summary>Parses a canonical FQName string such as <c>morphir/SDK:list#map</c>.</summary>
    /// <remarks>
    /// Exactly one <c>:</c> and then exactly one <c>#</c> are allowed, so that the string has one reading.
    /// </remarks>
    let parseFQName (input: string) : Result<FQName, string> =
        let invalid () =
            Error $"The FQName '{input}' must have the form package:module#name."

        if String.IsNullOrEmpty input then
            invalid ()
        else
            let colon = input.IndexOf ':'
            let hash = input.IndexOf '#'

            if
                colon < 0
                || hash < colon
                || input.LastIndexOf ':'
                   <> colon
                || input.LastIndexOf '#'
                   <> hash
            then
                invalid ()
            else
                match
                    parsePath (input.Substring(0, colon)),
                    parsePath (
                        input.Substring(
                            colon + 1,
                            hash
                            - colon
                            - 1
                        )
                    ),
                    parseName (input.Substring(hash + 1))
                with
                | Ok package, Ok modulePath, Ok local ->
                    Ok
                        {
                            PackagePath = PackageName package
                            ModulePath = ModulePath modulePath
                            LocalName = local
                        }
                | Error message, _, _
                | _, Error message, _
                | _, _, Error message -> Error message

    /// <summary>
    /// Writes a name's canonical string. A maximal run of two or more single-letter words becomes one
    /// uppercase segment, and a run of one stays a word.
    /// </summary>
    let nameToString (Name words) : string =
        let isLetterWord (word: string) =
            word.Length = 1
            && isLower word[0]

        let rec loop (remaining: string list) =
            match remaining with
            | [] -> []
            | word :: rest when isLetterWord word ->
                let run =
                    word
                    :: List.takeWhile isLetterWord rest

                let after =
                    List.skip
                        (run.Length
                         - 1)
                        rest

                match run with
                | [ single ] ->
                    single
                    :: loop after
                | _ ->
                    (String.concat "" run).ToUpperInvariant()
                    :: loop after
            | word :: rest ->
                word
                :: loop rest

        loop words
        |> String.concat "-"

    /// <summary>Writes a path's canonical string.</summary>
    let pathToString (Path names) : string =
        names
        |> List.map nameToString
        |> String.concat "/"

    /// <summary>Writes an FQName's canonical string.</summary>
    let fqNameToString (fqName: FQName) : string =
        let (PackageName package) = fqName.PackagePath
        let (ModulePath modulePath) = fqName.ModulePath
        $"{pathToString package}:{pathToString modulePath}#{nameToString fqName.LocalName}"
