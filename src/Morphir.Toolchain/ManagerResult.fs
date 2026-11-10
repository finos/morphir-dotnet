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
type Mirror =
    private
    | Mirror of string

    member this.Text =
        let (Mirror text) = this
        text

module Mirror =
    /// <summary>The schemes a mirror may use. The tool release metadata v1 page requires HTTPS for public mirrors and
    /// leaves other mirrors to transport configuration, so <c>http</c> is admitted too; which schemes a result may
    /// report is a contract gap. The scheme must be written out: a bare path such as <c>/srv/mirror</c>, which .NET reads
    /// as an implicit <c>file:</c> URI on Linux and macOS only, is refused on every operating system.</summary>
    let schemes = [ "https"; "http" ]

    /// <c>invalid_value</c> for text that is not an absolute <c>http</c> or <c>https</c> URL with a host,
    /// <c>credential_in_document</c> for one with user information, a query or a fragment.
    let tryParse (text: string) =
        let written =
            schemes
            |> List.exists (fun scheme -> text.StartsWith(scheme + "://", StringComparison.OrdinalIgnoreCase))

        match Uri.TryCreate(text, UriKind.Absolute) with
        | true, NonNull uri when written && List.contains uri.Scheme schemes && uri.Host <> "" ->
            if uri.UserInfo <> "" || uri.Query <> "" || uri.Fragment <> "" then
                Error ReaderCode.CredentialInDocument
            else
                Ok(Mirror text)
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
/// <summary>The likely cause of a <c>request_changed</c> refusal, when the request bytes differ from the locked ones
/// only by encoding: converting CRLF to LF, removing a leading UTF-8 byte order mark, or both, gives the locked
/// bytes.</summary>
[<RequireQualifiedAccess>]
type RequestChangeCause =
    | CrlfLineEndings
    | ByteOrderMark
    | CrlfLineEndingsAndByteOrderMark

module RequestChangeCause =
    let all =
        [ RequestChangeCause.CrlfLineEndings, "crlf-line-endings"
          RequestChangeCause.ByteOrderMark, "byte-order-mark"
          RequestChangeCause.CrlfLineEndingsAndByteOrderMark, "crlf-line-endings-and-byte-order-mark" ]

    let name (cause: RequestChangeCause) =
        all |> List.find (fun (candidate, _) -> candidate = cause) |> snd

[<RequireQualifiedAccess>]
type BindingProblem =
    /// Only <c>request_changed</c> may name a cause.
    | RequestChanged of cause: RequestChangeCause option
    | PlatformNotLocked of platform: Platform
    | LockChanged
    | ReceiptLockMismatch
    | LockRecipeMismatch
    | PlanChanged

module BindingProblem =
    let code (problem: BindingProblem) =
        match problem with
        | BindingProblem.RequestChanged _ -> BindingCode.RequestChanged
        | BindingProblem.PlatformNotLocked _ -> BindingCode.PlatformNotLocked
        | BindingProblem.LockChanged -> BindingCode.LockChanged
        | BindingProblem.ReceiptLockMismatch -> BindingCode.ReceiptLockMismatch
        | BindingProblem.LockRecipeMismatch -> BindingCode.LockRecipeMismatch
        | BindingProblem.PlanChanged -> BindingCode.PlanChanged

    /// The document whose member a binding code's path names.
    let document (code: BindingCode) =
        match code with
        | BindingCode.RequestChanged
        | BindingCode.PlatformNotLocked
        | BindingCode.LockRecipeMismatch -> DocumentKind.Lock
        | BindingCode.LockChanged
        | BindingCode.ReceiptLockMismatch -> DocumentKind.Receipt
        | BindingCode.PlanChanged -> DocumentKind.Result

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
    /// <c>document_unavailable</c>, always at the empty path.
    | DocumentUnavailable of document: DocumentKind
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
        | DiagnosticDetail.DocumentUnavailable _ -> Code.DocumentUnavailable
        | DiagnosticDetail.SelectionUnavailable _ -> Code.GenerationNotInstalled
        | DiagnosticDetail.LockBinding(problem, _) -> Code.Binding(BindingProblem.code problem)

type Diagnostic =
    { Detail: DiagnosticDetail
      Severity: Severity
      Message: NonBlankText
      /// A non-empty argument vector, such as <c>["dotnet", "tool", "update", "--global", "Morphir.Tool"]</c>.
      Instruction: Argv option }

