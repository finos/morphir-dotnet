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

    /// <summary>The handling options that a current-IR model carries (task 6.1). A classic model carries none.</summary>
    static member private CarriersOf(model: VersionedModel) : Carriers =
        match model.Distribution with
        | DistributionModel.V4(Morphir.IR.V4.Library(pkg, _, definition)) ->
            let fq path name : FQName = { PackagePath = pkg; ModulePath = path; LocalName = name }

            let modules = Morphir.IR.V4.OrderedMap.toList definition.Modules

            let values =
                [ for path, access in modules do
                      for name, value in Morphir.IR.V4.OrderedMap.toList access.Value.Values do
                          let output =
                              match value.Value.Value.Body with
                              | Morphir.IR.V4.ExpressionBody(output, _)
                              | Morphir.IR.V4.NativeBody(output, _)
                              | Morphir.IR.V4.ExternalBody(output, _) -> Some output
                              | Morphir.IR.V4.IncompleteBody _ -> None

                          match output |> Option.map Morphir.Lang.CSharp.HandlingCarrier.read with
                          | Some options when options <> Morphir.Lang.CSharp.HandlingOptions.empty -> fq path name, options
                          | _ -> () ]

            let carried owner (items: (Name * Morphir.IR.V4.Type) list) typeName =
                [ for field, t in items do
                      match (Morphir.Lang.CSharp.HandlingCarrier.read t).FloatEquality with
                      | Some equality -> (typeName, owner, field), equality
                      | None -> () ]

            let fields =
                [ for path, access in modules do
                      for name, typeDefinition in Morphir.IR.V4.OrderedMap.toList access.Value.Types do
                          match typeDefinition.Value.Value with
                          | Morphir.IR.V4.TypeAliasDefinition(_, Morphir.IR.V4.Type.Record(_, items)) -> yield! carried name items (fq path name)
                          | Morphir.IR.V4.CustomTypeDefinition(_, _, constructors) ->
                              for constructor, items in Morphir.IR.V4.OrderedMap.toList constructors do
                                  yield! carried constructor items (fq path name)
                          | _ -> () ]

            { Values = Map.ofList values; Fields = Map.ofList fields }
        | _ -> { Values = Map.empty; Fields = Map.empty }

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
            // The precedence order of OPT-1: the command line, the carrier in the model, the types that the caller selects
            // (GenerateWith), the configured keys, the default.
            let carriers = CSharpBackend.CarriersOf model
            let commandLine = options.CommandLine.FloatEquality
            let configured = options.Configured.FloatEquality
            let selected name = if ieee name then Some Morphir.Lang.CSharp.FloatEquality.Ieee else None

            let fieldEquality name owner field =
                commandLine
                |> Option.orElse (Map.tryFind (name, owner, field) carriers.Fields)
                |> Option.orElse (selected name)
                |> Option.orElse configured
                |> Option.defaultValue Morphir.Lang.CSharp.FloatEquality.Clr

            let typeEquality name =
                commandLine |> Option.orElse (selected name) |> Option.orElse configured |> Option.defaultValue Morphir.Lang.CSharp.FloatEquality.Clr

            let handling name =
                Morphir.Lang.CSharp.HandlingOptions.resolve [
                    options.CommandLine
                    Map.tryFind name carriers.Values |> Option.defaultValue Morphir.Lang.CSharp.HandlingOptions.empty
                    options.Configured
                ]

            let context: Context = {
                Naming = naming
                Catalog = catalog
                Layouts = layouts
                FieldEquality = fieldEquality
                TypeEquality = typeEquality
                Handling = handling
                Carriers = carriers
                Ct = ct
            }
            let publicModules = modules |> List.filter (fun (_, isPublic, _) -> isPublic) |> List.map (fun (path, _, _) -> path) |> Set.ofList

            let files =
                Modules.compile context pkg checkedValues publicModules.Contains
                |> List.map (fun (path, unit) -> { RelativePath = path; Text = Format.render unit })

            // Each generated assembly gets the polyfill file (PRD, D12): its types are behind guards, so it is the same for
            // every target framework. The files are in the order of their paths.
            let sources =
                (if List.isEmpty files then [] else files @ [ { RelativePath = Polyfill.path; Text = Polyfill.text } ])
                |> List.sortBy (fun source -> source.RelativePath)

            ct.ThrowIfCancellationRequested()
            result GenerationStatus.Success sources []
        with
        | :? OperationCanceledException when ct.IsCancellationRequested -> result GenerationStatus.Cancelled [] []
        | Refusal diagnostic -> result GenerationStatus.Failure [] [ diagnostic ]

    /// <summary>
    /// The options of the backend from the language version and the handling options of the command line and of
    /// <c>[codegen.csharp]</c>, each as keys and words (<c>float-equality</c>, <c>ieee</c>). An unknown key or word fails with
    /// <c>invalid-backend-option</c>. For C# callers.
    /// </summary>
    static member TryCreateOptions
        (
            languageVersion: int,
            commandLine: System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>,
            configured: System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>
        ) : Result<GenerationOptions, GenerationDiagnostic> =
        let pairs (items: System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>) =
            items |> Seq.map (fun pair -> pair.Key, pair.Value)

        BackendHandling.parse (pairs commandLine)
        |> Result.bind (fun cli ->
            BackendHandling.parse (pairs configured)
            |> Result.map (fun keys -> { LanguageVersion = languageVersion; CommandLine = cli; Configured = keys }))

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
