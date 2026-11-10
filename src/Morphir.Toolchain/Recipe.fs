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

/// <summary>A provider compiled into a host, with the model it exchanges with the host: the model a <c>compile</c>
/// built-in writes (<c>modelOutput</c>) or a <c>generate</c> built-in reads (<c>modelInput</c>). The operation names
/// the member, so a built-in cannot carry the other one.</summary>
type BuiltIn =
    { Operation: Operation
      ExtensionId: ExtensionId
      Version: SemVer
      Model: ModelFormat }

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
    /// An offering's MEP versions and WASM ABIs as sets of their text, compared ordinally.
    type private Speaks =
        { Versions: System.Collections.Generic.HashSet<string>
          Abis: System.Collections.Generic.HashSet<string> }

    /// <summary>Each offering's sets, built once: a fit check that scanned the lists made reading a recipe with many
    /// MEP versions and many providers quadratic (review of #582, R1).</summary>
    let private speaking = System.Runtime.CompilerServices.ConditionalWeakTable<HostOffering, Speaks>()

    let private speaks (offering: HostOffering) =
        speaking.GetValue(
            offering,
            fun offering ->
                { Versions = System.Collections.Generic.HashSet<string>(offering.ProtocolVersions |> List.map ProtocolVersion.text, System.StringComparer.Ordinal)
                  Abis = System.Collections.Generic.HashSet<string>(offering.WasmAbis |> List.map _.Text, System.StringComparer.Ordinal) }
        )

    /// <summary>The first reason a provider does not fit an offering on a platform, or <c>None</c> when it fits: the
    /// host speaks its MEP version, a process provider has an artifact for the platform, and a WASM provider's ABI is
    /// one the host lists. A <c>dotnet-tool</c> provider has no platform artifacts, so only the MEP version decides.
    /// A missing ABI is reported before any other reason.</summary>
    let onPlatform (offering: HostOffering) (platform: Platform) (provider: RecipeProvider) =
        let speaks = speaks offering

        match provider.Delivery with
        | RecipeDelivery.Wasm(abi, _) when not (speaks.Abis.Contains abi.Text) -> Some(Misfit.AbiUnavailable abi)
        | _ when not (speaks.Versions.Contains(ProtocolVersion.text provider.Protocol.Version)) -> Some Misfit.Protocol
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
            let! model = builtInModel operation o

            return
                { Operation = operation
                  ExtensionId = extensionId
                  Version = version
                  Model = model }
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
            let! kind = required "kind" (tag [ "tool-release", SourceTag.ToolRelease; "dotnet-tool", SourceTag.DotnetTool ]) o

            match kind with
            | SourceTag.ToolRelease ->
                let! toolId = required "toolId" (validated ToolId.tryParse) o
                return HostSource.ToolRelease toolId
            | SourceTag.DotnetTool ->
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
            let! protocolVersions = required "protocolVersions" (distinctArray (validated ProtocolVersion.tryParse)) o
            let! wasmAbis = optional "wasmAbis" (distinctArray token) o
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
                required
                    "delivery"
                    (tag
                        [ "process", Delivery.Process
                          "wasm", Delivery.Wasm
                          "dotnet-tool", Delivery.DotnetTool
                          "built-in", Delivery.BuiltIn ])
                    o

            // Built-ins belong to a host, so built-in is a known case in the wrong place (toolset-recipe.md, Providers).
            if delivery = Delivery.BuiltIn then
                return! refuse ReaderCode.InconsistentDocument (n.Path.Child "delivery")

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
                // Refused above.
                | Delivery.BuiltIn -> refuse ReaderCode.InconsistentDocument (n.Path.Child "delivery")
                | Delivery.DotnetTool ->
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

    /// The index of the first element that repeats an earlier one by <paramref name="key"/>, in linear time.
    let private firstRepeat (key: 'T -> 'K) (items: 'T list) = firstRepeatBy key items

    /// <summary>
    /// The index of the first operation that some offering can perform with neither a built-in nor a provider that
    /// fits it on some platform, or None. Whether a provider fits an offering depends only on the offering's MEP
    /// versions, WASM ABIs and platforms, and on the provider's MEP version, delivery, ABI and platforms
    /// (<see cref="Fit.onSomePlatform"/>). So offerings with the same three sets form one class, providers with the same
    /// fit key behave alike, and each key's classes are found once.
    /// </summary>
    /// <remarks>
    /// A key finds its classes through an index, never by checking every class: only classes that speak its MEP
    /// version can fit it, and for a WASM key only those that also list its ABI, so it scans the shorter of the two
    /// lists. Each key's classes are a bit set, and an operation passes when its candidates' sets, with the classes
    /// whose every offering has a built-in for it, hold every class. The work is linear in the recipe, apart from keys
    /// that share a MEP version: one <c>dotnet-tool</c> key and at most 255 process keys (one per set of platforms)
    /// per version. Checking every class for every key was quadratic: 10,000 distinct classes and keys took 18 s
    /// (root's benchmark of #582, 10 November 2026). Checking every offering for every operation, before classes, was
    /// quadratic too (review of #582, B1).
    /// </remarks>
    let internal firstUnperformed (recipe: Recipe) : int option =
        let hosts = Array.ofList recipe.Hosts
        let mask (platforms: (Platform * 'T) list) = platforms |> List.fold (fun bits (platform, _) -> bits ||| (1 <<< Platform.order platform)) 0

        let signature (host: HostOffering) =
            (host.ProtocolVersions |> List.map ProtocolVersion.text |> List.distinct |> List.sort),
            (host.WasmAbis |> List.map _.Text |> List.distinct |> List.sort),
            mask host.Platforms

        // The classes: each one's MEP versions, ABIs, platforms and number of offerings.
        let classOf = System.Collections.Generic.Dictionary<_, int>(HashIdentity.Structural)
        let versions = ResizeArray<System.Collections.Generic.HashSet<string>>()
        let abis = ResizeArray<System.Collections.Generic.HashSet<string>>()
        let platforms = ResizeArray<int>()
        let size = ResizeArray<int>()

        let hostClass =
            hosts
            |> Array.map (fun host ->
                let key = signature host

                match classOf.TryGetValue key with
                | true, c ->
                    size[c] <- size[c] + 1
                    c
                | false, _ ->
                    let c = size.Count
                    let spoken, listed, bits = key
                    classOf[key] <- c
                    versions.Add(System.Collections.Generic.HashSet<string>(spoken, System.StringComparer.Ordinal))
                    abis.Add(System.Collections.Generic.HashSet<string>(listed, System.StringComparer.Ordinal))
                    platforms.Add bits
                    size.Add 1
                    c)

        let classes = size.Count
        let words = (classes + 63) / 64

        let index (members: int -> seq<string>) =
            let byText = System.Collections.Generic.Dictionary<string, ResizeArray<int>>(System.StringComparer.Ordinal)

            for c in 0 .. classes - 1 do
                for text in members c do
                    match byText.TryGetValue text with
                    | true, list -> list.Add c
                    | false, _ -> byText[text] <- ResizeArray [ c ]

            byText

        let speakers = index (fun c -> versions[c])
        let listers = index (fun c -> abis[c])

        let classesWith (byText: System.Collections.Generic.Dictionary<string, ResizeArray<int>>) (text: string) =
            match byText.TryGetValue text with
            | true, list -> list
            | false, _ -> ResizeArray()

        let fitKey (provider: RecipeProvider) =
            ProtocolVersion.text provider.Protocol.Version,
            match provider.Delivery with
            | RecipeDelivery.Wasm(abi, _) -> "wasm " + abi.Text
            | RecipeDelivery.Process platforms -> "process " + string (mask platforms)
            | RecipeDelivery.DotnetTool _ -> "dotnet-tool"

        // For each fit key, the classes it fits as a bit set and how many offerings they hold, computed once.
        let fitting = System.Collections.Generic.Dictionary<_, uint64 array * int>(HashIdentity.Structural)

        let fits (provider: RecipeProvider) =
            let key = fitKey provider

            match fitting.TryGetValue key with
            | true, found -> found
            | false, _ ->
                let version = ProtocolVersion.text provider.Protocol.Version

                // Fit.onSomePlatform for a class that speaks the version: an offering lists at least one platform, a
                // process provider shares one with it, and a WASM provider's ABI is one it lists.
                let candidates, fitsClass =
                    match provider.Delivery with
                    | RecipeDelivery.Wasm(abi, _) ->
                        let bySpeaking = classesWith speakers version
                        let byListing = classesWith listers abi.Text

                        if bySpeaking.Count <= byListing.Count then
                            bySpeaking, (fun c -> platforms[c] <> 0 && abis[c].Contains abi.Text)
                        else
                            byListing, (fun c -> platforms[c] <> 0 && versions[c].Contains version)
                    | RecipeDelivery.Process delivered ->
                        let bits = mask delivered
                        classesWith speakers version, (fun c -> platforms[c] &&& bits <> 0)
                    | RecipeDelivery.DotnetTool _ -> classesWith speakers version, (fun c -> platforms[c] <> 0)

                let set = Array.zeroCreate<uint64> words
                let mutable offerings = 0

                for c in candidates do
                    if fitsClass c then
                        set[c / 64] <- set[c / 64] ||| (1UL <<< (c % 64))
                        offerings <- offerings + size[c]

                let found = set, offerings
                fitting[key] <- found
                found

        // For each operation, the classes whose every offering has a built-in for it: an offering is counted once per
        // operation, and a class is built in when its count reaches its size.
        let builtIn = System.Collections.Generic.Dictionary<Operation, uint64 array>(HashIdentity.Structural)
        let builtInCount = System.Collections.Generic.Dictionary<Operation * int, int>(HashIdentity.Structural)

        hosts
        |> Array.iteri (fun index host ->
            let c = hostClass[index]

            for operation in host.BuiltIns |> List.map _.Operation |> List.distinct do
                let count =
                    match builtInCount.TryGetValue((operation, c)) with
                    | true, n -> n + 1
                    | false, _ -> 1

                builtInCount[(operation, c)] <- count

                if count = size[c] then
                    let set =
                        match builtIn.TryGetValue operation with
                        | true, set -> set
                        | false, _ ->
                            let set = Array.zeroCreate<uint64> words
                            builtIn[operation] <- set
                            set

                    set[c / 64] <- set[c / 64] ||| (1UL <<< (c % 64)))

        let providersFor = recipe.Providers |> List.groupBy _.Operation |> dict

        let full (set: uint64 array) =
            let rest = classes % 64

            set
            |> Array.mapi (fun w bits -> if w = words - 1 && rest <> 0 then bits = (1UL <<< rest) - 1UL else bits = System.UInt64.MaxValue)
            |> Array.forall id

        let performedByAll (operation: Operation) =
            let candidates =
                match providersFor.TryGetValue operation with
                | true, providers -> providers |> List.distinctBy fitKey
                | false, _ -> []

            if candidates |> List.exists (fun provider -> snd (fits provider) = hosts.Length) then
                true
            else
                let covered =
                    match builtIn.TryGetValue operation with
                    | true, set -> Array.copy set
                    | false, _ -> Array.zeroCreate words

                for provider in candidates do
                    let set, _ = fits provider

                    for w in 0 .. words - 1 do
                        covered[w] <- covered[w] ||| set[w]

                full covered

        recipe.Operations |> List.tryFindIndex (performedByAll >> not)
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

            match firstUnperformed recipe with
            | Some index -> return! fail (operations.At index)
            | None -> ()

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
