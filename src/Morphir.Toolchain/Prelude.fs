// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Text

/// Railway helpers for the contract readers. Every reader returns the first failure it finds, so `for` stops at the
/// first error.
[<AutoOpen>]
module internal Prelude =

    type ResultBuilder() =
        member _.Return(value) : Result<'T, 'E> = Ok value
        member _.ReturnFrom(result: Result<'T, 'E>) = result
        member _.Bind(result: Result<'T, 'E>, next: 'T -> Result<'U, 'E>) = Result.bind next result
        member _.Zero() : Result<unit, 'E> = Ok()
        member _.Delay(body: unit -> Result<'T, 'E>) = body
        member _.Run(body: unit -> Result<'T, 'E>) = body ()

        member _.Combine(first: Result<unit, 'E>, rest: unit -> Result<'T, 'E>) = Result.bind rest first

        member _.For(items: 'T seq, body: 'T -> Result<unit, 'E>) : Result<unit, 'E> =
            use enumerator = items.GetEnumerator()
            let mutable state = Ok()

            while Result.isOk state && enumerator.MoveNext() do
                state <- body enumerator.Current

            state

    let result = ResultBuilder()

    module Result =
        /// Applies `f` to each item in order and stops at the first error.
        let traverse (f: 'T -> Result<'U, 'E>) (items: 'T list) : Result<'U list, 'E> =
            let rec go acc rest =
                match rest with
                | [] -> Ok(List.rev acc)
                | item :: tail ->
                    match f item with
                    | Ok value -> go (value :: acc) tail
                    | Error error -> Error error

            go [] items

        /// Applies `f` to each item with its index, in index order, and stops at the first error.
        let traversei (f: int -> 'T -> Result<'U, 'E>) (items: 'T list) : Result<'U list, 'E> =
            items |> List.indexed |> traverse (fun (index, item) -> f index item)

        /// The first error of a list of checks, in order.
        let firstError (checks: (unit -> Result<unit, 'E>) list) : Result<unit, 'E> =
            checks |> traverse (fun check -> check ()) |> Result.map ignore

    /// Compares two strings by their UTF-8 bytes, as unsigned values.
    let compareUtf8 (left: string) (right: string) =
        let a = Encoding.UTF8.GetBytes left
        let b = Encoding.UTF8.GetBytes right
        (ReadOnlySpan a).SequenceCompareTo(ReadOnlySpan b)

    /// Strict UTF-8, refusing invalid byte sequences instead of replacing them.
    let strictUtf8 = UTF8Encoding(false, true)

    let isAsciiLower (c: char) = c >= 'a' && c <= 'z'
    let isAsciiDigit (c: char) = c >= '0' && c <= '9'

    /// ASCII case-insensitive equality: only A-Z and a-z fold, never other characters.
    let equalsAsciiIgnoreCase (left: string) (right: string) =
        let fold (c: char) =
            if c >= 'A' && c <= 'Z' then char (int c + 32) else c

        left.Length = right.Length
        && Seq.forall2 (fun a b -> fold a = fold b) left right
