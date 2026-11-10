// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// A locked provider's delivery with exactly the identities that delivery requires.
[<RequireQualifiedAccess>]
type LockedDelivery =
    /// The model the built-in writes (compile) or reads (generate); the operation names the member.
    | BuiltIn of model: ModelFormat
    | Process of release: ReleaseBinding * artifact: TargetBinding * protocol: Protocol
    | Wasm of abi: Token * release: ReleaseBinding * artifact: TargetBinding * protocol: Protocol
    /// The release's extension descriptor, whose claims describe the guest, and the package bytes.
    | DotnetTool of release: ReleaseBinding * package: PackageBinding * protocol: Protocol

/// One provider of a platform closure.
type LockedProvider =
    { Operation: Operation
      ExtensionId: ExtensionId
      Version: SemVer
      Delivery: LockedDelivery }

module LockedProvider =
    let delivery (provider: LockedProvider) =
        match provider.Delivery with
        | LockedDelivery.BuiltIn _ -> Delivery.BuiltIn
        | LockedDelivery.Process _ -> Delivery.Process
        | LockedDelivery.Wasm _ -> Delivery.Wasm
        | LockedDelivery.DotnetTool _ -> Delivery.DotnetTool

/// One platform's complete dependency closure: the host artifact and one provider per required operation.
type Closure =
    { Host: TargetBinding
      Providers: LockedProvider list }

/// Why a set of closures cannot make a lock.
[<RequireQualifiedAccess>]
type ClosureProblem =
    | NoPlatform
    | RepeatedPlatform of platform: Platform
    | NoProvider of platform: Platform
    | RepeatedOperation of platform: Platform * index: int
    /// Closures that cover different operations from each other.
    | DifferentOperations

/// <summary>A lock's closures: at least one, one per platform in platform table order, each with one provider per
/// operation, all covering the same operations. Only <see cref="M:Morphir.Toolchain.ClosuresModule.tryCreate"/>
/// makes one.</summary>
type Closures =
    private
    | Closures of (Platform * Closure) list

    member this.Items =
        let (Closures items) = this
        items

