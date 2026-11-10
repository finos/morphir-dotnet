// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System

/// The manager commands a result reports. <c>run</c> passes the selected host's output through and writes no result.
[<RequireQualifiedAccess>]
type Command =
    | Setup
    | Restore
    | Update
    | Show
    | Doctor
    | List
    | Default
    | Rollback
    | Repair
    | Remove
    | SelfUpdate
    | Env

module Command =
    let all =
        [ Command.Setup, "setup"
          Command.Restore, "restore"
          Command.Update, "update"
          Command.Show, "show"
          Command.Doctor, "doctor"
          Command.List, "list"
          Command.Default, "default"
          Command.Rollback, "rollback"
          Command.Repair, "repair"
          Command.Remove, "remove"
          Command.SelfUpdate, "self-update"
          Command.Env, "env" ]

    let name (command: Command) =
        all |> List.find (fun (candidate, _) -> candidate = command) |> snd

[<RequireQualifiedAccess>]
type Outcome =
    | Planned
    | Succeeded
    | Failed

[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning

/// The component a plan entry or a diagnostic concerns.
[<RequireQualifiedAccess>]
type Component =
    | Host of implementation: Token * deployment: Token
    | Provider of operation: Operation

/// An alternative a diagnostic offers; each member is optional.
type Alternative =
    { Implementation: Token option
      Deployment: Token option
      Platform: Platform option }

/// Where a selection came from, in precedence order.
[<RequireQualifiedAccess>]
type SelectionSource =
    | Cli
    | Environment
    | Project of request: LocalPath
    | UserDefault

[<RequireQualifiedAccess>]
type TufRole =
    | Root
    | Timestamp
    | Snapshot
    | Targets

/// A mirror URL with no user information, query or fragment.
[<Struct>]
type Mirror =
    private
    | Mirror of string

    member this.Text =
        let (Mirror text) = this
        text

module Mirror =
    /// <c>invalid_value</c> for text that is not an absolute URL, <c>credential_in_document</c> for a URL with user
    /// information, a query or a fragment.
    let tryParse (text: string) =
        match Uri.TryCreate(text, UriKind.Absolute) with
        | true, NonNull uri when uri.UserInfo <> "" || uri.Query <> "" || uri.Fragment <> "" ->
            Error ReaderCode.CredentialInDocument
        | true, NonNull _ -> Ok(Mirror text)
        | _ -> Error ReaderCode.InvalidValue

/// A readiness check kind.
[<RequireQualifiedAccess>]
type CheckKind =
    | Integrity
    | Capability
    | Workflow

module CheckKind =
    /// The readiness a passed check of this kind reaches.
    let reaches (kind: CheckKind) =
        match kind with
        | CheckKind.Integrity -> Readiness.IntegrityVerified
        | CheckKind.Capability -> Readiness.CapabilityChecked
        | CheckKind.Workflow -> Readiness.WorkflowQualified

/// The five toolchain documents.
[<RequireQualifiedAccess>]
type DocumentKind =
    | Request
    | Lock
    | Recipe
    | Receipt
    | Result

module DocumentKind =
    let all =
        [ DocumentKind.Request, "request"
          DocumentKind.Lock, "lock"
          DocumentKind.Recipe, "recipe"
          DocumentKind.Receipt, "receipt"
          DocumentKind.Result, "result" ]

    let name (kind: DocumentKind) =
        all |> List.find (fun (candidate, _) -> candidate = kind) |> snd

/// What an <c>unsupported-artifact</c> diagnostic reports. Only <c>abi_unavailable</c> names an ABI.
[<RequireQualifiedAccess>]
type ArtifactProblem =
    | Unsupported
    | AbiUnavailable of abi: Token
    | NoCompatibleRelease

/// What a <c>lock-binding</c> diagnostic reports. Only <c>platform_not_locked</c> names a platform. Each code reports
/// a fixed document: the one whose member its path names.
[<RequireQualifiedAccess>]
type BindingProblem =
    | RequestChanged
    | PlatformNotLocked of platform: Platform
    | LockChanged
    | ReceiptLockMismatch
    | LockRecipeMismatch

module BindingProblem =
    let code (problem: BindingProblem) =
        match problem with
        | BindingProblem.RequestChanged -> BindingCode.RequestChanged
        | BindingProblem.PlatformNotLocked _ -> BindingCode.PlatformNotLocked
        | BindingProblem.LockChanged -> BindingCode.LockChanged
        | BindingProblem.ReceiptLockMismatch -> BindingCode.ReceiptLockMismatch
        | BindingProblem.LockRecipeMismatch -> BindingCode.LockRecipeMismatch

    /// The document whose member a binding code's path names.
    let document (code: BindingCode) =
        match code with
        | BindingCode.RequestChanged
        | BindingCode.PlatformNotLocked
        | BindingCode.LockRecipeMismatch -> DocumentKind.Lock
        | BindingCode.LockChanged
        | BindingCode.ReceiptLockMismatch -> DocumentKind.Receipt

/// A diagnostic's kind-specific members. The case is the diagnostic kind, so a code of another kind cannot be written.
[<RequireQualifiedAccess>]
type DiagnosticDetail =
    | UnsupportedArtifact of problem: ArtifactProblem * on: Component * platform: Platform * alternatives: Alternative list option
    | MissingPrerequisite of on: Component * prerequisite: Prerequisite * alternatives: Alternative list option
    | TrustFailure of
        code: TrustCode *
        repositoryId: Sha256 *
        role: TufRole option *
        target: TargetPath option *
        mirror: Mirror option
    | OfflineContentUnavailable of on: Component * target: TargetPath * sha256: Sha256
    | OwnershipConflict of on: Component * ownership: Ownership
    | ReadinessFailed of on: Component * check: CheckKind * reached: Readiness
    | UnsupportedContract of code: ReaderCode * document: DocumentKind * path: DocumentPath
    | SelectionUnavailable of source: SelectionSource
    | LockBinding of problem: BindingProblem * path: DocumentPath

module DiagnosticDetail =
    let code (detail: DiagnosticDetail) =
        match detail with
        | DiagnosticDetail.UnsupportedArtifact(problem, _, _, _) ->
            match problem with
            | ArtifactProblem.Unsupported -> Code.Artifact ArtifactCode.UnsupportedArtifact
            | ArtifactProblem.AbiUnavailable _ -> Code.Artifact ArtifactCode.AbiUnavailable
            | ArtifactProblem.NoCompatibleRelease -> Code.Artifact ArtifactCode.NoCompatibleRelease
        | DiagnosticDetail.MissingPrerequisite _ -> Code.MissingPrerequisite
        | DiagnosticDetail.TrustFailure(code, _, _, _, _) -> Code.Trust code
        | DiagnosticDetail.OfflineContentUnavailable _ -> Code.OfflineContentUnavailable
        | DiagnosticDetail.OwnershipConflict _ -> Code.OwnershipConflict
        | DiagnosticDetail.ReadinessFailed _ -> Code.ReadinessFailed
        | DiagnosticDetail.UnsupportedContract(code, _, _) -> Code.Reader code
        | DiagnosticDetail.SelectionUnavailable _ -> Code.GenerationNotInstalled
        | DiagnosticDetail.LockBinding(problem, _) -> Code.Binding(BindingProblem.code problem)

type Diagnostic =
    { Detail: DiagnosticDetail
      Severity: Severity
      Message: NonBlankText
      /// A non-empty argument vector, such as <c>["dotnet", "tool", "update", "--global", "Morphir.Tool"]</c>.
      Instruction: string list option }

/// Where a planned component comes from.
[<RequireQualifiedAccess>]
type ComponentSource =
    | RepositoryTarget of repositoryId: Sha256 * target: TargetPath * sha256: Sha256
    | RecipeDigest of repositoryId: Sha256 * target: TargetPath * sha256: Sha256
    | BuiltIn
    | ExternalOwner

/// Whether a component's readiness probe runs, and with what rights.
[<RequireQualifiedAccess>]
type Probe =
    | None
    | InHost
    | ProcessWithUserRights

/// What setup would do for one component.
type ComponentPlan =
    { Version: SemVer
      Source: ComponentSource
      DownloadBytes: uint64
      InstalledBytes: uint64
      UpdateOwner: Ownership
      Prerequisites: Prerequisite list
      Probe: Probe }

/// A planned component: the host, or a provider with its identity and delivery.
[<RequireQualifiedAccess>]
type PlannedComponent =
    | Host of implementation: Token * deployment: Token * plan: ComponentPlan
    | Provider of operation: Operation * extensionId: ExtensionId * delivery: Delivery * plan: ComponentPlan

[<RequireQualifiedAccess>]
type MetadataRefresh =
    | Required
    | NotRequired

/// A setup or update plan.
type Plan =
    { Implementation: Token
      Deployment: Token
      Platform: Platform
      Recipe: RecipeReference
      MetadataRefresh: MetadataRefresh
      Components: PlannedComponent list }

/// A plan with its canonical bytes and their SHA-256, the <c>planSha256</c> that <c>--expect-plan</c> compares.
type DigestedPlan =
    { Plan: Plan
      Canonical: byte array
      Sha256: Sha256 }

/// The selection a <c>show</c> result explains.
type Selection =
    { Source: SelectionSource
      GenerationId: Token
      Platform: Platform
      Host: HostIdentity
      LockSha256: Sha256 option }

/// <summary>A manager result, the one document <c>morphirup --json</c> writes to standard output.</summary>
type ManagerResult =
    { Command: Command
      Outcome: Outcome
      Plan: DigestedPlan option
      Selection: Selection option
      Diagnostics: Diagnostic list }

/// The manager result reader and writer (manager-result page).
module ResultDocument =
    open Reading

    /// The critical paths a 0.1.0-draft.1 result reader understands.
    let understoodCriticalPaths =
        set [ "schemaVersion"; "critical"; "command"; "outcome"; "plan"; "planSha256"; "selection"; "diagnostics" ]

    let private deliveries =
        Delivery.all |> List.map (fun (delivery, name) -> name, delivery)

    let private readinesses =
        Readiness.all |> List.map (fun (readiness, name) -> name, readiness)

    let private componentOf (n: Node) : Read<Component> =
        result {
            let! o = asObject n
            let! role = required "role" (tag [ "host", true; "provider", false ]) o

            if role then
                let! implementation = required "implementation" token o
                let! deployment = required "deployment" token o
                return Component.Host(implementation, deployment)
            else
                let! operation = operation o
                return Component.Provider operation
        }

    let private alternative (n: Node) : Read<Alternative> =
        result {
            let! o = asObject n
            let! implementation = optional "implementation" token o
            let! deployment = optional "deployment" token o
            let! platform = optional "platform" platform o

            return
                { Implementation = implementation
                  Deployment = deployment
                  Platform = platform }
        }

    let private ownership (n: Node) : Read<Ownership> =
        result {
            let! o = asObject n

            let! kind =
                required "kind" (tag [ "manager-owned", 0; "direct-archive", 1; "dotnet-tool", 2; "system-package", 3 ]) o

            match kind with
            | 0 -> return Ownership.ManagerOwned
            | 1 -> return Ownership.DirectArchive
            | 2 ->
                let! packageId = required "packageId" packageId o

                let! scope =
                    required "scope" (tag [ "global", ToolScope.Global; "tool-path", ToolScope.ToolPath; "local-manifest", ToolScope.LocalManifest ]) o

                return Ownership.DotnetTool(packageId, scope)
            | _ ->
                let! manager = required "manager" token o
                return Ownership.SystemPackage manager
        }

    let private selectionSource (n: Node) : Read<SelectionSource> =
        result {
            let! o = asObject n
            let! kind = required "kind" (tag [ "cli", 0; "environment", 1; "project", 2; "user-default", 3 ]) o

            match kind with
            | 0 -> return SelectionSource.Cli
            | 1 -> return SelectionSource.Environment
            | 2 ->
                let! path = required "path" (validated (LocalPath.tryParse PathSyntax.Any)) o
                return SelectionSource.Project path
            | _ -> return SelectionSource.UserDefault
        }

    let private diagnostic (n: Node) : Read<Diagnostic> =
        result {
            let! o = asObject n
            let! kind = required "kind" (tag (DiagnosticKind.all |> List.map (fun (kind, name) -> name, kind))) o
            let codePath = n.Path.Child "code"
            let! codeText = required "code" text o

            let! code =
                match Code.tryParse codeText with
                | None -> refuse ReaderCode.UnsupportedCase codePath
                | Some code when Code.kind code <> kind -> refuse ReaderCode.InconsistentDocument codePath
                | Some code -> Ok code

            let! severity = required "severity" (tag [ "error", Severity.Error; "warning", Severity.Warning ]) o
            let! message = required "message" (validated NonBlankText.tryParse) o

            let! instruction =
                optional
                    "instruction"
                    (fun i ->
                        array text i
                        |> Result.bind (fun args -> if List.isEmpty args then refuse ReaderCode.InconsistentDocument i.Path else Ok args))
                    o

            let abiPath = n.Path.Child "abi"
            let platformPath = n.Path.Child "platform"

            if has "abi" o && code <> Code.Artifact ArtifactCode.AbiUnavailable then
                return! refuse ReaderCode.InconsistentDocument abiPath

            let alternatives = optional "alternatives" (array alternative) o

            let! detail =
                match code with
                | Code.Artifact artifactCode ->
                    result {
                        let! on = required "component" componentOf o
                        let! platform = required "platform" platform o

                        let! problem =
                            match artifactCode with
                            | ArtifactCode.UnsupportedArtifact -> Ok ArtifactProblem.Unsupported
                            | ArtifactCode.NoCompatibleRelease -> Ok ArtifactProblem.NoCompatibleRelease
                            | ArtifactCode.AbiUnavailable ->
                                match tryMember "abi" o with
                                | None -> refuse ReaderCode.InconsistentDocument abiPath
                                | Some abi -> token abi |> Result.map ArtifactProblem.AbiUnavailable

                        let! alternatives = alternatives
                        return DiagnosticDetail.UnsupportedArtifact(problem, on, platform, alternatives)
                    }
                | Code.MissingPrerequisite ->
                    result {
                        let! on = required "component" componentOf o
                        let! prerequisite = required "prerequisite" prerequisite o
                        let! alternatives = alternatives
                        return DiagnosticDetail.MissingPrerequisite(on, prerequisite, alternatives)
                    }
                | Code.Trust trustCode ->
                    result {
                        let! repositoryId = required "repositoryId" sha256 o

                        let! role =
                            optional "role" (tag [ "root", TufRole.Root; "timestamp", TufRole.Timestamp; "snapshot", TufRole.Snapshot; "targets", TufRole.Targets ]) o

                        let! target = optional "target" targetPath o
                        let! mirror = optional "mirror" (validated Mirror.tryParse) o
                        return DiagnosticDetail.TrustFailure(trustCode, repositoryId, role, target, mirror)
                    }
                | Code.OfflineContentUnavailable ->
                    result {
                        let! on = required "component" componentOf o
                        let! target = required "target" targetPath o
                        let! sha = required "sha256" sha256 o
                        return DiagnosticDetail.OfflineContentUnavailable(on, target, sha)
                    }
                | Code.OwnershipConflict ->
                    result {
                        let! on = required "component" componentOf o
                        let! ownership = required "ownership" ownership o
                        return DiagnosticDetail.OwnershipConflict(on, ownership)
                    }
                | Code.ReadinessFailed ->
                    result {
                        let! on = required "component" componentOf o

                        let! check =
                            required "check" (tag [ "integrity", CheckKind.Integrity; "capability", CheckKind.Capability; "workflow", CheckKind.Workflow ]) o

                        let! reached = required "readiness" (tag readinesses) o

                        // The readiness reached is below the state the failed check would reach.
                        if Readiness.rank reached >= Readiness.rank (CheckKind.reaches check) then
                            return! refuse ReaderCode.InconsistentDocument (n.Path.Child "readiness")

                        return DiagnosticDetail.ReadinessFailed(on, check, reached)
                    }
                | Code.Reader readerCode ->
                    result {
                        let! document = required "document" (tag (DocumentKind.all |> List.map (fun (kind, name) -> name, kind))) o
                        let! path = required "path" text o
                        return DiagnosticDetail.UnsupportedContract(readerCode, document, DocumentPath.ofDotted path)
                    }
                | Code.GenerationNotInstalled ->
                    required "source" selectionSource o |> Result.map DiagnosticDetail.SelectionUnavailable
                | Code.Binding bindingCode ->
                    result {
                        let! document = required "document" (tag (DocumentKind.all |> List.map (fun (kind, name) -> name, kind))) o
                        let! path = required "path" text o

                        if document <> BindingProblem.document bindingCode then
                            return! refuse ReaderCode.InconsistentDocument (n.Path.Child "document")

                        let! problem =
                            match bindingCode, tryMember "platform" o with
                            | BindingCode.PlatformNotLocked, Some p -> platform p |> Result.map BindingProblem.PlatformNotLocked
                            | BindingCode.PlatformNotLocked, None
                            | _, Some _ -> refuse ReaderCode.InconsistentDocument platformPath
                            | BindingCode.RequestChanged, None -> Ok BindingProblem.RequestChanged
                            | BindingCode.LockChanged, None -> Ok BindingProblem.LockChanged
                            | BindingCode.ReceiptLockMismatch, None -> Ok BindingProblem.ReceiptLockMismatch
                            | BindingCode.LockRecipeMismatch, None -> Ok BindingProblem.LockRecipeMismatch

                        return DiagnosticDetail.LockBinding(problem, DocumentPath.ofDotted path)
                    }

            return
                { Detail = detail
                  Severity = severity
                  Message = message
                  Instruction = instruction }
        }

    let private componentSource (n: Node) : Read<ComponentSource> =
        result {
            let! o = asObject n

            let! kind =
                required "kind" (tag [ "repository-target", 0; "recipe-digest", 1; "built-in", 2; "external-owner", 3 ]) o

            match kind with
            | 0
            | 1 ->
                let! repositoryId = required "repositoryId" sha256 o
                let! target = required "target" targetPath o
                let! sha = required "sha256" sha256 o

                return
                    if kind = 0 then
                        ComponentSource.RepositoryTarget(repositoryId, target, sha)
                    else
                        ComponentSource.RecipeDigest(repositoryId, target, sha)
            | 2 -> return ComponentSource.BuiltIn
            | _ -> return ComponentSource.ExternalOwner
        }

    let private plannedComponent (n: Node) : Read<PlannedComponent> =
        result {
            let! o = asObject n
            let! on = required "component" componentOf o
            let! version = required "version" version o
            let! extensionId = optional "extensionId" extensionId o
            let! delivery = optional "delivery" (tag deliveries) o
            let! source = required "source" componentSource o
            let! downloadBytes = required "downloadBytes" integer o
            let! installedBytes = required "installedBytes" integer o
            let! updateOwner = required "updateOwner" ownership o
            let! prerequisites = optional "prerequisites" (array prerequisite) o

            let! probe =
                required "probe" (tag [ "none", Probe.None; "in-host", Probe.InHost; "process-with-user-rights", Probe.ProcessWithUserRights ]) o

            let plan =
                { Version = version
                  Source = source
                  DownloadBytes = downloadBytes
                  InstalledBytes = installedBytes
                  UpdateOwner = updateOwner
                  Prerequisites = defaultArg prerequisites []
                  Probe = probe }

            let inconsistent name = refuse ReaderCode.InconsistentDocument (n.Path.Child name)

            let! planned =
                match on, delivery, extensionId with
                | Component.Host _, Some _, _ -> inconsistent "delivery"
                | Component.Host _, _, Some _ -> inconsistent "extensionId"
                | Component.Host(implementation, deployment), None, None -> Ok(PlannedComponent.Host(implementation, deployment, plan))
                | Component.Provider _, None, _ -> inconsistent "delivery"
                | Component.Provider _, _, None -> inconsistent "extensionId"
                | Component.Provider operation, Some delivery, Some id -> Ok(PlannedComponent.Provider(operation, id, delivery, plan))

            // A process or dotnet-tool provider's readiness probe runs acquired code with the user's rights, and no
            // other component's does.
            let runsProcess =
                match planned with
                | PlannedComponent.Provider(_, _, (Delivery.Process | Delivery.DotnetTool), _) -> true
                | _ -> false

            if runsProcess <> (probe = Probe.ProcessWithUserRights) then
                return! inconsistent "probe"

            return planned
        }

    let private plan (n: Node) : Read<Plan> =
        result {
            let! o = asObject n
            let! implementation = required "implementation" token o
            let! deployment = required "deployment" token o
            let! platform = required "platform" platform o
            let! recipe = required "recipe" LockDocument.recipeReference o

            let! metadataRefresh =
                required "metadataRefresh" (tag [ "required", MetadataRefresh.Required; "not-required", MetadataRefresh.NotRequired ]) o

            let! components = required "components" (array plannedComponent) o

            return
                { Implementation = implementation
                  Deployment = deployment
                  Platform = platform
                  Recipe = recipe
                  MetadataRefresh = metadataRefresh
                  Components = components }
        }

    let private selection (n: Node) : Read<Selection> =
        result {
            let! o = asObject n
            let! source = required "source" selectionSource o
            let! generationId = required "generationId" token o
            let! platform = required "platform" platform o

            let! host =
                required
                    "host"
                    (fun h ->
                        result {
                            let! ho = asObject h
                            let! implementation = required "implementation" token ho
                            let! version = required "version" version ho
                            let! deployment = required "deployment" token ho

                            return
                                { Implementation = implementation
                                  Version = version
                                  Deployment = deployment }
                        })
                    o

            let! lockSha256 = optional "lockSha256" sha256 o

            return
                { Source = source
                  GenerationId = generationId
                  Platform = platform
                  Host = host
                  LockSha256 = lockSha256 }
        }

    /// The outcome rules: failed exactly when a diagnostic is an error; planned carries a plan.
    let internal outcomeIsConsistent (outcome: Outcome) (plan: 'P option) (diagnostics: Diagnostic list) =
        let hasError = diagnostics |> List.exists (fun d -> d.Severity = Severity.Error)

        match outcome with
        | Outcome.Failed -> hasError
        | Outcome.Succeeded -> not hasError
        | Outcome.Planned -> not hasError && Option.isSome plan

    let private body (o: ObjectNode) : Read<ManagerResult> =
        result {
            let! command = required "command" (tag (Command.all |> List.map (fun (command, name) -> name, command))) o

            let! outcome =
                required "outcome" (tag [ "planned", Outcome.Planned; "succeeded", Outcome.Succeeded; "failed", Outcome.Failed ]) o

            let! plan = optional "plan" plan o
            let! planSha256 = optional "planSha256" sha256 o
            let! selection = optional "selection" selection o
            let! diagnostics = required "diagnostics" (array diagnostic) o
            let shaPath = o.Path.Child "planSha256"

            let! digested =
                match plan, planSha256, tryMember "plan" o with
                | None, None, _ -> Ok None
                | Some plan, Some stated, Some planNode ->
                    Canonical.bytesAt planNode.Path planNode.Value
                    |> Result.bind (fun canonical ->
                        let actual = Sha256.ofBytes canonical

                        if actual <> stated then
                            refuse ReaderCode.InconsistentDocument shaPath
                        else
                            Ok(Some { Plan = plan; Canonical = canonical; Sha256 = actual }))
                | _ -> refuse ReaderCode.InconsistentDocument shaPath

            if not (outcomeIsConsistent outcome digested diagnostics) then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "outcome")

            return
                { Command = command
                  Outcome = outcome
                  Plan = digested
                  Selection = selection
                  Diagnostics = diagnostics }
        }

    /// Reads the exact bytes of a manager result, including the plan digest check.
    let read (bytes: byte array) : Result<ManagerResult, ReaderRefusal> =
        document Syntax.parseJson understoodCriticalPaths body bytes

    // ---- writing ------------------------------------------------------------------------------------------------

    let private tokenValue (token: Token) = Value.String token.Text

    let private componentValue (on: Component) =
        match on with
        | Component.Host(implementation, deployment) ->
            Value.Object
                [ "role", Value.String "host"
                  "implementation", tokenValue implementation
                  "deployment", tokenValue deployment ]
        | Component.Provider operation ->
            Value.Object [ "role", Value.String "provider"; yield! LockedProviderDocument.operationMembers operation ]

    let internal prerequisiteValue (prerequisite: Prerequisite) =
        let versions (comparators: Comparator list) =
            "versions", Value.Array(comparators |> List.map (fun c -> Value.String c.Text))

        Value.Object
            [ match prerequisite.Kind with
              | PrerequisiteKind.Runtime _ -> "kind", Value.String "runtime"
              | PrerequisiteKind.Sdk _ -> "kind", Value.String "sdk"
              | PrerequisiteKind.Native _ -> "kind", Value.String "native"
              "name", Value.String prerequisite.Name.Text
              match prerequisite.Kind with
              | PrerequisiteKind.Runtime list -> versions list
              | PrerequisiteKind.Sdk(list, phase) ->
                  versions list

                  "phase",
                  Value.String(
                      match phase with
                      | SdkPhase.Install -> "install"
                      | SdkPhase.Run -> "run"
                  )
              | PrerequisiteKind.Native(Some list) -> versions list
              | PrerequisiteKind.Native None -> ()
              match prerequisite.Platforms with
              | Some platforms -> "platforms", Value.Array(platforms |> List.map (Platform.name >> Value.String))
              | None -> () ]

    let private sourceValue (source: ComponentSource) =
        let bound kind (repositoryId: Sha256) (target: TargetPath) (sha: Sha256) =
            Value.Object
                [ "kind", Value.String kind
                  "repositoryId", Value.String repositoryId.Text
                  "target", Value.String target.Text
                  "sha256", Value.String sha.Text ]

        match source with
        | ComponentSource.RepositoryTarget(repositoryId, target, sha) -> bound "repository-target" repositoryId target sha
        | ComponentSource.RecipeDigest(repositoryId, target, sha) -> bound "recipe-digest" repositoryId target sha
        | ComponentSource.BuiltIn -> Value.Object [ "kind", Value.String "built-in" ]
        | ComponentSource.ExternalOwner -> Value.Object [ "kind", Value.String "external-owner" ]

    let private plannedValue (planned: PlannedComponent) =
        let common (plan: ComponentPlan) =
            [ "source", sourceValue plan.Source
              "downloadBytes", Value.Number(string plan.DownloadBytes)
              "installedBytes", Value.Number(string plan.InstalledBytes)
              "updateOwner", ReceiptDocument.ownershipValue plan.UpdateOwner
              "prerequisites", Value.Array(plan.Prerequisites |> List.map prerequisiteValue)
              "probe",
              Value.String(
                  match plan.Probe with
                  | Probe.None -> "none"
                  | Probe.InHost -> "in-host"
                  | Probe.ProcessWithUserRights -> "process-with-user-rights"
              ) ]

        match planned with
        | PlannedComponent.Host(implementation, deployment, plan) ->
            Value.Object
                [ "component", componentValue (Component.Host(implementation, deployment))
                  "version", Value.String plan.Version.Text
                  yield! common plan ]
        | PlannedComponent.Provider(operation, extensionId, delivery, plan) ->
            Value.Object
                [ "component", componentValue (Component.Provider operation)
                  "version", Value.String plan.Version.Text
                  "extensionId", Value.String extensionId.Text
                  "delivery", Value.String(Delivery.name delivery)
                  yield! common plan ]

    /// The plan as a document value, in the member order of the setup-plan fixture.
    let planValue (plan: Plan) =
        Value.Object
            [ "implementation", tokenValue plan.Implementation
              "deployment", tokenValue plan.Deployment
              "platform", Value.String(Platform.name plan.Platform)
              "recipe", LockDocument.recipeReferenceValue plan.Recipe
              "metadataRefresh",
              Value.String(
                  match plan.MetadataRefresh with
                  | MetadataRefresh.Required -> "required"
                  | MetadataRefresh.NotRequired -> "not-required"
              )
              "components", Value.Array(plan.Components |> List.map plannedValue) ]

    /// A plan with its canonical bytes and digest.
    let digest (plan: Plan) : DigestedPlan =
        match Canonical.bytes (planValue plan) with
        | Ok canonical ->
            { Plan = plan
              Canonical = canonical
              Sha256 = Sha256.ofBytes canonical }
        | Error _ -> failwith "unreachable: a plan value holds only strings, integers and arrays"

    let private selectionSourceValue (source: SelectionSource) =
        match source with
        | SelectionSource.Cli -> Value.Object [ "kind", Value.String "cli" ]
        | SelectionSource.Environment -> Value.Object [ "kind", Value.String "environment" ]
        | SelectionSource.Project path -> Value.Object [ "kind", Value.String "project"; "path", Value.String path.Text ]
        | SelectionSource.UserDefault -> Value.Object [ "kind", Value.String "user-default" ]

    let private alternativesMembers (alternatives: Alternative list option) =
        match alternatives with
        | None -> []
        | Some list ->
            [ "alternatives",
              Value.Array
                  [ for alternative in list ->
                        Value.Object
                            [ match alternative.Implementation with
                              | Some implementation -> "implementation", tokenValue implementation
                              | None -> ()
                              match alternative.Deployment with
                              | Some deployment -> "deployment", tokenValue deployment
                              | None -> ()
                              match alternative.Platform with
                              | Some platform -> "platform", Value.String(Platform.name platform)
                              | None -> () ] ] ]

    let private diagnosticValue (diagnostic: Diagnostic) =
        let code = DiagnosticDetail.code diagnostic.Detail

        let detail =
            match diagnostic.Detail with
            | DiagnosticDetail.UnsupportedArtifact(problem, on, platform, alternatives) ->
                [ "component", componentValue on
                  "platform", Value.String(Platform.name platform)
                  match problem with
                  | ArtifactProblem.AbiUnavailable abi -> "abi", tokenValue abi
                  | _ -> ()
                  yield! alternativesMembers alternatives ]
            | DiagnosticDetail.MissingPrerequisite(on, prerequisite, alternatives) ->
                [ "component", componentValue on
                  "prerequisite", prerequisiteValue prerequisite
                  yield! alternativesMembers alternatives ]
            | DiagnosticDetail.TrustFailure(_, repositoryId, role, target, mirror) ->
                [ "repositoryId", Value.String repositoryId.Text
                  match role with
                  | Some role ->
                      "role",
                      Value.String(
                          match role with
                          | TufRole.Root -> "root"
                          | TufRole.Timestamp -> "timestamp"
                          | TufRole.Snapshot -> "snapshot"
                          | TufRole.Targets -> "targets"
                      )
                  | None -> ()
                  match target with
                  | Some target -> "target", Value.String target.Text
                  | None -> ()
                  match mirror with
                  | Some mirror -> "mirror", Value.String mirror.Text
                  | None -> () ]
            | DiagnosticDetail.OfflineContentUnavailable(on, target, sha) ->
                [ "component", componentValue on
                  "target", Value.String target.Text
                  "sha256", Value.String sha.Text ]
            | DiagnosticDetail.OwnershipConflict(on, ownership) ->
                [ "component", componentValue on
                  "ownership", ReceiptDocument.ownershipValue ownership ]
            | DiagnosticDetail.ReadinessFailed(on, check, reached) ->
                [ "component", componentValue on
                  "check",
                  Value.String(
                      match check with
                      | CheckKind.Integrity -> "integrity"
                      | CheckKind.Capability -> "capability"
                      | CheckKind.Workflow -> "workflow"
                  )
                  "readiness", Value.String(Readiness.name reached) ]
            | DiagnosticDetail.UnsupportedContract(_, document, path) ->
                [ "document", Value.String(DocumentKind.name document)
                  "path", Value.String(DocumentPath.render path) ]
            | DiagnosticDetail.SelectionUnavailable source -> [ "source", selectionSourceValue source ]
            | DiagnosticDetail.LockBinding(problem, path) ->
                [ "document", Value.String(DocumentKind.name (BindingProblem.document (BindingProblem.code problem)))
                  "path", Value.String(DocumentPath.render path)
                  match problem with
                  | BindingProblem.PlatformNotLocked platform -> "platform", Value.String(Platform.name platform)
                  | _ -> () ]

        Value.Object
            [ "kind", Value.String(DiagnosticKind.name (Code.kind code))
              "code", Value.String(Code.name code)
              "severity",
              Value.String(
                  match diagnostic.Severity with
                  | Severity.Error -> "error"
                  | Severity.Warning -> "warning"
              )
              "message", Value.String diagnostic.Message.Text
              yield! detail
              match diagnostic.Instruction with
              | Some args -> "instruction", Value.Array(args |> List.map Value.String)
              | None -> () ]

    /// The result as a document value.
    let value (result: ManagerResult) =
        Value.Object
            [ "schemaVersion", Value.String ContractVersion.current
              "command", Value.String(Command.name result.Command)
              "outcome",
              Value.String(
                  match result.Outcome with
                  | Outcome.Planned -> "planned"
                  | Outcome.Succeeded -> "succeeded"
                  | Outcome.Failed -> "failed"
              )
              match result.Plan with
              | Some read ->
                  // The digest of what is written: a plan read with unknown members is written without them.
                  let digested = digest read.Plan
                  "plan", planValue digested.Plan
                  "planSha256", Value.String digested.Sha256.Text
              | None -> ()
              match result.Selection with
              | Some selection ->
                  "selection",
                  Value.Object
                      [ "source", selectionSourceValue selection.Source
                        "generationId", tokenValue selection.GenerationId
                        "platform", Value.String(Platform.name selection.Platform)
                        "host",
                        Value.Object
                            [ "implementation", tokenValue selection.Host.Implementation
                              "version", Value.String selection.Host.Version.Text
                              "deployment", tokenValue selection.Host.Deployment ]
                        match selection.LockSha256 with
                        | Some sha -> "lockSha256", Value.String sha.Text
                        | None -> () ]
              | None -> ()
              "diagnostics", Value.Array(result.Diagnostics |> List.map diagnosticValue) ]

    /// The bytes a manager writes to standard output: one result document.
    let write (result: ManagerResult) = JsonText.toBytes (value result)

    /// <summary>A result whose outcome agrees with its diagnostics: <c>failed</c> exactly when one is an error,
    /// <c>planned</c> only with a plan. Any other combination is refused, so a manager cannot write it.</summary>
    let create command outcome plan selection diagnostics : Result<ManagerResult, string> =
        if outcomeIsConsistent outcome plan diagnostics then
            Ok
                { Command = command
                  Outcome = outcome
                  Plan = plan
                  Selection = selection
                  Diagnostics = diagnostics }
        else
            Error "the outcome contradicts the diagnostics or the plan"
