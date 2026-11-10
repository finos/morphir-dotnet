module Morphir.Extensions.Tests.RegistryTests

open Expecto
open Morphir.IR.Model
open Morphir.Extensions

let private version text =
    match SourceFormatVersion.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad version {text}: {error}"

let private host = {
    Host = MorphirHost.Native
    Version = ReleaseVersion.Create "0.4.0"
}

let private backend (id: string) =
    ExtensionEntry.BuiltInBackend(
        ExtensionIdentity.Create(
            ExtensionId.Create id,
            ExtensionName.Create $"{id} generator",
            host.Version,
            null
        ),
        {
            Targets = NonEmptySet.create (TargetId.Create "csharp") []
            IrVersions = NonEmptySet.create (version "4.0.0") [ version "3" ]
        }
    )

let private formatVersion text =
    match Morphir.IR.Versioning.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad format version {text}: {error}"

let private validator (id: string) =
    ExtensionEntry.BuiltInValidator(
        ExtensionIdentity.Create(ExtensionId.Create id, ExtensionName.Create $"{id} analyzer", host.Version, null),
        ValidatorClaim.Create(formatVersion "1", formatVersion "2", formatVersion "3")
    )

let private catalogOf entries =
    match ExtensionCatalog.create host entries with
    | Ok catalog -> catalog
    | Error error -> failtest $"refused: {error}"

