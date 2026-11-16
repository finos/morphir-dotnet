// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.ModelSupport

open System
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd

let name (text: string) =
    match CanonicalNames.parseName text with
    | Ok name -> name
    | Error message -> failwith message

let pathOf (text: string) =
    match CanonicalNames.parsePath text with
    | Ok path -> path
    | Error message -> failwith message

let fq (text: string) =
    match CanonicalNames.parseFQName text with
    | Ok fq -> fq
    | Error message -> failwith message

let ta = TypeAttributes.empty
let va = ValueAttributes.empty

let intType = Type.Reference(ta, fq "morphir/SDK:basics#int", [])
let stringType = Type.Reference(ta, fq "morphir/SDK:string#string", [])

let ordered (entries: (Name * 'v) list) =
    match OrderedMap.ofList entries with
    | Ok map -> map
    | Error key -> failwith $"duplicate key {key}"

let orderedModules (entries: (ModulePath * 'v) list) =
    match OrderedMap.ofList entries with
    | Ok map -> map
    | Error key -> failwith $"duplicate module {key}"

let publicType definition : AccessControlled<Documented<TypeDefinition>> =
    { Access = Public; Value = { Doc = None; Value = definition } }

let publicValue definition : AccessControlled<Documented<ValueDefinition>> =
    { Access = Public; Value = { Doc = None; Value = definition } }

let expression (inputs: (string * Type) list) (output: Type) (body: Value) : ValueDefinition =
    { InputTypes = [ for n, t in inputs -> name n, t ]; Body = ExpressionBody(output, body) }

let library (modules: (string * ModuleDefinition) list) : Distribution =
    Library(
        PackageName(pathOf "acme/orders"),
        OrderedMap.empty,
        { Modules = orderedModules [ for path, m in modules -> ModulePath(pathOf path), { Access = Public; Value = m } ] }
    )

let moduleOf (types: (string * TypeDefinition) list) (values: (string * ValueDefinition) list) : ModuleDefinition =
    {
        Types = ordered [ for n, d in types -> name n, publicType d ]
        Values = ordered [ for n, d in values -> name n, publicValue d ]
        Doc = None
    }

let snapshot =
    match Sha256Digest.Parse("sha256:" + String('a', 64)) with
    | Ok digest -> digest
    | Error message -> failwith message

let artifact = ArtifactSelector.Package(PackageName(pathOf "acme/orders"))
let format = { Major = 4; Minor = 0; Patch = 0 }

/// The URIs of every node of a model, without the common prefix, in document order.
let fragments (distribution: Distribution) : string list =
    let prefix = "morphir://ir/pkg/acme/orders?format=4.0.0&rev=sha256:" + String('a', 64) + "#"

    NodeWalk.nodes artifact format snapshot distribution
    |> List.map (fun (address, _) -> (NodeAddress.toUri address).Substring prefix.Length)
