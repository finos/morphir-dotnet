// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The MEP frontend adapter (add-fsharp-compile group 9): the host side of a compileModel session with a guest
/// process, the classic re-encoding of versions 1 and 2, and parity with the in-process frontend over the corpus.
module Morphir.Compilation.Mep.Tests.MepFrontendTests

open System
open System.Diagnostics
open System.IO
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Mep
open Morphir.Conformance
open Morphir.Frontend.FSharp
open Morphir.IR
open Morphir.Mep

let private host =
    match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
    | null -> "dotnet"
    | path -> path

let private stderr = new StringWriter()

/// The working folder of the guests that the adapter starts: a test sandbox shared by these tests, removed after the
/// run by Program.fs. The adapter under test starts the guest process itself, so the guest cannot be confined with
/// TestSandbox.Apply; it runs in the sandbox, and the run's MachineTripwire checks the machine folders.
let private guestWork = lazy (Morphir.TestSupport.TestSandbox.Create "morphir-frontend-adapter-guests-")

/// Removes the guests' working folder, if a test made it.
let internal disposeGuestWork () =
    if guestWork.IsValueCreated then
        guestWork.Value.Dispose()

let private optionsFor (arguments: string list) : MepFrontendOptions =
    { Executable = host
      Arguments = Array.ofList arguments
      WorkingDirectory = guestWork.Value.Work
      Deadline = TimeSpan.FromMinutes 2.0
      Stderr = TextWriter.Synchronized stderr
      Host =
        { Kind = PeerKind.Cli
          Name = "morphir-dotnet-tests"
          Version = "1.0.0" } }

