---
title: "Middle end: node addresses and IR upgrades"
description: "The middle end runs on Morphir IR and gives Morphir IR back. It names nodes with semantic node addresses and brings older models to the current IR."
weight: 40
---

# Middle end: node addresses and IR upgrades

The middle end is the part of the pipeline that reads Morphir IR and writes Morphir IR: validation, transformation and rewriting.
It sits between a frontend and a backend. This page describes what exists today: the semantic node address and the IR
upgrade. The passes and the rewrite engine follow in the same change.

The code is in the package `Morphir.MiddleEnd`. It builds for `netstandard2.0` and for the repository frameworks, so a
compiler host (an MSBuild target or a Roslyn source generator) can call it.

## The current IR

The middle end works on the current IR, which is IR version 4. No contract of the middle end names a version. A model of an
older format reaches the current IR through upgrade steps: each step reads one format and writes the next. When a new IR
appears, one step is added and "current" moves.

```fsharp
open Morphir.IR
open Morphir.MiddleEnd

// The input of an upgrade: the model, the artifact that names it, and the digest of the bytes that it came from.
let input = { Model = classicModel; Artifact = ArtifactSelector.Workspace "orders"; Snapshot = digest }

match Morphir.Compilation.Checked.tryValue (CurrentIr.upgrade input) with
| Some upgraded -> upgraded.Model // a current-IR model; upgraded.Applications lists the nodes that moved
| None -> () // the diagnostics say why: no step reads the version, or a node has no form in the current IR
```

`morphir generate` upgrades a classic model when the selected backend reads the current IR only. A backend that reads
the classic model directly, such as the F# backend, gets the model as it was loaded. The report names the model as it was
loaded.

The step from the classic IR keeps every node and every name. A classic attribute that carries data moves to the
`extensions` of the node, under the key `classic`. A value that the current IR cannot hold, such as a float literal that is
not finite, is an error that names the value.

## Node addresses

A node address names a position in the model. It does not name a file or a source position. The text form is a Morphir URI:

```text
morphir://ir/pkg/acme/orders?format=4.0.0#/module/domain/value/calculate-total/body/apply/argument
```

The address model is the draft `0.1.0-draft.1` of [`finos/morphir`](https://github.com/finos/morphir/blob/main/docs/spec/ir/node-addresses.md).
The tests read the 29 URI cases of its fixture. An address has an artifact selector (a package or a workspace alias), a
format, a root, typed steps, a revision and an optional guard.

- An address that is not pinned and has an ordered step, such as a tuple element, needs a guard. The upgrade and the walk
  pin their addresses to the digest of the bytes of a model, so they need no guard.
- A parser accepts one canonical spelling. An alias, an unknown role or a noncanonical name is `invalid_node_uri`.
- `NodeWalk.nodes` gives each node of the own package of a model its address, in document order.

## The application list

An upgrade step, and later a rewrite rule, gives one record for each node that it moves or replaces: the rule or the step,
the address in the input model (pinned to the digest of the input), and the address in the output model. A node that no
record names keeps its path, and only the format of its address changes. A sidecar key that points at a node of the old
model finds the node in the new model through this list.

When a chain of steps runs, each record names the input of the whole chain, and a node that a later step replaces keeps
its first origin.
