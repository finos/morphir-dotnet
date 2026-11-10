// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The host side of an external backend: MepGenerationBackend starts a backend guest process, negotiates a MEP
/// session and serves the generation contract over morphir.backend.generateModel. The real guest gives the same
/// files as the built-in backend; scripted guests give each failure its own code.
module Morphir.CodeGeneration.FSharp.Extension.Tests.HostAdapterTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open Expecto
open Morphir.CodeGeneration.FSharp.Extension
open Morphir.CodeGeneration.FSharp.Extension.Tests.GuestSupport
open Morphir.Extensions
open Morphir.TestSupport
open Morphir.Tooling.Features.Generate

let private dotnet =
    match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
    | null -> "dotnet"
    | path -> path

let private stderr = new StringWriter()

/// The working folder of the guests that the adapter starts: a test sandbox shared by these tests, removed after the
/// run by Program.fs. The adapter under test starts the guest process itself, so the guest cannot be confined with
/// TestSandbox.Apply; it runs in the sandbox, and the run's MachineTripwire checks the machine folders.
let private guestWork = lazy (TestSandbox.Create "morphir-backend-adapter-guests-")

/// Removes the guests' working folder, if a test made it.
let internal disposeGuestWork () =
    if guestWork.IsValueCreated then
        guestWork.Value.Dispose()

/// The sandbox's environment for the guests that the adapter starts: the inherited variables, with the sandbox's
/// home and machine folders and its MORPHIR_* settings (TestSandbox.EnvironmentSnapshot).
let private sandboxEnvironment () : IReadOnlyDictionary<string, string> =
    let inherited =
        readOnlyDict
            [ for entry in Environment.GetEnvironmentVariables() |> Seq.cast<Collections.DictionaryEntry> ->
                  string entry.Key, string entry.Value ]

    guestWork.Value.EnvironmentSnapshot inherited

let private optionsFor (arguments: string list) (deadline: TimeSpan) =
    MepBackendOptions(
        dotnet,
        Array.ofList arguments,
        TargetId.Create "fsharp",
        Deadline = deadline,
        Stderr = TextWriter.Synchronized stderr,
        WorkingDirectory = guestWork.Value.Work,
        Environment = sandboxEnvironment ()
    )

