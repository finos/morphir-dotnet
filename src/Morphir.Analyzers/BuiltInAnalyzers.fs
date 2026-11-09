namespace Morphir.Analyzers

open System.Collections.Generic
open Morphir.Extensions

/// <summary>The analyzers that both hosts include. Hosts register this list; there is no assembly scanning.</summary>
[<AbstractClass; Sealed>]
type BuiltInAnalyzers =
    /// <summary>For C# callers: the built-in analyzers, ordered by id, with the host's release version.</summary>
    static member Create(version: ReleaseVersion) : IReadOnlyList<IModelAnalyzer> = [|
        TypeValidatorAnalyzer(version) :> IModelAnalyzer
    |]
