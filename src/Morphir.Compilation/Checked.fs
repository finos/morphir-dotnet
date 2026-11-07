namespace Morphir.Compilation

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

/// <summary>
/// A result that keeps diagnostics on success and on failure. A passed result never holds an error
/// diagnostic. A failed result holds at least one error, together with every diagnostic gathered before it.
/// F# callers use the <c>Checked</c> module, the <c>check { }</c> builder and the <c>Passed</c>/<c>Failed</c>
/// patterns; C# callers use <c>Pass</c>, <c>Fail</c>, <c>TryGetValue</c> and <c>Match</c>.
/// </summary>
[<RequireQualifiedAccess>]
type Checked<'T> =
    private
    | PassedCase of value: 'T * diagnostics: Diagnostic list
    | FailedCase of diagnostics: Diagnostic list

    /// <summary>True when the result has a value.</summary>
    member this.IsPassed =
        match this with
        | PassedCase _ -> true
        | FailedCase _ -> false

    /// <summary>Every diagnostic, in the order it was produced.</summary>
    member this.Diagnostics: IReadOnlyList<Diagnostic> =
        match this with
        | PassedCase(_, diagnostics)
        | FailedCase diagnostics -> List.toArray diagnostics :> IReadOnlyList<_>

    /// <summary>Gets the value when the result passed. For C# callers.</summary>
    member this.TryGetValue([<Out; MaybeNullWhen(false)>] value: byref<'T>) : bool =
        match this with
        | PassedCase(result, _) ->
            value <- result
            true
        | FailedCase _ ->
            value <- Unchecked.defaultof<_>
            false

    /// <summary>Calls <paramref name="passed"/> or <paramref name="failed"/>. For C# callers.</summary>
    member this.Match<'R>
        (
            passed: Func<'T, IReadOnlyList<Diagnostic>, 'R>,
            failed: Func<IReadOnlyList<Diagnostic>, 'R>
        ) : 'R =
        match this with
        | PassedCase(value, _) -> passed.Invoke(value, this.Diagnostics)
        | FailedCase _ -> failed.Invoke this.Diagnostics

    override this.ToString() =
        match this with
        | PassedCase(_, diagnostics) -> $"Passed with {List.length diagnostics} diagnostic(s)"
        | FailedCase diagnostics -> $"Failed with {List.length diagnostics} diagnostic(s)"

    /// <summary>A passed result with no diagnostics. For C# callers.</summary>
    static member Pass(value: 'T) : Checked<'T> = PassedCase(value, [])

    /// <summary>
    /// A result from a value and the diagnostics gathered for it: failed when any diagnostic is an error,
    /// passed otherwise. For C# callers.
    /// </summary>
    static member Of(value: 'T, diagnostics: IEnumerable<Diagnostic>) : Checked<'T> =
        let diagnostics = List.ofSeq diagnostics

        if
            diagnostics
            |> List.exists (fun x -> x.IsError)
        then
            FailedCase diagnostics
        else
            PassedCase(value, diagnostics)

    /// <summary>
    /// A failed result. Throws <c>ArgumentException</c> when no diagnostic is an error. For C# callers.
    /// </summary>
    static member Fail(diagnostics: IEnumerable<Diagnostic>) : Checked<'T> =
        let diagnostics = List.ofSeq diagnostics

        if
            not (
                diagnostics
                |> List.exists (fun x -> x.IsError)
            )
        then
            invalidArg (nameof diagnostics) "A failed result needs at least one error diagnostic."

        FailedCase diagnostics

/// <summary>Builds and combines checked results. For F# callers.</summary>
[<RequireQualifiedAccess>]
module Checked =

    /// <summary>A passed result with no diagnostics.</summary>
    let pass value : Checked<'T> = Checked.PassedCase(value, [])

    /// <summary>A failed result. Throws <c>ArgumentException</c> when no diagnostic is an error.</summary>
    let fail (diagnostics: Diagnostic list) : Checked<'T> = Checked<'T>.Fail diagnostics

    /// <summary>A result from a value and its diagnostics: failed when any diagnostic is an error.</summary>
    let ofDiagnostics value (diagnostics: Diagnostic list) : Checked<'T> =
        Checked<'T>.Of(value, diagnostics)

    /// <summary>Records one diagnostic. An error fails; a warning, info or hint passes.</summary>
    let note (diagnostic: Diagnostic) : Checked<unit> = ofDiagnostics () [ diagnostic ]

    /// <summary>Every diagnostic, in the order it was produced.</summary>
    let diagnostics (result: Checked<'T>) =
        match result with
        | Checked.PassedCase(_, diagnostics)
        | Checked.FailedCase diagnostics -> diagnostics

    /// <summary>The value, when the result passed.</summary>
    let tryValue (result: Checked<'T>) =
        match result with
        | Checked.PassedCase(value, _) -> Some value
        | Checked.FailedCase _ -> None

    /// <summary>Maps the value and keeps the diagnostics.</summary>
    let map (mapping: 'T -> 'U) (result: Checked<'T>) : Checked<'U> =
        match result with
        | Checked.PassedCase(value, diagnostics) -> Checked.PassedCase(mapping value, diagnostics)
        | Checked.FailedCase diagnostics -> Checked.FailedCase diagnostics

    /// <summary>
    /// Runs a dependent step. Diagnostics stay in production order. The step does not run after a failure.
    /// </summary>
    let bind (binder: 'T -> Checked<'U>) (result: Checked<'T>) : Checked<'U> =
        match result with
        | Checked.FailedCase diagnostics -> Checked.FailedCase diagnostics
        | Checked.PassedCase(value, first) ->
            match binder value with
            | Checked.PassedCase(next, second) ->
                Checked.PassedCase(
                    next,
                    first
                    @ second
                )
            | Checked.FailedCase second ->
                Checked.FailedCase(
                    first
                    @ second
                )

    /// <summary>
    /// Combines two independent results. Both always count: the diagnostics of the left come before those
    /// of the right, and the result fails when either fails.
    /// </summary>
    let zip (left: Checked<'T>) (right: Checked<'U>) : Checked<'T * 'U> =
        match left, right with
        | Checked.PassedCase(a, first), Checked.PassedCase(b, second) ->
            Checked.PassedCase(
                (a, b),
                first
                @ second
            )
        | _ ->
            Checked.FailedCase(
                diagnostics left
                @ diagnostics right
            )

    /// <summary>Applies a function to every item independently and collects every diagnostic, in item order.</summary>
    let traverse (mapping: 'T -> Checked<'U>) (items: 'T list) : Checked<'U list> =
        let folder item (state: Checked<'U list>) =
            zip (mapping item) state
            |> map (fun (head, tail) ->
                head
                :: tail
            )

        List.foldBack folder items (pass [])

    /// <summary>Collects independent results, in order, with every diagnostic.</summary>
    let sequence (results: Checked<'T> list) : Checked<'T list> = traverse id results

/// <summary>The <c>Passed</c> and <c>Failed</c> patterns over a checked result.</summary>
[<AutoOpen>]
module CheckedPatterns =

    /// <summary>Matches a passed result with its value and diagnostics, or a failed result with its diagnostics.</summary>
    let (|Passed|Failed|) (result: Checked<'T>) =
        match result with
        | Checked.PassedCase(value, diagnostics) -> Passed(value, diagnostics)
        | Checked.FailedCase diagnostics -> Failed diagnostics

/// <summary>
/// The <c>check { }</c> builder. <c>let!</c> runs dependent steps and stops at the first failure; <c>and!</c>
/// runs independent steps and keeps every diagnostic from all of them.
/// </summary>
type CheckBuilder() =
    /// <summary>Runs a dependent step.</summary>
    member _.Bind(result: Checked<'T>, binder: 'T -> Checked<'U>) = Checked.bind binder result
    /// <summary>Combines independent steps for <c>and!</c>.</summary>
    member _.MergeSources(left: Checked<'T>, right: Checked<'U>) = Checked.zip left right
    /// <summary>Maps the last step.</summary>
    member _.BindReturn(result: Checked<'T>, mapping: 'T -> 'U) = Checked.map mapping result
    /// <summary>Returns a value with no diagnostics.</summary>
    member _.Return(value: 'T) = Checked.pass value
    /// <summary>Returns a checked result as it is.</summary>
    member _.ReturnFrom(result: Checked<'T>) = result
    /// <summary>A passed unit result.</summary>
    member _.Zero() = Checked.pass ()
    /// <summary>Runs the second step after the first passes.</summary>
    member _.Combine(first: Checked<unit>, second: unit -> Checked<'T>) = Checked.bind second first
    /// <summary>Delays a step.</summary>
    member _.Delay(body: unit -> Checked<'T>) = body
    /// <summary>Runs a delayed step.</summary>
    member _.Run(body: unit -> Checked<'T>) = body ()

/// <summary>The <c>check { }</c> computation expression.</summary>
[<AutoOpen>]
module CheckBuilderInstance =
    /// <summary>The <c>check { }</c> computation expression (DIAG-1).</summary>
    let check = CheckBuilder()