/// The real guest, morphir-fsharp-backend, as a framework-dependent dll.
let private realGuest () =
    optionsFor [ Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-backend.dll") ] (TimeSpan.FromMinutes 2.0)

/// A scripted guest of FakeBackendGuests.
let private fakeGuest (scenario: string) (deadline: TimeSpan) =
    optionsFor [ Path.Combine(AppContext.BaseDirectory, "Morphir.CodeGeneration.FSharp.Extension.Tests.dll"); "--fake-backend-guest"; scenario ] deadline

let private connectOutcome (options: MepBackendOptions) =
    MepGenerationBackend.ConnectAsync(options, CancellationToken.None).GetAwaiter().GetResult()

let private connect (options: MepBackendOptions) =
    match connectOutcome options with
    | :? Outcome<MepGenerationBackend>.Success as success -> success.Value
    | failure -> failtest $"The guest connects: {failure}"

let private dispose (backend: MepGenerationBackend) =
    (backend :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

let private load (path: string) =
    let loader = GenerationModelLoader(BoundedModelCapture())

    match loader.LoadAsync(ModelInput(path, InputFormat.Json), ModelReadLimits.Default, CancellationToken.None).AsTask().Result with
    | :? Outcome<LoadedModel>.Success as success -> success.Value
    | failure -> failtest $"The loader reads {path}: {failure}"

let private optionsOf (pairs: (string * string) list) =
    BackendOptions
        [ for key, json in pairs ->
              use document = JsonDocument.Parse json
              KeyValuePair(key, document.RootElement.Clone()) ]

let private validated (backend: IGenerationBackend) (pairs: (string * string) list) =
    match backend.ValidateOptions(optionsOf pairs) with
    | :? Outcome<ValidatedBackendOptions>.Success as success -> success.Value
    | failure -> failtest $"The options validate: {failure}"

let private generateWith (backend: IGenerationBackend) (model: LoadedModel) (pairs: (string * string) list) =
    backend
        .GenerateAsync(BackendGenerationRequest(model, backend.Descriptor.Target, validated backend pairs), CancellationToken.None)
        .AsTask()
        .GetAwaiter()
        .GetResult()

/// The files of a generated tree in its declared order (the manifest order), else by path.
let private filesOf (tree: Morphir.IR.Pipeline.VFileTree) =
    let files = Morphir.IR.Pipeline.VFileTree.toFileMap tree

    let order =
        Morphir.IR.Pipeline.VFileTree.tryGet Morphir.IR.Pipeline.VFileTree.declaredOrder tree
        |> Option.defaultWith (fun () -> files |> Map.keys |> List.ofSeq)

    [ for location in order ->
          match Morphir.IR.Pipeline.VFile.readText files[location] with
          | Ok text -> location, text
          | Error failure -> failtest $"{location} reads as text: {failure}" ]

let private artifactsOf (outcome: Outcome<GenerationArtifacts>) =
    match outcome with
    | :? Outcome<GenerationArtifacts>.Success as success -> success.Value
    | failure -> failtest $"Generation succeeds: {failure}"

let private failureOf (outcome: Outcome<'T>) =
    match outcome with
    | :? Outcome<'T>.Failure as failure -> failure.Error
    | success -> failtest $"Expected a failure: {success}"

let private codes (failure: GenerationFailure) = [ for diagnostic in failure.Diagnostics -> diagnostic.Code ]

let private messages (failure: GenerationFailure) =
    String.Join(" | ", [ for diagnostic in failure.Diagnostics -> diagnostic.Message ])

let private builtIns () : IGenerationBackend list = [ FSharpGenerationBackend(); CSharpGenerationBackend() ]

/// True when the process whose id a fake guest wrote to stderr (fake-guest-pid=N) no longer runs.
let private guestGone (stderrText: string) =
    match Text.RegularExpressions.Regex.Match(stderrText, @"fake-guest-pid=(\d+)") with
    | found when found.Success ->
        try
            use guestProcess = Diagnostics.Process.GetProcessById(int found.Groups[1].Value)
            guestProcess.HasExited
        with :? ArgumentException ->
            true
    | _ -> failtest $"The guest wrote its process id: {stderrText}"

/// A scripted guest with its own stderr writer.
let private fakeGuestWith (scenario: string) (deadline: TimeSpan) (writer: TextWriter) =
    MepBackendOptions(
        dotnet,
        [| Path.Combine(AppContext.BaseDirectory, "Morphir.CodeGeneration.FSharp.Extension.Tests.dll"); "--fake-backend-guest"; scenario |],
        TargetId.Create "fsharp",
        Deadline = deadline,
        Stderr = TextWriter.Synchronized writer,
        WorkingDirectory = guestWork.Value.Work,
        Environment = sandboxEnvironment ()
    )

[<Tests>]
let tests =
    testSequenced
    <| testList
        "MEP backend host adapter"
        [ testCase "A guest that blocks its read loop: generate is a timeout, and disposal stops the process within about two deadlines"
          <| fun _ ->
              // The guest never reads shutdown or exit, so disposal must not wait for it beyond its deadline.
              let deadline = TimeSpan.FromSeconds 3.0
              let writer = new StringWriter()
              let watch = Diagnostics.Stopwatch.StartNew()
              let backend = connect (fakeGuestWith "blocks-read-loop" deadline writer)

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  Expect.contains (codes failure) "extension.timeout" "the code"
              finally
                  dispose backend

              watch.Stop()
              Expect.isLessThan watch.Elapsed (deadline + deadline + TimeSpan.FromSeconds 10.0) "generate and disposal end within about two deadlines"
              Expect.isTrue (guestGone (writer.ToString())) "The guest process is gone after dispose"

          testCase "A model over the agreed decoded limit is not sent: extension.not_admitted names the limit"
          <| fun _ ->
              // The offered limits are lowered through the options, so a small fixture is over them; the agreed
              // limits are then at most these.
              let options =
                  MepBackendOptions(
                      dotnet,
                      [| Path.Combine(AppContext.BaseDirectory, "Morphir.CodeGeneration.FSharp.Extension.Tests.dll"); "--fake-backend-guest"; "only-v4" |],
                      TargetId.Create "fsharp",
                      Deadline = TimeSpan.FromMinutes 1.0,
                      Stderr = TextWriter.Synchronized stderr,
                      WorkingDirectory = guestWork.Value.Work,
                      Environment = sandboxEnvironment (),
                      OfferLimits =
                          ({ MaxEncodedBytes = 4096
                             MaxDecodedBytes = 100
                             MaxDepth = 4096
                             MaxNodes = 50_000_000 }
                          : Morphir.Mep.ModelTransportLimits)
                  )

              let backend = connect options

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  Expect.equal (codes failure) [ "extension.not_admitted" ] "the code"
                  Expect.stringContains (messages failure) "100" "the message names the limit"
                  Expect.isFalse backend.HasExited "the guest still runs: nothing was sent that it could fail on"
              finally
                  dispose backend

          testCase "The guest runs with the environment that the options give, not the host's"
          <| fun _ ->
              let writer = new StringWriter()
              let environment = Dictionary<string, string>(sandboxEnvironment ())
              environment["MORPHIR_FAKE_GUEST_MARKER"] <- "from-options"

              let options =
                  MepBackendOptions(
                      dotnet,
                      [| Path.Combine(AppContext.BaseDirectory, "Morphir.CodeGeneration.FSharp.Extension.Tests.dll"); "--fake-backend-guest"; "print-env" |],
                      TargetId.Create "fsharp",
                      Deadline = TimeSpan.FromMinutes 1.0,
                      Stderr = TextWriter.Synchronized writer,
                      WorkingDirectory = guestWork.Value.Work,
                      Environment = environment
                  )

              dispose (connect options)
              let text = writer.ToString()
              Expect.stringContains text "fake-guest-marker=from-options" "the variable from the options"
              Expect.stringContains text $"fake-guest-home={guestWork.Value.Home}" "the sandbox's home, not the user's"

          testCase "A result with more artifacts than the host accepts is a protocol error that names the bound"
          <| fun _ ->
              let backend = connect (fakeGuest "too-many-artifacts" (TimeSpan.FromMinutes 1.0))

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  Expect.equal (codes failure) [ "extension.protocol_error" ] "the code"
                  Expect.stringContains (messages failure) (string MepGenerationBackend.MaxArtifacts) "names the bound"
              finally
                  dispose backend

          testCase "A -32800 answer that the host did not ask for is a protocol error, not a user cancellation"
          <| fun _ ->
              let backend = connect (fakeGuest "spontaneous-cancel" (TimeSpan.FromMinutes 1.0))

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  Expect.notEqual failure.Kind GenerationFailureKind.Cancelled "not a cancellation"
                  Expect.equal (codes failure) [ "extension.protocol_error" ] "the code"
              finally
                  dispose backend

          testCase "A guest diagnostic position beyond a signed 32-bit number is kept at the largest position, not negative"
          <| fun _ ->
              let backend = connect (fakeGuest "huge-position" (TimeSpan.FromMinutes 1.0))

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  let diagnostic = failure.Diagnostics |> Seq.find (fun diagnostic -> diagnostic.Code = "fake.huge")
                  let range = diagnostic.Range |> nonNull

                  for position in [ range.Start; range.End ] do
                      Expect.equal position.Line Int32.MaxValue "the line"
                      Expect.equal position.Character Int32.MaxValue "the character"
              finally
                  dispose backend

          testCase "A malformed describe while connecting is extension.protocol_error, and the guest process is gone"
          <| fun _ ->
              let writer = new StringWriter()

              let failure =
                  failureOf (connectOutcome (fakeGuestWith "malformed-describe" (TimeSpan.FromMinutes 1.0) writer))

              Expect.equal (codes failure) [ "extension.protocol_error" ] "the code"
              Expect.isTrue (guestGone (writer.ToString())) "The guest process is gone after the failed connect"

          testCase "A model file with a UTF-8 byte order mark is sent without it and gives the same files"
          <| fun _ ->
              let plain = File.ReadAllBytes(irFixture "daily-rate-v4.json")
              let withMark = Path.Combine(guestWork.Value.Work, "daily-rate-v4-bom.json")
              File.WriteAllBytes(withMark, Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] plain)
              let backend = connect (realGuest ())

              try
                  let expected = filesOf (artifactsOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])).Tree
                  let actual = filesOf (artifactsOf (generateWith backend (load withMark) [])).Tree
                  Expect.equal actual expected "the same files with and without the byte order mark"
              finally
                  dispose backend

          testCase "The adapter names the guest by its own identity and declares the agreed tuples as capabilities"
          <| fun _ ->
              let backend = connect (realGuest ())

              try
                  Expect.equal backend.Descriptor.Id.Value FSharpGuest.extension.Id "The guest's extension id, not morphir.fsharp"
                  Expect.equal backend.Descriptor.Target.Value "fsharp" "The target"
                  Expect.isNotEmpty backend.ExtensionVersion "The guest's version"
                  Expect.equal backend.Identity $"{FSharpGuest.extension.Id} {backend.ExtensionVersion}" "id and version"

                  let declared =
                      [ for capability in backend.Descriptor.Capabilities ->
                            (if capability.Family.IsV4 then "V4" else "Classic"), [ for version in capability.Versions -> version.Text ] |> List.sort ]
                      |> List.distinct
                      |> List.sort

                  Expect.equal declared [ "Classic", [ "3"; "3.0.0" ]; "V4", [ "4"; "4.0.0" ] ] "IR 4.0.0 serves v4 4, and 3.0.0 serves classic 3"
              finally
                  dispose backend

              Expect.isTrue backend.HasExited "Disposing ends the guest process"

          testCase "Through the adapter the real guest gives the files of the built-in backend, in its order, for IR 3 and IR 4 with and without optimize"
          <| fun _ ->
              let cases =
                  [ "daily-rate-v3.json", []
                    "daily-rate-v4.json", []
                    "constant-folding-v3.json", [ "optimize", "true" ]
                    "constant-folding-v3.json", [ "optimize", "false" ]
                    "constant-folding-v4.json", [ "optimize", "true" ]
                    "constant-folding-v4.json", [ "optimize", "false" ] ]

              let backend = connect (realGuest ())
              let builtIn = FSharpGenerationBackend()

              try
                  let outputs =
                      [ for name, options in cases ->
                            let model = load (irFixture name)
                            let label = $"{name} with {options}"
                            let external = filesOf (artifactsOf (generateWith backend model options)).Tree
                            let expected = filesOf (artifactsOf (generateWith builtIn model options)).Tree
                            Expect.isNonEmpty expected $"{label}: the built-in generates files"
                            Expect.sequenceEqual (List.map fst external) (List.map fst expected) $"{label}: same paths in the same order"
                            Expect.sequenceEqual external expected $"{label}: same text"
                            (name, options), external ]

                  for name in [ "constant-folding-v3.json"; "constant-folding-v4.json" ] do
                      let text optimize =
                          outputs |> List.find (fun ((n, o), _) -> n = name && o = [ "optimize", optimize ]) |> snd |> List.map snd |> String.concat "\n"

                      Expect.notEqual (text "true") (text "false") $"{name}: optimize reaches the guest"
              finally
                  dispose backend

          testCase "generate through the adapter, selected explicitly, reports the guest's identity; the default resolution stays the built-in"
          <| fun _ ->
              let backend = connect (realGuest ())
              use sandbox = TestSandbox.Create "morphir-backend-adapter-"

              try
                  let model = load (irFixture "daily-rate-v4.json")
                  let target = RequestedTarget.Parse "fsharp"

                  // Nothing selected: the registered built-ins resolve fsharp to morphir.fsharp. The adapter is
                  // connected (its guest runs) but not registered: nothing adds it to the registry, so the default
                  // resolution cannot see it.
                  match RegisteredBackendResolver(builtIns ()).Resolve(target, model.Descriptor) with
                  | :? Outcome<IGenerationBackend>.Success as success ->
                      Expect.equal success.Value.Descriptor.Id.Value "morphir.fsharp" "The default backend for fsharp"
                  | failure -> failtest $"The default resolves: {failure}"

                  // An explicit selection resolves over the selected backend only.
                  let settings =
                      GenerationSettings(
                          ModelInput(irFixture "daily-rate-v4.json", InputFormat.Json),
                          target,
                          BackendOptions.Empty,
                          PublicationPolicy(sandbox.PathOf "generated"),
                          ModelReadLimits.Default
                      )

                  let outcome =
                      GenerationStages
                          .RunFrom(model, settings, RegisteredBackendResolver([ backend :> IGenerationBackend ]), GenerationPublisher(), CancellationToken.None)
                          .GetAwaiter()
                          .GetResult()

                  match outcome with
                  | :? Outcome<GenerationSuccess>.Success as success ->
                      Expect.equal success.Value.Backend FSharpGuest.extension.Id "The report names the external backend"
                      Expect.isNonEmpty success.Value.Publication.Written "The files are published"
                  | failure -> failtest $"generate succeeds: {failure}"
              finally
                  dispose backend

          testCase "A model that the generator rejects is a semantic failure with the guest's diagnostics"
          <| fun _ ->
              let backend = connect (realGuest ())

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-mismatch-v3.json")) [])
                  Expect.equal failure.Kind GenerationFailureKind.Semantic "semantic"
                  Expect.exists failure.Diagnostics (fun diagnostic -> diagnostic.Severity = "error") "an error diagnostic"
              finally
                  dispose backend

          testCase "Options that the guest refuses are an invocation failure that names the guest"
          <| fun _ ->
              let backend = connect (realGuest ())

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [ "optimize", "\"no\"" ])
                  Expect.equal failure.Kind GenerationFailureKind.Invocation "invocation"
                  Expect.contains (codes failure) "extension.not_admitted" "the code"
                  Expect.stringContains (messages failure) FSharpGuest.extension.Id "the guest is named"
              finally
                  dispose backend

          testCase "A guest that shares no tuple with the host is installed but not ready: the failure names the tuples"
          <| fun _ ->
              let failure = failureOf (connectOutcome (fakeGuest "ion-only" (TimeSpan.FromMinutes 1.0)))
              Expect.contains (codes failure) "extension.tuple_unavailable" "the code"
              Expect.stringContains (messages failure) "ion-binary" "the guest's tuple is named"
              Expect.stringContains (messages failure) "morphir-json" "the host's offer is named"

          testCase "A model in a tuple that the session did not agree is refused before any request, naming the tuple"
          <| fun _ ->
              let backend = connect (fakeGuest "only-v4" (TimeSpan.FromMinutes 1.0))

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v3.json")) [])
                  Expect.contains (codes failure) "extension.tuple_unavailable" "the code"
                  Expect.stringContains (messages failure) "3.0.0" "the tuple the model needs is named"
              finally
                  dispose backend

          testCase "The host applies its own path check to the guest's artifacts"
          <| fun _ ->
              for scenario, fragment in [ "unsafe-path", "../escape.fs"; "case-collision", "AB.fs" ] do
                  let backend = connect (fakeGuest scenario (TimeSpan.FromMinutes 1.0))

                  try
                      let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                      Expect.equal failure.Kind GenerationFailureKind.Operational $"{scenario}: operational"
                      Expect.contains (codes failure) "extension.unsafe_artifact" $"{scenario}: the code"
                      Expect.stringContains (messages failure) fragment $"{scenario}: the path is named"
                  finally
                      dispose backend

          testCase "A result that is not a generate result, or a frame over the limit, is a protocol error"
          <| fun _ ->
              for scenario in [ "malformed-result"; "oversized-frame" ] do
                  let backend = connect (fakeGuest scenario (TimeSpan.FromSeconds 30.0))

                  try
                      let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                      Expect.equal failure.Kind GenerationFailureKind.Operational $"{scenario}: operational"
                      Expect.contains (codes failure) "extension.protocol_error" $"{scenario}: the code"
                  finally
                      dispose backend

                  Expect.isTrue backend.HasExited $"{scenario}: the guest process is gone"

          testCase "A guest that exits during the request is a protocol error, not a hang"
          <| fun _ ->
              let backend = connect (fakeGuest "exit-mid-request" (TimeSpan.FromMinutes 1.0))

              try
                  let started = Diagnostics.Stopwatch.StartNew()
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  Expect.contains (codes failure) "extension.protocol_error" "the code"
                  Expect.isLessThan started.Elapsed (TimeSpan.FromSeconds 30.0) "The end of the connection ends the request"
              finally
                  dispose backend

              Expect.isTrue backend.HasExited "The guest process is gone"

          testCase "A guest that never answers is a timeout at the deadline, and disposing stops its process"
          <| fun _ ->
              let backend = connect (fakeGuest "never-answers" (TimeSpan.FromSeconds 3.0))

              try
                  let failure = failureOf (generateWith backend (load (irFixture "daily-rate-v4.json")) [])
                  Expect.contains (codes failure) "extension.timeout" "the code"
              finally
                  dispose backend

              Expect.isTrue backend.HasExited "The guest process is gone" ]
