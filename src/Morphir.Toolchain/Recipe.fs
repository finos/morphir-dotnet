// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// An authenticated target bound by exact digest.
type TargetBinding = { Target: TargetPath; Sha256: Sha256 }

/// How a host offering is published.
[<RequireQualifiedAccess>]
type HostSource =
    /// A tool release metadata v1 tool id; each separately published offering has its own.
    | ToolRelease of toolId: ToolId
    /// A .NET tool package and the command it installs.
    | DotnetTool of packageId: PackageId * command: Token

/// How a host offering runs.
[<RequireQualifiedAccess>]
type Execution =
    | NativeAot
    | Jit

/// A provider compiled into a host.
type BuiltIn =
    { Operation: Operation
      ExtensionId: ExtensionId
      Version: SemVer
      ModelInput: ModelInput }

/// A host offering: one implementation, release and deployment, with its artifact for each supported platform.
type HostOffering =
    { Implementation: Token
      Version: SemVer
      Deployment: Token
      Execution: Execution
      Source: HostSource
      ProtocolVersions: ProtocolVersion list
      WasmAbis: Token list
      Prerequisites: Prerequisite list
      BuiltIns: BuiltIn list
      /// The artifact per platform, in platform table order. Never empty; never a musl platform for a tool release.
      Platforms: (Platform * TargetBinding) list }

/// The extension release descriptor a provider binds, in the repository that publishes it.
type ReleaseBinding =
    { RepositoryId: Sha256
      DescriptorTarget: TargetPath
      DescriptorSha256: Sha256 }

/// A .NET tool package binding.
type PackageBinding =
    { PackageId: PackageId
      Command: Token
      Target: TargetPath
      Sha256: Sha256 }

/// The deliveries an independently released provider can have, with the identities each requires.
[<RequireQualifiedAccess>]
type RecipeDelivery =
    /// One artifact per platform, in platform table order, at least one.
    | Process of platforms: (Platform * TargetBinding) list
    /// One portable artifact for one ABI.
    | Wasm of abi: Token * artifact: TargetBinding
    /// A .NET tool package, which has no platform artifacts.
    | DotnetTool of package: PackageBinding

/// An independently released provider.
type RecipeProvider =
    { Operation: Operation
      ExtensionId: ExtensionId
      Version: SemVer
      Release: ReleaseBinding
      Protocol: Protocol
      Prerequisites: Prerequisite list
      Delivery: RecipeDelivery }

module RecipeProvider =
    let delivery (provider: RecipeProvider) =
        match provider.Delivery with
        | RecipeDelivery.Process _ -> Delivery.Process
        | RecipeDelivery.Wasm _ -> Delivery.Wasm
        | RecipeDelivery.DotnetTool _ -> Delivery.DotnetTool

    let abi (provider: RecipeProvider) =
        match provider.Delivery with
        | RecipeDelivery.Wasm(abi, _) -> Some abi
        | _ -> None

/// A toolset recipe, as read from its exact bytes.
type Recipe =
    { Preset: Token
      Version: SemVer
      /// The required operations, in workflow order.
      Operations: Operation list
      Hosts: HostOffering list
      /// Independently released providers, in preference order.
      Providers: RecipeProvider list
      /// The SHA-256 of the exact recipe bytes.
      Sha256: Sha256 }

/// Why a provider does not fit a host offering.
[<RequireQualifiedAccess>]
type Misfit =
    /// A WASM provider whose ABI the host does not list in <c>wasmAbis</c>.
    | AbiUnavailable of abi: Token
    /// The host does not speak the provider's MEP version.
    | Protocol
    /// A process provider without an artifact for the platform.
    | NoArtifact

/// Whether providers fit host offerings (toolset recipe page, consistency rules).
module Fit =
    /// <summary>The first reason a provider does not fit an offering on a platform, or <c>None</c> when it fits: the
    /// host speaks its MEP version, a process provider has an artifact for the platform, and a WASM provider's ABI is
    /// one the host lists. A <c>dotnet-tool</c> provider has no platform artifacts, so only the MEP version decides.
    /// A missing ABI is reported before any other reason.</summary>
    let onPlatform (offering: HostOffering) (platform: Platform) (provider: RecipeProvider) =
        match provider.Delivery with
        | RecipeDelivery.Wasm(abi, _) when not (List.contains abi offering.WasmAbis) -> Some(Misfit.AbiUnavailable abi)
        | _ when not (List.contains (Protocol.version provider.Protocol) offering.ProtocolVersions) -> Some Misfit.Protocol
        | RecipeDelivery.Process platforms when not (platforms |> List.exists (fst >> (=) platform)) -> Some Misfit.NoArtifact
        | _ -> None

    /// Fit without a platform: a process provider fits when it has an artifact for at least one platform the offering
    /// also lists.
    let onSomePlatform (offering: HostOffering) (provider: RecipeProvider) =
        offering.Platforms |> List.exists (fun (platform, _) -> (onPlatform offering platform provider).IsNone)

