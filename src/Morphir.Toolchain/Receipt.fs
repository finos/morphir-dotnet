// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// The scope of a .NET tool installation.
[<RequireQualifiedAccess>]
type ToolScope =
    | Global
    | ToolPath
    | LocalManifest

/// Who owns an installation, and so who updates or removes it.
[<RequireQualifiedAccess>]
type Ownership =
    | ManagerOwned
    | DirectArchive
    | DotnetTool of packageId: PackageId * scope: ToolScope
    | SystemPackage of manager: Token

/// How an installation's bytes were authenticated. Publisher authentication is a separate fact from the claim check.
[<RequireQualifiedAccess>]
type Authentication =
    /// Directly by TUF target metadata.
    | RepositoryTarget of repositoryId: Sha256 * targetsVersion: uint64
    /// By a digest in an authenticated recipe, which is the receipt's own recipe.
    | RecipeDigest of recipe: RecipeReference
    /// Not by the manager at all.
    | ExternalOwner

type Launch =
    { Program: LocalPath
      Args: LaunchArguments }

type Installation =
    { Ownership: Ownership
      Authentication: Authentication
      Launch: Launch }

[<RequireQualifiedAccess>]
type ProbeSource =
    | Describe
    | SessionFallback

/// Whether the installed artifact was run to compare its capability claims.
[<RequireQualifiedAccess>]
type ClaimCheck =
    | Probed of source: ProbeSource
    | Unchecked

/// A check's status. Anything other than passed states its reason, so a skipped or unsupported check stays visible.
[<RequireQualifiedAccess>]
type CheckStatus =
    | Passed
    | Failed of reason: NonBlankText
    | Skipped of reason: NonBlankText
    | Unsupported of reason: NonBlankText

/// A component's exactly one integrity and one capability check.
type ComponentChecks =
    { Integrity: CheckStatus
      Capability: CheckStatus }

/// A generation's exactly one workflow check.
type WorkflowCheck = { Workflow: Token; Status: CheckStatus }

/// The ordered readiness states.
[<RequireQualifiedAccess>]
type Readiness =
    | Acquired
    | IntegrityVerified
    | CapabilityChecked
    | WorkflowQualified

/// Readiness is derived from checks, never asserted.
module Readiness =
    let all =
        [ Readiness.Acquired, "acquired"
          Readiness.IntegrityVerified, "integrity-verified"
          Readiness.CapabilityChecked, "capability-checked"
          Readiness.WorkflowQualified, "workflow-qualified" ]

    let name (readiness: Readiness) =
        all |> List.find (fun (candidate, _) -> candidate = readiness) |> snd

    let rank (readiness: Readiness) =
        all |> List.findIndex (fun (candidate, _) -> candidate = readiness)

    /// A component is integrity-verified when its integrity check passed and capability-checked when both passed.
    let ofComponent (checks: ComponentChecks) =
        match checks.Integrity, checks.Capability with
        | CheckStatus.Passed, CheckStatus.Passed -> Readiness.CapabilityChecked
        | CheckStatus.Passed, _ -> Readiness.IntegrityVerified
        | _ -> Readiness.Acquired

    /// A generation is workflow-qualified when every component is capability-checked and the workflow check passed,
    /// and otherwise has the lowest component readiness.
    let ofGeneration (components: ComponentChecks list) (workflow: WorkflowCheck) =
        let lowest = components |> List.map ofComponent |> List.minBy rank

        if lowest = Readiness.CapabilityChecked && workflow.Status = CheckStatus.Passed then
            Readiness.WorkflowQualified
        else
            lowest

/// How the installed host runs. Native AOT has no runtime; a JIT host has a bundled or a system runtime.
[<RequireQualifiedAccess>]
type HostExecution =
    | NativeAot
    | JitBundled of runtime: SemVer
    | JitSystem of runtime: SemVer

type InstalledHostIdentity =
    { Implementation: Token
      Version: SemVer
      Deployment: Token
      Artifact: TargetBinding }

type InstalledHost =
    { Identity: InstalledHostIdentity
      Execution: HostExecution
      Installation: Installation
      Checks: ComponentChecks }

/// An installed provider. A built-in is part of the host and has no installation; every other provider has one, with
/// its claim check.
[<RequireQualifiedAccess>]
type ProviderInstallation =
    | PartOfHost
    | Installed of installation: Installation * claimCheck: ClaimCheck

