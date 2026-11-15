// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.UpgradeTests

open System
open System.IO
open System.Text.Json
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.MiddleEnd
open Morphir.MiddleEnd.Tests.ModelSupport

let private fixtureText (name: string) =
    let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "MorphirIR", name))
    File.ReadAllText root

let private classicModel (name: string) : VersionedModel =
    use document = JsonDocument.Parse(fixtureText name)

    match Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement with
    | Ok classic ->
        {
            SourceVersion = SourceFormatVersion.Create(string classic.Version)
            Distribution = DistributionModel.Classic classic.Distribution
        }
    | Error error -> failtest $"The fixture {name} is not a classic model: {error.Message}"

let private currentModel (name: string) : Distribution =
    use document = JsonDocument.Parse(fixtureText name)

    match Morphir.Json.V4.V4Json.readDistribution document.RootElement with
    | Ok decoded -> decoded.Distribution
    | Error error -> failtest $"The fixture {name} is not a current model: {error}"

let private current = Versioning.Classic 4

let private inputOf (model: VersionedModel) : UpgradeInput =
    { Model = model; Artifact = artifact; Snapshot = snapshot }

let private outcome (result: Checked<UpgradeOutcome>) =
    match Checked.tryValue result with
    | Some outcome -> outcome
    | None ->
        let messages = Checked.diagnostics result |> List.map (fun d -> d.Message) |> String.concat "; "
        failtest $"Expected an upgrade, got: {messages}"

let private v4Of (model: VersionedModel) =
    match model.Distribution with
    | DistributionModel.V4 distribution -> distribution
    | DistributionModel.Classic _ -> failtest "Expected a current model"

let private perFixture =
    [ for classicName, currentName in [ "daily-rate-v3.json", "daily-rate-v4.json"; "constant-folding-v3.json", "constant-folding-v4.json" ] ->
          test $"{classicName} upgrades to the model that {currentName} holds" {
              let upgraded = outcome (UpgradeChain.upgrade [ ClassicToCurrent.step ] current (inputOf (classicModel classicName)))
              Expect.equal (v4Of upgraded.Model) (currentModel currentName) "the current model"
              Expect.equal (Versioning.version upgraded.Model.SourceVersion.Version) "4" "the source version of the output"
          } ]

let private fixedTests =
        [ test "a model that is already current needs no step" {
              let model: VersionedModel =
                  { SourceVersion = SourceFormatVersion.Create "4"; Distribution = DistributionModel.V4(currentModel "daily-rate-v4.json") }

              let upgraded = outcome (UpgradeChain.upgrade [] current (inputOf model))
              Expect.equal upgraded.Model model "unchanged"
              Expect.isEmpty upgraded.Applications "no record"
          }

          test "the upgrade is deterministic" {
              let input = inputOf (classicModel "daily-rate-v3.json")
              let first = outcome (UpgradeChain.upgrade [ ClassicToCurrent.step ] current input)
              let second = outcome (UpgradeChain.upgrade [ ClassicToCurrent.step ] current input)
              Expect.equal first.Model second.Model "same model"
              Expect.equal first.Snapshot second.Snapshot "same snapshot"
              Expect.equal first.Applications second.Applications "same list"
          }

          test "the digest of the output is the digest of its V4 bytes" {
              let upgraded = outcome (UpgradeChain.upgrade [ ClassicToCurrent.step ] current (inputOf (classicModel "daily-rate-v3.json")))

              match Snapshot.ofDistribution (v4Of upgraded.Model) with
              | Ok digest -> Expect.equal upgraded.Snapshot digest "digest"
              | Error message -> failtest message
          }

          test "a classic model with no step that reads its version has no path" {
              let model = classicModel "daily-rate-v3.json"

              let old: VersionedModel =
                  { model with SourceVersion = SourceFormatVersion.Create "0" }

              let result = UpgradeChain.upgrade [ ClassicToCurrent.step ] current (inputOf old)
              Expect.isNone (Checked.tryValue result) "no model"

              Expect.equal
                  (Checked.diagnostics result |> List.map (fun d -> Diagnostic.code d |> Option.map (fun c -> c.Value)))
                  [ Some "ir.upgrade_no_path" ]
                  "no path"
          }

          test "a node that the current IR cannot hold is an error that names it" {
              let finite = classicModel "daily-rate-v3.json"

              // A float literal that is not finite has no form in the current IR.
              let poisoned =
                  match finite.Distribution with
                  | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(package, deps, definition)) ->
                      let poisonValue (access: Morphir.IR.Classic.AccessControlled.AccessControlled<Morphir.IR.Classic.Documented.Documented<Morphir.IR.Classic.ValueDefinition<JsonElement, JsonElement>>>) =
                          let inner =
                              match access.Value with
                              | Morphir.IR.Classic.Documented.WithDocumentation(_, v)
                              | Morphir.IR.Classic.Documented.WithoutDocumentation v -> v

                          let body =
                              Morphir.IR.Classic.Value.Literal(Unchecked.defaultof<JsonElement>, Morphir.IR.Classic.FloatLiteral nan)

                          let poisoned = Morphir.IR.Classic.Documented.WithoutDocumentation { inner with Body = body }
                          { access with Value = poisoned }

                      let poisonModule (moduleDefinition: Morphir.IR.Classic.AccessControlled.AccessControlled<Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement>>) =
                          let values = moduleDefinition.Value.Values |> Map.map (fun _ access -> poisonValue access)
                          let inner = { moduleDefinition.Value with Values = values }
                          { moduleDefinition with Value = inner }

                      let modules = definition.Modules |> Map.map (fun _ m -> poisonModule m)
                      let rewritten = Morphir.IR.Classic.Distribution.Library(package, deps, { definition with Modules = modules })
                      { finite with Distribution = DistributionModel.Classic rewritten }
                  | other -> failtest $"Expected a classic library, got {other}"

              let result = UpgradeChain.upgrade [ ClassicToCurrent.step ] current (inputOf poisoned)
              Expect.isNone (Checked.tryValue result) "no model"
              let diagnostic = Checked.diagnostics result |> List.head
              Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "ir.upgrade_unrepresentable") "code"
              Expect.stringContains diagnostic.Message "float literal" "names the literal"
          } ]

