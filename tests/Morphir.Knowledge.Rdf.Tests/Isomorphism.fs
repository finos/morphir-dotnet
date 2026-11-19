// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Dataset isomorphism for tests: equal up to a bijection of blank nodes.
module Morphir.Knowledge.Rdf.Tests.Isomorphism

open System.Collections.Generic
open Morphir.Knowledge.Rdf

let private blankNodes (dataset: RdfDataset) =
    [ for quad in dataset.Quads do
        match quad.Subject with
        | RdfSubject.BlankNode node -> yield node
        | _ -> ()
        match quad.Object with
        | RdfObject.BlankNode node -> yield node
        | _ -> ()
        match quad.Graph with
        | GraphLocation.NamedGraph(GraphName.BlankNodeName node) -> yield node
        | _ -> () ]
    |> List.distinct

let private rename (map: IReadOnlyDictionary<BlankNodeId, BlankNodeId>) (quad: Quad) =
    let node value = map[value]
    { Subject =
        match quad.Subject with
        | RdfSubject.BlankNode value -> RdfSubject.BlankNode(node value)
        | other -> other
      Predicate = quad.Predicate
      Object =
        match quad.Object with
        | RdfObject.BlankNode value -> RdfObject.BlankNode(node value)
        | other -> other
      Graph =
        match quad.Graph with
        | GraphLocation.NamedGraph(GraphName.BlankNodeName value) ->
            GraphLocation.NamedGraph(GraphName.BlankNodeName(node value))
        | other -> other }

/// A cheap invariant of a blank node: how often it occurs in each position.
let private signature (dataset: RdfDataset) node =
    dataset.Quads
    |> Seq.sumBy (fun quad ->
        (if quad.Subject = RdfSubject.BlankNode node then 1 else 0)
        + (if quad.Object = RdfObject.BlankNode node then 100 else 0)
        + (if quad.Graph = GraphLocation.NamedGraph(GraphName.BlankNodeName node) then 10000 else 0))

/// Whether two datasets are equal up to renaming blank nodes, by backtracking.
/// Intended for the small graphs of unit tests.
let isomorphic (left: RdfDataset) (right: RdfDataset) =
    let leftNodes = blankNodes left
    let rightNodes = blankNodes right
    let target = set right.Quads

    if left.Quads.Length <> right.Quads.Length || leftNodes.Length <> rightNodes.Length then
        false
    else
        let map = Dictionary<BlankNodeId, BlankNodeId>()
        let used = HashSet<BlankNodeId>()

        let rec search remaining =
            match remaining with
            | [] -> left.Quads |> Seq.forall (fun quad -> target.Contains(rename map quad))
            | node :: rest ->
                rightNodes
                |> List.exists (fun candidate ->
                    if used.Contains candidate || signature left node <> signature right candidate then
                        false
                    else
                        map[node] <- candidate
                        used.Add candidate |> ignore
                        let found = search rest
                        map.Remove node |> ignore
                        used.Remove candidate |> ignore
                        found)

        search leftNodes
