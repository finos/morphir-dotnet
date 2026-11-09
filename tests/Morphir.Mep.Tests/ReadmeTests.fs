// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.ReadmeTests

open System
open System.IO
open System.IO.Pipelines
open System.Text.Json
open System.Threading
open Expecto
open Morphir.Mep

// README example start
let runSession (claims: CapabilityClaimSet) (capabilities: ExtensionCapabilities) =
    task {
        let guest =
            Guest(
                { Claims = claims
                  Extension = claims.Extension
                  Capabilities = capabilities
                  Methods = readOnlyDict [] }
            )

        let toGuest = Pipe()
        let toHost = Pipe()
        let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), toHost.Writer.AsStream(), CancellationToken.None)

        let host =
            HostSession(
                toHost.Reader.AsStream(),
                toGuest.Writer.AsStream(),
                { Kind = PeerKind.Cli; Name = "morphir"; Version = "0.4.0" },
                None
            )

        let hostRun = host.RunAsync CancellationToken.None
        let! session = host.InitializeAsync CancellationToken.None
        let! ping = host.PingAsync CancellationToken.None
        let! _ = host.ShutdownAsync CancellationToken.None
        do! host.ExitAsync()
        let! guestEnd = guestRun
        toHost.Writer.Complete()
        let! _ = hostRun
        return session, ping, guestEnd
    }
// README example end

let private block (text: string) (start: string) (finish: string) =
    let first = text.IndexOf(start, StringComparison.Ordinal) + start.Length
    text.Substring(first, text.IndexOf(finish, first, StringComparison.Ordinal) - first).Trim()

[<Tests>]
let tests =
    testList
        "Morphir.Mep README"
        [ testCase "the README example is the compiled example"
          <| fun _ ->
              let root = Path.Combine(__SOURCE_DIRECTORY__, "..", "..")
              let readme = File.ReadAllText(Path.Combine(root, "src", "Morphir.Mep", "README.md"))
              let source = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "ReadmeTests.fs"))

              Expect.equal
                  (block readme "```fsharp" "```")
                  (block source "// README example start" "// README example end")
                  "Same code"

          testCase "the README example runs a session"
          <| fun _ ->
              let claims =
                  JsonSerializer.Deserialize(
                      """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"x","name":"X","version":"1.0.0","types":["validator"]},"capabilities":{}}""",
                      MepJson.Default.CapabilityClaimSet
                  )
                  |> nonNull

              let capabilities =
                  JsonSerializer.Deserialize("{}", MepJson.Default.ExtensionCapabilities) |> nonNull

              let session, ping, guestEnd = (runSession claims capabilities).Result
              Expect.isOk session "Initialized"
              Expect.isOk ping "Ping"
              Expect.equal guestEnd ConnectionEnd.Stopped "Exit" ]
