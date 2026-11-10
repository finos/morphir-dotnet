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

/// <summary>The second review's shapes for the candidate rule (review of #583, B1), over the signed F# recipe: n
/// offerings that differ only in their implementation, each reduced to one platform and no built-ins, so that many fit
/// in a recipe. Shape A has n compile operations and one fitting <c>dotnet-tool</c> provider for each; shape B has one
/// operation, n providers whose MEP version no offering speaks, and then one that fits.</summary>
let candidateShape (shape: char) (n: int) =
    let recipe = parsed (SignedCaseTests.targetBytes "toolchains/recipes/fsharp/1.0.0.json")
    let host0 = (arrayItems (memberOf "hosts" recipe)).Head
    let firstPlatform = (objectMembers (memberOf "platforms" host0)).Head
    let dotnetProvider = arrayItems (memberOf "providers" recipe) |> List.item 1

    let host (i: int) =
        host0
        |> withMember "implementation" (Value.String $"i{i}")
        |> withMember "builtIns" (Value.Array [])
        |> withMember "platforms" (Value.Object [ firstPlatform ])

    let compile (language: string) = Value.Object [ "operation", Value.String "compile"; "language", Value.String language ]

    let operations, providers =
        match shape with
        | 'A' ->
            [ for i in 1..n -> compile $"l{i}" ],
            [ for i in 1..n ->
                  dotnetProvider |> withMember "language" (Value.String $"l{i}") |> withMember "extensionId" (Value.String $"x{i}") ]
        | _ ->
            [ compile "fsharp" ],
            [ for i in 1..n ->
                  dotnetProvider
                  |> withMember "extensionId" (Value.String $"x{i}")
                  |> withMember "protocolVersion" (Value.String "0.2.0-unspoken") ]
            @ [ dotnetProvider |> withMember "extensionId" (Value.String "fits") ]

    recipe
    |> withMember "hosts" (Value.Array [ for i in 1..n -> host i ])
    |> withMember "operations" (Value.Array operations)
    |> withMember "providers" (Value.Array providers)
    |> Canonical.bytes
    |> ok "canonical"