/// Where a planned component comes from.
[<RequireQualifiedAccess>]
type ComponentSource =
    | RepositoryTarget of repositoryId: Sha256 * target: TargetPath * sha256: Sha256
    | RecipeDigest of repositoryId: Sha256 * target: TargetPath * sha256: Sha256
    | BuiltIn
    | ExternalOwner

/// <summary>How setup runs a component's code: the plan member <c>probe</c>. <c>none</c> names a component setup never
/// runs; no component of a <c>0.1.0-draft.1</c> setup or update plan is one, so a plan never states it.</summary>
[<RequireQualifiedAccess>]
type Probe =
    | None
    | InHost
    | ProcessWithUserRights

/// A planned provider's delivery. Its probe follows from it.
[<RequireQualifiedAccess>]
type PlannedDelivery =
    | BuiltIn
    | Process
    | Wasm
    | DotnetTool

module PlannedDelivery =
    let delivery (planned: PlannedDelivery) =
        match planned with
        | PlannedDelivery.BuiltIn -> Delivery.BuiltIn
        | PlannedDelivery.Process -> Delivery.Process
        | PlannedDelivery.Wasm -> Delivery.Wasm
        | PlannedDelivery.DotnetTool -> Delivery.DotnetTool

    let ofDelivery (delivery: Delivery) =
        match delivery with
        | Delivery.BuiltIn -> PlannedDelivery.BuiltIn
        | Delivery.Process -> PlannedDelivery.Process
        | Delivery.Wasm -> PlannedDelivery.Wasm
        | Delivery.DotnetTool -> PlannedDelivery.DotnetTool

/// <summary>A prerequisite of a planned component that applies on the plan's platform. A plan's prerequisites are
/// already filtered, so a planned prerequisite names no platforms.</summary>
type PlannedPrerequisite =
    { Kind: PrerequisiteKind
      Name: NonBlankText }

module PlannedPrerequisite =
    let ofPrerequisite (prerequisite: Prerequisite) : PlannedPrerequisite =
        { Kind = prerequisite.Kind
          Name = prerequisite.Name }

/// What setup would do for one component.
type ComponentPlan =
    { Version: SemVer
      Source: ComponentSource
      DownloadBytes: uint64
      InstalledBytes: uint64
      UpdateOwner: Ownership
      Prerequisites: PlannedPrerequisite list }

/// A planned component: the host, or a provider with its identity and delivery.
[<RequireQualifiedAccess>]
type PlannedComponent =
    | Host of implementation: Token * deployment: Token * plan: ComponentPlan
    | Provider of operation: Operation * extensionId: ExtensionId * delivery: PlannedDelivery * plan: ComponentPlan

module PlannedComponent =
    /// <summary>The component's probe. Readiness runs the staged host to check it and its built-ins, so the host is
    /// <c>process-with-user-rights</c>; so is every <c>process</c> and <c>dotnet-tool</c> guest, which installation
    /// or readiness runs. A built-in or WASM provider runs inside the host.</summary>
    let probe (planned: PlannedComponent) =
        match planned with
        | PlannedComponent.Host _
        | PlannedComponent.Provider(_, _, PlannedDelivery.Process, _)
        | PlannedComponent.Provider(_, _, PlannedDelivery.DotnetTool, _) -> Probe.ProcessWithUserRights
        | PlannedComponent.Provider(_, _, PlannedDelivery.BuiltIn, _)
        | PlannedComponent.Provider(_, _, PlannedDelivery.Wasm, _) -> Probe.InHost

    let plan (planned: PlannedComponent) =
        match planned with
        | PlannedComponent.Host(_, _, plan)
        | PlannedComponent.Provider(_, _, _, plan) -> plan

[<RequireQualifiedAccess>]
type MetadataRefresh =
    | Required
    | NotRequired

/// <summary>A setup or update plan: everything the command will do and write, so that its digest pins all of it,
/// including <c>lock</c>, the complete lock the command writes when it succeeds.</summary>
type Plan =
    { Implementation: Token
      Deployment: Token
      Platform: Platform
      Recipe: RecipeReference
      MetadataRefresh: MetadataRefresh
      /// What the plan installs for <c>Platform</c>: the host first, then one provider per provider of its closure.
      Components: PlannedComponent list
      /// Every platform's closure, not only <c>Platform</c>'s.
      Lock: Lock }
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

