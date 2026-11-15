// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open System
open System.Threading
open Morphir.Lang.CSharp.Backend
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// The C# backend: it plans the files from the checked model, builds each file as a Roslyn syntax tree, and lays the text out.
/// It is a pure function of the model and the options, with no file or process access.
/// </summary>
[<AbstractClass; Sealed>]
type CSharpBackend private () =

    /// <summary>The only language version that the backend writes.</summary>
    static member LanguageVersion = 15

    static member private Core
        (
            model: VersionedModel,
            options: GenerationOptions,
            namespaceOverride: NamespaceOverride option,
            ieee: FQName -> bool,
            ct: CancellationToken
        ) : GenerationResult =
        let result status sources diagnostics = {
            Status = status
            Family = model.Family
            SourceVersion = model.SourceVersion.Text
            LanguageVersion = options.LanguageVersion
            Sources = sources
            Diagnostics = diagnostics
            Runtime = { TargetFramework = "net10.0"; Package = "Morphir.SDK.CSharp" }
        }

        try
            ct.ThrowIfCancellationRequested()

            if namespaceOverride |> Option.exists (fun value -> obj.ReferenceEquals(value, null)) then
                Admission.fail "invalid-namespace" "$options/namespace" "An explicit namespace override must be validated and non-null."

            if options.LanguageVersion <> CSharpBackend.LanguageVersion then
                Admission.fail "unsupported-language" "$options/languageVersion" "Only C# 15 is supported. Use language-version 15."

            let versions = if model.Family = ModelFamily.Classic then [ "1"; "2"; "3"; "3.0.0"; "3.1.0" ] else [ "4"; "4.0.0" ]

            if not (List.contains model.SourceVersion.Text versions) then
                Admission.fail "unsupported-version" "$sourceVersion" "The source version is not supported for this model family."

            let package =
                match model.Distribution with
                | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, _, _)) -> Some pkg
                | DistributionModel.V4(Morphir.IR.V4.Library(pkg, _, _)) -> Some pkg
                | _ -> None

            let naming: TypeEmission.Naming = { Package = package; Namespace = namespaceOverride |> Option.map (fun value -> value.Value) }

            let initialCatalog, _ = TypeEmission.prepareUnionAware naming [] model ct
            let checkedValues = ValueLowering.declarations model ct |> ValueLowering.check initialCatalog ct
            let records = ValueLowering.recordDeclarations initialCatalog model ct checkedValues
            let catalog, _ = TypeEmission.prepareUnionAware naming records model ct
            let pkg, modules = Checks.moduleList model
            Checks.validateCollisions ct catalog pkg modules
            Checks.validateLayout ct naming catalog pkg modules

            let layouts = ModuleLayouts.compute naming pkg (modules |> List.map (fun (path, _, names) -> path, not (List.isEmpty names)))
            let context: Context = { Naming = naming; Catalog = catalog; Layouts = layouts; Ieee = ieee; Ct = ct }
            let publicModules = modules |> List.filter (fun (_, isPublic, _) -> isPublic) |> List.map (fun (path, _, _) -> path) |> Set.ofList

            let files =
                Modules.compile context pkg checkedValues publicModules.Contains
                |> List.map (fun (path, unit) -> { RelativePath = path; Text = Format.render unit })

            // Each generated assembly gets the polyfill file (PRD, D12): its types are behind guards, so it is the same for
            // every target framework. The files of the model come first, in the order of their paths.
            let sources =
                match files |> List.sortBy (fun source -> source.RelativePath) with
                | [] -> []
                | sorted -> sorted @ [ { RelativePath = Polyfill.path; Text = Polyfill.text } ]

            ct.ThrowIfCancellationRequested()
            result GenerationStatus.Success sources []
        with
        | :? OperationCanceledException when ct.IsCancellationRequested -> result GenerationStatus.Cancelled [] []
        | Refusal diagnostic -> result GenerationStatus.Failure [] [ diagnostic ]

    /// <summary>Generates the C# of a model with the default namespace of its package.</summary>
    static member Generate(model: VersionedModel, options: GenerationOptions, cancellationToken: CancellationToken) : GenerationResult =
        CSharpBackend.Core(model, options, None, (fun _ -> false), cancellationToken)

    /// <summary>Generates the C# of a model in the namespace of the override.</summary>
    static member Generate
        (
            model: VersionedModel,
            options: GenerationOptions,
            namespaceOverride: NamespaceOverride,
            cancellationToken: CancellationToken
        ) : GenerationResult =
        CSharpBackend.Core(model, options, Some namespaceOverride, (fun _ -> false), cancellationToken)

    /// <summary>
    /// Generates the C# of a model with the types for which <paramref name="ieee"/> is true written with IEEE equality: NaN is
    /// not equal to NaN, and the Morphir equality members are written. Other types have the equality of plain C#.
    /// </summary>
    static member GenerateWith
        (
            model: VersionedModel,
            options: GenerationOptions,
            namespaceOverride: NamespaceOverride option,
            ieee: FQName -> bool,
            cancellationToken: CancellationToken
        ) : GenerationResult =
        CSharpBackend.Core(model, options, namespaceOverride, ieee, cancellationToken)