[<Tests>]
let tests =
    testList "Review hardening" [
        testCase "the candidate rule is linear in offerings times operations (shape A)" (fun () ->
            // Review of #583, B1: every offering was checked for every operation.
            let bytes = candidateShape 'A' 10_000
            Expect.isLessThan bytes.Length (16 * 1024 * 1024) "within the recipe bound"

            match withinBudget 10.0 "the candidate rule over 10,000 offerings and operations" (fun () -> RecipeDocument.read bytes) with
            | Ok recipe -> Expect.equal recipe.Operations.Length 10_000 "every operation"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "the candidate rule is linear in offerings times unfit providers (shape B)" (fun () ->
            let bytes = candidateShape 'B' 10_000
            Expect.isLessThan bytes.Length (16 * 1024 * 1024) "within the recipe bound"

            match withinBudget 10.0 "the candidate rule over 10,000 offerings and unfit providers" (fun () -> RecipeDocument.read bytes) with
            | Ok recipe -> Expect.equal recipe.Providers.Length 10_001 "every provider"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "an operation one offering cannot perform is still refused" (fun () ->
            // Control: the classes of offerings must not hide one that no candidate fits.
            let recipe = parsed (candidateShape 'B' 3)
            let hosts = arrayItems (memberOf "hosts" recipe)
            let deaf = hosts.Head |> withMember "protocolVersions" (Value.Array [ Value.String "0.2.0-other" ])
            let edited = recipe |> withMember "hosts" (Value.Array(hosts @ [ deaf |> withMember "implementation" (Value.String "deaf") ]))

            match RecipeDocument.read (Canonical.bytes edited |> ok "canonical") with
            | Error refusal -> Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (ReaderCode.InconsistentDocument, "operations[0]") "no candidate fits"
            | Ok _ -> failtest "an offering without a candidate was read")
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
        testCase "a host plan source with the right target and another digest is inconsistent_document at source.sha256" (fun () ->
            // Review of #582, R5: the contract author decided the target is checked first, at source.target, then the
            // digest, at source.sha256: the coming case result/negative/plan-host-source-other-digest.json.
            let text = Encoding.UTF8.GetString(Fixtures.bytes "result/setup-plan.json")
            let digest = String.replicate 64 "d"
            let at = text.IndexOf digest
            Expect.isGreaterThan at 0 "the host source digest"
            let edited = text.Substring(0, at) + String.replicate 64 "e" + text.Substring(at + digest.Length)

            match ResultDocument.read (Encoding.UTF8.GetBytes edited) with
            | Error refusal ->
                Expect.equal
                    (refusal.Code, DocumentPath.render refusal.Path)
                    (ReaderCode.InconsistentDocument, "plan.components[0].source.sha256")
                    "refused at the host's source digest"
            | Ok _ -> failtest "a host source naming another digest was read")
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

let private withoutMember (name: string) (value: Value) =
    Value.Object(objectMembers value |> List.filter (fst >> (<>) name))

let private plusMember (name: string) (added: Value) (value: Value) =
    Value.Object(objectMembers value @ [ name, added ])

/// <summary>Root's distinct-class shape for #582 (10 November 2026), over shape B: each offering adds its own MEP
/// version <c>0.2.0-host-i</c> to the common one, so no two offerings form one class, and each of the first n providers
/// has its own version that it does not fit with. With <paramref name="relevant"/>, provider i names offering i's own
/// version but as a WASM provider with an ABI no offering lists, so it reaches that offering's class and still does
/// not fit; otherwise it names <c>0.2.0-unfit-i</c>, which no offering speaks. With <paramref name="fitting"/> the last
/// provider, which fits every offering, is kept.</summary>
let distinctShape (relevant: bool) (fitting: bool) (n: int) =
    let recipe = parsed (candidateShape 'B' n)
    let hosts = arrayItems (memberOf "hosts" recipe)
    let providers = arrayItems (memberOf "providers" recipe)

    let host (i: int) (h: Value) =
        h |> withMember "protocolVersions" (Value.Array(arrayItems (memberOf "protocolVersions" h) @ [ Value.String $"0.2.0-host-{i}" ]))

    let wasm (version: string) (p: Value) =
        p
        |> withoutMember "packageId"
        |> withoutMember "command"
        |> withoutMember "packageTarget"
        |> withoutMember "packageSha256"
        |> withoutMember "prerequisites"
        |> withMember "delivery" (Value.String "wasm")
        |> withMember "protocolVersion" (Value.String version)
        |> plusMember "abi" (Value.String "absent")
        |> plusMember "artifactTarget" (Value.String "extensions/fsharp-frontend-wasm/0.6.0/morphir-bytes-v1.wasm")
        |> plusMember "artifactSha256" (Value.String(String.replicate 64 "c"))

    let unfit =
        providers
        |> List.take n
        |> List.mapi (fun i p ->
            if relevant then wasm $"0.2.0-host-{i}" p else p |> withMember "protocolVersion" (Value.String $"0.2.0-unfit-{i}"))

    recipe
    |> withMember "hosts" (Value.Array(hosts |> List.mapi host))
    |> withMember "providers" (Value.Array(if fitting then unfit @ [ List.last providers ] else unfit))
    |> Canonical.bytes
    |> ok "canonical"

/// The candidate rule as the toolset recipe page states it, offering by offering and provider by provider: the index
/// of the first operation that some offering performs with neither a built-in nor a provider that fits it.
let private referenceUnperformed (recipe: Recipe) =
    recipe.Operations
    |> List.tryFindIndex (fun operation ->
        not (
            recipe.Hosts
            |> List.forall (fun host ->
                host.BuiltIns |> List.exists (fun b -> b.Operation = operation)
                || recipe.Providers |> List.exists (fun p -> p.Operation = operation && Fit.onSomePlatform host p))
        ))

[<Tests>]
let distinctClassTests =
    let expectRead (bytes: byte array) (what: string) =
        Expect.isLessThan bytes.Length (16 * 1024 * 1024) "within the recipe bound"

        match withinBudget 10.0 what (fun () -> RecipeDocument.read bytes) with
        | Ok recipe -> Expect.equal recipe.Providers.Length 10_001 "every provider"
        | Error refusal -> failtestf "refused: %A" refusal

    let expectRefused (bytes: byte array) (what: string) =
        match withinBudget 10.0 what (fun () -> RecipeDocument.read bytes) with
        | Error refusal -> Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (ReaderCode.InconsistentDocument, "operations[0]") "no candidate fits"
        | Ok _ -> failtest "an offering without a candidate was read"

    // Root's benchmark of #582 at a856ddaa: 10,000 distinct classes and 10,000 distinct provider keys took 18.3 s on one
    // CPU, because each key was checked against every class.
    testList "The candidate rule over distinct classes and keys" [
        testCase "10,000 distinct classes and 10,000 unfit keys no offering speaks, then one that fits" (fun () ->
            expectRead (distinctShape false true 10_000) "the candidate rule over distinct classes and unspoken keys")

        testCase "without the provider that fits, the operation is refused" (fun () ->
            expectRefused (distinctShape false false 10_000) "refusing distinct classes and unspoken keys")

        testCase "10,000 distinct classes and 10,000 keys that each reach one class and do not fit, then one that fits" (fun () ->
            expectRead (distinctShape true true 10_000) "the candidate rule over distinct classes and misfit keys")

        testCase "without the provider that fits, misfit keys do not cover their classes" (fun () ->
            expectRefused (distinctShape true false 10_000) "refusing distinct classes and misfit keys")

        testCase "an offering speaking only its own version is covered by the provider that speaks it" (fun () ->
            // Control: offering 0 drops the common version, and provider 0 names offering 0's own version with a
            // delivery that fits it (dotnet-tool), so every offering still has a candidate.
            let recipe = parsed (distinctShape false true 3)
            let hosts = arrayItems (memberOf "hosts" recipe)
            let providers = arrayItems (memberOf "providers" recipe)
            let deaf = hosts.Head |> withMember "protocolVersions" (Value.Array [ Value.String "0.2.0-host-0" ])
            let speaker = providers.Head |> withMember "protocolVersion" (Value.String "0.2.0-host-0")

            let edited =
                recipe |> withMember "hosts" (Value.Array(deaf :: hosts.Tail)) |> withMember "providers" (Value.Array(speaker :: providers.Tail))

            Expect.isOk (RecipeDocument.read (Canonical.bytes edited |> ok "canonical")) "covered"

            let unheard = recipe |> withMember "hosts" (Value.Array(deaf :: hosts.Tail))
            expectRefused (Canonical.bytes unheard |> ok "canonical") "an offering no provider speaks to")

        testCase "the rule agrees with the page's offering-by-offering reading on 3,000 seeded recipes" (fun () ->
            let signed =
                match RecipeDocument.read (SignedCaseTests.targetBytes "toolchains/recipes/fsharp/1.0.0.json") with
                | Ok recipe -> recipe
                | Error refusal -> failtestf "the signed recipe: %A" refusal

            let pv (text: string) = ProtocolVersion.tryParse text |> ok text
            let token (text: string) = Token.tryParse text |> ok text
            let versions = [ pv "0.2.0-draft.1"; pv "0.2.0-a"; pv "0.2.0-b" ]
            let abis = [ token "x"; token "y" ]
            let platforms = signed.Hosts.Head.Platforms
            let operations = signed.Operations
            let builtIn = signed.Hosts |> List.collect _.BuiltIns |> List.head
            let processProvider = signed.Providers.Head
            let dotnetProvider = signed.Providers |> List.find (fun p -> RecipeProvider.delivery p = Delivery.DotnetTool)
            let binding = platforms.Head |> snd
            let random = System.Random 582

            let subset (items: 'T list) = items |> List.filter (fun _ -> random.Next 2 = 0)
            let pick (items: 'T list) = items[random.Next items.Length]

            for trial in 1..3_000 do
                let hosts =
                    [ for i in 0 .. random.Next(1, 5) ->
                          { signed.Hosts.Head with
                              Implementation = token $"h{i}"
                              ProtocolVersions = subset versions
                              WasmAbis = subset abis
                              Platforms = subset platforms
                              BuiltIns = subset operations |> List.map (fun operation -> { builtIn with Operation = operation }) } ]

                let providers =
                    [ for _ in 1 .. random.Next(0, 6) ->
                          let template, delivery =
                              match random.Next 3 with
                              | 0 -> processProvider, RecipeDelivery.Process(match subset platforms with [] -> [ platforms.Head ] | some -> some)
                              | 1 -> dotnetProvider, dotnetProvider.Delivery
                              | _ -> processProvider, RecipeDelivery.Wasm(pick abis, binding)

                          { template with
                              Operation = pick operations
                              Protocol = { template.Protocol with Version = pick versions }
                              Delivery = delivery } ]

                let recipe = { signed with Hosts = hosts; Providers = providers }
                Expect.equal (RecipeDocument.firstUnperformed recipe) (referenceUnperformed recipe) $"trial {trial}")
    ]