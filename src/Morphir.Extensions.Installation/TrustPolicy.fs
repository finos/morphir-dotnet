// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain

module private TrustDocuments =
    let schema = "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/trust.schema.json"
    let official = "https://github.com/finos/morphir"
    let path = "config/toolchains/trust.json"
    let field name value = SharedJson.members "trust" value |> SharedJson.required "trust" name |> SharedJson.text name
    let repository (text: string) =
        match Uri.TryCreate(text,UriKind.Absolute) with
        | true, uri when uri.Scheme = "https" && uri.UserInfo = "" && uri.Query = "" && uri.Fragment = "" && uri.AbsoluteUri.TrimEnd('/') = text && uri.AbsolutePath <> "/" -> text
        | _ -> Operations.fail "trust.invalid_repository" "A canonical HTTPS repository URI without credentials, query or trailing slash is required."
    let row name source minimum state = Value.Object [
        "name",Value.String name; "repository",Value.String source
        "minimumAssurance",Value.String minimum; "state",Value.String state ]
    let defaults = [row "official" official "release-checksum" "enabled"]
    let read (root: SafeDirectory) =
        StateOwnership.checkUnderLock root CancellationToken.None
        if not (File.Exists(Path.Combine(root.Path,path))) then []
        else
            use input = FileSafety.openRelative root (Path.Combine(root.Path,path))
            if input.Length > 1048576L then Operations.fail "trust.invalid_document" "Trust policy exceeds its bound."
            use memory = new MemoryStream()
            let buffer = Array.zeroCreate<byte> 4096
            let mutable count = input.Read(buffer,0,buffer.Length)
            while count <> 0 do
                if memory.Length + int64 count > 1048576L then Operations.fail "trust.invalid_document" "Trust policy grew past its bound."
                memory.Write(buffer,0,count)
                count <- input.Read(buffer,0,buffer.Length)
            let document = SharedJson.read "trust" (memory.ToArray()) |> StateOwnership.unwrap
            let items = SharedJson.members "trust" document
            SharedJson.schema "trust" schema items
            let closed allowed fields =
                if fields |> List.exists(fun (key,_) -> not (List.contains key allowed)) then
                    Operations.fail "trust.invalid_document" "Unsupported trust policy member."
            closed ["$schema";"relationships"] items
            let rows = SharedJson.required "trust" "relationships" items |> SharedJson.array "relationships"
            let names = rows |> List.map(fun value ->
                let fields = SharedJson.members "relationship" value
                closed ["name";"repository";"minimumAssurance";"state"] fields
                let name = field "name" value
                match Token.tryParse name with Ok _ -> () | Error _ -> Operations.fail "trust.invalid_document" "Invalid relationship name."
                repository (field "repository" value) |> ignore
                if not (List.contains (field "minimumAssurance" value) ["release-checksum";"publisher-authenticated"])
                   || not (List.contains (field "state" value) ["enabled";"disabled"]) then
                    Operations.fail "trust.invalid_document" "Unsupported assurance or relationship state."
                name)
            if names <> List.sort names || names.Length <> (Set.ofList names).Count then
                Operations.fail "trust.invalid_document" "Trust relationship names must be unique and sorted."
            rows
    let inspect home =
        if not (Path.IsPathFullyQualified home) then Operations.fail "trust.invalid_home" "Morphir Home must be absolute."
        if Directory.Exists home then
            use root = SafeDirectory.Open(home,false)
            read root
        else []
    let effective rows =
        (rows @ (defaults |> List.filter(fun fallback -> not (rows |> List.exists(fun row -> field "name" row = field "name" fallback)))))
        |> List.sortBy(field "name")
    let bytes rows = Value.Object ["$schema",Value.String schema;"relationships",Value.Array rows] |> JsonText.toBytes
    let change rows name source minimum remove =
        match Token.tryParse name with Ok _ -> () | Error _ -> Operations.fail "trust.invalid_name" "Invalid relationship name."
        if not (List.contains minimum ["release-checksum";"publisher-authenticated"]) then Operations.fail "trust.invalid_assurance" "Unsupported assurance minimum."
        let existing = effective rows |> List.tryFind(fun row -> field "name" row = name)
        let source =
            if remove then existing |> Option.map(field "repository") |> Option.defaultWith(fun () -> Operations.fail "trust.not_found" "No such trust relationship.")
            else repository source
        match existing with
        | Some previous when field "repository" previous <> source -> Operations.fail "trust.repository_conflict" "A stable relationship name already binds another repository."
        | _ -> ()
        let minimum = if remove then existing |> Option.map(field "minimumAssurance") |> Option.defaultValue minimum else minimum
        row name source minimum (if remove then "disabled" else "enabled") :: (rows |> List.filter(fun row -> field "name" row <> name))
        |> List.sortBy(field "name")

/// Shared local policy for checksum delivery. A requested minimum is never proof of authentication.
[<AbstractClass; Sealed>]
type internal TrustPolicy private () =
    static member DefaultHome =
        match Environment.GetEnvironmentVariable "MORPHIR_HOME" with
        | null | "" -> Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".morphir")
        | home -> home
    static member ListBytes(home: string) : byte array = TrustDocuments.inspect home |> TrustDocuments.effective |> TrustDocuments.bytes
    static member Check(home: string, repository: string) : unit =
        let rows = TrustDocuments.inspect home |> TrustDocuments.effective
        let selected =
            if repository = TrustDocuments.official then rows |> List.tryFind(fun row -> TrustDocuments.field "name" row = "official")
            else rows |> List.tryFind(fun row -> TrustDocuments.field "repository" row = repository)
        match selected with
        | None -> Operations.fail "trust.source_disabled" "The acquisition repository is not enabled by local trust policy."
        | Some row when TrustDocuments.field "state" row <> "enabled" -> Operations.fail "trust.source_disabled" "The acquisition repository is disabled by local trust policy."
        | Some row when TrustDocuments.field "minimumAssurance" row <> "release-checksum" ->
            Operations.fail "trust.assurance_not_met" "This delivery has HTTPS and release checksums. It is NOT publisher-authenticated and cannot meet the requested policy."
        | Some _ -> ()
    static member ChangeAsync(home: string, name: string, repository: string, minimum: string, remove: bool, dryRun: bool, ct: CancellationToken) : Task =
        task {
            ct.ThrowIfCancellationRequested()
            let planned = TrustDocuments.inspect home |> fun rows -> TrustDocuments.change rows name repository minimum remove
            if not dryRun then
                use root = SafeDirectory.Open(home,true)
                use locks = root.Child("locks",true)
                use! lease = locks.NamedLeaseAsync("toolchains.trust.lock",true,true,ct)
                let rows = TrustDocuments.read root |> fun rows -> TrustDocuments.change rows name repository minimum remove
                ct.ThrowIfCancellationRequested()
                FileSafety.withDirectories root ["config";"toolchains"] true (fun folder -> folder.ReplaceStateFile("trust.json",TrustDocuments.bytes rows))
            else planned |> ignore
        }
