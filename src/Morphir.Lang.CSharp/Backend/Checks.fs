// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Backend

open System.Collections.Generic
open System.Threading
open Morphir.Lang.CSharp.Emit
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

    /// <summary>The kind of a name in a C# namespace or class.</summary>
    type private MemberKind =
        | Namespace
        | Class
        | Type
        | Method

    /// <summary>A name in a C# namespace or class, with the declaration of the model that owns it.</summary>
    type private Member = {
        Identifier: string
        Kind: MemberKind
        Arity: int
        Owner: string
        Location: string
    }

    /// <summary>
    /// Whether two names cannot stand in one C# namespace or class. A method has no other name next to it (CS0102). Two
    /// types, classes or namespaces with the same name collide when they have the same number of type parameters (CS0101,
    /// CS0102): C# allows the generic record <c>Rules&lt;TA&gt;</c> next to the static class <c>Rules</c>.
    /// </summary>
    let private collide (first: Member) (second: Member) =
        first.Identifier = second.Identifier
        && (first.Kind = Method
            || second.Kind = Method
            || first.Arity = second.Arity)

    /// <summary>
    /// The check that each C# namespace and class of the module layout has each name only once, and that no static class
    /// has the name of the class that holds it (CS0542). The names are the module classes and namespaces, the types, the
    /// cases of the closed form and the methods of the values. A diagnostic names both owners.
    /// </summary>
    let validateLayout (ct: CancellationToken) (naming: TypeEmission.Naming) (catalog: Map<_, TypeEmission.Declaration>) pkg modules =
        let layouts =
            ModuleLayouts.compute naming pkg (modules |> List.map (fun (path, _, names) -> path, not (List.isEmpty names)))

        let describe path = TypeEmission.describeModulePath ct path
        let moduleLocation path = "$modules/" + describe path
        let containers = Dictionary<string list, ResizeArray<Member>>(HashIdentity.Structural)

        let add (container: string list) (item: Member) =
            ct.ThrowIfCancellationRequested()

            let items =
                match containers.TryGetValue container with
                | true, items -> items
                | _ ->
                    let items = ResizeArray<Member>()
                    containers.Add(container, items)
                    items

            // A namespace, or the class of a container path, is one name however many modules are below it.
            let same (other: Member) =
                other.Kind = item.Kind
                && other.Identifier = item.Identifier
                && (item.Kind = Namespace || item.Kind = Class)

            if not (items.Exists(fun other -> same other)) then
                match items |> Seq.tryFind (collide item) with
                | Some other ->
                    Admission.fail
                        "name-collision"
                        item.Location
                        (item.Identifier + " is the C# name of " + other.Owner + " and of " + item.Owner + ".")
                | None -> items.Add item

        let ownerOfClass (full: string list) =
            layouts
            |> Map.tryPick (fun path layout -> if layout.Kind = StaticClass && layout.Container = full then Some path else None)

        // The namespaces and static classes of the modules, from the outside in.
        for layout in layouts |> Map.toList |> List.map snd |> List.sortBy (fun layout -> List.length layout.Container, layout.Container) do
            let namespaceLength = List.length layout.Namespace

            for length in 1 .. List.length layout.Container do
                let full = List.take length layout.Container
                let container = List.take (length - 1) full
                let identifier = List.last full

                if length <= namespaceLength then
                    add container { Identifier = identifier; Kind = Namespace; Arity = 0; Owner = "the namespace " + String.concat "." full; Location = moduleLocation layout.Path }
                else
                    let owner, location =
                        match ownerOfClass full with
                        | Some path -> "the class of the module " + describe path, moduleLocation path
                        | None -> "the class " + String.concat "." full, moduleLocation layout.Path

                    if length - 1 > namespaceLength && identifier = List.last container then
                        Admission.fail
                            "name-collision"
                            location
                            (identifier + " is the C# name of " + owner + " and of the class " + String.concat "." container + " that holds it.")

                    add container { Identifier = identifier; Kind = Class; Arity = 0; Owner = owner; Location = location }

        // The types of each module: records, closed records with their cases, and unions.
        for declaration in catalog |> Map.toList |> List.map snd do
            match Map.tryFind declaration.Name.ModulePath layouts with
            | None -> ()
            | Some layout ->
                let typeName = TypeEmission.title declaration.Name.LocalName
                let path = describe declaration.Name.ModulePath

                let item identifier arity owner =
                    { Identifier = identifier; Kind = Type; Arity = arity; Owner = owner; Location = declaration.Location }

                match declaration.Definition with
                | TypeEmission.Alias(TypeEmission.Fields _ as body) ->
                    let owner, _ = TypeEmission.recordTarget ct catalog declaration.Location body

                    if owner.Name = declaration.Name then
                        let arity = TypeEmission.recordParameters ct catalog declaration body |> List.length
                        add layout.Container (item (ModuleLayouts.identifier layouts declaration.Name) arity ("the type " + typeName + " of the module " + path))
                | TypeEmission.Cases(_, cases) ->
                    let arity = List.length declaration.Parameters
                    add layout.Container (item (ModuleLayouts.identifier layouts declaration.Name) arity ("the type " + typeName + " of the module " + path))

                    if not declaration.Wrapper then
                        for caseName, _ in cases do
                            let identifier = ModuleLayouts.identifier layouts { declaration.Name with LocalName = caseName }
                            add layout.Container (item identifier arity ("the case " + TypeEmission.title caseName + " of the type " + typeName))
                | TypeEmission.Alias _ -> ()

        // The methods of the values of each module.
        for path, _, names in modules do
            let layout = Map.find path layouts

            for name in names do
                let fq = FQName.fqName pkg path name

                add
                    layout.Container
                    {
                        Identifier = ModuleLayouts.identifier layouts fq
                        Kind = Method
                        Arity = 0
                        Owner = "the value " + TypeEmission.title name + " of the module " + describe path
                        Location = moduleLocation path + "/values/" + TypeEmission.title name
                    }

    /// <summary>
    /// Runs every check of the backend on a model: the type catalog, the lowering and type check of the values, and the
    /// collision checks. A refusal is raised as <c>Refusal</c>, as the backend of today raises it.
    /// </summary>
    let run (naming: TypeEmission.Naming) (model: VersionedModel) (ct: CancellationToken) : unit =
        let initialCatalog, _ = TypeEmission.prepareUnionAware naming [] model ct

        let checkedValues =
            ValueLowering.declarations model ct
            |> ValueLowering.check initialCatalog ct

        let records = ValueLowering.recordDeclarations initialCatalog model ct checkedValues
        let catalog, _ = TypeEmission.prepareUnionAware naming records model ct
        let pkg, modules = moduleList model
        validateCollisions ct catalog pkg modules
        validateLayout ct naming catalog pkg modules