module Closures =
    let tryCreate (closures: (Platform * Closure) list) : Result<Closures, ClosureProblem> =
        let ordered = closures |> List.sortBy (fst >> Platform.order)

        let repeated (key: 'T -> 'K) (items: 'T list) =
            items
            |> List.indexed
            |> List.tryFind (fun (index, item) -> items |> List.take index |> List.exists (fun earlier -> key earlier = key item))

        result {
            if List.isEmpty ordered then
                return! Error ClosureProblem.NoPlatform

            match repeated fst ordered with
            | Some(_, (platform, _)) -> return! Error(ClosureProblem.RepeatedPlatform platform)
            | None -> ()

            for platform, closure in ordered do
                if List.isEmpty closure.Providers then
                    return! Error(ClosureProblem.NoProvider platform)

                match repeated _.Operation closure.Providers with
                | Some(index, _) -> return! Error(ClosureProblem.RepeatedOperation(platform, index))
                | None -> ()

            let operationSets = ordered |> List.map (fun (_, closure) -> closure.Providers |> List.map _.Operation |> set) |> List.distinct

            if List.length operationSets > 1 then
                return! Error ClosureProblem.DifferentOperations

            return Closures ordered
        }

/// The exact host, the same on every platform.
type HostIdentity =
    { Implementation: Token
      Version: SemVer
      Deployment: Token }

/// The authenticated recipe a lock was resolved from.
type RecipeReference =
    { /// SHA-256 of the canonical initial root <c>signed</c> object, as in tool release metadata v1.
      RepositoryId: Sha256
      Target: TargetPath
      Sha256: Sha256 }

/// <summary>A portable lock, <c>.morphir/toolchain.lock.json</c>: the exact resolution of a request.</summary>
type Lock =
    { RequestSha256: Sha256
      Recipe: RecipeReference
      Host: HostIdentity
      /// At least one closure, in platform table order.
      Platforms: Closures
      /// The SHA-256 of the exact lock bytes, which a receipt binds. A lock built in memory has the digest of its
      /// written bytes.
      Identity: Sha256 }

module Lock =
    let closure (platform: Platform) (lock: Lock) =
        lock.Platforms.Items |> List.tryFind (fst >> (=) platform) |> Option.map snd

/// Shared reading and writing of locked providers, also used by receipts.
module LockedProviderDocument =
    open Reading

    let private deliveries =
        Delivery.all |> List.map (fun (delivery, name) -> name, delivery)

    let private release (o: ObjectNode) : Read<ReleaseBinding> =
        result {
            let! repositoryId = required "repositoryId" sha256 o
            let! descriptorTarget = required "descriptorTarget" targetPath o
            let! descriptorSha256 = required "descriptorSha256" sha256 o

            return
                { RepositoryId = repositoryId
                  DescriptorTarget = descriptorTarget
                  DescriptorSha256 = descriptorSha256 }
        }

    let private artifact (o: ObjectNode) : Read<TargetBinding> =
        result {
            let! target = required "artifactTarget" targetPath o
            let! sha = required "artifactSha256" sha256 o
            return { Target = target; Sha256 = sha }
        }

    let internal read (n: Node) : Read<LockedProvider> =
        result {
            let! o = asObject n
            let! operation = operation o
            let! delivery = required "delivery" (tag deliveries) o
            let! extensionId = required "extensionId" extensionId o
            let! version = required "version" version o

            let! locked =
                match delivery with
                | Delivery.BuiltIn -> builtInModel operation o |> Result.map LockedDelivery.BuiltIn
                | Delivery.Process ->
                    result {
                        let! release = release o
                        let! artifact = artifact o
                        let! protocol = protocol operation o
                        return LockedDelivery.Process(release, artifact, protocol)
                    }
                | Delivery.Wasm ->
                    result {
                        let! abi = required "abi" token o
                        let! release = release o
                        let! artifact = artifact o
                        let! protocol = protocol operation o
                        return LockedDelivery.Wasm(abi, release, artifact, protocol)
                    }
                | Delivery.DotnetTool ->
                    result {
                        let! release = release o
                        let! packageId = required "packageId" packageId o
                        let! command = required "command" token o
                        let! target = required "packageTarget" targetPath o
                        let! sha = required "packageSha256" sha256 o
                        let! protocol = protocol operation o

                        let package =
                            { PackageId = packageId
                              Command = command
                              Target = target
                              Sha256 = sha }

                        return LockedDelivery.DotnetTool(release, package, protocol)
                    }

            return
                { Operation = operation
                  ExtensionId = extensionId
                  Version = version
                  Delivery = locked }
        }

    let internal operationMembers (operation: Operation) =
        match operation with
        | Operation.Compile language -> [ "operation", Value.String "compile"; "language", Value.String language.Text ]
        | Operation.Generate target -> [ "operation", Value.String "generate"; "target", Value.String target.Text ]

    let internal modelFormatValue (input: ModelFormat) =
        Value.Object
            [ "profile", Value.String input.Profile.Text
              "codec", Value.String input.Codec.Text
              "irVersion", Value.String input.IrVersion.Text ]

    let internal protocolMembers (protocol: Protocol) =
        let tuple = protocol.Tuple

        [ "protocolVersion", Value.String protocol.Version.Text
          "modelTuple",
          Value.Object
              [ "operation", Value.String(ModelOperation.text tuple.Operation)
                "profile", Value.String tuple.Profile.Text
                "codec", Value.String tuple.Codec.Text
                "envelopeVersion", Value.String tuple.EnvelopeVersion.Text
                "irVersion", Value.String tuple.IrVersion.Text ] ]

    let private releaseMembers (release: ReleaseBinding) =
        [ "repositoryId", Value.String release.RepositoryId.Text
          "descriptorTarget", Value.String release.DescriptorTarget.Text
          "descriptorSha256", Value.String release.DescriptorSha256.Text ]

    let private artifactMembers (artifact: TargetBinding) =
        [ "artifactTarget", Value.String artifact.Target.Text
          "artifactSha256", Value.String artifact.Sha256.Text ]

    /// The provider in its locked form, in the member order of the approved lock example.
    let value (provider: LockedProvider) =
        Value.Object
            [ yield! operationMembers provider.Operation
              "delivery", Value.String(Delivery.name (LockedProvider.delivery provider))
              "extensionId", Value.String provider.ExtensionId.Text
              "version", Value.String provider.Version.Text
              match provider.Delivery with
              | LockedDelivery.BuiltIn model -> builtInModelMember provider.Operation, modelFormatValue model
              | LockedDelivery.Process(release, artifact, protocol) ->
                  yield! releaseMembers release
                  yield! artifactMembers artifact
                  yield! protocolMembers protocol
              | LockedDelivery.Wasm(abi, release, artifact, protocol) ->
                  "abi", Value.String abi.Text
                  yield! releaseMembers release
                  yield! artifactMembers artifact
                  yield! protocolMembers protocol
              | LockedDelivery.DotnetTool(release, package, protocol) ->
                  yield! releaseMembers release
                  "packageId", Value.String package.PackageId.Text
                  "command", Value.String package.Command.Text
                  "packageTarget", Value.String package.Target.Text
                  "packageSha256", Value.String package.Sha256.Text
                  yield! protocolMembers protocol ]

/// The portable lock reader and writer (toolchain-lock page).
module LockDocument =
    open Reading

    let private providerMembers =
        [ "operation"
          "language"
          "target"
          "delivery"
          "extensionId"
          "version"
          "abi"
          "repositoryId"
          "descriptorTarget"
          "descriptorSha256"
          "artifactTarget"
          "artifactSha256"
          "packageId"
          "command"
          "packageTarget"
          "packageSha256"
          "protocolVersion"
          "modelTuple"
          "modelTuple.operation"
          "modelTuple.profile"
          "modelTuple.codec"
          "modelTuple.envelopeVersion"
          "modelTuple.irVersion"
          "modelInput"
          "modelInput.profile"
          "modelInput.codec"
          "modelInput.irVersion"
          "modelOutput"
          "modelOutput.profile"
          "modelOutput.codec"
          "modelOutput.irVersion" ]

    /// The critical paths a 0.1.0-draft.1 lock reader understands.
    let understoodCriticalPaths =
        set
            [ "schemaVersion"
              "critical"
              "requestSha256"
              "recipe"
              "recipe.repositoryId"
              "recipe.target"
              "recipe.sha256"
              "host"
              "host.implementation"
              "host.version"
              "host.deployment"
              "platforms"
              "platforms.*"
              "platforms.*.hostTarget"
              "platforms.*.hostSha256"
              "platforms.*.providers"
              yield! providerMembers |> List.map (fun name -> "platforms.*.providers." + name) ]

    let internal recipeReference (n: Node) : Read<RecipeReference> =
        result {
            let! o = asObject n
            let! repositoryId = required "repositoryId" sha256 o
            let! target = required "target" targetPath o
            let! sha = required "sha256" sha256 o

            return
                { RepositoryId = repositoryId
                  Target = target
                  Sha256 = sha }
        }

    let private host (n: Node) : Read<HostIdentity> =
        result {
            let! o = asObject n
            let! implementation = required "implementation" token o
            let! version = required "version" version o
            let! deployment = required "deployment" token o

            return
                { Implementation = implementation
                  Version = version
                  Deployment = deployment }
        }

    let private closure (_: Platform) (n: Node) : Read<Closure> =
        result {
            let! o = asObject n
            let! hostTarget = required "hostTarget" targetPath o
            let! hostSha256 = required "hostSha256" sha256 o
            let providersPath = n.Path.Child "providers"
            let! providers = required "providers" (array LockedProviderDocument.read) o

            if List.isEmpty providers then
                return! refuse ReaderCode.InconsistentDocument providersPath

            for index, provider in List.indexed providers do
                if providers |> List.take index |> List.exists (fun earlier -> earlier.Operation = provider.Operation) then
                    return! refuse ReaderCode.InconsistentDocument (providersPath.At index)

            return
                { Host = { Target = hostTarget; Sha256 = hostSha256 }
                  Providers = providers }
        }

    let private body (identity: Sha256) (o: ObjectNode) : Read<Lock> =
        result {
            let! requestSha256 = required "requestSha256" sha256 o
            let! recipe = required "recipe" recipeReference o
            let! host = required "host" host o
            let platformsPath = o.Path.Child "platforms"
            let! platforms = required "platforms" (platformMap closure) o

            // Each closure was checked as it was read; what remains relates the closures to each other.
            let! platforms =
                Closures.tryCreate platforms
                |> Result.mapError (fun problem ->
                    match problem with
                    | ClosureProblem.NoProvider platform ->
                        { Code = ReaderCode.InconsistentDocument; Path = (platformsPath.Child(Platform.name platform)).Child "providers" }
                    | ClosureProblem.RepeatedOperation(platform, index) ->
                        { Code = ReaderCode.InconsistentDocument; Path = ((platformsPath.Child(Platform.name platform)).Child "providers").At index }
                    | ClosureProblem.NoPlatform
                    | ClosureProblem.RepeatedPlatform _
                    | ClosureProblem.DifferentOperations -> { Code = ReaderCode.InconsistentDocument; Path = platformsPath })

            return
                { RequestSha256 = requestSha256
                  Recipe = recipe
                  Host = host
                  Platforms = platforms
                  Identity = identity }
        }

    /// Reads the exact bytes of a lock.
    let read (bytes: byte array) : Result<Lock, ReaderRefusal> =
        document Syntax.parseJson understoodCriticalPaths (body (Sha256.ofBytes bytes)) bytes

    let internal recipeReferenceValue (recipe: RecipeReference) =
        Value.Object
            [ "repositoryId", Value.String recipe.RepositoryId.Text
              "target", Value.String recipe.Target.Text
              "sha256", Value.String recipe.Sha256.Text ]

    /// The lock as a document value, in the member order of the approved example. It names <c>host</c> and
    /// <c>platforms</c> as critical, as that example does.
    let value (lock: Lock) =
        Value.Object
            [ "schemaVersion", Value.String ContractVersion.current
              "critical", Value.Array [ Value.String "host"; Value.String "platforms" ]
              "requestSha256", Value.String lock.RequestSha256.Text
              "recipe", recipeReferenceValue lock.Recipe
              "host",
              Value.Object
                  [ "implementation", Value.String lock.Host.Implementation.Text
                    "version", Value.String lock.Host.Version.Text
                    "deployment", Value.String lock.Host.Deployment.Text ]
              "platforms",
              Value.Object
                  [ for platform, closure in lock.Platforms.Items ->
                        Platform.name platform,
                        Value.Object
                            [ "hostTarget", Value.String closure.Host.Target.Text
                              "hostSha256", Value.String closure.Host.Sha256.Text
                              "providers", Value.Array(closure.Providers |> List.map LockedProviderDocument.value) ] ] ]

    /// The bytes a manager writes for a lock.
    let write (lock: Lock) = JsonText.toBytes (value lock)

    /// <summary>A lock nested in another document, such as a plan's <c>lock</c>: the same checks, with refusals under
    /// its path. It has no bytes of its own, so its identity is that of the lock it would be written as.</summary>
    let internal readNested (n: Node) : Read<Lock> =
        envelopeAt understoodCriticalPaths n
        |> Result.bind (body (Sha256.ofBytes [||]))
        |> Result.map (fun lock -> { lock with Identity = Sha256.ofBytes (write lock) })
