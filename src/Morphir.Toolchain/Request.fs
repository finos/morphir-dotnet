// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Text

/// The provider delivery cases.
[<RequireQualifiedAccess>]
type Delivery =
    /// Compiled into the host.
    | BuiltIn
    /// A native process speaking Content-Length framed MEP.
    | Process
    /// A portable module for one named ABI.
    | Wasm
    /// A .NET tool package installed into an isolated tool path.
    | DotnetTool

module Delivery =
    let all =
        [ Delivery.BuiltIn, "built-in"
          Delivery.Process, "process"
          Delivery.Wasm, "wasm"
          Delivery.DotnetTool, "dotnet-tool" ]

    let name (delivery: Delivery) =
        all |> List.find (fun (candidate, _) -> candidate = delivery) |> snd

/// What the request asks of the host release: a moving channel or one exact release.
[<RequireQualifiedAccess>]
type HostRelease =
    | Channel of channel: Channel
    | Exact of version: SemVer

type HostRequest =
    { Implementation: Token
      Deployment: Token
      Release: HostRelease }

/// The delivery an explicit provider choice names. Only the <c>wasm</c> delivery can name an ABI.
[<RequireQualifiedAccess>]
type ChosenDelivery =
    | BuiltIn
    | Process
    | Wasm of abi: Token option
    | DotnetTool

module ChosenDelivery =
    let delivery (chosen: ChosenDelivery) =
        match chosen with
        | ChosenDelivery.BuiltIn -> Delivery.BuiltIn
        | ChosenDelivery.Process -> Delivery.Process
        | ChosenDelivery.Wasm _ -> Delivery.Wasm
        | ChosenDelivery.DotnetTool -> Delivery.DotnetTool

/// How an explicit choice selects its provider: by delivery (and optionally identity), or by identity alone. A choice
/// with neither cannot be written.
[<RequireQualifiedAccess>]
type ChoiceSelector =
    | Delivery of delivery: ChosenDelivery * extensionId: ExtensionId option
    | Identity of extensionId: ExtensionId

/// An explicit provider choice; at most one per operation.
type ProviderChoice =
    { Operation: Operation
      Selector: ChoiceSelector }

module ProviderChoice =
    let delivery (choice: ProviderChoice) =
        match choice.Selector with
        | ChoiceSelector.Delivery(delivery, _) -> Some(ChosenDelivery.delivery delivery)
        | ChoiceSelector.Identity _ -> None

    let extensionId (choice: ProviderChoice) =
        match choice.Selector with
        | ChoiceSelector.Delivery(_, id) -> id
        | ChoiceSelector.Identity id -> Some id

    let abi (choice: ProviderChoice) =
        match choice.Selector with
        | ChoiceSelector.Delivery(ChosenDelivery.Wasm abi, _) -> abi
        | _ -> None

type ToolsetRequest =
    { Preset: Token
      Providers: ProviderChoice list }

/// <summary>A project toolchain request, <c>.morphir/toolchain.toml</c>, as read.</summary>
type Request =
    { /// The platforms to prepare closures for; absent means the current platform.
      Platforms: Platform list option
      Host: HostRequest
      Toolset: ToolsetRequest
      /// The request's identity: the SHA-256 of the file's exact bytes. Nothing is canonicalized first.
      Identity: Sha256 }

