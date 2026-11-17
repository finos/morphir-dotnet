namespace Morphir.IR.Files

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open Morphir.IR
open Morphir.IR.Trees

/// <summary>The IR family of a Morphir file's document: which JSON its typed name positions are written in.</summary>
[<RequireQualifiedAccess>]
type IrFamily =
    /// <summary>Classic v3 JSON (<c>formatVersion</c> 3 or <c>"3.1.0"</c>): names, paths and FQNames as word arrays.</summary>
    | Classic3
    /// <summary>IR v4 JSON: names, paths and FQNames as canonical strings.</summary>
    | V4

/// <summary>
/// The file-local name tables of a compacted Morphir file (the parent's <c>ir/name-compaction</c>): each alias id with
/// the spelling it stands for, by the type of position it may fill.
/// </summary>
type FileContext = {
    /// <summary>The <c>names</c> table: aliases of names, such as <c>invoice-line</c>.</summary>
    Names: Map<string, Name>
    /// <summary>The <c>packagePaths</c> table: aliases of package paths, such as <c>my-org/billing</c>.</summary>
    PackagePaths: Map<string, Path>
    /// <summary>The <c>modulePaths</c> table: aliases of module paths, such as <c>invoicing</c>.</summary>
    ModulePaths: Map<string, Path>
    /// <summary>The <c>fqNames</c> table: aliases of fully qualified names, such as <c>morphir/SDK:basics#int</c>.</summary>
    FqNames: Map<string, FQName>
    /// <summary>The member paths that the file's <c>critical</c> list says a reader must understand.</summary>
    Critical: string list
}

/// <summary>The limits a reader puts on a file's context before it expands any reference.</summary>
type ContextLimits = {
    /// <summary>The most entries one table may hold.</summary>
    MaxEntries: int
    /// <summary>The most UTF-8 bytes one expanded spelling may have.</summary>
    MaxNameLength: int
}

/// <summary>Functions for <see cref="ContextLimits"/>.</summary>
[<RequireQualifiedAccess>]
module ContextLimits =
    /// <summary>
    /// The limits of <c>ir-context</c> 0.1.0-draft.1: 16,384 entries per table and 1,024 UTF-8 bytes per spelling
    /// (the parent's <c>docs/spec/ir/context/examples/README.md</c>).
    /// </summary>
    let draft1: ContextLimits = {
        MaxEntries = 16_384
        MaxNameLength = 1_024
    }

