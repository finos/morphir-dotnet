// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Frontend.FSharp.Analysis
open Morphir.IR

/// <summary>
/// The in-process F# frontend (FE-1). It runs the frontend stages of FSF-1 once, and builds the model with the
/// builder of the requested family (MID-1).
/// </summary>
[<RequireQualifiedAccess>]
module FSharpFrontend =

    /// <summary>The identity of the in-process F# frontend.</summary>
    [<CompiledName("Identity")>]
    let identity = ComponentId.Create "morphir-dotnet.fsharp"

    /// <summary>The language id that the frontend reads.</summary>
    [<CompiledName("LanguageId")>]
    let languageId = LanguageId.Create "fsharp"

    let private language: LanguageCapability = {
        Id = languageId
        // A signature file is accepted as input, so that admission refuses it with its catalog code.
        FileExtensions = [| ".fs"; ".fsi" |]
    }

    /// <summary>Every target that has both a registered writer and a registered builder, in writer order.</summary>
    let private outputs (builders: FamilyBuilderRegistry) (writers: WriterRegistry) = FrontendTargets.supported builders writers

    let private failure message : FrontendOutcome =
        FrontendOutcome.Failed
            {
                Message = message
                Diagnostics = [||]
            }

    let private moduleName (plan: ModulePlan) =
        let (ModulePath path) = plan.Path
        Path.toString Name.toTitleCase "." path

    let private run
        (builders: FamilyBuilderRegistry)
        (request: CompileRequest)
        (cancellationToken: CancellationToken)
        =
        task {
            let documents =
                request.DocumentList
                |> List.map (fun document -> {
                    FSharpDocument.Uri = document.Uri
                    Text = document.Text
                })

            let! evidence =
                FSharpAnalysis.analyze EvidenceLimits.Default documents cancellationToken

            return check {
                let! evidence = evidence

                let! plan =
                    Planning.plan
                        {
                            Package = request.Package
                            ExposedModules = request.ExposedModules
                        }
                        evidence

                let! builder = builders.Resolve request.Output.Family
                let! model = builder.Build(PackageLowering(plan, evidence))

                return {
                    Model = model
                    Modules = [| for planned in plan.Modules -> moduleName planned.Plan |]
                }
            }
        }

    /// <summary>
    /// Builds the in-process F# frontend over the given builders and writers. It advertises every target for
    /// which both are registered. For F# and C# callers.
    /// </summary>
    [<CompiledName("Create")>]
    let create (builders: FamilyBuilderRegistry) (writers: WriterRegistry) : IFrontend =
        let capability = {
            Languages = [| language |]
            Outputs = outputs builders writers
            Incremental = false
            MultiDocument = true
        }

        { new IFrontend with
            member _.Identity = identity
            member _.Transport = FrontendTransport.InProcess
            member _.Capability = capability

            member _.CompileAsync(request, cancellationToken) =
                if
                    request.LanguageId
                    <> languageId
                then
                    Task.FromResult(
                        failure
                            $"The F# frontend {identity} does not read the language '{request.LanguageId}'. It reads '{languageId}'."
                    )
                elif
                    not (
                        capability.Outputs
                        |> Seq.contains request.Output
                    )
                then
                    let produced = String.Join(", ", capability.Outputs)

                    Task.FromResult(
                        failure
                            $"The F# frontend {identity} does not produce {request.Output}. It produces: {produced}."
                    )
                else
                    task {
                        cancellationToken.ThrowIfCancellationRequested()
                        let! result = run builders request cancellationToken
                        return FrontendOutcome.Completed result
                    }
        }

    /// <summary>
    /// The in-process F# frontend with the native classic and v4 builders and the classic and v4 writers: it
    /// produces classic format versions 1, 2 and 3 and IR v4 (<c>4</c>).
    /// </summary>
    [<CompiledName("Registration")>]
    let registration: IFrontend =
        create
            (FamilyBuilderRegistry.Create [
                ClassicFamilyBuilder() :> IFamilyBuilder
                V4FamilyBuilder()
            ])
            (WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ])
