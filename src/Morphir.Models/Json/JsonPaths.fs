namespace Morphir.Json

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Globalization
open System.Text

/// <summary>
/// The JSON locations that the readers and writers of Morphir IR report, as RFC 9535 normalized-style paths: a member
/// whose name is an ASCII member-name shorthand (a letter or <c>_</c>, then letters, digits and <c>_</c>) follows a
/// dot, and every other member is a bracketed name selector with its quote, backslash and control characters escaped.
/// </summary>
[<RequireQualifiedAccess>]
module JsonPaths =
    let private isLetter (c: char) =
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

    /// <summary>Whether a member name is an ASCII RFC 9535 member-name shorthand, which a path writes after a dot.</summary>
    let isShorthand (name: string) =
        name <> ""
        && (isLetter name[0] || name[0] = '_')
        && name |> Seq.forall (fun c -> isLetter c || (c >= '0' && c <= '9') || c = '_')

    /// <summary>Appends the step to a member to a path in a builder.</summary>
    let appendMember (builder: StringBuilder) (name: string) =
        if isShorthand name then
            builder.Append('.').Append(name) |> ignore
        else
            builder.Append("[\"") |> ignore

            for c in name do
                match c with
                | '"' -> builder.Append "\\\""
                | '\\' -> builder.Append "\\\\"
                | c when c < ' ' -> builder.Append("\\u").Append((int c).ToString("x4", CultureInfo.InvariantCulture))
                | c -> builder.Append c
                |> ignore

            builder.Append("\"]") |> ignore

    /// <summary>The location of a member of the value at <paramref name="location"/>, such as <c>$.a</c> or <c>$["a-b"]</c>.</summary>
    let memberAt (location: string) (name: string) =
        let builder = StringBuilder(location)
        appendMember builder name
        builder.ToString()

    /// <summary>The location of an item of the array at <paramref name="location"/>, such as <c>$.a[0]</c>.</summary>
    let itemAt (location: string) (index: int) =
        location + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"
