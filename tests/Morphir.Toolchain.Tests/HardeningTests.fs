// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Rules the fixtures do not pin, from the independent review of the first slice.
module Morphir.Toolchain.Tests.HardeningTests

open System.Text
open Expecto
open Morphir.Toolchain

let private refusal (result: Result<'T, ReaderRefusal>) =
    match result with
    | Ok _ -> failtest "expected a refusal"
    | Error refusal -> Code.readerName refusal.Code, DocumentPath.render refusal.Path

/// The approved lock fragment with its critical list replaced.
let private lockWithCritical (critical: string) =
    let text = Encoding.UTF8.GetString(Fixtures.bytes "lock/approved-fragment.json")
    Encoding.UTF8.GetBytes(text.Replace("\"critical\": [\"host\", \"platforms\"]", $"\"critical\": [{critical}]"))

/// A result with one diagnostic whose path member is the given text.
let private resultWithPath (path: string) =
    Encoding.UTF8.GetBytes(
        $"""{{"schemaVersion":"0.1.0-draft.1","command":"doctor","outcome":"failed","diagnostics":[{{"kind":"unsupported-contract","code":"invalid_value","severity":"error","message":"m","document":"lock","path":"{path}"}}]}}"""
    )

/// <summary>Runs a read under a time budget on its own task, so a quadratic check fails the test within the budget
/// instead of hanging the suite. The budget is generous: a linear read takes well under a second.</summary>
let withinBudget (seconds: float) (what: string) (read: unit -> 'T) : 'T =
    let task = System.Threading.Tasks.Task.Run(fun () -> read ())

    if task.Wait(System.TimeSpan.FromSeconds seconds) then
        task.Result
    else
        failtestf "%s took longer than %.0f s" what seconds

/// The synthetic recipe with <paramref name="count"/> distinct entries added to the first offering's array
/// <paramref name="member'"/>, each spelled by <paramref name="entry"/>.
let recipeWithEntries (member': string) (entry: int -> string) (count: int) =
    let text = Encoding.UTF8.GetString(Fixtures.bytes "recipe/fsharp-synthetic.json")
    let marker = $"\"{member'}\": ["
    let at = text.IndexOf marker
    Expect.isGreaterThan at 0 $"the recipe has {member'}"
    let extra = String.concat ", " [ for i in 1..count -> $"\"{entry i}\"" ]
    let empty = text.[at + marker.Length] = ']'
    Encoding.UTF8.GetBytes(text.Insert(at + marker.Length, if empty then extra else extra + ", "))

let private parsed (bytes: byte array) =
    match Syntax.parseJson bytes with
    | Ok value -> value
    | Error refusal -> failtestf "does not parse: %A" refusal

let private objectMembers (value: Value) =
    match value with
    | Value.Object members -> members
    | _ -> failtest "not an object"

let private memberOf (name: string) (value: Value) = objectMembers value |> List.find (fst >> (=) name) |> snd

let private withMember (name: string) (replacement: Value) (value: Value) =
    Value.Object(objectMembers value |> List.map (fun (n, v) -> if n = name then n, replacement else n, v))

let private arrayItems (value: Value) =
    match value with
    | Value.Array items -> items
    | _ -> failtest "not an array"

/// <summary>The synthetic recipe reduced to its native-aot offering, whose built-ins perform
/// <paramref name="count"/> compile operations besides its generate operation, so it reads, resolves and locks.</summary>
let manyBuiltIns (count: int) =
    let recipe = parsed (Fixtures.bytes "recipe/fsharp-synthetic.json")
    let host = (arrayItems (memberOf "hosts" recipe)).Head
    let generate = arrayItems (memberOf "builtIns" host)

    let compile (i: int) =
        Value.Object
            [ "operation", Value.String "compile"
              "language", Value.String $"l{i}"
              "extensionId", Value.String "morphir-dotnet.fsharp"
              "version", Value.String "1.2.3"
              "modelOutput", Value.Object [ "profile", Value.String "morphir-ir"; "codec", Value.String "morphir-json"; "irVersion", Value.String "4.0.0" ] ]

    let operations =
        [ for i in 1..count -> Value.Object [ "operation", Value.String "compile"; "language", Value.String $"l{i}" ] ]
        @ [ Value.Object [ "operation", Value.String "generate"; "target", Value.String "fsharp" ] ]

    recipe
    |> withMember "operations" (Value.Array operations)
    |> withMember "hosts" (Value.Array [ host |> withMember "builtIns" (Value.Array([ for i in 1..count -> compile i ] @ generate)) ])
    |> JsonText.toBytes

let private requestFor (choices: int) =
    let choice (i: int) = $"[[toolset.providers]]\noperation = \"compile\"\nlanguage = \"l{i}\"\ndelivery = \"built-in\"\n"

    Encoding.UTF8.GetBytes(
        "schemaVersion = \"0.1.0-draft.1\"\n[host]\nimplementation = \"dotnet\"\ndeployment = \"native-aot\"\nchannel = \"stable\"\n[toolset]\npreset = \"fsharp\"\n"
        + String.concat "" [ for i in 1..choices -> choice i ]
    )

let private ok (what: string) (result: Result<'T, 'E>) =
    match result with
    | Ok value -> value
    | Error error -> failtestf "%s: %A" what error

[<Tests>]
let tests =
    testList "Review hardening" [
        testCase "a recipe of 100,000 operations is refused in linear time" (fun () ->
            // The repeated-operation check compared each operation with every earlier one.
            let recipe = parsed (Fixtures.bytes "recipe/fsharp-synthetic.json")
            let operations = [ for i in 1..100_000 -> Value.Object [ "operation", Value.String "compile"; "language", Value.String $"l{i}" ] ]
            let bytes = JsonText.toBytes (withMember "operations" (Value.Array operations) recipe)

            match withinBudget 20.0 "reading 100,000 operations" (fun () -> RecipeDocument.read bytes) with
            | Error refusal ->
                // No offering performs l1, so the candidate rule refuses it, after the repetition check has run.
                Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (ReaderCode.InconsistentDocument, "operations[0]") "no candidate"
            | Ok _ -> failtest "an operation without a candidate was read")

        testCase "a recipe of 1,000 unfit providers and 100,000 MEP versions is read in linear time" (fun () ->
            // Every fit check scanned the offering's MEP versions.
            let recipe = parsed (recipeWithEntries "protocolVersions" (fun i -> $"0.2.0-{i}") 100_000)
            let providers = arrayItems (memberOf "providers" recipe)

            let unfit =
                [ for i in 1..1_000 ->
                      providers.Head
                      |> withMember "version" (Value.String $"0.7.{i}")
                      |> withMember "protocolVersion" (Value.String "0.2.9") ]

            let bytes = JsonText.toBytes (withMember "providers" (Value.Array(unfit @ providers)) recipe)

            match withinBudget 20.0 "reading 1,000 unfit providers" (fun () -> RecipeDocument.read bytes) with
            | Ok recipe -> Expect.equal recipe.Providers.Length 1_003 "every provider"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "a request of 100,000 provider choices reads in linear time" (fun () ->
            let bytes = requestFor 100_000

            match withinBudget 20.0 "reading 100,000 choices" (fun () -> RequestDocument.read bytes) with
            | Ok request -> Expect.equal request.Toolset.Providers.Length 100_000 "every choice"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "50,000 built-in operations read, resolve, lock, bind and plan in linear time" (fun () ->
            // The candidate rule, resolution, the closure checks, the lock against its recipe and the plan against its
            // lock each looked an operation up by a list scan.
            withinBudget 60.0 "50,000 built-in operations" (fun () ->
                let recipe = RecipeDocument.read (manyBuiltIns 50_000) |> ok "the recipe"
                let request = RequestDocument.read (requestFor 50_000) |> ok "the request"
                let target = TargetPath.tryParse "toolchains/recipes/fsharp/1.0.0.json" |> ok "target"
                let repository = Sha256.tryParse (String.replicate 64 "b") |> ok "repository"
                let lock, _ = Resolution.lock recipe repository target request Platform.WinX64 |> ok "the lock"
                Bindings.lockMatchesRecipe lock recipe repository |> ok "the lock binds its recipe"

                let digested, _ =
                    Planning.plan
                        { Recipe = recipe
                          RecipeRepository = repository
                          RecipeTarget = target
                          Request = request
                          Platform = Platform.WinX64
                          Sizes = fun _ -> Some { DownloadBytes = 1UL; InstalledBytes = 1UL }
                          MetadataRefresh = MetadataRefresh.Required }
                    |> ok "the plan"

                let result = ResultDocument.create Command.Setup Outcome.Planned (Some digested) None [] |> ok "the result"
                let read = ResultDocument.read (JsonText.toBytes (ResultDocument.value result)) |> ok "the result reads"
                Expect.isSome read.Plan "the plan reads back against its lock")
            |> ignore)

        testCase "a lock whose closure has 100,000 providers reads in linear time" (fun () ->
            let lock = parsed (Fixtures.bytes "lock/built-in-frontend.json")
            let platforms = memberOf "platforms" lock
            let platform, closure = (objectMembers platforms).Head
            let provider = (arrayItems (memberOf "providers" closure)).Head

            let providers =
                [ for i in 1..100_000 -> provider |> withMember "language" (Value.String $"l{i}") ]

            let edited =
                lock |> withMember "platforms" (Value.Object [ platform, closure |> withMember "providers" (Value.Array providers) ])

            let bytes = JsonText.toBytes edited

            match withinBudget 20.0 "reading 100,000 locked providers" (fun () -> LockDocument.read bytes) with
            | Ok lock -> Expect.equal (lock.Platforms.Items.Head |> snd).Providers.Length 100_000 "every provider"
            | Error refusal -> failtestf "refused: %A" refusal)
        testCase "a recipe offering with 100,000 MEP versions reads in linear time" (fun () ->
            // Review of #582, R1: the duplicate check compared each element with every earlier one.
            let bytes = recipeWithEntries "protocolVersions" (fun i -> $"0.2.0-{i}") 100_000
            Expect.isLessThan bytes.Length (16 * 1024 * 1024) "within the recipe bound"

            match withinBudget 20.0 "reading 100,000 protocolVersions" (fun () -> RecipeDocument.read bytes) with
            | Ok recipe -> Expect.equal recipe.Hosts.Head.ProtocolVersions.Length 100_001 "every version"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "a recipe offering with 100,000 WASM ABIs reads in linear time" (fun () ->
            let bytes = recipeWithEntries "wasmAbis" (fun i -> $"abi-{i}") 100_000

            match withinBudget 20.0 "reading 100,000 wasmAbis" (fun () -> RecipeDocument.read bytes) with
            | Ok recipe -> Expect.equal recipe.Hosts.Head.WasmAbis.Length 100_000 "every ABI"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "a repeated entry among 100,000 is still refused at the repetition" (fun () ->
            let bytes = recipeWithEntries "protocolVersions" (fun i -> if i = 100_000 then "0.2.0-1" else $"0.2.0-{i}") 100_000

            match withinBudget 20.0 "refusing a repetition among 100,000" (fun () -> RecipeDocument.read bytes) with
            | Error refusal ->
                Expect.equal
                    (refusal.Code, DocumentPath.render refusal.Path)
                    (ReaderCode.InconsistentDocument, "hosts[0].protocolVersions[99999]")
                    "the repetition"
            | Ok _ -> failtest "a repeated version was read")
        testList "a refused critical path is reported exactly as written" [
            for critical in [ ".host"; "..x"; "host."; "a..b"; "platforms.*.unknownMember" ] ->
                testCase critical (fun () ->
                    Expect.equal
                        (refusal (LockDocument.read (lockWithCritical $"\"{critical}\"")))
                        ("unsupported_critical_member", critical)
                        "the diagnostic path is the critical entry")
        ]

        testList "a result's diagnostic path round-trips exactly" [
            for path in [ ".x"; "a..b"; "providers[0].identity"; "x."; "" ] ->
                testCase $"'{path}'" (fun () ->
                    match ResultDocument.read (resultWithPath path) with
                    | Ok read ->
                        let again = ResultDocument.read (ResultDocument.write read) |> Result.map _.Diagnostics

                        match again with
                        | Ok [ { Detail = DiagnosticDetail.UnsupportedContract(_, _, written) } ] ->
                            Expect.equal (DocumentPath.render written) path "the path text"
                        | other -> failtestf "unexpected %A" other
                    | Error refusal -> failtestf "refused: %A" refusal)
        ]

        testList "deep nesting is refused before any parser recurses" [
            let approved = Encoding.UTF8.GetString(Fixtures.bytes "request/approved.toml")

            for name, text in
                [ "TOML arrays 10,000 deep", "a = " + String.replicate 10000 "[" + String.replicate 10000 "]"
                  "TOML inline tables 10,000 deep", "a = " + String.replicate 10000 "{b = " + "1" + String.replicate 10000 "}"
                  "TOML arrays 65 deep", "a = " + String.replicate 65 "[" + String.replicate 65 "]" ] ->
                testCase name (fun () ->
                    Expect.equal (refusal (Syntax.parseToml (Encoding.UTF8.GetBytes text))) ("invalid_document", "") "refused at the document")

            yield testCase "TOML arrays 60 deep still parse" (fun () ->
                let text = "a = " + String.replicate 60 "[" + String.replicate 60 "]"
                Expect.isOk (Syntax.parseToml (Encoding.UTF8.GetBytes text)) "within the bound")

            yield testCase "JSON 10,000 deep" (fun () ->
                let text = String.replicate 10000 "{\"a\":" + "1" + String.replicate 10000 "}"
                Expect.equal (refusal (Syntax.parseJson (Encoding.UTF8.GetBytes text))) ("invalid_document", "") "refused at the document")

            yield testCase "brackets inside TOML strings and comments do not count" (fun () ->
                let deep = String.replicate 10000 "["
                let text =
                    approved
                    + $"\nnote = \"{deep}\"\nliteral = '{deep}'\nmulti = \"\"\"\n{deep}\"\"\"\"\nraw = '''{deep}'''\n# {deep}\n"
                Expect.isOk (RequestDocument.read (Encoding.UTF8.GetBytes(text.Replace("[host]", "x = 1\n[host]")))) "a valid request")
        ]
        testCase "U+FFFE, which string.Normalize refuses, is its own NFC and never stops a document" (fun () ->
            let noncharacter = string (char 0xFFFE)
            let decomposed = "e" + string (char 0x0301)
            let precomposed = string (char 0x00E9)

            match Canonical.bytes (Value.String("a" + noncharacter + decomposed)) with
            | Ok bytes ->
                Expect.equal (Encoding.UTF8.GetString bytes) ("\"a" + noncharacter + precomposed + "\"") "normalized around it"
            | Error refusal -> failtestf "refused: %A" refusal

            Expect.isOk (TargetPath.tryParse ("notes/a" + noncharacter + ".txt")) "an NFC target path that holds it"
            Expect.equal (TargetPath.tryParse ("notes/a" + noncharacter + decomposed) |> Result.map ignore) (Error ReaderCode.NonPortablePath) "and one that is not NFC")
        testList "a built-in with a protocol member is inconsistent_document at that member" [
            // toolchain-lock.md and installation-receipt.md: a built-in has no MEP session. The fixtures pin
            // protocolVersion in a lock and modelTuple in a recipe; these pin the other members and the receipt.
            let tuple =
                "{\"operation\": \"morphir.frontend.compileModel\", \"profile\": \"morphir-ir\", \"codec\": \"morphir-json\", \"envelopeVersion\": \"0.2.0\", \"irVersion\": \"4.0.0\"}"

            let withMember (fixture: string) (name: string) (value: string) =
                let text = Encoding.UTF8.GetString(Fixtures.bytes fixture)
                let marker = "\"delivery\": \"built-in\","
                let at = text.IndexOf marker
                Expect.isGreaterThan at 0 "the first built-in"
                Encoding.UTF8.GetBytes(text.Insert(at + marker.Length, $" \"{name}\": {value},"))

            let refusedAt (read: byte array -> Result<'T, ReaderRefusal>) (bytes: byte array) (path: string) =
                match read bytes with
                | Error refusal -> Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (ReaderCode.InconsistentDocument, path) path
                | Ok _ -> failtestf "a built-in with %s was read" path

            for name, value in [ "protocolVersion", "\"0.2.0\""; "modelTuple", tuple ] do
                yield
                    testCase $"{name} in a lock" (fun () ->
                        refusedAt LockDocument.read (withMember "lock/built-in-frontend.json" name value) $"platforms.win-x64.providers[0].{name}")

                yield
                    testCase $"{name} in a receipt" (fun () ->
                        refusedAt ReceiptDocument.read (withMember "receipt/win-x64-qualified.json" name value) $"providers[1].identity.{name}")
        ]
        testCase "a host plan component whose source names another artifact is inconsistent_document at source.target" (fun () ->
            // manager-result.md: the host component's source target is that platform's host artifact, at source.target.
            let text = Encoding.UTF8.GetString(Fixtures.bytes "result/setup-plan.json")
            let artifact = "artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip"
            // The first mention is the host component's source; the plan's lock keeps the locked artifact.
            let at = text.IndexOf artifact
            Expect.isGreaterThan at 0 "the host artifact"
            let edited = text.Substring(0, at) + "artifacts/morphir-dotnet-native-aot/1.2.3/other.zip" + text.Substring(at + artifact.Length)

            match ResultDocument.read (Encoding.UTF8.GetBytes edited) with
            | Error refusal ->
                Expect.equal
                    (refusal.Code, DocumentPath.render refusal.Path)
                    (ReaderCode.InconsistentDocument, "plan.components[0].source.target")
                    "refused at the host's source target"
            | Ok _ -> failtest "a host source naming another artifact was read")
        testCase "a host plan component with a recipe-digest source is inconsistent_document at its source" (fun () ->
            // manager-result.md: a host's source is repository-target or external-owner. No fixture pins recipe-digest.
            let text = Encoding.UTF8.GetString(Fixtures.bytes "result/setup-plan.json")
            let at = text.IndexOf "\"repository-target\""
            Expect.isGreaterThan at 0 "the host's source"
            let edited = text.Substring(0, at) + "\"recipe-digest\"" + text.Substring(at + "\"repository-target\"".Length)

            match ResultDocument.read (Encoding.UTF8.GetBytes edited) with
            | Error refusal ->
                Expect.equal
                    (refusal.Code, DocumentPath.render refusal.Path)
                    (ReaderCode.InconsistentDocument, "plan.components[0].source")
                    "refused at the host's source"
            | Ok _ -> failtest "a host authenticated by the recipe's digest was read")
        testCase "only the exact draft text is supported; build metadata is refused" (fun () ->
            Expect.equal (ContractVersion.check "0.1.0-draft.1") (Ok()) "the draft"
            Expect.equal (ContractVersion.check "0.1.0-draft.1+meta") (Error ReaderCode.UnsupportedContractVersion) "build metadata"
            Expect.equal (ContractVersion.check "0.1.0-draft.01") (Error ReaderCode.InvalidValue) "not SemVer")

        testList "mirrors need an explicit http or https scheme on every operating system" [
            for text, expected in
                [ "https://mirror.example.invalid/morphir/", Ok()
                  "HTTPS://mirror.example.invalid/", Ok()
                  "http://mirror.internal.invalid/morphir/", Ok()
                  "/srv/mirror", Error ReaderCode.InvalidValue
                  "file:///srv/mirror", Error ReaderCode.InvalidValue
                  @"c:\mirror", Error ReaderCode.InvalidValue
                  "c:/mirror", Error ReaderCode.InvalidValue
                  "ftp://mirror.example.invalid/", Error ReaderCode.InvalidValue
                  "https:///no-host", Error ReaderCode.InvalidValue
                  "mirror.example.invalid", Error ReaderCode.InvalidValue
                  "https://ada:s3cret@mirror.example.invalid/", Error ReaderCode.CredentialInDocument
                  "https://mirror.example.invalid/?token=x", Error ReaderCode.CredentialInDocument
                  "https://mirror.example.invalid/#top", Error ReaderCode.CredentialInDocument ] ->
                testCase text (fun () -> Expect.equal (Mirror.tryParse text |> Result.map ignore) expected text)
        ]
    ]
