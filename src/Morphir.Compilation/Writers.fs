namespace Morphir.Compilation

open System.Collections.Generic
open Morphir.IR
open Morphir.IR.Model
open Morphir.Json.Classic
open Morphir.Json.V4

/// <summary>The diagnostic codes of the IR writers.</summary>
[<RequireQualifiedAccess>]
module WriterCodes =
    /// <summary>The requested family cannot hold an element of the model exactly.</summary>
    let Unrepresentable = DiagnosticCode.Create "morphir.unrepresentable"

    /// <summary>The distribution kind cannot be written for the requested version.</summary>
    let UnsupportedDistribution =
        DiagnosticCode.Create "compile.unsupported_distribution"

    /// <summary>The writer received a model of another family.</summary>
    let FamilyMismatch = DiagnosticCode.Create "compile.ir_family_mismatch"

/// <summary>
/// Writes classic library models as JSON for format versions 1, 2 and 3 (IRW-1). Type attributes are empty
/// objects and value attributes are the values' types.
/// </summary>
[<Sealed>]
type ClassicIrWriter() =
    static let versions = [ Versioning.Classic 1; Versioning.Classic 2; Versioning.Classic 3 ]

    static let profiles = [
        Versioning.Classic 1, ClassicWireProfile.v1
        Versioning.Classic 2, ClassicWireProfile.v2
        Versioning.Classic 3, ClassicWireProfile.v3
    ]

    /// <summary>The identity of the classic JSON writer.</summary>
    static member Identity = ComponentId.Create "morphir-dotnet.classic-json"

    override _.ToString() = "classic JSON writer (1, 2, 3)"

    interface IIrWriter with
        member _.Identity = ClassicIrWriter.Identity
        member _.Family = ModelFamily.Classic
        member _.Versions = List.toArray versions :> IReadOnlyList<_>

        member _.Write(target, options, model) =
            match
                model,
                profiles
                |> List.tryFind (fun (version, _) ->
                    FormatVersion.sameVersion version target.Version
                )
            with
            | FamilyModel.V4 _, _ ->
                Checked.fail [
                    Diagnostic.error
                        WriterCodes.FamilyMismatch
                        "The classic writer received a v4 model."
                ]
            | _, None ->
                Checked.fail [
                    Diagnostic.error
                        RegistryCodes.UnsupportedIrVersion
                        $"The classic writer does not write format version {Versioning.version target.Version}."
                ]
            | FamilyModel.Classic distribution, Some(_, profile) ->
                match ClassicJsonWriter.writeTyped profile options.Indented distribution with
                | Ok bytes -> Checked.pass bytes
                | Error error ->
                    let code =
                        if error.Rule = "unsupported-distribution" then
                            WriterCodes.UnsupportedDistribution
                        else
                            WriterCodes.Unrepresentable

                    Checked.fail [ Diagnostic.error code $"{error.Message} ({error.Location})" ]

/// <summary>
/// Writes v4 models as canonical IR v4 JSON (IRW-1). It declares <c>Classic 4</c>, which also matches
/// <c>4.0.0</c>, and writes <c>"formatVersion": 4</c>. A later v4 version, such as a pre-release, is a separate
/// writer that declares it.
/// </summary>
[<Sealed>]
type V4IrWriter() =
    static let versions = [ Versioning.Classic 4 ]

    /// <summary>The identity of the v4 JSON writer.</summary>
    static member Identity = ComponentId.Create "morphir-dotnet.v4-json"

    override _.ToString() = "v4 JSON writer (4)"

    interface IIrWriter with
        member _.Identity = V4IrWriter.Identity
        member _.Family = ModelFamily.V4
        member _.Versions = List.toArray versions :> IReadOnlyList<_>

        member _.Write(target, options, model) =
            match model with
            | FamilyModel.Classic _ ->
                Checked.fail [
                    Diagnostic.error
                        WriterCodes.FamilyMismatch
                        "The v4 writer received a classic model."
                ]
            | FamilyModel.V4 _ when
                not (
                    versions
                    |> List.exists (FormatVersion.sameVersion target.Version)
                )
                ->
                Checked.fail [
                    Diagnostic.error
                        RegistryCodes.UnsupportedIrVersion
                        $"The v4 writer does not write format version {Versioning.version target.Version}."
                ]
            | FamilyModel.V4 distribution ->
                match V4JsonWriter.writeDocument options.Indented target.Version distribution with
                | Ok bytes -> Checked.pass bytes
                | Error error ->
                    Checked.fail [
                        Diagnostic.error
                            WriterCodes.Unrepresentable
                            $"{error.Message} ({error.Location})"
                    ]
