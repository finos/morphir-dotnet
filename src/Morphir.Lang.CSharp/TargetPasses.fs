// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System.Text.Json
open System.Threading
open Morphir.Compilation
open Morphir.CodeGeneration.CSharp
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.MiddleEnd

/// <summary>The target-side passes for the target <c>csharp</c>: the passes that run right before the C# backend.</summary>
[<RequireQualifiedAccess>]
module CSharpTargetPasses =

    /// <summary>The target id that the passes claim.</summary>
    [<CompiledName("Target")>]
    let target = "csharp"

    /// <summary>The id of the admission pass, the first target-side pass for <c>csharp</c>.</summary>
    [<CompiledName("AdmissionId")>]
    let admissionId = "morphir-dotnet.csharp.admission"

    let private locationData (location: string) : JsonElement =
        // Utf8JsonWriter, not a serializer: the serializer needs reflection, which Native AOT does not have.
        use stream = new System.IO.MemoryStream()

        (use writer = new Utf8JsonWriter(stream)
         writer.WriteStartObject()
         writer.WriteString("location", location)
         writer.WriteEndObject())

        use document = JsonDocument.Parse(stream.ToArray())
        document.RootElement.Clone()

    /// <summary>
    /// The model of a package for the checks: a library of the modules that the view lists. Every module is public here, since
    /// the checks do not read the access of a module.
    /// </summary>
    let private packageOf (unit: ModuleUnit) (view: IPackageView) : VersionedModel =
        let modules =
            view.ModulePaths
            |> List.choose (fun path ->
                let definition = if path = unit.Path then Some unit.Definition else view.TryModule path
                definition |> Option.map (fun found -> ModulePath path, { Access = Public; Value = found }))

        let map =
            match OrderedMap.ofList modules with
            | Ok map -> map
            | Error _ -> OrderedMap.empty

        {
            SourceVersion = CurrentIr.reachableVersion
            Distribution = DistributionModel.V4(Library(unit.Package, OrderedMap.empty, { Modules = map }))
        }

    /// <summary>
    /// The checks of the backend of today as a pass: the type catalog, the lowering and type check of the values, and the
    /// collision checks, with the rules, codes and messages of the backend. The checks need the whole package, so the pass runs
    /// them once, in the unit of the first module, and reads the other modules through the package view. It changes no module.
    /// The checks use the default naming: the namespace option of the backend stays with the backend.
    /// </summary>
    [<CompiledName("Admission")>]
    let admission: IModelTransform =
        { new IModelTransform with
            member _.Id = admissionId
            member _.Name = "C# admission checks"
            member _.Description = "Checks that the model fits the C# backend: types, names, the lowering of values, and name collisions."
            member _.Stage = PassStage.BeforeBackend target
            member _.Removes = Set.empty

            member _.Run(unit, view) =
                let unchanged = { Module = unit.Definition; Applications = [] }

                match view.ModulePaths with
                | first :: _ when first = unit.Path ->
                    let model = packageOf unit view
                    let naming: TypeEmission.Naming = { Package = Some unit.Package; Namespace = None }

                    try
                        Checks.run naming model CancellationToken.None
                        Checked.pass unchanged
                    with Refusal refusal ->
                        Checked.fail [
                            Diagnostic.error (DiagnosticCode.Create refusal.Rule) refusal.Message
                            |> Diagnostic.withData (locationData refusal.Location)
                        ]
                | _ -> Checked.pass unchanged }

    /// <summary>The passes for <c>csharp</c>, in the order that the stage runs them.</summary>
    [<CompiledName("All")>]
    let all: IModelTransform list = [ admission ]
