namespace Morphir.Extensions

open System
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

/// <summary>
/// An extension id: one or more lowercase portable tokens joined by dots, at most 238 characters,
/// for example <c>morphir.csharp</c>. Each token follows the upstream rule; dots are a morphir-dotnet superset.
/// </summary>
/// <remarks>Equality and order are ordinal. F# callers use <c>Parse</c>; C# callers use <c>TryParse</c>.</remarks>
[<CustomEquality; CustomComparison>]
type ExtensionId =
    private
    | ExtensionId of string

    /// <summary>The canonical text.</summary>
    member this.Value =
        let (ExtensionId value) = this
        value

    /// <summary>The canonical text.</summary>
    override this.ToString() = this.Value

    /// <summary>
    /// True when the id is reserved for a host's built-in frontends and backends: it starts with <c>morphir.</c>
    /// (<see cref="Morphir.Mep.ReservedExtensionIds"/>). A host refuses a guest that reports such an id.
    /// </summary>
    member this.ReservedForBuiltIns = Morphir.Mep.ReservedExtensionIds.IsReserved this.Value

    /// <summary>True for a value of the same type with equal content. Text is compared ordinally.</summary>
    override this.Equals(other: obj | null) =
        match other with
        | :? ExtensionId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    /// <summary>A hash that agrees with <c>Equals</c>.</summary>
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ExtensionId as id -> String.CompareOrdinal(this.Value, id.Value)
            | _ -> invalidArg (nameof other) "Not an ExtensionId."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<ExtensionId, ParseFailure> =
        Text.dotted "extension id" 238 text
        |> Result.map ExtensionId

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<ExtensionId>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>)
        : bool =
        match ExtensionId.Parse text with
        | Ok id ->
            result <- id
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds an id from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : ExtensionId =
        match ExtensionId.Parse literal with
        | Ok id -> id
        | Error failure ->
            invalidArg (nameof literal) (Text.refusedLiteral "extension id" literal failure)

/// <summary>A generation target id, for example <c>csharp</c>. It follows the extension id grammar.</summary>
/// <remarks>Equality and order are ordinal. F# callers use <c>Parse</c>; C# callers use <c>TryParse</c>.</remarks>
[<CustomEquality; CustomComparison>]
type TargetId =
    private
    | TargetId of string

    /// <summary>The canonical text.</summary>
    member this.Value =
        let (TargetId value) = this
        value

    /// <summary>The canonical text.</summary>
    override this.ToString() = this.Value

    /// <summary>True for a value of the same type with equal content. Text is compared ordinally.</summary>
    override this.Equals(other: obj | null) =
        match other with
        | :? TargetId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    /// <summary>A hash that agrees with <c>Equals</c>.</summary>
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? TargetId as id -> String.CompareOrdinal(this.Value, id.Value)
            | _ -> invalidArg (nameof other) "Not a TargetId."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<TargetId, ParseFailure> =
        Text.dotted "target id" 238 text
        |> Result.map TargetId

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<TargetId>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>)
        : bool =
        match TargetId.Parse text with
        | Ok id ->
            result <- id
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds a target id from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : TargetId =
        match TargetId.Parse literal with
        | Ok id -> id
        | Error failure ->
            invalidArg (nameof literal) (Text.refusedLiteral "target id" literal failure)