[<Tests>]
let classicToCurrent = testList "IR upgrade: classic to current" (perFixture @ fixedTests)

// ----- the application list, with steps that move a node -----

let private pos steps = NodeRoot.Value(NodeOwner.OwnPackage, pathOf "domain", name "evaluate"), steps

let private addressAt (revision: ArtifactRevision) (format: FormatTriplet) (steps: NodeStep list) =
    NodeAddress.unchecked artifact format (fst (pos steps)) steps revision

let private digestOf (character: char) =
    match Sha256Digest.Parse("sha256:" + String(character, 64)) with
    | Ok digest -> digest
    | Error message -> failwith message

let private v4Model (version: string) : VersionedModel =
    {
        SourceVersion = SourceFormatVersion.Create version
        Distribution = DistributionModel.V4(library [ "domain", moduleOf [] [] ])
    }

/// A step that reads one version, writes another, and reports that it moved the node `from` to the node `to`.
let private movingStep (id: string) (reads: Versioning.FormatVersion) (writes: Versioning.FormatVersion) (output: char) (moves: (NodeStep list * NodeStep list) list) =
    { new IIrUpgrade with
        member _.Id = RuleId.Create id
        member _.From = reads
        member _.To = writes
        member _.Accepts(version) = FormatVersion.sameVersion version reads

        member _.Upgrade(input) =
            let before = ArtifactRevision.Pinned input.Snapshot
            let after = ArtifactRevision.Pinned(digestOf output)
            let readFormat = FormatTriplet.ofVersion reads
            let writeFormat = FormatTriplet.ofVersion writes

            let applications =
                [ for source, target in moves ->
                      {
                          Rule = RuleId.Create id
                          Before = addressAt before readFormat source
                          After = addressAt after writeFormat target
                          Origin = None
                      } ]

            Checked.pass
                {
                    Model = v4Model (Versioning.version writes)
                    Snapshot = digestOf output
                    Applications = applications
                } }