/// The project toolchain request reader (toolchain-request page).
module RequestDocument =
    open Reading

    /// The critical paths a 0.1.0-draft.1 request reader understands.
    let understoodCriticalPaths =
        set
            [ "schemaVersion"
              "critical"
              "platforms"
              "host"
              "host.implementation"
              "host.deployment"
              "host.channel"
              "host.version"
              "toolset"
              "toolset.preset"
              "toolset.providers"
              "toolset.providers.operation"
              "toolset.providers.language"
              "toolset.providers.target"
              "toolset.providers.delivery"
              "toolset.providers.extensionId"
              "toolset.providers.abi" ]

    let private deliveries =
        Delivery.all |> List.map (fun (delivery, name) -> name, delivery)

    let private choice (n: Node) : Read<ProviderChoice> =
        result {
            let! o = asObject n
            let! operation = operation o
            let! delivery = optional "delivery" (tag deliveries) o
            let! extensionId = optional "extensionId" extensionId o
            let! abi = optional "abi" token o

            let! selector =
                match delivery, extensionId, abi with
                | None, None, _ -> refuse ReaderCode.InconsistentDocument n.Path
                | delivery, _, Some _ when delivery <> Some Delivery.Wasm ->
                    refuse ReaderCode.InconsistentDocument (n.Path.Child "abi")
                | Some Delivery.Wasm, id, abi -> Ok(ChoiceSelector.Delivery(ChosenDelivery.Wasm abi, id))
                | Some Delivery.BuiltIn, id, _ -> Ok(ChoiceSelector.Delivery(ChosenDelivery.BuiltIn, id))
                | Some Delivery.Process, id, _ -> Ok(ChoiceSelector.Delivery(ChosenDelivery.Process, id))
                | Some Delivery.DotnetTool, id, _ -> Ok(ChoiceSelector.Delivery(ChosenDelivery.DotnetTool, id))
                | None, Some id, _ -> Ok(ChoiceSelector.Identity id)

            return
                { Operation = operation
                  Selector = selector }
        }

    let private host (n: Node) : Read<HostRequest> =
        result {
            let! o = asObject n
            let! implementation = required "implementation" token o
            let! deployment = required "deployment" token o

            let! release =
                match tryMember "channel" o, tryMember "version" o with
                | Some channel, None -> validated Channel.tryParse channel |> Result.map HostRelease.Channel
                | None, Some exact -> version exact |> Result.map HostRelease.Exact
                | _ -> refuse ReaderCode.InconsistentDocument n.Path

            return
                { Implementation = implementation
                  Deployment = deployment
                  Release = release }
        }

    let private toolset (n: Node) : Read<ToolsetRequest> =
        result {
            let! o = asObject n
            let! preset = required "preset" token o
            let! providers = optional "providers" (array choice) o
            let providers = defaultArg providers []
            let providersPath = n.Path.Child "providers"

            for index, choice in List.indexed providers do
                if providers |> List.take index |> List.exists (fun earlier -> earlier.Operation = choice.Operation) then
                    return! refuse ReaderCode.InconsistentDocument (providersPath.At index)

            return { Preset = preset; Providers = providers }
        }

    let private body (identity: Sha256) (o: ObjectNode) : Read<Request> =
        result {
            let! platforms = optional "platforms" (array platform) o

            match platforms with
            | Some list when List.length (List.distinct list) <> List.length list ->
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "platforms")
            | _ -> ()

            let! host = required "host" host o
            let! toolset = required "toolset" toolset o

            return
                { Platforms = platforms
                  Host = host
                  Toolset = toolset
                  Identity = identity }
        }

    /// <summary>Reads the exact bytes of a request. A refusal names the first failing rule in the order of checks.
    /// </summary>
    let read (bytes: byte array) : Result<Request, ReaderRefusal> =
        document Syntax.parseToml understoodCriticalPaths (body (Sha256.ofBytes bytes)) bytes

    /// <summary>
    /// The request bytes for a request given on the command line: TOML 1.0.0 in the member order and line layout of
    /// the approved example, without a final line feed, so the approved example given as options has the approved
    /// example file's identity.
    /// </summary>
    let write (host: HostRequest) (preset: Token) (platforms: Platform list option) : byte array =
        let quote (text: string) = "\"" + text + "\""
        let lines = ResizeArray<string>()
        lines.Add("schemaVersion = " + quote ContractVersion.current)

        match platforms with
        | Some list ->
            lines.Add("platforms = [" + String.Join(", ", list |> List.map (Platform.name >> quote)) + "]")
        | None -> ()

        lines.Add "[host]"
        lines.Add("implementation = " + quote host.Implementation.Text)
        lines.Add("deployment = " + quote host.Deployment.Text)

        match host.Release with
        | HostRelease.Channel channel -> lines.Add("channel = " + quote (Channel.text channel))
        | HostRelease.Exact version -> lines.Add("version = " + quote version.Text)

        lines.Add "[toolset]"
        lines.Add("preset = " + quote preset.Text)
        Encoding.UTF8.GetBytes(String.Join("\n", lines))
