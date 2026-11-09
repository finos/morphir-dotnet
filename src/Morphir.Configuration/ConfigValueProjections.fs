namespace Morphir.Configuration

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.InteropServices

/// <summary>
/// Typed reads of a <see cref="ConfigValue"/> for C# callers. Each member decides on the union case in F#, with an
/// exhaustive match, so a new case cannot silently pass through a C# type test.
/// </summary>
[<AbstractClass; Sealed>]
type ConfigValueProjections =

    /// <summary>The text of a string value. False for every other case.</summary>
    static member TryText(value: ConfigValue, [<Out>] text: byref<string>) : bool =
        match value with
        | ConfigValue.String item ->
            text <- item
            true
        | ConfigValue.Null
        | ConfigValue.Boolean _
        | ConfigValue.Integer _
        | ConfigValue.BigInteger _
        | ConfigValue.Number _
        | ConfigValue.Temporal _
        | ConfigValue.Array _
        | ConfigValue.Object _
        | ConfigValue.SecretReference _ ->
            text <- Unchecked.defaultof<_>
            false

    /// <summary>
    /// The text of a version setting: a string as written, or an integer in invariant culture (for example
    /// <c>format_version = 4</c>). False for every other case.
    /// </summary>
    static member TryVersionText(value: ConfigValue, [<Out>] text: byref<string>) : bool =
        match value with
        | ConfigValue.String item ->
            text <- item
            true
        | ConfigValue.Integer item ->
            text <- item.ToString(CultureInfo.InvariantCulture)
            true
        | ConfigValue.Null
        | ConfigValue.Boolean _
        | ConfigValue.BigInteger _
        | ConfigValue.Number _
        | ConfigValue.Temporal _
        | ConfigValue.Array _
        | ConfigValue.Object _
        | ConfigValue.SecretReference _ ->
            text <- Unchecked.defaultof<_>
            false

    /// <summary>
    /// The string items of an array value, in order; items of other kinds are not text and are left out. False when
    /// the value is not an array.
    /// </summary>
    static member TryTextList(value: ConfigValue, [<Out>] items: byref<IReadOnlyList<string>>) : bool =
        match value with
        | ConfigValue.Array values ->
            items <-
                values
                |> List.choose (fun item ->
                    let mutable text = null
                    if ConfigValueProjections.TryText(item, &text) then Some text else None)
                |> Array.ofList
                :> IReadOnlyList<string>

            true
        | ConfigValue.Null
        | ConfigValue.String _
        | ConfigValue.Boolean _
        | ConfigValue.Integer _
        | ConfigValue.BigInteger _
        | ConfigValue.Number _
        | ConfigValue.Temporal _
        | ConfigValue.Object _
        | ConfigValue.SecretReference _ ->
            items <- Unchecked.defaultof<_>
            false

    /// <summary>
    /// The names of the child tables of an object value that hold <paramref name="key"/>, in ordinal order. Empty
    /// when the value is not an object.
    /// </summary>
    static member TablesWithKey(value: ConfigValue, key: string) : IReadOnlyList<string> =
        let holds (child: ConfigValue) =
            match child with
            | ConfigValue.Object table -> table.ContainsKey key
            | ConfigValue.Null
            | ConfigValue.String _
            | ConfigValue.Boolean _
            | ConfigValue.Integer _
            | ConfigValue.BigInteger _
            | ConfigValue.Number _
            | ConfigValue.Temporal _
            | ConfigValue.Array _
            | ConfigValue.SecretReference _ -> false

        match value with
        | ConfigValue.Object tables ->
            tables
            |> Map.toArray
            |> Array.filter (fun (_, child) -> holds child)
            |> Array.map fst
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left, right))
            :> IReadOnlyList<string>
        | ConfigValue.Null
        | ConfigValue.String _
        | ConfigValue.Boolean _
        | ConfigValue.Integer _
        | ConfigValue.BigInteger _
        | ConfigValue.Number _
        | ConfigValue.Temporal _
        | ConfigValue.Array _
        | ConfigValue.SecretReference _ -> Array.empty :> IReadOnlyList<string>
