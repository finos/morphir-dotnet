// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open Morphir.CodeGeneration.CSharp
open Morphir.IR

/// <summary>How a module is written: at namespace level, or as a static class.</summary>
type internal ModuleKind =
    /// <summary>A module that has only types, with no module that has values above it: its types stand in the namespace.</summary>
    | NamespaceOnly
    /// <summary>A module that has values, or that is below one: a static class, nested in the class of the module above it.</summary>
    | StaticClass

/// <summary>Where the code of one module goes.</summary>
type internal ModuleLayout = {
    Path: ModulePath
    Kind: ModuleKind
    /// <summary>The segments of the full name of the container after <c>global::</c>.</summary>
    Container: string list
    /// <summary>The module whose file holds this module: itself, or the module of the outermost static class above it.</summary>
    FileRoot: ModulePath
    /// <summary>The segments of the namespace that holds the file root.</summary>
    Namespace: string list
}

/// <summary>
/// Maps modules to namespaces and static classes: a module that has only types is a namespace; a module that has values is a
/// static class in the namespace of the modules above it; a module below a static-class module is a nested static class.
/// </summary>
module internal ModuleLayouts =

    let private segments (ModulePath(Path names)) = names |> List.map TypeEmission.title

    /// <summary>The segments of the namespace of the package, or of its override.</summary>
    let packageNamespace (naming: TypeEmission.Naming) (pkg: PackageName) : string list =
        match naming.Package, naming.Namespace with
        | Some own, Some value when own = pkg -> value.Split('.') |> List.ofArray |> List.map (fun segment -> segment.TrimStart('@'))
        | _ ->
            let (PackageName(Path names)) = pkg
            names |> List.map TypeEmission.title

    /// <summary>The layout of each module, given the modules and whether each has values.</summary>
    let compute (naming: TypeEmission.Naming) (pkg: PackageName) (modules: (ModulePath * bool) list) : Map<ModulePath, ModuleLayout> =
        let root = packageNamespace naming pkg
        let withValues = modules |> List.filter snd |> List.map fst |> Set.ofList

        let prefixes (ModulePath(Path names)) =
            [ for length in 1 .. List.length names - 1 -> ModulePath(Path(List.take length names)) ]

        modules
        |> List.map (fun (path, hasValues) ->
            let (ModulePath(Path names)) = path
            let length = List.length names

            // The outermost module above this one that has values: the static class that holds this module.
            let above = prefixes path |> List.tryFind withValues.Contains

            let layout =
                match above with
                | Some outer ->
                    let (ModulePath(Path outerNames)) = outer
                    let outerContainer = root @ (segments outer |> List.take (List.length outerNames - 1)) @ [ List.last (segments outer) ]
                    let below = names |> List.skip (List.length outerNames) |> List.map TypeEmission.title

                    {
                        Path = path
                        Kind = StaticClass
                        Container = outerContainer @ below
                        FileRoot = outer
                        Namespace = root @ (segments outer |> List.take (List.length outerNames - 1))
                    }
                | None when hasValues ->
                    let parents = segments path |> List.take (length - 1)

                    {
                        Path = path
                        Kind = StaticClass
                        Container = root @ parents @ [ List.last (segments path) ]
                        FileRoot = path
                        Namespace = root @ parents
                    }
                | None ->
                    {
                        Path = path
                        Kind = NamespaceOnly
                        Container = root @ segments path
                        FileRoot = path
                        Namespace = root @ segments path
                    }

            path, layout)
        |> Map.ofList

    /// <summary>The segments of the full name of a type or a value of a module, after <c>global::</c>.</summary>
    let qualified (layouts: Map<ModulePath, ModuleLayout>) (name: FQName) : string list =
        match Map.tryFind name.ModulePath layouts with
        | Some layout -> layout.Container @ [ TypeEmission.title name.LocalName ]
        | None -> failwith "A name refers to a module that the layout does not have."
