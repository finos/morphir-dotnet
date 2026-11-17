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
/// An entry of a context table: its spelling exactly as the file writes it, and the value that spelling reads as.
/// </summary>
/// <remarks>
/// A <see cref="Name"/> holds words, so it cannot give back every spelling: <c>FR2052A</c> and <c>FR-2052-a</c> read
/// as one value. A v4 position therefore expands to <see cref="Spelling"/>, and only a classic word-array position
/// uses <see cref="Value"/>.
/// </remarks>
type TableEntry<'v> = {
    /// <summary>The spelling as the table writes it, such as <c>FR2052A</c>.</summary>
    Spelling: string
    /// <summary>The value the spelling reads as.</summary>
    Value: 'v
}

/// <summary>
/// The file-local name tables of a compacted Morphir file (the parent's <c>ir/name-compaction</c>): each alias id with
/// the spelling it stands for, by the type of position it may fill.
/// </summary>
type FileContext = {
    /// <summary>The <c>names</c> table: aliases of names, such as <c>invoice-line</c>.</summary>
    Names: Map<string, TableEntry<Name>>
    /// <summary>The <c>packagePaths</c> table: aliases of package paths, such as <c>my-org/billing</c>.</summary>
    PackagePaths: Map<string, TableEntry<Path>>
    /// <summary>The <c>modulePaths</c> table: aliases of module paths, such as <c>invoicing</c>.</summary>
    ModulePaths: Map<string, TableEntry<Path>>
    /// <summary>The <c>fqNames</c> table: aliases of fully qualified names, such as <c>morphir/SDK:basics#int</c>.</summary>
    FqNames: Map<string, TableEntry<FQName>>
    /// <summary>The member paths that the file's <c>critical</c> list says a reader must understand.</summary>
    Critical: string list
}

/// <summary>The limits a reader puts on a file's context before it expands any reference.</summary>
type ContextLimits =
    {
        /// <summary>The most entries one table may hold.</summary>
        MaxEntries: int
        /// <summary>The most UTF-8 bytes one expanded spelling may have.</summary>
        MaxNameLength: int
        /// <summary>
        /// The deepest nesting of JSON that a file may have, as it is written and once its references are expanded. A
        /// file deeper than this is refused with <c>depth_limit_exceeded</c>.
        /// </summary>
        MaxDepth: int
    }

    /// <summary>The same limits with <paramref name="depth"/> as <see cref="MaxDepth"/>; for C# callers.</summary>
    member this.WithMaxDepth(depth: int) : ContextLimits = { this with MaxDepth = depth }

/// <summary>Functions for <see cref="ContextLimits"/>.</summary>
[<RequireQualifiedAccess>]
module ContextLimits =
    /// <summary>
    /// The limits of <c>ir-context</c> 0.1.0-draft.1: 16,384 entries per table and 1,024 UTF-8 bytes per spelling
    /// (the parent's <c>docs/spec/ir/context/examples/README.md</c>), with the nesting depth of the readers of a
    /// document tree (<c>V4JsonDecoding.MaxDepth</c>, 512).
    /// </summary>
    let draft1: ContextLimits = {
        MaxEntries = 16_384
        MaxNameLength = 1_024
        MaxDepth = Morphir.Json.V4.V4JsonDecoding.MaxDepth
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

    /// <summary>
    /// Whether a text is a name by the grammar of the parent's <c>docs/spec/draft/names.md</c>: segments joined by
    /// <c>-</c>, each a word (<c>[a-z0-9]+</c>) or an initialism (<c>[A-Z0-9]+</c>). A name's canonical form writes each
    /// segment as it is read, so every text the grammar accepts is canonical.
    /// </summary>
    let isName (text: string) =
        let isAscii (accepts: char -> bool) (segment: string) =
            segment
            |> Seq.forall (fun c -> accepts c || Char.IsAsciiDigit c)

        let isSegment (segment: string) =
            segment.Length > 0
            && (isAscii Char.IsAsciiLetterLower segment
                || isAscii Char.IsAsciiLetterUpper segment)

        text.Split('-')
        |> Array.forall isSegment

    /// Whether a text is a path: names joined by <c>/</c>.
    let isPath (text: string) =
        text.Split('/')
        |> Array.forall isName

    /// Whether a text is an FQName: <c>package:module#name</c>, with exactly one <c>:</c> and then one <c>#</c>.
    let isFqName (text: string) =
        match text.Split(':') with
        | [| package; rest |] ->
            match rest.Split('#') with
            | [| modulePath; local |] ->
                isPath package
                && isPath modulePath
                && isName local
            | _ -> false
        | _ -> false

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

    /// <summary>
    /// One table: each id with its spelling, within the limits. A spelling is admitted when
    /// <paramref name="isSpelling"/> accepts it by the grammar of its table's type (<paramref name="kind"/>), and is
    /// kept as written beside the value <paramref name="parse"/> reads it as.
    /// </summary>
    let private table
        (limits: ContextLimits)
        file
        (tableName: string)
        (kind: string)
        (isSpelling: string -> bool)
        (parse: string -> Result<'v, string>)
        (element: JsonElement)
        : Map<string, TableEntry<'v>> * StoreError list =
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
                    elif not (isSpelling spelling) then
                        Error(error "context.invalid_entry" file $"{where}.{id} is '{spelling}', which is not {kind}.")
                    else
                        match parse spelling with
                        | Ok parsed -> Ok(id, { Spelling = spelling; Value = parsed })
                        | Error message ->
                            Error(
                                error
                                    "context.invalid_entry"
                                    file
                                    $"{where}.{id} is '{spelling}', which is not {kind}: {message}"
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
    /// duplicate members, malformed or over-limit tables, and spellings that are not of their table's type. Unknown
    /// members of the context are ignored.
    /// </summary>
    let read (limits: ContextLimits) file (critical: string list) (context: JsonElement) : FileContext * StoreError list =
        if context.ValueKind <> JsonValueKind.Object then
            empty critical, [ error "context.invalid_envelope" file "The envelope's context is not an object." ]
        else
            let members, duplicates = membersOnce file "The context" context

            let tableOf name kind isSpelling parse =
                match
                    members
                    |> List.tryFind (fun (key, _) -> key = name)
                with
                | Some(_, element) -> table limits file name kind isSpelling parse element
                | None -> Map.empty, []

            let name = "a name: words and initialisms joined by '-'"
            let path = "a path: names joined by '/'"
            let fqName = "an FQName: package:module#name"
            let names, nameProblems = tableOf "names" name isName CanonicalNames.parseName
            let packagePaths, packageProblems = tableOf "packagePaths" path isPath CanonicalNames.parsePath
            let modulePaths, moduleProblems = tableOf "modulePaths" path isPath CanonicalNames.parsePath
            let fqNames, fqNameProblems = tableOf "fqNames" fqName isFqName CanonicalNames.parseFQName

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
