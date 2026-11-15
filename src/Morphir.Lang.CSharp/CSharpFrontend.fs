// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System
open System.Threading
open System.Threading.Tasks
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// The in-process C# frontend. It compiles the documents with Roslyn against fixed references, runs the admission
/// analyzer, plans the modules, and lowers symbols and operations to the current IR with the builder of the V4 family.
/// </summary>
[<RequireQualifiedAccess>]
module CSharpFrontend =

    /// <summary>The identity of the in-process C# frontend.</summary>
    [<CompiledName("Identity")>]
    let identity = ComponentId.Create "morphir-dotnet.csharp"

    /// <summary>The language id that the frontend reads.</summary>
    [<CompiledName("LanguageId")>]
    let languageId = LanguageId.Create PackageInfo.Language

    let private language: LanguageCapability = {
        Id = languageId
        FileExtensions = [| PackageInfo.SourceExtension |]
    }

    let private failure message : FrontendOutcome =
        FrontendOutcome.Failed
            {
                Message = message
                Diagnostics = [||]
            }

    let private moduleName (planned: PlannedModule) =
        let (ModulePath path) = planned.Path
        Path.toString Name.toTitleCase "." path

    let private run (builders: FamilyBuilderRegistry) (request: CompileRequest) (cancellationToken: CancellationToken) =
        let documents = request.DocumentList

        match CSharpCompiler.compile documents with
        | Error diagnostic -> Checked.fail [ diagnostic ]
        | Ok compiled ->
            let compilerDiagnostics = CSharpCompiler.compilerDiagnostics compiled cancellationToken

            if compilerDiagnostics |> List.exists (fun d -> d.IsError) then
                // C# errors stop compilation: the refusals of the subset would only repeat the same mistake.
                Checked.fail compilerDiagnostics
            else
                let admission = CSharpCompiler.admissionDiagnostics compiled cancellationToken

                if not (List.isEmpty admission) then
                    Checked.fail (compilerDiagnostics @ admission)
                else
                    let lowered =
                        check {
                            let! plan = Planning.plan request.Package request.ExposedModules compiled cancellationToken
                            let! builder = builders.Resolve request.Output.Family
                            let! model = builder.Build(PackageLowering plan)

                            return {
                                Model = model
                                Modules = [ for planned in plan.Modules -> moduleName planned ]
                            }
                        }

                    // Warnings of the C# compiler come first, then the diagnostics of the later stages.
                    Checked.ofDiagnostics () compilerDiagnostics
                    |> Checked.bind (fun () -> lowered)

    /// <summary>
    /// Builds the in-process C# frontend over the given builders and writers. It advertises the targets of the current
    /// IR family for which both are registered: the C# frontend writes the current IR only. For F# and C# callers.
    /// </summary>
    [<CompiledName("Create")>]
    let create (builders: FamilyBuilderRegistry) (writers: WriterRegistry) : IFrontend =
        let outputs =
            FrontendTargets.supported builders writers
            |> Seq.filter (fun target -> target.Family = ModelFamily.V4)
            |> Array.ofSeq

        let capability = {
            Languages = [| language |]
            Outputs = outputs
            Incremental = false
            MultiDocument = true
        }

        { new IFrontend with
            member _.Identity = identity
            member _.Transport = FrontendTransport.InProcess
            member _.Capability = capability

            member _.CompileAsync(request, cancellationToken) =
                if request.LanguageId <> languageId then
                    Task.FromResult(
                        failure
                            $"The C# frontend {identity} does not read the language '{request.LanguageId}'. It reads '{languageId}'."
                    )
                elif not (capability.Outputs |> Seq.contains request.Output) then
                    let produced = String.Join(", ", capability.Outputs)

                    Task.FromResult(
                        failure $"The C# frontend {identity} does not produce {request.Output}. It produces: {produced}."
                    )
                else
                    task {
                        cancellationToken.ThrowIfCancellationRequested()
                        return FrontendOutcome.Completed(run builders request cancellationToken)
                    }
        }

    /// <summary>The in-process C# frontend with the V4 builder and writer. It produces the current IR (<c>4</c>).</summary>
    [<CompiledName("Registration")>]
    let registration: IFrontend =
        create
            (FamilyBuilderRegistry.Create [ V4FamilyBuilder() :> IFamilyBuilder ])
            (WriterRegistry.Create [ V4IrWriter() :> IIrWriter ])
