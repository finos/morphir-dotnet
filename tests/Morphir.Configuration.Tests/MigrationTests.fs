module Morphir.Configuration.Tests.MigrationTests

open Expecto
open Morphir.Configuration
open Morphir.Configuration.Tests.ResolutionTests

let private classicRaw =
    obj [
        "name", text "Orders"
        "sourceDirectory", text "elm-src"
        "exposedModules", ConfigValue.Array [ text "Orders" ]
    ]

let private manifestAt path role raw =
    let identity = source path role DocumentFormat.Json
    { Source = identity
      Required = true
      Content = SourceContent.Available(Normalization.normalize identity raw Map.empty) }

let private manifest raw = manifestAt "/repo/morphir.json" SourceRole.RootPrimary raw
let private target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml
let private plan raw existing = ProjectMigration.plan (manifest raw) target existing
let private empty = DocumentDefaults.emptyValue

[<Tests>]
let mappingTests =
    testList "Project migration mapping" [
        test "maps classic fields and materializes only classic defaults" {
            let result = plan classicRaw empty
            Expect.isFalse result.Blocked "complete source is migratable"
            for path, value in [
                ["project"; "name"], text "Orders"
                ["project"; "source_directory"], text "elm-src"
                ["project"; "exposed_modules"], ConfigValue.Array [text "Orders"]
                ["frontend"; "language"], text "elm"
                ["pipeline"; "backend"], text "checkpoint"
                ["pipeline"; "checkpoint_format"], text "morphir-json"
            ] do
                Expect.equal (ConfigTree.tryFind path result.Candidate) (Some value) "mapped value"
            Expect.isNone (ConfigTree.tryFind ["ir"] result.Candidate) "do not pin unrelated IR defaults"
            Expect.equal result.Edits.Length 6 "only required settings"
            Expect.equal (result.Ledger |> List.filter (fun field -> not field.RawPath.IsEmpty) |> List.length) 3 "raw settings covered"
        }
        test "materializes missing name and source directory from classic recognition" {
            let result = plan (obj ["exposedModules", ConfigValue.Array []]) empty
            Expect.isFalse result.Blocked "recognized minimal classic manifest"
            Expect.equal (ConfigTree.tryFind ["project"; "name"] result.Candidate) (Some(text "user")) "classic fallback"
            Expect.equal (ConfigTree.tryFind ["project"; "source_directory"] result.Candidate) (Some(text "src")) "classic source fallback"
        }
        test "supports canonical extras and explicit frontend and pipeline choices" {
            let raw = classicRaw |> ConfigTree.set ["cache"; "enabled"] (ConfigValue.Boolean false)
                                 |> ConfigTree.set ["frontend"; "language"] (text "custom")
                                 |> ConfigTree.set ["pipeline"; "backend"] (text "custom")
                                 |> ConfigTree.set ["ir"; "format_version"] (ConfigValue.Integer 3L)
                                 |> ConfigTree.set ["custom"; "literal.key"] (ConfigValue.Array [text "a"])
            let result = plan raw empty
            Expect.isFalse result.Blocked "supported extra values"
            Expect.equal (ConfigTree.tryFind ["cache"; "enabled"] result.Candidate) (Some(ConfigValue.Boolean false)) "typed extra"
            Expect.equal (ConfigTree.tryFind ["frontend"; "language"] result.Candidate) (Some(text "custom")) "explicit frontend"
            Expect.equal (ConfigTree.tryFind ["pipeline"; "backend"] result.Candidate) (Some(text "custom")) "explicit backend"
            Expect.equal (ConfigTree.tryFind ["ir"; "format_version"] result.Candidate) (Some(ConfigValue.Integer 3L)) "authored IR setting retained"
            Expect.isTrue (result.Ledger |> List.exists (fun field -> field.RawPath = ["custom"; "literal.key"] && field.Disposition = "retained")) "raw nested value accounted"
        }
        test "canonical dependency maps retain their supported values" {
            let dependencies = obj ["sdk", obj ["path", text "../sdk"]; "remote", text "1.0"]
            let result = plan (classicRaw |> ConfigTree.set ["dependencies"] dependencies) empty
            Expect.isFalse result.Blocked "canonical dependency shape supported"
            Expect.equal (ConfigTree.tryFind ["dependencies"] result.Candidate) (Some dependencies) "dependencies retained"
        }
        test "member checkpoint defaults retain base workspace choices" {
            let classicMember = manifestAt "/repo/orders/morphir.json" SourceRole.MemberPrimary classicRaw
            let memberTarget = source "/repo/orders/morphir.toml" SourceRole.MemberPrimary DocumentFormat.Toml
            let workspace = supplied (source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml)
                                    (obj ["pipeline", obj ["backend", text "workspace-custom"]])
            let input = { DocumentDefaults.resolutionInput with Sources = [workspace; classicMember]; Scope = ProjectScope.Member }
            let result = ProjectMigration.planWithContext classicMember memberTarget empty input
            Expect.isFalse result.Blocked "workspace base context is representable"
            Expect.equal (ConfigTree.tryFind ["pipeline"; "backend"] result.Candidate) (Some(text "workspace-custom")) "retain inherited behavior instead of promoting lower legacy default"
        }
        test "ignored raw nonempty dependency and decorations shapes block" {
            for key, value in [
                "dependencies", ConfigValue.Array [text "legacy.json"]
                "localDependencies", obj ["sdk", text "../sdk"]
                "decorations", obj ["note", text "annotation"]
            ] do
                let result = plan (classicRaw |> ConfigTree.set [key] value) empty
                Expect.isTrue result.Blocked "loss is blocking"
                Expect.isTrue (result.Ledger |> List.exists (fun field -> field.RawPath = [key] && field.Disposition = "blocking")) "raw ignored value retained in ledger"
        }
        test "empty ignored legacy values retain equivalent absence" {
            for key, value in ["localDependencies", ConfigValue.Array []; "decorations", obj []; "dependencies", ConfigValue.Array []] do
                let result = plan (classicRaw |> ConfigTree.set [key] value) empty
                Expect.isFalse result.Blocked "empty legacy setting has no effective value"
                Expect.isTrue (result.Ledger |> List.exists (fun field -> field.RawPath = [key] && field.Disposition = "retained")) "empty legacy field accounted"
        }
        test "same-file canonical conflict blocks even equal values" {
            let raw = classicRaw |> ConfigTree.set ["project"; "name"] (text "Orders")
            let result = plan raw empty
            Expect.isTrue result.Blocked "normalization conflict survives planning"
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.legacy_conflict")) "typed conflict"
        }
        test "existing equal target values remain untouched and missing values are separate edits" {
            let existing = obj ["project", obj ["name", text "Orders"]; "unrelated", text "keep"]
            let result = plan classicRaw existing
            Expect.isFalse result.Blocked "nonconflicting merge"
            Expect.equal result.Edits.Length 5 "equal field needs no edit"
            Expect.equal (ConfigTree.tryFind ["unrelated"] result.Candidate) (Some(text "keep")) "unrelated target retained"
            let repeat = plan classicRaw result.Candidate
            Expect.isFalse repeat.Blocked "repeat applicable"
            Expect.isEmpty repeat.Edits "repeat is no-op"
        }
        test "existing different target values block rather than overwrite" {
            let existing = obj ["project", obj ["source_directory", text "src"]]
            let result = plan classicRaw existing
            Expect.isTrue result.Blocked "conflicting source directory"
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "project.migrate_conflict" && diagnostic.KeyPath = ["project"; "source_directory"])) "conflict names canonical field"
            Expect.equal (ConfigTree.tryFind ["project"; "source_directory"] result.Candidate) (Some(text "src")) "target never overwritten"
        }
        test "target incompatible ancestor shape blocks" {
            let result = plan classicRaw (obj ["project", text "do not replace"])
            Expect.isTrue result.Blocked "scalar cannot be traversed"
        }
        test "null and out-of-range integers block including nested ignored values" {
            for value in [ConfigValue.Null; ConfigValue.BigInteger(System.Numerics.BigInteger.Pow(2I, 80)); ConfigValue.Array [ConfigValue.Null]] do
                let result = plan (classicRaw |> ConfigTree.set ["custom"] value) empty
                Expect.isTrue result.Blocked "TOML cannot represent source losslessly"
                Expect.isTrue (result.Ledger |> List.exists (fun field -> field.RawPath = ["custom"] && field.Disposition = "blocking")) "unrepresentable field accounted"
        }
        test "invalid classic shape and nonclassic documents block" {
            for raw in [obj ["project", obj ["name", text "Modern"]]; obj ["name", text "Bad"]; classicRaw |> ConfigTree.set ["sourceDirectory"] (ConfigValue.Integer 4L)] do
                Expect.isTrue (plan raw empty).Blocked "invalid input"
        }
        test "ledger redacts nested secrets and opaque references" {
            let raw = classicRaw |> ConfigTree.set ["custom"; "api_token"] (text "sensitive")
                                 |> ConfigTree.set ["custom"; "reference"] (obj ["env", text "PRIVATE_LOCATION"])
            let result = plan raw empty
            Expect.isFalse result.Blocked "secrets remain canonical for application"
            for path in [["custom"; "api_token"]; ["custom"; "reference"]] do
                Expect.equal (result.Ledger |> List.find (fun field -> field.RawPath = path) |> fun field -> field.Value) (text "[REDACTED]") "safe preview"
            Expect.equal (ConfigTree.tryFind ["custom"; "api_token"] result.Candidate) (Some(text "sensitive")) "canonical value preserved"
        }
    ]