type InstalledProvider =
    { Identity: LockedProvider
      Installation: ProviderInstallation
      Checks: ComponentChecks }

/// <summary>A local installation receipt for one generation, under
/// <c>MORPHIR_HOME/toolchains/generations/&lt;id&gt;/</c>. Readiness is not stored: it is derived from the checks.
/// </summary>
type Receipt =
    { GenerationId: Token
      Platform: Platform
      LockSha256: Sha256
      Recipe: RecipeReference
      Host: InstalledHost
      Providers: InstalledProvider list
      Workflow: WorkflowCheck }

module Receipt =
    let readiness (receipt: Receipt) =
        Readiness.ofGeneration (receipt.Host.Checks :: (receipt.Providers |> List.map _.Checks)) receipt.Workflow

/// The installation receipt reader and writer (installation-receipt page).
module ReceiptDocument =
    open Reading

    /// The critical paths a 0.1.0-draft.1 receipt reader understands.
    let understoodCriticalPaths =
        set
            [ "schemaVersion"
              "critical"
              "generationId"
              "platform"
              "lockSha256"
              "recipe"
              "host"
              "host.identity"
              "host.execution"
              "host.installation"
              "host.readiness"
              "host.checks"
              "providers"
              "providers.identity"
              "providers.installation"
              "providers.readiness"
              "providers.checks"
              "readiness"
              "checks" ]

    [<RequireQualifiedAccess>]
    type private CheckKind =
        | Integrity
        | Capability
        | Workflow

    let private check (n: Node) : Read<CheckKind * CheckStatus * Token option> =
        result {
            let! o = asObject n

            let! kind =
                required "check" (tag [ "integrity", CheckKind.Integrity; "capability", CheckKind.Capability; "workflow", CheckKind.Workflow ]) o

            let! status =
                required
                    "status"
                    (tag
                        [ "passed", CheckStatusTag.Passed
                          "failed", CheckStatusTag.Failed
                          "skipped", CheckStatusTag.Skipped
                          "unsupported", CheckStatusTag.Unsupported ])
                    o

            let! reason = optional "reason" (validated NonBlankText.tryParse) o
            let! workflow = optional "workflow" token o

            let! status =
                match status, reason with
                | CheckStatusTag.Passed, _ -> Ok CheckStatus.Passed
                | (CheckStatusTag.Failed | CheckStatusTag.Skipped | CheckStatusTag.Unsupported), None ->
                    refuse ReaderCode.InconsistentDocument (n.Path.Child "reason")
                | CheckStatusTag.Failed, Some reason -> Ok(CheckStatus.Failed reason)
                | CheckStatusTag.Skipped, Some reason -> Ok(CheckStatus.Skipped reason)
                | CheckStatusTag.Unsupported, Some reason -> Ok(CheckStatus.Unsupported reason)

            // workflow is a member of the workflow case: required there, a member out of place elsewhere
            // (installation-receipt.md, Checks).
            match kind, workflow with
            | CheckKind.Workflow, None -> return! refuse ReaderCode.InvalidDocument (n.Path.Child "workflow")
            | CheckKind.Integrity, Some _
            | CheckKind.Capability, Some _ -> return! refuse ReaderCode.InconsistentDocument (n.Path.Child "workflow")
            | CheckKind.Workflow, Some _
            | CheckKind.Integrity, None
            | CheckKind.Capability, None -> ()

            return kind, status, workflow
        }

    let internal componentChecks (n: Node) : Read<ComponentChecks> =
        result {
            let! checks = array check n

            let only kind =
                checks |> List.filter (fun (candidate, _, _) -> candidate = kind) |> List.map (fun (_, status, _) -> status)

            match only CheckKind.Integrity, only CheckKind.Capability, only CheckKind.Workflow with
            | [ integrity ], [ capability ], [] ->
                return
                    { Integrity = integrity
                      Capability = capability }
            | _ -> return! refuse ReaderCode.InconsistentDocument n.Path
        }

    /// <summary>A component's readiness. <c>workflow-qualified</c> is a known state, a generation's, so a component
    /// that records it is <c>inconsistent_document</c> at its <c>readiness</c>, as any other state its checks do not
    /// derive is.</summary>
    let private componentReadiness =
        tag
            [ "acquired", Readiness.Acquired
              "integrity-verified", Readiness.IntegrityVerified
              "capability-checked", Readiness.CapabilityChecked
              "workflow-qualified", Readiness.WorkflowQualified ]

    /// A component's stated readiness must equal the readiness its checks derive.
    let private derivedComponent (o: ObjectNode) (checks: ComponentChecks) : Read<unit> =
        result {
            let! stated = required "readiness" componentReadiness o

            if stated <> Readiness.ofComponent checks then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "readiness")
        }

    /// An installation owner, shared by receipts and results: <c>dotnet-tool</c> requires <c>packageId</c> and
    /// <c>scope</c>, <c>system-package</c> requires <c>manager</c>.
    let internal ownership (n: Node) : Read<Ownership> =
        result {
            let! o = asObject n

            let! kind =
                required
                    "kind"
                    (tag
                        [ "manager-owned", OwnershipTag.ManagerOwned
                          "direct-archive", OwnershipTag.DirectArchive
                          "dotnet-tool", OwnershipTag.DotnetTool
                          "system-package", OwnershipTag.SystemPackage ])
                    o

            match kind with
            | OwnershipTag.ManagerOwned -> return Ownership.ManagerOwned
            | OwnershipTag.DirectArchive -> return Ownership.DirectArchive
            | OwnershipTag.DotnetTool ->
                let! packageId = required "packageId" packageId o

                let! scope =
                    required "scope" (tag [ "global", ToolScope.Global; "tool-path", ToolScope.ToolPath; "local-manifest", ToolScope.LocalManifest ]) o

                return Ownership.DotnetTool(packageId, scope)
            | OwnershipTag.SystemPackage ->
                let! manager = required "manager" token o
                return Ownership.SystemPackage manager
        }
    let private authentication (recipe: RecipeReference) (n: Node) : Read<Authentication> =
        result {
            let! o = asObject n

            let! kind =
                required
                    "kind"
                    (tag
                        [ "repository-target", AuthenticationTag.RepositoryTarget
                          "recipe-digest", AuthenticationTag.RecipeDigest
                          "local-developer", AuthenticationTag.LocalDeveloper
                          "external-owner", AuthenticationTag.ExternalOwner ])
                    o

            match kind with
            | AuthenticationTag.RepositoryTarget ->
                let! repositoryId = required "repositoryId" sha256 o
                let! targetsVersion = required "targetsVersion" integer o
                return Authentication.RepositoryTarget(repositoryId, targetsVersion)
            | AuthenticationTag.RecipeDigest ->
                let! repositoryId = required "repositoryId" sha256 o
                let! target = required "recipeTarget" targetPath o
                let! sha = required "recipeSha256" sha256 o

                let named =
                    { RepositoryId = repositoryId
                      Target = target
                      Sha256 = sha }

                if named <> recipe then
                    return! refuse ReaderCode.InconsistentDocument n.Path

                return Authentication.RecipeDigest named
            | AuthenticationTag.LocalDeveloper ->
                // An unsigned developer artifact cannot equal a locked digest, so it is never part of a generation;
                // bytes that do equal one were authenticated by that recipe digest (installation receipt page).
                return! refuse ReaderCode.InconsistentDocument n.Path
            | AuthenticationTag.ExternalOwner -> return Authentication.ExternalOwner
        }

    let private launch (platform: Platform) (n: Node) : Read<Launch> =
        result {
            let! o = asObject n
            let! program = required "program" (validated (LocalPath.tryParse (LocalPath.syntaxFor platform))) o

            let argsPath = n.Path.Child "args"
            let! values = required "args" (array text) o

            let! args =
                LaunchArguments.tryCreate values
                |> Result.mapError (fun index -> { Code = ReaderCode.InvalidValue; Path = argsPath.At index })

            return { Program = program; Args = args }
        }

    let private installation (platform: Platform) (recipe: RecipeReference) (o: ObjectNode) : Read<Installation> =
        result {
            let! ownership = required "ownership" ownership o
            let! authentication = required "authentication" (authentication recipe) o
            let! launch = required "launch" (launch platform) o

            match ownership, authentication with
            | Ownership.ManagerOwned, Authentication.ExternalOwner ->
                // A manager-owned installation always says how the manager authenticated it.
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "authentication")
            | _ -> ()

            return
                { Ownership = ownership
                  Authentication = authentication
                  Launch = launch }
        }

    let private execution (identity: InstalledHostIdentity) (n: Node) : Read<HostExecution> =
        result {
            let! o = asObject n
            let! mode = required "mode" (tag [ "native-aot", Execution.NativeAot; "jit", Execution.Jit ]) o
            let! runtimeNode = objectMember "runtime" o
            let! kind =
                required "kind" (tag [ "none", RuntimeTag.None; "bundled", RuntimeTag.Bundled; "system", RuntimeTag.System ]) runtimeNode

            let runtimePath = n.Path.Child "runtime"

            let! execution =
                match mode, kind with
                | Execution.NativeAot, RuntimeTag.None -> Ok HostExecution.NativeAot
                | Execution.Jit, RuntimeTag.Bundled -> required "version" version runtimeNode |> Result.map HostExecution.JitBundled
                | Execution.Jit, RuntimeTag.System -> required "version" version runtimeNode |> Result.map HostExecution.JitSystem
                | Execution.NativeAot, (RuntimeTag.Bundled | RuntimeTag.System)
                | Execution.Jit, RuntimeTag.None -> refuse ReaderCode.InconsistentDocument runtimePath

            // For the dotnet implementation the deployment fixes the execution.
            let expected =
                if identity.Implementation.Text <> "dotnet" then
                    None
                else
                    match identity.Deployment.Text, execution with
                    | "native-aot", HostExecution.NativeAot -> None
                    | "native-aot", _ -> Some()
                    | "self-contained", HostExecution.JitBundled _ -> None
                    | "self-contained", _ -> Some()
                    | ("framework-dependent" | "dotnet-tool"), HostExecution.JitSystem _ -> None
                    | ("framework-dependent" | "dotnet-tool"), _ -> Some()
                    | _ -> None

            if expected.IsSome then
                return! refuse ReaderCode.InconsistentDocument n.Path

            return execution
        }

    let private hostIdentity (n: Node) : Read<InstalledHostIdentity> =
        result {
            let! o = asObject n
            let! implementation = required "implementation" token o
            let! version = required "version" version o
            let! deployment = required "deployment" token o
            let! target = required "target" targetPath o
            let! sha = required "sha256" sha256 o

            return
                { Implementation = implementation
                  Version = version
                  Deployment = deployment
                  Artifact = { Target = target; Sha256 = sha } }
        }

    let private host (platform: Platform) (recipe: RecipeReference) (n: Node) : Read<InstalledHost> =
        result {
            let! o = asObject n
            let! identity = required "identity" hostIdentity o
            let! execution = required "execution" (execution identity) o
            let! installationNode = objectMember "installation" o
            let! installation = installation platform recipe installationNode
            let! checks = required "checks" componentChecks o
            do! derivedComponent o checks

            return
                { Identity = identity
                  Execution = execution
                  Installation = installation
                  Checks = checks }
        }

    let private provider readIdentity (platform: Platform) (recipe: RecipeReference) (host: InstalledHost) (n: Node) : Read<InstalledProvider> =
        result {
            let! o = asObject n
            let! identity = required "identity" (LockedProviderDocument.readWithIdentity readIdentity) o
            let installationPath = n.Path.Child "installation"

            let! installation =
                match identity.Delivery, tryMember "installation" o with
                | LockedDelivery.BuiltIn _, Some _ -> refuse ReaderCode.InconsistentDocument installationPath
                | LockedDelivery.BuiltIn _, None -> Ok ProviderInstallation.PartOfHost
                | _, None -> refuse ReaderCode.InvalidDocument installationPath
                | _, Some installed ->
                    result {
                        let! installedNode = asObject installed
                        let! installation = installation platform recipe installedNode

                        let! claim =
                            required "claimCheck" (tag [ "probed", ClaimTag.Probed; "unchecked", ClaimTag.Unchecked ]) installedNode

                        let! probeSource =
                            optional "probeSource" (tag [ "describe", ProbeSource.Describe; "session-fallback", ProbeSource.SessionFallback ]) installedNode

                        let probePath = installationPath.Child "probeSource"

                        let! claimCheck =
                            match claim, probeSource with
                            | ClaimTag.Probed, Some source -> Ok(ClaimCheck.Probed source)
                            | ClaimTag.Unchecked, None -> Ok ClaimCheck.Unchecked
                            // probeSource is a member of the probed case: required there, out of place elsewhere.
                            | ClaimTag.Probed, None -> refuse ReaderCode.InvalidDocument probePath
                            | ClaimTag.Unchecked, Some _ -> refuse ReaderCode.InconsistentDocument probePath

                        return ProviderInstallation.Installed(installation, claimCheck)
                    }

            let checksPath = n.Path.Child "checks"
            let! checks = required "checks" componentChecks o

            let outcome status =
                match status with
                | CheckStatus.Passed -> CheckStatusTag.Passed
                | CheckStatus.Failed _ -> CheckStatusTag.Failed
                | CheckStatus.Skipped _ -> CheckStatusTag.Skipped
                | CheckStatus.Unsupported _ -> CheckStatusTag.Unsupported

            match identity.Delivery with
            | LockedDelivery.BuiltIn _ when outcome checks.Integrity <> outcome host.Checks.Integrity ->
                // A built-in is part of the host's bytes, so its integrity check has the host's outcome.
                return! refuse ReaderCode.InconsistentDocument checksPath
            | _ -> ()

            do! derivedComponent o checks

            return
                { Identity = identity
                  Installation = installation
                  Checks = checks }
        }

    let private workflowChecks (n: Node) : Read<WorkflowCheck> =
        result {
            let! checks = array check n

            match checks with
            | [ _, status, Some workflow ] -> return { Workflow = workflow; Status = status }
            | _ -> return! refuse ReaderCode.InconsistentDocument n.Path
        }

    let private body readIdentity (o: ObjectNode) : Read<Receipt> =
        result {
            let! generationId = required "generationId" token o
            let! platform = required "platform" platform o
            let! lockSha256 = required "lockSha256" sha256 o
            let! recipe = required "recipe" LockDocument.recipeReference o
            let! host = required "host" (host platform recipe) o
            let! providers = required "providers" (array (provider readIdentity platform recipe host)) o
            let! workflow = required "checks" workflowChecks o

            let readiness =
                tag
                    [ "acquired", Readiness.Acquired
                      "integrity-verified", Readiness.IntegrityVerified
                      "capability-checked", Readiness.CapabilityChecked
                      "workflow-qualified", Readiness.WorkflowQualified ]

            let! stated = required "readiness" readiness o

            let receipt =
                { GenerationId = generationId
                  Platform = platform
                  LockSha256 = lockSha256
                  Recipe = recipe
                  Host = host
                  Providers = providers
                  Workflow = workflow }

            if stated <> Receipt.readiness receipt then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "readiness")

            return receipt
        }

    /// Reads the exact bytes of a receipt.
    let read (bytes: byte array) : Result<Receipt, ReaderRefusal> =
        document Syntax.parseJson understoodCriticalPaths (body extensionId) bytes

    /// Reads a preview receipt; readiness still follows the recorded checks.
    let internal readPreview (bytes: byte array) : Result<Receipt, ReaderRefusal> =
        previewDocument PreviewDocument.Receipt understoodCriticalPaths (body previewExtensionId) bytes

    // ---- writing ------------------------------------------------------------------------------------------------

    let private statusMembers (status: CheckStatus) =
        match status with
        | CheckStatus.Passed -> [ "status", Value.String "passed" ]
        | CheckStatus.Failed reason -> [ "status", Value.String "failed"; "reason", Value.String reason.Text ]
        | CheckStatus.Skipped reason -> [ "status", Value.String "skipped"; "reason", Value.String reason.Text ]
        | CheckStatus.Unsupported reason -> [ "status", Value.String "unsupported"; "reason", Value.String reason.Text ]

    let private componentChecksValue (checks: ComponentChecks) =
        Value.Array
            [ Value.Object [ "check", Value.String "integrity"; yield! statusMembers checks.Integrity ]
              Value.Object [ "check", Value.String "capability"; yield! statusMembers checks.Capability ] ]

    let internal ownershipValue (ownership: Ownership) =
        match ownership with
        | Ownership.ManagerOwned -> Value.Object [ "kind", Value.String "manager-owned" ]
        | Ownership.DirectArchive -> Value.Object [ "kind", Value.String "direct-archive" ]
        | Ownership.DotnetTool(packageId, scope) ->
            let scope =
                match scope with
                | ToolScope.Global -> "global"
                | ToolScope.ToolPath -> "tool-path"
                | ToolScope.LocalManifest -> "local-manifest"

            Value.Object
                [ "kind", Value.String "dotnet-tool"
                  "packageId", Value.String packageId.Text
                  "scope", Value.String scope ]
        | Ownership.SystemPackage manager ->
            Value.Object [ "kind", Value.String "system-package"; "manager", Value.String manager.Text ]

    let private authenticationValue (authentication: Authentication) =
        match authentication with
        | Authentication.RepositoryTarget(repositoryId, targetsVersion) ->
            Value.Object
                [ "kind", Value.String "repository-target"
                  "repositoryId", Value.String repositoryId.Text
                  "targetsVersion", Value.Number(string targetsVersion) ]
        | Authentication.RecipeDigest recipe ->
            Value.Object
                [ "kind", Value.String "recipe-digest"
                  "repositoryId", Value.String recipe.RepositoryId.Text
                  "recipeTarget", Value.String recipe.Target.Text
                  "recipeSha256", Value.String recipe.Sha256.Text ]
        | Authentication.ExternalOwner -> Value.Object [ "kind", Value.String "external-owner" ]

    let private installationMembers (installation: Installation) =
        [ "ownership", ownershipValue installation.Ownership
          "authentication", authenticationValue installation.Authentication ]

    let private launchValue (launch: Launch) =
        Value.Object
            [ "program", Value.String launch.Program.Text
              "args", Value.Array(launch.Args.Items |> List.map Value.String) ]

    /// The receipt as a document value; readiness values are the derived ones.
    let value (receipt: Receipt) =
        let execution =
            match receipt.Host.Execution with
            | HostExecution.NativeAot -> Value.Object [ "mode", Value.String "native-aot"; "runtime", Value.Object [ "kind", Value.String "none" ] ]
            | HostExecution.JitBundled runtime ->
                Value.Object
                    [ "mode", Value.String "jit"
                      "runtime", Value.Object [ "kind", Value.String "bundled"; "version", Value.String runtime.Text ] ]
            | HostExecution.JitSystem runtime ->
                Value.Object
                    [ "mode", Value.String "jit"
                      "runtime", Value.Object [ "kind", Value.String "system"; "version", Value.String runtime.Text ] ]

        let identity = receipt.Host.Identity

        Value.Object
            [ "schemaVersion", Value.String ContractVersion.current
              "generationId", Value.String receipt.GenerationId.Text
              "platform", Value.String(Platform.name receipt.Platform)
              "lockSha256", Value.String receipt.LockSha256.Text
              "recipe", LockDocument.recipeReferenceValue receipt.Recipe
              "host",
              Value.Object
                  [ "identity",
                    Value.Object
                        [ "implementation", Value.String identity.Implementation.Text
                          "version", Value.String identity.Version.Text
                          "deployment", Value.String identity.Deployment.Text
                          "target", Value.String identity.Artifact.Target.Text
                          "sha256", Value.String identity.Artifact.Sha256.Text ]
                    "execution", execution
                    "installation",
                    Value.Object
                        [ yield! installationMembers receipt.Host.Installation
                          "launch", launchValue receipt.Host.Installation.Launch ]
                    "readiness", Value.String(Readiness.name (Readiness.ofComponent receipt.Host.Checks))
                    "checks", componentChecksValue receipt.Host.Checks ]
              "providers",
              Value.Array
                  [ for provider in receipt.Providers ->
                        Value.Object
                            [ "identity", LockedProviderDocument.value provider.Identity
                              match provider.Installation with
                              | ProviderInstallation.PartOfHost -> ()
                              | ProviderInstallation.Installed(installation, claimCheck) ->
                                  "installation",
                                  Value.Object
                                      [ yield! installationMembers installation
                                        match claimCheck with
                                        | ClaimCheck.Probed source ->
                                            "claimCheck", Value.String "probed"

                                            "probeSource",
                                            Value.String(
                                                match source with
                                                | ProbeSource.Describe -> "describe"
                                                | ProbeSource.SessionFallback -> "session-fallback"
                                            )
                                        | ClaimCheck.Unchecked -> "claimCheck", Value.String "unchecked"
                                        "launch", launchValue installation.Launch ]
                              "readiness", Value.String(Readiness.name (Readiness.ofComponent provider.Checks))
                              "checks", componentChecksValue provider.Checks ] ]
              "readiness", Value.String(Readiness.name (Receipt.readiness receipt))
              "checks",
              Value.Array
                  [ Value.Object
                        [ "check", Value.String "workflow"
                          yield! statusMembers receipt.Workflow.Status
                          "workflow", Value.String receipt.Workflow.Workflow.Text ] ] ]

    /// The bytes a manager writes for a receipt.
    let write (receipt: Receipt) = JsonText.toBytes (value receipt)
