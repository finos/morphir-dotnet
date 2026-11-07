namespace Morphir.Extensions

open System
open System.Collections
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

/// <summary>An immutable set with at least one element, ordered by the element type.</summary>
/// <remarks>
/// F# callers use the <c>NonEmptySet</c> module. C# callers use <c>Create</c>, <c>TryCreate</c> and <c>Map</c>,
/// not the compiled <c>NonEmptySetModule</c>, which uses F# function and list types.
/// The element type must implement <c>IComparable</c>: C# cannot see the F# comparison constraint, and an element
/// type without it fails at run time when the set is built.
/// </remarks>
[<Sealed>]
type NonEmptySet<'T when 'T: comparison> internal (items: Set<'T>) =
    do
        if items.IsEmpty then
            invalidArg (nameof items) "A NonEmptySet needs at least one element."

    member internal _.Items = items

    /// <summary>The number of elements.</summary>
    member _.Count = items.Count

    /// <summary>True when the set has the element.</summary>
    member _.Contains(item: 'T) = items.Contains item

    /// <summary>For C# callers: a set of the first element and the other elements.</summary>
    static member Create(first: 'T, [<ParamArray>] rest: 'T array) : NonEmptySet<'T> =
        NonEmptySet<'T>(
            Set.ofArray rest
            |> Set.add first
        )

    /// <summary>For C# callers: a set of the elements, or false when there is no element.</summary>
    static member TryCreate
        (
            items: IEnumerable<'T>,
            [<Out; MaybeNullWhen(false)>] result: byref<NonEmptySet<'T>>
        ) : bool =
        let set = Set.ofSeq items

        if set.IsEmpty then
            result <- Unchecked.defaultof<_>
            false
        else
            result <- NonEmptySet<'T>(set)
            true

    /// <summary>For C# callers: the set of mapped elements. Equal results collapse to one element.</summary>
    member _.Map<'U when 'U: comparison>(mapping: Func<'T, 'U>) : NonEmptySet<'U> =
        NonEmptySet<'U>(Set.map mapping.Invoke items)

    /// <summary>True for a value of the same type with equal content. Text is compared ordinally.</summary>
    override _.Equals(other: obj | null) =
        match other with
        | :? NonEmptySet<'T> as set -> items = set.Items
        | _ -> false

    /// <summary>A hash that agrees with <c>Equals</c>.</summary>
    override _.GetHashCode() = hash items

    /// <summary>The canonical text.</summary>
    override _.ToString() =
        items
        |> Seq.map string
        |> String.concat ", "

    interface IComparable with
        member _.CompareTo(other: obj | null) =
            match other with
            | :? NonEmptySet<'T> as set -> compare items set.Items
            | _ -> invalidArg (nameof other) "Not a NonEmptySet of the same element type."

    interface IReadOnlyCollection<'T> with
        member _.Count = items.Count

        member _.GetEnumerator() : IEnumerator<'T> =
            (items :> IEnumerable<'T>).GetEnumerator()

        member _.GetEnumerator() : IEnumerator = (items :> IEnumerable).GetEnumerator()

/// <summary>F# functions for <c>NonEmptySet</c>.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module NonEmptySet =
    /// <summary>A set of the first element and the other elements.</summary>
    let create (first: 'T) (rest: 'T seq) : NonEmptySet<'T> =
        NonEmptySet<'T>(
            Set.ofSeq rest
            |> Set.add first
        )

    /// <summary>A set of the elements, or <c>ParseFailure.Empty field</c> when there is no element.</summary>
    let ofSeq (field: string) (items: 'T seq) : Result<NonEmptySet<'T>, ParseFailure> =
        let set = Set.ofSeq items

        if set.IsEmpty then
            Error(ParseFailure.Empty field)
        else
            Ok(NonEmptySet<'T>(set))

    /// <summary>The set of mapped elements. Equal results collapse to one element.</summary>
    let map (mapping: 'T -> 'U) (set: NonEmptySet<'T>) : NonEmptySet<'U> =
        NonEmptySet<'U>(Set.map mapping set.Items)

    /// <summary>The elements in order.</summary>
    let toList (set: NonEmptySet<'T>) : 'T list = Set.toList set.Items
