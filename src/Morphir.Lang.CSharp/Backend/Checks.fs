// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open System.Threading
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Model

/// <summary>
/// The checks of the C# backend that need no text: the type catalog, the lowering of values with its type check, and the
/// name-collision checks of the modules. They were part of the backend of today; the target-side pass for <c>csharp</c> runs
/// them before the backend, and the text emission still runs them on its way to the sources.
/// </summary>
module internal Checks =

    /// <summary>
    /// The package and, for each module in order, its path, whether it is public and the names of its values. Only a library
    /// is admitted.
    /// </summary>
    let moduleList (model: VersionedModel) =
        match model.Distribution with
        | DistributionModel.Classic(Distribution.Library(pkg, _, definition)) ->
            pkg,
            definition.Modules
            |> Map.toList
            |> List.map (fun (path, modul) ->
                path,
                modul.Access = AccessControlled.Public,
                modul.Value.Values
                |> Map.toList
                |> List.map fst
            )
        | DistributionModel.V4(Morphir.IR.V4.Library(pkg, _, definition)) ->
            pkg,
            definition.Modules
            |> Morphir.IR.V4.OrderedMap.toList
            |> List.sortBy fst
            |> List.map (fun (path, modul) ->
                path,
                modul.Access = Morphir.IR.V4.Public,
                modul.Value.Values
                |> Morphir.IR.V4.OrderedMap.toList
                |> List.map fst
            )
        | _ ->
            Admission.fail
                "unsupported-distribution"
                "$"
                "Only admitted Library distributions can be generated."

    /// <summary>The check that no module has a value that shares its identity with an emitted type.</summary>
    let validateCollisions (ct: CancellationToken) (catalog: Map<_, TypeEmission.Declaration>) pkg modules =
        ct.ThrowIfCancellationRequested()

        if not (List.isEmpty modules) then
            TypeEmission.validateModules
                ct
                pkg
                (modules
                 |> List.map (fun (path, _, names) -> path, names))

        for path, _, names in modules do
            ct.ThrowIfCancellationRequested()

            for name in names do
                ct.ThrowIfCancellationRequested()

                for declaration in
                    catalog
                    |> Map.toList
                    |> List.map snd do
                    ct.ThrowIfCancellationRequested()

                    let emitted =
                        match declaration.Definition with
                        | TypeEmission.Cases _ -> true
                        | TypeEmission.Alias(TypeEmission.Fields _ as body) ->
                            let owner, _ =
                                TypeEmission.recordTarget ct catalog declaration.Location body

                            owner.Name = declaration.Name
                        | _ -> false

                    if
                        emitted
                        && declaration.Name.ModulePath = path
                        && TypeEmission.identifier declaration.Name.LocalName = TypeEmission.identifier
                            name
                    then
                        Admission.fail
                            "name-collision"
                            declaration.Location
                            (TypeEmission.title name
                             + " shares an emitted type and value identity.")

    /// <summary>
    /// Runs every check of the backend on a model: the type catalog, the lowering and type check of the values, and the
    /// collision checks. A refusal is raised as <c>Refusal</c>, as the backend of today raises it.
    /// </summary>
    let run (naming: TypeEmission.Naming) (model: VersionedModel) (ct: CancellationToken) : unit =
        let initialCatalog, _ = TypeEmission.prepare naming model ct

        let checkedValues =
            ValueLowering.declarations model ct
            |> ValueLowering.check initialCatalog ct

        let records = ValueLowering.recordDeclarations initialCatalog model ct checkedValues
        let catalog, _ = TypeEmission.prepareWith naming records model ct
        let pkg, modules = moduleList model
        validateCollisions ct catalog pkg modules