[<Tests>]
let applications =
    let case index = NodeStep.PatternMatchCaseBody index
    let finalVersion = Versioning.SemVer { Major = 4; Minor = 1; Patch = 0; PreRelease = None; BuildMetadata = None }
    let middleVersion = Versioning.SemVer { Major = 4; Minor = 0; Patch = 0; PreRelease = None; BuildMetadata = None }

    testList
        "IR upgrade: the application list"
        [ test "a step that removes an unreachable case records each case that moved, pinned to its input" {
              let step = movingStep "https://example.org/steps/remove-case" (Versioning.Classic 3) middleVersion 'b' [ [ case 1 ], [ case 0 ]; [ case 2 ], [ case 1 ] ]
              let upgraded = outcome (UpgradeChain.upgrade [ step ] middleVersion (inputOf (v4Model "3")))
              Expect.equal upgraded.Applications.Length 2 "two records"

              let first = upgraded.Applications[0]
              Expect.equal (string first.Rule) "https://example.org/steps/remove-case" "the rule is the step"
              Expect.equal first.Before.Steps [ case 1 ] "the case that moved"
              Expect.equal first.After.Steps [ case 0 ] "its new place"
              Expect.equal first.Before.Revision (ArtifactRevision.Pinned snapshot) "pinned to the input"
              Expect.equal first.Before.Format { Major = 3; Minor = 0; Patch = 0 } "the format of the input"
              Expect.equal first.After.Revision (ArtifactRevision.Pinned(digestOf 'b')) "pinned to the output"
              Expect.equal first.After.Format { Major = 4; Minor = 0; Patch = 0 } "the format of the output"
          }

          test "a node that a later step replaces keeps its first origin, and one record names it" {
              let first = movingStep "https://example.org/steps/first" (Versioning.Classic 3) middleVersion 'b' [ [ case 1 ], [ case 0 ] ]
              let second = movingStep "https://example.org/steps/second" middleVersion finalVersion 'c' [ [ case 0 ], [ case 0 ] ]
              let upgraded = outcome (UpgradeChain.upgrade [ first; second ] finalVersion (inputOf (v4Model "3")))

              Expect.equal upgraded.Applications.Length 1 "one record for the node"
              let record = upgraded.Applications[0]
              Expect.equal (string record.Rule) "https://example.org/steps/second" "the last replacement"
              Expect.equal record.Before.Steps [ case 1 ] "the place in the input of the chain"
              Expect.equal record.Before.Revision (ArtifactRevision.Pinned snapshot) "pinned to the input of the chain"
              Expect.equal record.Before.Format { Major = 3; Minor = 0; Patch = 0 } "the format of the input"
              Expect.equal record.After.Steps [ case 0 ] "the place in the output"
              Expect.equal record.After.Revision (ArtifactRevision.Pinned(digestOf 'c')) "pinned to the output of the last step"
              Expect.equal upgraded.Snapshot (digestOf 'c') "the snapshot of the output"
          }

          test "a record of an earlier step for another node stays" {
              let first = movingStep "https://example.org/steps/first" (Versioning.Classic 3) middleVersion 'b' [ [ case 1 ], [ case 0 ]; [ NodeStep.Body ], [ NodeStep.LambdaBody ] ]
              let second = movingStep "https://example.org/steps/second" middleVersion finalVersion 'c' [ [ case 0 ], [ case 0 ] ]
              let upgraded = outcome (UpgradeChain.upgrade [ first; second ] finalVersion (inputOf (v4Model "3")))

              Expect.equal upgraded.Applications.Length 2 "two records"
              let rules = upgraded.Applications |> List.map (fun r -> string r.Rule) |> List.sort
              Expect.equal rules [ "https://example.org/steps/first"; "https://example.org/steps/second" ] "both steps"
          }

          test "a chain with no step for a version stops with no path" {
              let step = movingStep "https://example.org/steps/only" middleVersion finalVersion 'c' []
              let result = UpgradeChain.upgrade [ step ] finalVersion (inputOf (v4Model "3"))
              Expect.isNone (Checked.tryValue result) "no model"
          }

          test "a rule id is an absolute IRI" {
              Expect.isOk (RuleId.Parse "https://morphir.finos.org/rules/ir/identity-lambda") "http IRI"
              Expect.isOk (RuleId.Parse "urn:morphir:rule:1") "urn"
              Expect.isError (RuleId.Parse "identity-lambda") "no scheme"
              Expect.isError (RuleId.Parse "") "empty"
              Expect.isError (RuleId.Parse "1http://x") "scheme starts with a letter"
          } ]