[<Tests>]
let tests =
    testList "Registry" [
        test "hosts, deliveries and types have MEP and CLI wire values" {
            Expect.equal MorphirHost.Native.WireValue "morphir" "native"
            Expect.equal MorphirHost.DotnetTool.WireValue "dotnet-morphir" "dotnet tool"
            Expect.equal ExtensionDelivery.BuiltIn.WireValue "built-in" "built-in"

            Expect.equal
                ([
                    ExtensionType.Frontend
                    ExtensionType.Backend
                    ExtensionType.Transform
                    ExtensionType.Validator
                    ExtensionType.Workspace
                 ]
                 |> List.map (fun item -> item.WireValue))
                [ "frontend"; "backend"; "transform"; "validator"; "workspace" ]
                "MEP extension types"
        }

        test "an entry for all hosts is available in both hosts" {
            let entry = backend "morphir.csharp"
            Expect.isTrue (entry.IsAvailableIn(MorphirHost.Native, WasmRuntimeAvailability.Available)) "native"
            Expect.isTrue (entry.IsAvailableIn(MorphirHost.DotnetTool, WasmRuntimeAvailability.Available)) "dotnet tool"

            Expect.equal
                (List.ofSeq entry.Hosts.WireValues)
                [ "morphir"; "dotnet-morphir" ]
                "both hosts"
        }

        test "an entry for one host is available only there" {
            let entry =
                { backend "acme.frontend" with
                    Hosts = HostAvailability.Only MorphirHost.DotnetTool
                }

            Expect.isFalse (entry.IsAvailableIn(MorphirHost.Native, WasmRuntimeAvailability.Available)) "not native"
            Expect.isTrue (entry.IsAvailableIn(MorphirHost.DotnetTool, WasmRuntimeAvailability.Available)) "dotnet tool"
            Expect.equal (List.ofSeq entry.Hosts.WireValues) [ "dotnet-morphir" ] "one host"
        }

        test "a built-in backend entry derives its type from its capability" {
            let entry = backend "morphir.csharp"
            Expect.equal entry.Delivery ExtensionDelivery.BuiltIn "built-in"
            Expect.equal entry.Hosts HostAvailability.AllHosts "all hosts"
            Expect.equal (List.ofSeq entry.Types) [ ExtensionType.Backend ] "backend"
            Expect.isNotNull entry.BackendClaimOrNull "backend claim"
            Expect.isNull entry.Identity.DescriptionOrNull "no description"
        }

        test "the catalog orders entries by id with ordinal comparison" {
            let catalog =
                catalogOf [
                    backend "morphir.fsharp"
                    backend "acme.lint"
                    backend "morphir.csharp"
                ]

            Expect.equal
                (catalog.Entries
                 |> Seq.map (fun item -> item.Identity.Id.Value)
                 |> List.ofSeq)
                [ "acme.lint"; "morphir.csharp"; "morphir.fsharp" ]
                "ordinal order"

            Expect.equal catalog.Host host "host"
        }

        test "the catalog refuses duplicate ids and names each one" {
            let entries = [
                backend "morphir.csharp"
                backend "morphir.csharp"
                backend "morphir.fsharp"
                backend "morphir.fsharp"
                backend "acme.lint"
            ]

            match ExtensionCatalog.create host entries with
            | Ok _ -> failtest "accepted duplicates"
            | Error(RegistryError.DuplicateIds ids) ->
                Expect.equal
                    (ids
                     |> Seq.map (fun item -> item.Value)
                     |> List.ofSeq)
                    [ "morphir.csharp"; "morphir.fsharp" ]
                    "ids"

                let diagnostics = (RegistryError.DuplicateIds ids).Diagnostics

                Expect.equal
                    (diagnostics
                     |> Seq.map (fun item -> item.Code.WireValue)
                     |> List.ofSeq
                     |> List.distinct)
                    [ "extension.duplicate_id" ]
                    "code"

                Expect.stringContains diagnostics[0].Message "morphir.csharp" "names the first id"
                Expect.stringContains diagnostics[1].Message "morphir.fsharp" "names the second id"
        }

        test "a validator entry derives its type from its validator claim" {
            let entry = validator "morphir.type-validator"
            Expect.equal (List.ofSeq entry.Types) [ ExtensionType.Validator ] "validator"
            Expect.isNull entry.BackendClaimOrNull "no backend claim"

            match entry.ValidatorClaimOrNull with
            | null -> failtest "no validator claim"
            | claim ->
                Expect.equal (List.ofSeq claim.IrVersionTexts) [ "1"; "2"; "3" ] "classic versions in reader spelling"
                Expect.isTrue (claim.Reads(formatVersion "3")) "reads 3"
                Expect.isFalse (claim.Reads(formatVersion "4.0.0")) "does not read 4.0.0"

            Expect.equal (string entry.Capabilities) "validator (1, 2, 3)" "capability text"
            Expect.equal entry.CapabilitySummary "validator (1, 2, 3)" "capability summary"
        }

        test "a backend capability summary is its type" {
            Expect.equal (backend "morphir.csharp").CapabilitySummary "backend" "backend summary"
        }

        test "a validator claim keeps semantic pre-release versions" {
            let claim = ValidatorClaim.Create(formatVersion "4.0.0-rc.1")
            Expect.equal (List.ofSeq claim.IrVersionTexts) [ "4.0.0-rc.1" ] "pre-release kept"
        }

        test "a validator claim with no IR version cannot be built" {
            Expect.isError (ValidatorClaim.ofSeq []) "F# callers get an error"

            let ok, claim = ValidatorClaim.TryCreate([])
            Expect.isFalse ok "C# callers get false"
            Expect.isTrue (obj.ReferenceEquals(claim, null)) "no claim"

            let ok, claim = ValidatorClaim.TryCreate([ formatVersion "2"; formatVersion "2" ])
            Expect.isTrue ok "one distinct version"
            Expect.equal (List.ofSeq claim.IrVersionTexts) [ "2" ] "duplicates collapse"
        }

        test "a validator claim parses IR version texts and refuses bad ones" {
            match ValidatorClaim.Parse [ "3"; "1"; "4.0.0-rc.1" ] with
            | Ok claim -> Expect.equal (List.ofSeq claim.IrVersionTexts) [ "1"; "3"; "4.0.0-rc.1" ] "parsed"
            | Error failure -> failtest $"refused: {failure}"

            Expect.isError (ValidatorClaim.Parse []) "no version"
            Expect.isError (ValidatorClaim.Parse [ "3"; null ]) "null text"

            match ValidatorClaim.Parse [ "3"; "three" ] with
            | Error(ParseFailure.InvalidVersion(field, _)) -> Expect.equal field "IR version" "field"
            | other -> failtest $"unexpected {other}"

            let ok, claim, failure = ValidatorClaim.TryParse([ "2" ])
            Expect.isTrue ok "C# parse"
            Expect.equal (List.ofSeq claim.IrVersionTexts) [ "2" ] "C# claim"
            Expect.isTrue (obj.ReferenceEquals(failure, null)) "no failure"
        }

        test "TryCreate gives the catalog or the error" {
            let ok, catalog, error =
                ExtensionCatalog.TryCreate(host, [ backend "morphir.csharp" ])

            Expect.isTrue ok "created"
            Expect.equal catalog.Entries.Count 1 "one entry"
            Expect.isTrue (obj.ReferenceEquals(error, null)) "no error"
        }
    ]