let private guestOptions () =
    optionsFor [ Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-frontend.dll") ]

let private fakeOptions (scenario: string) =
    optionsFor [ Path.Combine(AppContext.BaseDirectory, "Morphir.Compilation.Mep.Tests.dll"); "--fake-guest"; scenario ]

let private connect (options: MepFrontendOptions) =
    match MepFrontend.ConnectAsync(options, CancellationToken.None).GetAwaiter().GetResult() with
    | Ok frontend -> frontend
    | Error failure -> failtest $"The guest connects: {failure.Message}"

let private dispose (frontend: MepFrontend) =
    (frontend :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

let private writers =
    WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ]

let private identity = { Name = "morphir-fsharp-frontend"; Version = "test"; Sha256 = String('0', 64) }

let private conformanceRoot =
    let rec find (directory: DirectoryInfo | null) =
        match directory with
        | null -> failtest "The repository root has no conformance directory"
        | found when Directory.Exists(Path.Combine(found.FullName, "conformance", "suites")) ->
            Path.Combine(found.FullName, "conformance")
        | found -> find found.Parent

    find (DirectoryInfo AppContext.BaseDirectory)

let private suite =
    lazy
        (let artifacts =
            [ for file in Directory.GetFiles(conformanceRoot, "*", SearchOption.AllDirectories) ->
                  { Path = Path.GetRelativePath(conformanceRoot, file).Replace('\\', '/')
                    Bytes = File.ReadAllBytes file } ]

         let options =
             { Suite = "frontend-fsharp"
               VocabularyVersion = "1.1"
               IrVersions = [ "1"; "2"; "3"; "4" ]
               InputKinds = [ "files"; "directory"; "project file" ] }

         match Corpus.read options artifacts with
         | Ok suite -> suite
         | Error issues -> failtest $"The corpus is valid: {issues}")

let private requestWith (text: string) (version: Versioning.FormatVersion) (frontend: IFrontend) =
    let target =
        frontend.Capability.Outputs
        |> Seq.find (fun output -> FormatVersion.sameVersion output.Version version)

    let path =
        match CanonicalNames.parsePath "acme/demo" with
        | Ok path -> path
        | Error error -> failtest $"{error}"

    match
        CompileRequest.Parse(
            LanguageId.Create "fsharp",
            null,
            [ { Uri = "Pricing.fs"
                LanguageId = LanguageId.Create "fsharp"
                Version = 1UL
                Text = text } ],
            PackageName path,
            null,
            target,
            ""
        )
    with
    | Ok request -> request
    | Error failure -> failtest $"{failure}"

let private request =
    requestWith "namespace Acme.Demo\nmodule Pricing =\n    let seats (n: int) : int = n + 1\n"

/// A value whose IR nests much deeper than 64 JSON containers: 40 nested additions.
let private deepSource =
    let expression = String.replicate 40 "(n + " + "n" + String.replicate 40 ")"
    $"namespace Acme.Demo\nmodule Pricing =\n    let deep (n: int) : int = {expression}\n"

[<Tests>]
let tests =
    testSequenced
    <| testList
        "MEP frontend adapter"
        [ testCase "A request too large for one frame is a protocol error of the guest process, not an exception"
          <| fun _ ->
              let options =
                  MepGuestOptions.Create(
                      host,
                      [ Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-frontend.dll") ],
                      guestWork.Value.Work,
                      TimeSpan.FromMinutes 1.0,
                      TextWriter.Synchronized stderr,
                      "morphir-dotnet-tests",
                      "1.0.0"
                  )

              let guest =
                  match MepGuestProcess.StartAsync(options, Morphir.Frontend.FSharp.Extension.FSharpGuest.support, CancellationToken.None).GetAwaiter().GetResult() with
                  | Ok guest -> guest
                  | Error failure -> failtest $"The guest starts: {failure}"

              try
                  // One byte more than a frame holds, before the JSON-RPC envelope is added.
                  use document = Text.Json.JsonDocument.Parse("\"" + String('a', ContentLengthFraming.MaxBodyBytes) + "\"")

                  match guest.SendAsync(Method.CompileModel, document.RootElement, CancellationToken.None).GetAwaiter().GetResult() with
                  | MepGuestReply.Failed(MepGuestFailure.ProtocolError message) -> Expect.stringContains message "compileModel" "names the request"
                  | other -> failtest $"Expected a protocol error: {other}"
              finally
                  (guest :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

          testCase "The session capability comes from the agreed tuples: v4 and classic versions 1 to 3"
          <| fun _ ->
              let frontend = connect (guestOptions ())

              try
                  let capability = (frontend :> IFrontend).Capability
                  Expect.equal (frontend :> IFrontend).Transport FrontendTransport.Mep "transport"
                  Expect.equal (frontend :> IFrontend).Identity.Value "org.finos.morphir.fsharp-frontend" "identity"
                  Expect.sequenceEqual (capability.Languages |> Seq.map (fun language -> language.Id.Value)) [ "fsharp" ] "languages"
                  Expect.isTrue capability.MultiDocument "multiDocument"

                  Expect.equal
                      (capability.Outputs |> Seq.map (fun output -> Versioning.version output.Version) |> List.ofSeq)
                      [ "4"; "1"; "2"; "3" ]
                      "4.0.0 serves v4, and 3.0.0 serves classic 1, 2 and 3"
              finally
                  dispose frontend

          testCase "Every corpus case through the mep-guest adapter equals the in-process result"
          <| fun _ ->
              let frontend = connect (guestOptions ())

              try
                  let mep = MepGuestAdapter.create identity frontend writers
                  let local = FrontendAdapter.create identity FSharpFrontend.registration writers
                  Expect.equal mep.Name "mep-guest" "adapter name"

                  let cases = suite.Value.Cases |> List.filter (fun case -> case.Exclusion.IsNone)
                  Expect.isNonEmpty cases "The corpus has cases"

                  let observe (adapter: Adapter) (case: ConformanceCase) =
                      adapter.Observe case.Input CancellationToken.None |> fun task -> task.GetAwaiter().GetResult()

                  for case in cases do
                      let expected = observe local case
                      let actual = observe mep case
                      let signature = case.Input.Documents |> List.exists (fun document -> document.Uri.EndsWith ".fsi")

                      match expected, actual with
                      | Ok _, Error failure when signature ->
                          // The guest claims .fs only (spec), so the host refuses a signature file before dispatch;
                          // in process, admission refuses it with fsharp.unsupported_signature_file.
                          Expect.stringContains failure.Message "claims no language" $"{case.Id}: refused before dispatch"
                      | Ok expected, Ok actual ->
                          Expect.equal actual.Success expected.Success $"{case.Id} IR {case.Input.IrVersion}: success"
                          Expect.equal actual.ModelJson expected.ModelJson $"{case.Id} IR {case.Input.IrVersion}: the IR bytes"
                          Expect.equal actual.Diagnostics expected.Diagnostics $"{case.Id} IR {case.Input.IrVersion}: diagnostics"
                      | _ -> failtest $"{case.Id}: both paths observe the case: {expected} / {actual}"
              finally
                  dispose frontend

          testCase "Classic version 2 through MEP is compiled at 3.0.0 and written as version 2"
          <| fun _ ->
              let frontend = connect (guestOptions ())

              try
                  let compile (target: IFrontend) =
                      match target.CompileAsync(request (Versioning.Classic 2) target, CancellationToken.None).GetAwaiter().GetResult() with
                      | FrontendOutcome.Completed result ->
                          match Checked.tryValue result with
                          | Some compiled ->
                              let resolved = writers.Resolve(Versioning.Classic 2) |> Checked.tryValue |> Option.get
                              resolved.Writer.Write(resolved.Target, WriteOptions.Compact, compiled.Model) |> Checked.tryValue |> Option.get
                          | None -> failtest $"The source compiles: {result.Diagnostics}"
                      | FrontendOutcome.Failed failure -> failtest failure.Message

                  let remote = compile frontend
                  let local = compile FSharpFrontend.registration
                  Expect.sequenceEqual remote local "The classic version 2 bytes are equal"
                  Expect.stringStarts (Text.Encoding.UTF8.GetString remote) "{\"formatVersion\":2" "format version 2"
              finally
                  dispose frontend

          testCase "A model nested deeper than 64 JSON containers is read with the agreed depth limit"
          <| fun _ ->
              let frontend = connect (guestOptions ())

              try
                  for version in [ Versioning.Classic 4; Versioning.Classic 3 ] do
                      let compile (target: IFrontend) =
                          match target.CompileAsync(requestWith deepSource version target, CancellationToken.None).GetAwaiter().GetResult() with
                          | FrontendOutcome.Completed result ->
                              match Checked.tryValue result with
                              | Some compiled ->
                                  let resolved = writers.Resolve version |> Checked.tryValue |> Option.get
                                  resolved.Writer.Write(resolved.Target, WriteOptions.Compact, compiled.Model) |> Checked.tryValue |> Option.get
                              | None -> failtest $"The source compiles: {result.Diagnostics}"
                          | FrontendOutcome.Failed failure -> failtest failure.Message

                      let remote = compile frontend
                      let local = compile FSharpFrontend.registration
                      Expect.sequenceEqual remote local $"IR {Versioning.version version}: the deep model is equal"
              finally
                  dispose frontend

          testCase "A guest that selects only 4.0.0 gets no request for classic 3, and the refusal names the extension"
          <| fun _ ->
              let frontend = connect (fakeOptions "only-v4")

              try
                  let outputs = (frontend :> IFrontend).Capability.Outputs |> Seq.map (fun output -> Versioning.version output.Version)
                  Expect.sequenceEqual outputs [ "4" ] "Only v4 is admitted"

                  let classic =
                      let v4 = request (Versioning.Classic 4) frontend

                      match
                          CompileRequest.Parse(
                              v4.LanguageId,
                              null,
                              v4.Documents,
                              v4.Package,
                              null,
                              { Family = Model.ModelFamily.Classic; Version = Versioning.Classic 3 },
                              ""
                          )
                      with
                      | Ok request -> request
                      | Error failure -> failtest $"{failure}"

                  match (frontend :> IFrontend).CompileAsync(classic, CancellationToken.None).GetAwaiter().GetResult() with
                  | FrontendOutcome.Failed failure ->
                      Expect.stringContains failure.Message "org.finos.morphir.fsharp-frontend" "names the extension"
                      Expect.stringContains failure.Message "4.0.0" "names the agreed tuples"

                      Expect.equal
                          (failure.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.CodeOrNull |> string) |> List.ofSeq)
                          [ "extension.not_admitted" ]
                          "the code"
                  | completed -> failtest $"Expected a refusal before dispatch: {completed}"
              finally
                  dispose frontend

              Expect.isFalse (stderr.ToString().Contains "compileModel was sent") "The host sent no compileModel"

          testCase "A guest that selects no tuple from an Ion-only support gets no request, and the refusal is extension.tuple_unavailable"
          <| fun _ ->
              let frontend = connect (fakeOptions "ion-only")

              try
                  Expect.isEmpty (frontend :> IFrontend).Capability.Outputs "The empty agreement admits no output"

                  let path =
                      match CanonicalNames.parsePath "acme/demo" with
                      | Ok path -> path
                      | Error error -> failtest $"{error}"

                  let v4 =
                      match
                          CompileRequest.Parse(
                              LanguageId.Create "fsharp",
                              null,
                              [ { Uri = "Pricing.fs"
                                  LanguageId = LanguageId.Create "fsharp"
                                  Version = 1UL
                                  Text = "namespace Acme.Demo\nmodule Pricing =\n    let seats (n: int) : int = n + 1\n" } ],
                              PackageName path,
                              null,
                              { Family = Model.ModelFamily.V4; Version = Versioning.Classic 4 },
                              ""
                          )
                      with
                      | Ok request -> request
                      | Error failure -> failtest $"{failure}"

                  match (frontend :> IFrontend).CompileAsync(v4, CancellationToken.None).GetAwaiter().GetResult() with
                  | FrontendOutcome.Failed failure ->
                      Expect.stringContains failure.Message "org.finos.morphir.fsharp-frontend" "names the extension"

                      Expect.equal
                          (failure.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.CodeOrNull |> string) |> List.ofSeq)
                          [ "extension.tuple_unavailable" ]
                          "the code"
                  | completed -> failtest $"Expected a refusal before dispatch: {completed}"
              finally
                  dispose frontend

              Expect.isFalse (stderr.ToString().Contains "ion-only compileModel was sent") "The host sent no compileModel"

          testCase "A guest that writes a log line to stdout fails the compile with a protocol diagnostic, and no process stays"
          <| fun _ ->
              let frontend = connect (fakeOptions "stdout-log")

              try
                  match (frontend :> IFrontend).CompileAsync(request (Versioning.Classic 4) frontend, CancellationToken.None).GetAwaiter().GetResult() with
                  | FrontendOutcome.Failed failure ->
                      Expect.stringContains failure.Message "org.finos.morphir.fsharp-frontend" "names the extension"

                      Expect.equal
                          (failure.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.CodeOrNull |> string) |> List.ofSeq)
                          [ "extension.protocol_error" ]
                          "the code"
                  | completed -> failtest $"Expected a protocol failure: {completed}"
              finally
                  dispose frontend

              Expect.isTrue frontend.HasExited "The guest process has exited after dispose"

          testCase "A guest that never answers compileModel is a timeout at the deadline, and no process stays"
          <| fun _ ->
              let frontend = connect { fakeOptions "never-answers" with Deadline = TimeSpan.FromSeconds 3.0 }

              try
                  match (frontend :> IFrontend).CompileAsync(request (Versioning.Classic 4) frontend, CancellationToken.None).GetAwaiter().GetResult() with
                  | FrontendOutcome.Failed failure ->
                      Expect.equal
                          (failure.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.CodeOrNull |> string) |> List.ofSeq)
                          [ "extension.timeout" ]
                          "the code"
                  | completed -> failtest $"Expected a timeout: {completed}"
              finally
                  dispose frontend

              Expect.isTrue frontend.HasExited "The guest process has exited after dispose"

          testCase "A guest that blocks its read loop: compile is a timeout, and disposal stops the process within about two deadlines"
          <| fun _ ->
              // The guest never reads shutdown or exit, so disposal must not wait for it beyond its deadline.
              let deadline = TimeSpan.FromSeconds 3.0
              let writer = new StringWriter()

              let options =
                  { fakeOptions "blocks-read-loop" with
                      Deadline = deadline
                      Stderr = TextWriter.Synchronized writer }

              let watch = Stopwatch.StartNew()
              let frontend = connect options

              try
                  match (frontend :> IFrontend).CompileAsync(request (Versioning.Classic 4) frontend, CancellationToken.None).GetAwaiter().GetResult() with
                  | FrontendOutcome.Failed failure ->
                      Expect.equal
                          (failure.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.CodeOrNull |> string) |> List.ofSeq)
                          [ "extension.timeout" ]
                          "the code"
                  | completed -> failtest $"Expected a timeout: {completed}"
              finally
                  dispose frontend

              watch.Stop()
              Expect.isLessThan watch.Elapsed (deadline + deadline + TimeSpan.FromSeconds 10.0) "compile and disposal end within about two deadlines"

              let id =
                  match Text.RegularExpressions.Regex.Match(writer.ToString(), @"fake-guest-pid=(\d+)") with
                  | found when found.Success -> int found.Groups[1].Value
                  | _ -> failtest $"The guest wrote its process id: {writer}"

              let running =
                  try
                      use guestProcess = Process.GetProcessById id
                      not guestProcess.HasExited
                  with :? ArgumentException ->
                      false

              Expect.isFalse running $"The guest process {id} is gone after dispose"

          testCase "An executable that does not exist is an unavailable extension"
          <| fun _ ->
              let options =
                  { guestOptions () with
                      Executable = Path.Combine(AppContext.BaseDirectory, "no-such-guest") }

              match MepFrontend.ConnectAsync(options, CancellationToken.None).GetAwaiter().GetResult() with
              | Error failure ->
                  Expect.equal
                      (failure.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.CodeOrNull |> string) |> List.ofSeq)
                      [ "extension.unavailable" ]
                      "the code"
              | Ok frontend ->
                  dispose frontend
                  failtest "A missing executable cannot connect" ]
