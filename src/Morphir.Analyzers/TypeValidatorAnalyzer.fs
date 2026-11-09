namespace Morphir.Analyzers

open System.Collections.Generic
open System.Threading.Tasks
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins
open Morphir.Extensions

/// <summary>
/// The built-in analyzer <c>morphir.type-validator</c>: it runs the pipeline <c>TypeValidator</c> on the package of a
/// classic model and reports its definite type mismatches. It reads classic format versions 1, 2 and 3. It does not
/// change the model.
/// </summary>
[<Sealed>]
type TypeValidatorAnalyzer(version: ReleaseVersion) =
    static let id = ExtensionId.Create "morphir.type-validator"

    static let claim =
        ValidatorClaim.Create(Versioning.Classic 1, Versioning.Classic 2, Versioning.Classic 3)

    let entry =
        ExtensionEntry.BuiltInValidator(
            ExtensionIdentity.Create(
                id,
                ExtensionName.Create "Type validator",
                version,
                ExtensionDescription.Create "Reports definite type mismatches in classic models."
            ),
            claim
        )

    /// <summary>The extension id <c>morphir.type-validator</c>.</summary>
    static member Id = id

    /// <summary>
    /// The diagnostics for a classic package, in the order in which the type validator reports them. The package name
    /// lets the validator resolve the package's own type aliases.
    /// </summary>
    static member AnalyzePackage
        (
            packageName: PackageName,
            package: Package.PackageDefinition<'ta, 'va>
        ) : IReadOnlyList<Morphir.Mep.Diagnostic> =
        let _, file = (TypeValidator.createForPackage packageName).Transform package VFile.empty

        [|
            for message in file.Messages -> MessageMapping.toDiagnostic message
        |]

    /// <summary>The extension id and version.</summary>
    override _.ToString() = $"{id} {version}"

    interface IModelAnalyzer with
        member _.Entry = entry

        member _.AnalyzeAsync(model, ct) =
            ct.ThrowIfCancellationRequested()

            let diagnostics =
                match model with
                | DistributionModel.Classic(Classic.Distribution.Library(packageName, _, definition)) ->
                    TypeValidatorAnalyzer.AnalyzePackage(packageName, definition)
                // A specification has no value definitions, so there is nothing to check.
                | DistributionModel.Classic(Classic.Distribution.Specs _)
                // The claim has no V4 version, so the runner never selects this analyzer for a V4 model.
                | DistributionModel.V4 _ -> [||]

            Task.FromResult diagnostics