[<Tests>]
let verificationTests =
    testList "Project migration independent verification" [
        test "candidate must supply classic defaults after retained source is excluded" {
            let classic = manifest classicRaw
            let converted = ProjectMigration.plan classic target empty
            let input = {DocumentDefaults.resolutionInput with Sources = [classic]}
            let verification = ProjectMigration.verify classic target (converted.Candidate |> ConfigTree.remove "frontend") input "/repo"
            Expect.isFalse verification.Verified "retained JSON cannot supply forgotten frontend"
            Expect.isTrue (verification.Differences |> List.exists (fun difference -> difference.KeyPath = ["frontend"; "language"])) "independent mismatch"
        }
        test "all ambient roles and runtime maps are removed from both sides" {
            let classic = manifest classicRaw
            let converted = ProjectMigration.plan classic target empty
            let wrong = converted.Candidate |> ConfigTree.set ["project"; "source_directory"] (text "wrong")
            let overrides = obj ["project", obj ["source_directory", text "masked"]]
            for role in [SourceRole.System; SourceRole.Global; SourceRole.UserOverride; SourceRole.WorkspaceUserOverride; SourceRole.WorkspaceProfile; SourceRole.MemberProfile; SourceRole.Selected; SourceRole.Explicit; SourceRole.Environment] do
                let identity = {source ("/ambient/" + string role + ".toml") role DocumentFormat.Toml with Profile = Some "dev"}
                let input =
                    { DocumentDefaults.resolutionInput with
                        Sources = [classic; supplied identity overrides]
                        Environment = Map.ofList ["MORPHIR_PROJECT__SOURCE_DIRECTORY", "masked"]
                        Explicit = overrides
                        Profiles = {DocumentDefaults.profileOptions with Selection = ProfileSelection.Names ["dev"]} }
                let verification = ProjectMigration.verify classic target wrong input "/repo"
                Expect.isFalse verification.Verified (string role + " must not mask conversion")
                let difference = verification.Differences |> List.find (fun difference -> difference.KeyPath = ["project"; "source_directory"])
                Expect.equal difference.Before (Some(text "elm-src")) "base before value"
                Expect.equal difference.After (Some(text "wrong")) "base candidate value"
                Expect.equal difference.BeforePath (Some "/repo/elm-src") "before path meaning"
                Expect.equal difference.AfterPath (Some "/repo/wrong") "candidate path meaning"
        }
        test "old target is replaced rather than allowed to mask a wrong candidate" {
            let classic = manifest classicRaw
            let converted = ProjectMigration.plan classic target empty
            let input = {DocumentDefaults.resolutionInput with Sources = [classic; supplied target converted.Candidate]}
            let verification = ProjectMigration.verify classic target (converted.Candidate |> ConfigTree.remove "pipeline") input "/repo"
            Expect.isFalse verification.Verified "prior target cannot supply dropped pipeline settings"
            Expect.isTrue (verification.Differences |> List.exists (fun difference -> difference.KeyPath = ["pipeline"; "backend"])) "checkpoint omitted"
        }
        test "equivalent candidate reports expected provenance changes separately" {
            let classic = manifest classicRaw
            let converted = ProjectMigration.plan classic target empty
            let input = {DocumentDefaults.resolutionInput with Sources = [classic]}
            let verification = ProjectMigration.verify classic target converted.Candidate input "/repo"
            Expect.isTrue verification.Verified "independent values equal"
            Expect.isEmpty verification.Differences "no value differences"
            let origin = verification.OriginChanges |> List.find (fun origin -> origin.KeyPath = ["project"; "name"])
            Expect.equal (origin.Before |> Option.map (fun origin -> origin.Source.Path)) (Some classic.Source.Path) "classic origin"
            Expect.equal (origin.After |> Option.map (fun origin -> origin.Source.Path)) (Some target.Path) "TOML origin"
        }
        test "member equivalence retains declared workspace base and project-relative paths" {
            let classic = manifestAt "/repo/orders/morphir.json" SourceRole.MemberPrimary classicRaw
            let memberTarget = source "/repo/orders/.config/morphir/config.toml" SourceRole.MemberPrimary DocumentFormat.Toml
            let workspace = supplied (source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml)
                                    (obj ["project", obj ["name", text "Root"]
                                          "workspace", obj ["out_dir", text "base-output"; "members", ConfigValue.Array [text "orders"]]
                                          "cache", obj ["enabled", ConfigValue.Boolean false]
                                          "pipeline", obj ["backend", text "workspace-custom"]])
            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [workspace; classic]
                    Scope = ProjectScope.Member
                    OwningWorkspace = Some {Root = "/repo"; OutputDirectory = Some "/repo/base-output"} }
            let converted = ProjectMigration.planWithContext classic memberTarget empty input
            let verification = ProjectMigration.verify classic memberTarget converted.Candidate input "/repo/orders"
            Expect.isTrue verification.Verified "member is equivalent without source"
            Expect.equal (ConfigTree.tryFind ["cache"; "enabled"] verification.After) (Some(ConfigValue.Boolean false)) "workspace nonproject settings retained"
            Expect.equal (ConfigTree.tryFind ["pipeline"; "backend"] verification.After) (Some(text "workspace-custom")) "workspace pipeline choice retained"
            Expect.equal (ConfigTree.tryFind ["project"; "name"] verification.After) (Some(text "Orders")) "root identity does not replace member"
        }
        test "typed mismatches are not coerced and secret differences stay redacted" {
            let raw = classicRaw |> ConfigTree.set ["custom"; "credential"] (text "old-secret")
                                 |> ConfigTree.set ["custom"; "count"] (ConfigValue.Integer 1L)
            let classic = manifest raw
            let converted = ProjectMigration.plan classic target empty
            let wrong = converted.Candidate |> ConfigTree.set ["custom"; "credential"] (text "new-secret")
                                            |> ConfigTree.set ["custom"; "count"] (text "1")
            let verification = ProjectMigration.verify classic target wrong DocumentDefaults.resolutionInput "/repo"
            Expect.isFalse verification.Verified "typed and secret mismatch"
            Expect.equal verification.Differences.Length 2 "compare canonical values before redaction"
            let secret = verification.Differences |> List.find (fun difference -> difference.KeyPath = ["custom"; "credential"])
            Expect.equal secret.Before (Some(text "[REDACTED]")) "safe old value"
            Expect.equal secret.After (Some(text "[REDACTED]")) "safe new value"
        }
        test "candidate schema or source errors refuse equivalence" {
            let classic = manifest classicRaw
            let converted = ProjectMigration.plan classic target empty
            let wrong = converted.Candidate |> ConfigTree.set ["project"; "exposed_modules"] (text "invalid")
            let verification = ProjectMigration.verify classic target wrong DocumentDefaults.resolutionInput "/repo"
            Expect.isFalse verification.Verified "invalid candidate"
            Expect.isTrue (verification.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Severity = DiagnosticSeverity.Error)) "structured validation"
        }
        test "conflicts report both redacted values with independent origins" {
            let raw = classicRaw |> ConfigTree.set ["custom"; "password"] (text "source-secret")
            let targetRaw = empty |> ConfigTree.set ["project"; "source_directory"] (text "wrong")
                                  |> ConfigTree.set ["custom"; "password"] (text "target-secret")
            let plan = ProjectMigration.plan (manifest raw) target targetRaw
            let conflict = plan.Conflicts |> List.find (fun c -> c.KeyPath = ["project"; "source_directory"])
            Expect.equal conflict.SourceValue (text "elm-src") "mapped source value"
            Expect.equal conflict.TargetValue (text "wrong") "conflicting target value"
            let secret = plan.Conflicts |> List.find (fun c -> c.KeyPath = ["custom"; "password"])
            Expect.equal secret.SourceValue (text "[REDACTED]") "source redaction"
            Expect.equal secret.TargetValue (text "[REDACTED]") "target redaction"
        }
        test "verification retains declared base workspace output context" {
            let classic = manifest classicRaw
            let root = source "/workspace/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml
            let context = {
                DocumentDefaults.resolutionInput with
                    Sources = [ { Source = root; Required = true; Content = SourceContent.Available(Normalization.normalize root (empty |> ConfigTree.set ["workspace"; "out_dir"] (text "base")) Map.empty) } ]
                    OwningWorkspace = Some { Root = "/workspace"; OutputDirectory = Some "/ambient/personal" }
            }
            let converted = ProjectMigration.planWithContext classic target empty context
            let verified = ProjectMigration.verify classic target converted.Candidate context "/repo"
            Expect.equal verified.BaseWorkspace.Value.OutputDirectory (Some "/workspace/base") "declared base ownership"
        }
    ]
