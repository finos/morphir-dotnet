---
title: "Middle end: passes, rewrite rules and node addresses"
description: "The middle end runs on Morphir IR and gives Morphir IR back. It runs passes before and after the backend stages, rewrites nodes with rules, names nodes with semantic node addresses and brings older models to the current IR."
weight: 40
---

# Middle end: passes, rewrite rules and node addresses

The middle end is the part of the pipeline that reads Morphir IR and writes Morphir IR: validation, transformation and rewriting.
It sits between a frontend and a backend. This page describes the passes, the rewrite rules and engine, the application
list, the semantic node address and the IR upgrade.

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

`morphir generate` upgrades a classic model when the selected backend reads the current IR only, such as the C#
backend. A backend that reads
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

## Passes

A pass is a registered extension of the type `transform`. Its claim names one stage:

| Stage | When it runs | Command |
| --- | --- | --- |
| `after-frontend` | Right after a frontend, on the model that it wrote | `morphir compile` (between the stages `frontend` and `write`) |
| `before-backend`, with a target id | Right before the backend of that target, on the model that the backend reads | `morphir generate`, and the generation of `morphir compile --target` |

A command runs the passes of one stage in the order of their registration. A pass of another target does not run. The
stage `transform` of `morphir compile` starts only when at least one pass has the stage after a frontend.

A pass reads and writes the current IR, one module at a time. It does not write a file, start a process or read the clock.
It returns the changed module and the list of its applications, or an error diagnostic. An error diagnostic stops the
command before the next stage: nothing is published, and the report has the diagnostic.

When the model is in an older family and no upgrade applies, the passes do not run. The result has one `info` diagnostic
with the code `transform.skipped`, which names the family and the format version. `morphir generate` upgrades a classic
model first when the backend reads the current IR only, so the passes before that backend run on the upgraded model.

A backend that is an extension reads the bytes of the model. The built-in passes do not run for it.

`morphir extension list` shows each built-in pass as an entry of the type `transform`, with its stage and, for a stage before
a backend, its target:

```text
morphir-dotnet.csharp.admission  C# admission checks  0.4.0  transform (before-backend csharp)  built-in
```

The stage is a morphir-dotnet report member. MEP 0.2.0-draft.1 has no transform capability, so the stage is never written
into a MEP message.

### The passes for C#

The first pass for the target `csharp` is `morphir-dotnet.csharp.admission`. It runs the checks that the C# backend ran
before: the type catalog, the lowering of the values with its type check, and the name collisions. The diagnostics have the
code, the message and the location of the backend, for example `name-collision` and `unbound-variable`. The checks need the
whole package, so the pass runs them once, in the unit of the first module, and reads the other modules through the package
view. It changes no module. It uses the default naming; the namespace option stays with the backend.

## Rewrite rules

A rewrite rule is compiled code with a catalog entry. It is not a script. A rule has:

- an id, which is an absolute IRI;
- a title;
- an example: a value before and the value after, which the test suite runs;
- the IR features that it removes and adds, for example `value.lambda`;
- a function from the node and a context to a replacement node, or to none when the rule does not match the node.

A rule without an example does not ship. The catalog check fails and names the rule. The check also fails when two rules have
one id, when the engine does not change the example before to the example after, and when the example does not show the
features that the rule says it removes and adds. The codes are `rewrite.rule_without_example`, `rewrite.duplicate_rule`,
`rewrite.example_mismatch` and `rewrite.feature_claim_mismatch`.

The engine applies a rule set with one of three strategies:

- **Bottom-up**: once, children before their parent.
- **Top-down**: once, a parent before its children.
- **Fixed point**: bottom-up rounds until a round changes nothing, with a limit on the number of changing rounds. A rule set that
  does not settle within the limit gives the error `rewrite.not_settled`, which names the rules, and no model.

At a node, the first rule that matches replaces the node. The replacement takes the attributes of the node that it replaces, so
a later diagnostic points to the source of the user.

This is the rule "an identity lambda becomes its argument", with the pass that runs it. The tests of the repository run this
text: the catalog check, and the pass on a model whose value `net` has the body `Apply (Lambda x x) price`.

```fsharp
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd

let private empty = ValueAttributes.empty
let private x = Name.fromString "x"
let private price = Value.Variable(empty, Name.fromString "price")

// The function x -> x.
let private identity =
    Value.Lambda(empty, AsPattern(empty, WildcardPattern empty, x), Value.Variable(empty, x))

/// The rule "an identity lambda becomes its argument".
let identityLambda: RewriteRule =
    { Id = RuleId.Create "https://morphir.finos.org/rules/identity-lambda"
      Title = "An identity lambda becomes its argument"
      // The example: the value before and the value after.
      Example = Some { Before = Value.Apply(empty, identity, price); After = price }
      Removes = Set.ofList [ IrFeature.Value ValueKind.Lambda; IrFeature.Value ValueKind.Apply ]
      Adds = Set.empty
      Apply =
        fun _ value ->
            match value with
            | Value.Apply(_, Value.Lambda(_, AsPattern(_, WildcardPattern _, bound), Value.Variable(_, used)), argument) when
                bound = used
                ->
                Some argument
            | _ -> None }

/// The pass that runs the rule on each module, right after a frontend.
let pass: IModelTransform =
    RewritePass.create
        "acme.identity-lambda"
        "Identity lambda"
        "Replaces an identity lambda that is applied to an argument by the argument."
        PassStage.AfterFrontend
        Strategy.BottomUp
        [ identityLambda ]
```

After the pass, the body of `net` is `price`, and the application list has one record.

### Reading other modules

A rule reads a module other than the one that it processes only through the package view. The view shows the package as it
was before the pass, so the result does not depend on the order of the modules. The view records each read of another module,
and the report lists the reads under `lookups`. The list of module paths is not a read.

### The feature order check

A backend declares the IR features that it cannot take. After the passes of the stage before it, the host checks the model:
when a feature remains, the command fails before the backend with the error `transform.feature_remains`, which names the
feature, the backend and the first module that has it. The check reads the model, not the claims of the passes, so a pass
that does not remove what it says is found.

## The application list

An upgrade step or a rewrite rule gives one record for each node that it moves or replaces: the rule or the step,
the address in the input model of the pipeline (pinned to the digest of the input), the address in the output model (pinned
to the digest of the output), and the source location of the node when it has one. A node that no
record names keeps its path, and only the format of its address changes. A sidecar key that points at a node of the old
model finds the node in the new model through this list.

When a chain of steps runs, each record names the input of the whole chain, and a node that a later step replaces keeps
its first origin.

A pass that replaces a replacement keeps the first origin: the new record names the node of the input model, and the record of
the earlier pass is dropped. A record below a node that a later pass replaces is dropped too, because its node is gone.

With `--json`, `morphir generate` and `morphir compile` report the passes that ran. The member `passes` is written only when a
pass ran:

```json
"passes": {
  "ran": ["acme.identity-lambda"],
  "applications": [
    {
      "rule": "https://morphir.finos.org/rules/identity-lambda",
      "before": "morphir://ir/workspace/input?format=4.0.0&rev=sha256:<input digest>#/module/orders%2Fpricing/value/net/body",
      "after": "morphir://ir/workspace/input?format=4.0.0&rev=sha256:<output digest>#/module/orders%2Fpricing/value/net/body",
      "origin": { "startLine": 12, "startColumn": 5, "endLine": 12, "endColumn": 23 }
    }
  ],
  "lookups": [
    { "pass": "acme.inline-rate", "from": "orders/pricing", "module": "orders/tax", "value": "rate" }
  ]
}
```