/// The toolset recipe reader (toolset-recipe page).
module RecipeDocument =
    open Reading

    let private hostMembers =
        [ "implementation"
          "version"
          "deployment"
          "execution"
          "source"
          "protocolVersions"
          "wasmAbis"
          "prerequisites"
          "builtIns"
          "platforms" ]

    let private providerMembers =
        [ "operation"
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
          "modelInput"
          "prerequisites"
          "platforms" ]

    /// The critical paths a 0.1.0-draft.1 recipe reader understands.
    let understoodCriticalPaths =
        set
            [ "schemaVersion"
              "critical"
              "preset"
              "version"
              "operations"
              "hosts"
              yield! hostMembers |> List.map (fun name -> "hosts." + name)
              "hosts.platforms.*"
              "hosts.platforms.*.target"
              "hosts.platforms.*.sha256"
              "providers"
              yield! providerMembers |> List.map (fun name -> "providers." + name)
              "providers.platforms.*"
              "providers.platforms.*.artifactTarget"
              "providers.platforms.*.artifactSha256" ]

    let private operationObject (n: Node) = asObject n |> Result.bind operation

    let private builtIn (n: Node) : Read<BuiltIn> =
        result {
            let! o = asObject n
            let! operation = operation o
            let! extensionId = required "extensionId" extensionId o
            let! version = required "version" version o
            let! modelInput = required "modelInput" modelInput o

            return
                { Operation = operation
                  ExtensionId = extensionId
                  Version = version
                  ModelInput = modelInput }
        }

    let private targetBinding targetName shaName (n: Node) : Read<TargetBinding> =
        result {
            let! o = asObject n
            let! target = required targetName targetPath o
            let! sha256 = required shaName sha256 o
            return { Target = target; Sha256 = sha256 }
        }

    let private source (n: Node) : Read<HostSource> =
        result {
            let! o = asObject n
            let! kind = required "kind" (tag [ "tool-release", true; "dotnet-tool", false ]) o

            if kind then
                let! toolId = required "toolId" (validated ToolId.tryParse) o
                return HostSource.ToolRelease toolId
            else
                let! packageId = required "packageId" packageId o
                let! command = required "command" token o
                return HostSource.DotnetTool(packageId, command)
        }

    let private nonEmptyPlatforms (n: Node) (platforms: (Platform * 'T) list) : Read<(Platform * 'T) list> =
        if List.isEmpty platforms then refuse ReaderCode.InconsistentDocument n.Path else Ok platforms

    let private host (n: Node) : Read<HostOffering> =
        result {
            let! o = asObject n
            let! implementation = required "implementation" token o
            let! version = required "version" version o
            let! deployment = required "deployment" token o
            let! execution = required "execution" (tag [ "native-aot", Execution.NativeAot; "jit", Execution.Jit ]) o
            let! source = required "source" source o
            let! protocolVersions = required "protocolVersions" (array (validated ProtocolVersion.tryParse)) o
            let! wasmAbis = optional "wasmAbis" (array token) o
            let! prerequisites = optional "prerequisites" (array prerequisite) o
            let! builtIns = optional "builtIns" (array builtIn) o

            let platformEntry (platform: Platform) (entry: Node) =
                match source with
                | HostSource.ToolRelease _ when Platform.isMusl platform ->
                    // A tool release metadata v1 platform states no C library.
                    refuse ReaderCode.InconsistentDocument entry.Path
                | _ -> targetBinding "target" "sha256" entry

            let! platforms =
                required "platforms" (fun p -> platformMap platformEntry p |> Result.bind (nonEmptyPlatforms p)) o

            return
                { Implementation = implementation
                  Version = version
                  Deployment = deployment
                  Execution = execution
                  Source = source
                  ProtocolVersions = protocolVersions
                  WasmAbis = defaultArg wasmAbis []
                  Prerequisites = defaultArg prerequisites []
                  BuiltIns = defaultArg builtIns []
                  Platforms = platforms }
        }

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

    let private provider (n: Node) : Read<RecipeProvider> =
        result {
            let! o = asObject n
            let! operation = operation o

            let! delivery =
                required "delivery" (tag [ "process", Delivery.Process; "wasm", Delivery.Wasm; "dotnet-tool", Delivery.DotnetTool ]) o

            let! extensionId = required "extensionId" extensionId o
            let! version = required "version" version o
            let! release = release o

            let! recipeDelivery =
                match delivery with
                | Delivery.Process ->
                    let platformsPath = n.Path.Child "platforms"

                    match tryMember "platforms" o with
                    | None -> refuse ReaderCode.InconsistentDocument platformsPath
                    | Some platforms ->
                        platformMap (fun _ entry -> targetBinding "artifactTarget" "artifactSha256" entry) platforms
                        |> Result.bind (nonEmptyPlatforms platforms)
                        |> Result.map RecipeDelivery.Process
                | Delivery.Wasm ->
                    result {
                        let! abi = required "abi" token o
                        let! artifact = targetBinding "artifactTarget" "artifactSha256" n
                        return RecipeDelivery.Wasm(abi, artifact)
                    }
                | _ ->
                    result {
                        let! packageId = required "packageId" packageId o
                        let! command = required "command" token o
                        let! target = required "packageTarget" targetPath o
                        let! sha256 = required "packageSha256" sha256 o

                        return
                            RecipeDelivery.DotnetTool
                                { PackageId = packageId
                                  Command = command
                                  Target = target
                                  Sha256 = sha256 }
                    }

            let! protocol = protocol operation o
            let! prerequisites = optional "prerequisites" (array prerequisite) o

            return
                { Operation = operation
                  ExtensionId = extensionId
                  Version = version
                  Release = release
                  Protocol = protocol
                  Prerequisites = defaultArg prerequisites []
                  Delivery = recipeDelivery }
        }

    /// The index of the first element that repeats an earlier one by <paramref name="key"/>.
    let private firstRepeat (key: 'T -> 'K) (items: 'T list) =
        items
        |> List.indexed
        |> List.tryFind (fun (index, item) -> items |> List.take index |> List.exists (fun earlier -> key earlier = key item))
        |> Option.map fst

    /// The consistency rules marked "after reading", in the order of their table.
    let private consistency (path: DocumentPath) (recipe: Recipe) : Read<Recipe> =
        let operations = path.Child "operations"
        let hosts = path.Child "hosts"
        let providers = path.Child "providers"
        let fail (at: DocumentPath) = refuse ReaderCode.InconsistentDocument at
        let builtInIds = recipe.Hosts |> List.collect (fun host -> host.BuiltIns |> List.map _.ExtensionId) |> set

        result {
            if List.isEmpty recipe.Operations then
                return! fail operations

            if List.isEmpty recipe.Hosts then
                return! fail hosts

            match firstRepeat id recipe.Operations with
            | Some index -> return! fail (operations.At index)
            | None -> ()

            match firstRepeat (fun (host: HostOffering) -> host.Implementation, host.Deployment) recipe.Hosts with
            | Some index -> return! fail (hosts.At index)
            | None -> ()

            for hostIndex, host in List.indexed recipe.Hosts do
                match firstRepeat (fun (builtIn: BuiltIn) -> builtIn.Operation) host.BuiltIns with
                | Some index -> return! fail (((hosts.At hostIndex).Child "builtIns").At index)
                | None -> ()

            match
                firstRepeat
                    (fun (provider: RecipeProvider) ->
                        provider.Operation, provider.ExtensionId, RecipeProvider.delivery provider, provider.Version.Text)
                    recipe.Providers
            with
            | Some index -> return! fail (providers.At index)
            | None -> ()

            for index, provider in List.indexed recipe.Providers do
                if builtInIds.Contains provider.ExtensionId then
                    return! fail ((providers.At index).Child "extensionId")

            for index, operation in List.indexed recipe.Operations do
                let performs (host: HostOffering) =
                    host.BuiltIns |> List.exists (fun builtIn -> builtIn.Operation = operation)
                    || recipe.Providers
                       |> List.exists (fun provider -> provider.Operation = operation && Fit.onSomePlatform host provider)

                if not (List.forall performs recipe.Hosts) then
                    return! fail (operations.At index)

            return recipe
        }

    let private body (identity: Sha256) (o: ObjectNode) : Read<Recipe> =
        result {
            let! preset = required "preset" token o
            let! version = required "version" version o
            let! operations = required "operations" (array operationObject) o
            let! hosts = required "hosts" (array host) o
            let! providers = required "providers" (array provider) o

            return!
                consistency
                    o.Path
                    { Preset = preset
                      Version = version
                      Operations = operations
                      Hosts = hosts
                      Providers = providers
                      Sha256 = identity }
        }

    /// Reads the exact bytes of a recipe. Authenticating them is the caller's: this reader trusts nothing it reads.
    let read (bytes: byte array) : Result<Recipe, ReaderRefusal> =
        document Syntax.parseJson understoodCriticalPaths (body (Sha256.ofBytes bytes)) bytes
