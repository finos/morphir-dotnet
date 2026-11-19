// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System
open System.Collections.Immutable
open Microsoft.CodeAnalysis
open Morphir.Compilation

/// <summary>
/// The fixed reference set of the C# frontend: the .NET 10 reference assemblies, <c>Morphir.SDK.CSharp</c> and the
/// attribute assembly. An IDE compiles with the references of the project, and <c>morphir compile</c> with these, so an
/// admission rule refuses a symbol from any other assembly.
/// </summary>
module internal FixedReferences =

    let private fromImage (name: string) =
        match Embedded.tryRead name with
        | Some bytes -> Ok(MetadataReference.CreateFromImage(ImmutableArray.Create<byte>(bytes)) :> MetadataReference)
        | None -> Error name

    let private load =
        lazy
            (let framework =
                Basic.Reference.Assemblies.Net100.References.All
                |> Seq.map (fun reference -> reference :> MetadataReference)
                |> List.ofSeq

             let embedded = [ "Morphir.SDK.CSharp.dll"; "Morphir.Lang.CSharp.Attributes.dll" ] |> List.map fromImage

             match embedded |> List.choose (function Error name -> Some name | Ok _ -> None) with
             | [] -> Ok(framework @ (embedded |> List.choose (function Ok reference -> Some reference | Error _ -> None)))
             | missing -> Error missing)

    /// <summary>The references, or the names of the embedded assemblies that are missing.</summary>
    let all () : Result<MetadataReference list, string list> = load.Force()

    /// <summary>The diagnostic for a reference set that cannot load.</summary>
    let unavailable (missing: string list) : Diagnostic =
        let names = String.Join(", ", missing)

        Diagnostic.error
            CSharpCodes.referencesUnavailable
            $"The C# frontend cannot load its fixed references. The package misses the embedded assemblies: {names}."