/// Reads and checks the context of an envelope (<c>ir/name-compaction</c>): its tables, their limits and its
/// <c>critical</c> list.
module internal NameContext =
    /// The member paths that draft.1 recognizes in a <c>critical</c> list.
    let recognizedCritical = [
        "$schema"
        "context"
        "context.names"
        "context.packagePaths"
        "context.modulePaths"
        "context.fqNames"
        "critical"
        "document"
    ]

    let private error code file message : StoreError = StoreErrors.error code (Some file) message

    /// An alias id: ASCII letters and digits, starting with a letter.
    let private isAliasId (id: string) =
        id.Length > 0
        && Char.IsAsciiLetter id[0]
        && id
           |> Seq.forall Char.IsAsciiLetterOrDigit

    /// The members of an object in document order, with each name that appears more than once refused.
    let membersOnce file (where: string) (element: JsonElement) =
        let seen = HashSet<string>(StringComparer.Ordinal)

        let members = [ for property in element.EnumerateObject() -> property.Name, property.Value ]

        let duplicates =
            members
            |> List.filter (fun (name, _) -> not (seen.Add name))
            |> List.map fst
            |> List.distinct
            |> List.map (fun name ->
                error "context.duplicate_entry" file $"{where} has the member '{name}' more than once."
            )

        members, duplicates

    /// One table: each id with its spelling, parsed by <paramref name="parse"/>, within the limits.
    let private table
        (limits: ContextLimits)
        file
        (tableName: string)
        (parse: string -> Result<'v, string>)
        (print: 'v -> string)
        (element: JsonElement)
        : Map<string, 'v> * StoreError list =
        let where = $"context.{tableName}"

        if element.ValueKind <> JsonValueKind.Object then
            Map.empty, [ error "context.invalid_entry" file $"{where} is not an object." ]
        else
            let entries, duplicates = membersOnce file where element

            let count =
                if entries.Length > limits.MaxEntries then
                    [
                        error
                            "context.too_many_entries"
                            file
                            $"{where} has {entries.Length} entries, and a table holds at most {limits.MaxEntries}."
                    ]
                else
                    []

            let read (id: string, value: JsonElement) =
                if not (isAliasId id) then
                    Error(
                        error
                            "context.invalid_entry"
                            file
                            $"{where} has the alias id '{id}'; an id is ASCII letters and digits, starting with a letter."
                    )
                elif value.ValueKind <> JsonValueKind.String then
                    Error(error "context.invalid_entry" file $"{where}.{id} is not a string.")
                else
                    let spelling = nonNull (value.GetString())
                    let bytes = Encoding.UTF8.GetByteCount spelling

                    if bytes > limits.MaxNameLength then
                        Error(
                            error
                                "context.name_too_long"
                                file
                                $"{where}.{id} is {bytes} bytes long, and a spelling has at most {limits.MaxNameLength}."
                        )
                    else
                        match parse spelling with
                        | Ok parsed when print parsed = spelling -> Ok(id, parsed)
                        | Ok parsed ->
                            Error(
                                error
                                    "context.invalid_entry"
                                    file
                                    $"{where}.{id} is '{spelling}', and its canonical spelling is '{print parsed}'."
                            )
                        | Error message ->
                            Error(
                                error
                                    "context.invalid_entry"
                                    file
                                    $"{where}.{id} is not a canonical spelling of its kind: {message}"
                            )

            // The count is checked first, so an oversized table is refused without reading each entry.
            if not count.IsEmpty then
                Map.empty, duplicates @ count
            else
                let results = entries |> List.map read

                let found =
                    results
                    |> List.choose (
                        function
                        | Ok entry -> Some entry
                        | Error _ -> None
                    )
                    |> Map.ofList

                let problems =
                    results
                    |> List.choose (
                        function
                        | Error problem -> Some problem
                        | Ok _ -> None
                    )

                found, duplicates @ problems

    let empty critical : FileContext = {
        Names = Map.empty
        PackagePaths = Map.empty
        ModulePaths = Map.empty
        FqNames = Map.empty
        Critical = critical
    }

    /// <summary>
    /// The tables of a context, with the envelope's <paramref name="critical"/> list, and the problems found:
    /// duplicate members, malformed or over-limit tables, and spellings that are not canonical. Unknown members of
    /// the context are ignored.
    /// </summary>
    let read (limits: ContextLimits) file (critical: string list) (context: JsonElement) : FileContext * StoreError list =
        if context.ValueKind <> JsonValueKind.Object then
            empty critical, [ error "context.invalid_envelope" file "The envelope's context is not an object." ]
        else
            let members, duplicates = membersOnce file "The context" context

            let tableOf name parse print =
                match
                    members
                    |> List.tryFind (fun (key, _) -> key = name)
                with
                | Some(_, element) -> table limits file name parse print element
                | None -> Map.empty, []

            let names, nameProblems = tableOf "names" CanonicalNames.parseName CanonicalNames.nameToString
            let packagePaths, packageProblems = tableOf "packagePaths" CanonicalNames.parsePath CanonicalNames.pathToString
            let modulePaths, moduleProblems = tableOf "modulePaths" CanonicalNames.parsePath CanonicalNames.pathToString
            let fqNames, fqNameProblems = tableOf "fqNames" CanonicalNames.parseFQName CanonicalNames.fqNameToString

            {
                Names = names
                PackagePaths = packagePaths
                ModulePaths = modulePaths
                FqNames = fqNames
                Critical = critical
            },
            duplicates
            @ nameProblems
            @ packageProblems
            @ moduleProblems
            @ fqNameProblems