/// <summary>A manager result, the one document <c>morphirup --json</c> writes to standard output. Only
/// <c>ResultDocument.create</c> and the reader make one, so its outcome always agrees with its diagnostics and its
/// plan.</summary>
type ManagerResult =
    private
        { command: Command
          outcome: Outcome
          plan: DigestedPlan option
          selection: Selection option
          diagnostics: Diagnostic list }

    member this.Command = this.command
    member this.Outcome = this.outcome
    member this.Plan = this.plan
    member this.Selection = this.selection
    member this.Diagnostics = this.diagnostics
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
            let! role = required "role" (tag [ "host", RoleTag.Host; "provider", RoleTag.Provider ]) o

            match role with
            | RoleTag.Host ->
                let! implementation = required "implementation" token o
                let! deployment = required "deployment" token o
                return Component.Host(implementation, deployment)
            | RoleTag.Provider ->
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

    let private ownership = ReceiptDocument.ownership

    let private selectionSource (n: Node) : Read<SelectionSource> =
        result {
            let! o = asObject n
            let! kind =
                required
                    "kind"
                    (tag
                        [ "cli", SelectionTag.Cli
                          "environment", SelectionTag.Environment
                          "project", SelectionTag.Project
                          "user-default", SelectionTag.UserDefault ])
                    o

            match kind with
            | SelectionTag.Cli -> return SelectionSource.Cli
            | SelectionTag.Environment -> return SelectionSource.Environment
            | SelectionTag.Project ->
                let! path = required "path" (validated (LocalPath.tryParse PathSyntax.Any)) o
                return SelectionSource.Project path
            | SelectionTag.UserDefault -> return SelectionSource.UserDefault
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
                        |> Result.bind (fun args -> Argv.tryCreate args |> Result.mapError (fun code -> { Code = code; Path = i.Path })))
                    o

            let abiPath = n.Path.Child "abi"
            let platformPath = n.Path.Child "platform"
            let causePath = n.Path.Child "cause"

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
                        return DiagnosticDetail.UnsupportedContract(readerCode, document, DocumentPath.verbatim path)
                    }
                | Code.DocumentUnavailable ->
                    result {
                        let! document = required "document" (tag (DocumentKind.all |> List.map (fun (kind, name) -> name, kind))) o
                        let! path = required "path" text o

                        // The document as a whole is unavailable, so the path is the empty path.
                        if path <> "" then
                            return! refuse ReaderCode.InconsistentDocument (n.Path.Child "path")

                        return DiagnosticDetail.DocumentUnavailable document
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
                            | BindingCode.RequestChanged, None ->
                                optional "cause" (tag (RequestChangeCause.all |> List.map (fun (cause, name) -> name, cause))) o
                                |> Result.map BindingProblem.RequestChanged
                            | BindingCode.LockChanged, None -> Ok BindingProblem.LockChanged
                            | BindingCode.ReceiptLockMismatch, None -> Ok BindingProblem.ReceiptLockMismatch
                            | BindingCode.LockRecipeMismatch, None -> Ok BindingProblem.LockRecipeMismatch
                            | BindingCode.PlanChanged, None -> Ok BindingProblem.PlanChanged

                        // Only request_changed has a cause.
                        if bindingCode <> BindingCode.RequestChanged && has "cause" o then
                            return! refuse ReaderCode.InconsistentDocument causePath

                        return DiagnosticDetail.LockBinding(problem, DocumentPath.verbatim path)
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
                required
                    "kind"
                    (tag
                        [ "repository-target", ComponentSourceTag.RepositoryTarget
                          "recipe-digest", ComponentSourceTag.RecipeDigest
                          "built-in", ComponentSourceTag.BuiltIn
                          "external-owner", ComponentSourceTag.ExternalOwner ])
                    o

            let bound () =
                result {
                    let! repositoryId = required "repositoryId" sha256 o
                    let! target = required "target" targetPath o
                    let! sha = required "sha256" sha256 o
                    return repositoryId, target, sha
                }

            match kind with
            | ComponentSourceTag.RepositoryTarget ->
                let! repositoryId, target, sha = bound ()
                return ComponentSource.RepositoryTarget(repositoryId, target, sha)
            | ComponentSourceTag.RecipeDigest ->
                let! repositoryId, target, sha = bound ()
                return ComponentSource.RecipeDigest(repositoryId, target, sha)
            | ComponentSourceTag.BuiltIn -> return ComponentSource.BuiltIn
            | ComponentSourceTag.ExternalOwner -> return ComponentSource.ExternalOwner
        }

    /// A plan prerequisite: already filtered to the plan's platform, so one that names platforms is inconsistent.
    let private plannedPrerequisite (n: Node) : Read<PlannedPrerequisite> =
        prerequisite n
        |> Result.bind (fun read ->
            match read.Platforms with
            | Some _ -> refuse ReaderCode.InconsistentDocument (n.Path.Child "platforms")
            | None -> Ok(PlannedPrerequisite.ofPrerequisite read))

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
            let! prerequisites = required "prerequisites" (array plannedPrerequisite) o

            let! probe =
                required "probe" (tag [ "none", Probe.None; "in-host", Probe.InHost; "process-with-user-rights", Probe.ProcessWithUserRights ]) o

            let plan =
                { Version = version
                  Source = source
                  DownloadBytes = downloadBytes
                  InstalledBytes = installedBytes
                  UpdateOwner = updateOwner
                  Prerequisites = prerequisites }

            let inconsistent name = refuse ReaderCode.InconsistentDocument (n.Path.Child name)

            let! planned =
                match on, delivery, extensionId with
                | Component.Host _, Some _, _ -> inconsistent "delivery"
                | Component.Host _, None, Some _ -> inconsistent "extensionId"
                | Component.Host(implementation, deployment), None, None -> Ok(PlannedComponent.Host(implementation, deployment, plan))
                | Component.Provider _, None, _ -> inconsistent "delivery"
                | Component.Provider _, Some _, None -> inconsistent "extensionId"
                | Component.Provider operation, Some delivery, Some id ->
                    Ok(PlannedComponent.Provider(operation, id, PlannedDelivery.ofDelivery delivery, plan))

            // The probe follows from the component; a plan states it so that its digest covers it.
            if probe <> PlannedComponent.probe planned then
                return! inconsistent "probe"

            return planned
        }

    /// <summary>The plan against its lock (manager result page, plans): the lock is for the plan's recipe, host and
    /// platform, and the components are exactly that platform's closure, from the recipe's repository.</summary>
    let private agreesWithLock (path: DocumentPath) (plan: Plan) : Read<unit> =
        let lockPath = path.Child "lock"
        let componentsPath = path.Child "components"
        let lock = plan.Lock

        result {
            if lock.Recipe <> plan.Recipe then
                return! refuse ReaderCode.InconsistentDocument (lockPath.Child "recipe")

            if lock.Host.Implementation <> plan.Implementation || lock.Host.Deployment <> plan.Deployment then
                return! refuse ReaderCode.InconsistentDocument (lockPath.Child "host")

            let! closure =
                Lock.closure plan.Platform lock
                |> Option.map Ok
                |> Option.defaultWith (fun () -> refuse ReaderCode.InconsistentDocument (lockPath.Child "platforms"))

            let hosts = plan.Components |> List.filter (function PlannedComponent.Host _ -> true | _ -> false)

            let operations =
                plan.Components
                |> List.choose (function
                    | PlannedComponent.Provider(operation, _, _, _) -> Some operation
                    | PlannedComponent.Host _ -> None)

            if
                List.length hosts <> 1
                || List.length operations <> List.length closure.Providers
                || set operations <> set (closure.Providers |> List.map _.Operation)
            then
                return! refuse ReaderCode.InconsistentDocument componentsPath

            // A bound source names the expected bytes, from the recipe's repository.
            let boundTo (at: DocumentPath) (expected: TargetBinding) (source: ComponentSource) =
                match source with
                | ComponentSource.RepositoryTarget(repositoryId, target, sha)
                | ComponentSource.RecipeDigest(repositoryId, target, sha) ->
                    if target <> expected.Target || sha <> expected.Sha256 then
                        refuse ReaderCode.InconsistentDocument (at.Child "source")
                    elif repositoryId <> plan.Recipe.RepositoryId then
                        refuse ReaderCode.InconsistentDocument ((at.Child "source").Child "repositoryId")
                    else
                        Ok()
                | ComponentSource.ExternalOwner -> Ok()
                | ComponentSource.BuiltIn -> refuse ReaderCode.InconsistentDocument (at.Child "source")

            for index, planned in List.indexed plan.Components do
                let at = componentsPath.At index

                match planned with
                | PlannedComponent.Host(_, _, host) ->
                    if host.Version <> lock.Host.Version then
                        return! refuse ReaderCode.InconsistentDocument at

                    do! boundTo at closure.Host host.Source
                | PlannedComponent.Provider(operation, id, delivery, provider) ->
                    let locked = closure.Providers |> List.find (fun p -> p.Operation = operation)

                    if
                        id <> locked.ExtensionId
                        || provider.Version <> locked.Version
                        || PlannedDelivery.delivery delivery <> LockedProvider.delivery locked
                    then
                        return! refuse ReaderCode.InconsistentDocument at

                    match locked.Delivery with
                    | LockedDelivery.BuiltIn _ ->
                        if provider.Source <> ComponentSource.BuiltIn then
                            return! refuse ReaderCode.InconsistentDocument (at.Child "source")
                    | LockedDelivery.Process(_, artifact, _)
                    | LockedDelivery.Wasm(_, _, artifact, _) -> do! boundTo at artifact provider.Source
                    | LockedDelivery.DotnetTool(_, package, _) ->
                        do! boundTo at { Target = package.Target; Sha256 = package.Sha256 } provider.Source
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
            let! lock = required "lock" LockDocument.readNested o

            let plan =
                { Implementation = implementation
                  Deployment = deployment
                  Platform = platform
                  Recipe = recipe
                  MetadataRefresh = metadataRefresh
                  Components = components
                  Lock = lock }

            do! agreesWithLock n.Path plan
            return plan
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
                { command = command
                  outcome = outcome
                  plan = digested
                  selection = selection
                  diagnostics = diagnostics }
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

    let private prerequisiteMembers (kind: PrerequisiteKind) (name: NonBlankText) (platforms: Platform list option) =
        let versions (comparators: Comparator list) =
            "versions", Value.Array(comparators |> List.map (fun c -> Value.String c.Text))

        Value.Object
            [ match kind with
              | PrerequisiteKind.Runtime _ -> "kind", Value.String "runtime"
              | PrerequisiteKind.Sdk _ -> "kind", Value.String "sdk"
              | PrerequisiteKind.Native _ -> "kind", Value.String "native"
              "name", Value.String name.Text
              match kind with
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
              match platforms with
              | Some platforms -> "platforms", Value.Array(platforms |> List.map (Platform.name >> Value.String))
              | None -> () ]

    let internal prerequisiteValue (prerequisite: Prerequisite) =
        prerequisiteMembers prerequisite.Kind prerequisite.Name prerequisite.Platforms

    let private plannedPrerequisiteValue (prerequisite: PlannedPrerequisite) =
        prerequisiteMembers prerequisite.Kind prerequisite.Name None

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
        let probe = PlannedComponent.probe planned

        let common (plan: ComponentPlan) =
            [ "source", sourceValue plan.Source
              "downloadBytes", Value.Number(string plan.DownloadBytes)
              "installedBytes", Value.Number(string plan.InstalledBytes)
              "updateOwner", ReceiptDocument.ownershipValue plan.UpdateOwner
              "prerequisites", Value.Array(plan.Prerequisites |> List.map plannedPrerequisiteValue)
              "probe",
              Value.String(
                  match probe with
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
                  "delivery", Value.String(Delivery.name (PlannedDelivery.delivery delivery))
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
              "components", Value.Array(plan.Components |> List.map plannedValue)
              "lock", LockDocument.value plan.Lock ]

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
            | DiagnosticDetail.DocumentUnavailable document ->
                [ "document", Value.String(DocumentKind.name document)
                  "path", Value.String "" ]
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
              | Some args -> "instruction", Value.Array(args.Items |> List.map Value.String)
              | None -> ()
              // After the instruction, in the member order of the request_changed fixture.
              match diagnostic.Detail with
              | DiagnosticDetail.LockBinding(BindingProblem.RequestChanged(Some cause), _) ->
                  "cause", Value.String(RequestChangeCause.name cause)
              | _ -> () ]

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
                { command = command
                  outcome = outcome
                  plan = plan
                  selection = selection
                  diagnostics = diagnostics }
        else
            Error "the outcome contradicts the diagnostics or the plan"
