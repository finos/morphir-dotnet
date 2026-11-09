namespace Morphir.Compilation

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Morphir.IR
open Morphir.IR.Model

/// <summary>Why a component identity was refused.</summary>
[<RequireQualifiedAccess>]
type ComponentIdFailure =
    /// <summary>The text is null or empty.</summary>
    | Empty
    /// <summary>The zero-based dot-separated segment is not a valid token.</summary>
    | InvalidSegment of segment: int

    override this.ToString() =
        match this with
        | Empty -> "A component identity is empty."
        | InvalidSegment segment ->
            $"Component identity segment {segment} is not a lowercase token of letters, digits, '_' or '-'."

/// <summary>
/// The stable identity of a writer, builder or project-file provider, for example <c>msbuild</c> or
/// <c>morphir-dotnet.classic-json</c>: lowercase tokens joined by dots.
/// </summary>
[<CustomEquality; CustomComparison>]
type ComponentId =
    private
    | ComponentId of string

    /// <summary>The identity text.</summary>
    member this.Value = let (ComponentId value) = this in value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? ComponentId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ComponentId as id -> String.CompareOrdinal(this.Value, id.Value)
            | _ -> invalidArg (nameof other) "Not a ComponentId."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<ComponentId, ComponentIdFailure> =
        match text with
        | null -> Error ComponentIdFailure.Empty
        | value when value.Length = 0 -> Error ComponentIdFailure.Empty
        | value ->
            let isToken (segment: string) =
                segment.Length > 0
                && Char.IsAsciiLetterLower segment[0]
                && segment
                   |> Seq.forall (fun c ->
                       Char.IsAsciiLetterLower c
                       || Char.IsAsciiDigit c
                       || c = '_'
                       || c = '-'
                   )

            match
                value.Split '.'
                |> Array.tryFindIndex (
                    isToken
                    >> not
                )
            with
            | Some index -> Error(ComponentIdFailure.InvalidSegment index)
            | None -> Ok(ComponentId value)

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<ComponentId>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<ComponentIdFailure>)
        : bool =
        match ComponentId.Parse text with
        | Ok id ->
            result <- id
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds an identity from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : ComponentId =
        match ComponentId.Parse literal with
        | Ok id -> id
        | Error error -> invalidArg (nameof literal) (error.ToString())

/// <summary>How a classic writer spells a type or value that has no documentation.</summary>
[<RequireQualifiedAccess>]
type AbsentDocumentation =
    /// <summary>Writes the member without a documentation wrapper. The output is smaller. This is the default.</summary>
    | Omit
    /// <summary>
    /// Writes the member in a <c>{"doc": "", "value": ...}</c> wrapper, as morphir-elm does. A reader then sees
    /// empty documentation. IR v4 always omits absent documentation.
    /// </summary>
    | EmptyWrapper

    override this.ToString() =
        match this with
        | Omit -> "omit"
        | EmptyWrapper -> "empty wrapper"

/// <summary>How a writer lays out its JSON.</summary>
type WriteOptions =
    {
        /// <summary>True for two-space indented output; false for compact output.</summary>
        Indented: bool
        /// <summary>How a classic writer spells a type or value that has no documentation.</summary>
        AbsentDocumentation: AbsentDocumentation
    }

    /// <summary>Compact output that omits absent documentation.</summary>
    static member Compact = {
        Indented = false
        AbsentDocumentation = AbsentDocumentation.Omit
    }

    /// <summary>The same options with <paramref name="indented"/> layout. For C# callers.</summary>
    member this.WithIndented(indented: bool) = { this with Indented = indented }

    /// <summary>The same options with another spelling of absent documentation. For C# callers.</summary>
    member this.WithAbsentDocumentation(absent: AbsentDocumentation) =
        { this with
            AbsentDocumentation = absent
        }

    override this.ToString() =
        let layout = if this.Indented then "indented" else "compact"
        $"{layout}, absent documentation: {this.AbsentDocumentation}"

/// <summary>Writes one IR family as JSON for the exact format versions it declares (VER-1).</summary>
type IIrWriter =
    /// <summary>A stable identity for reports and diagnostics.</summary>
    abstract Identity: ComponentId
    /// <summary>The family the writer accepts.</summary>
    abstract Family: ModelFamily
    /// <summary>The exact versions the writer writes. Never empty.</summary>
    abstract Versions: IReadOnlyList<Versioning.FormatVersion>

    /// <summary>Writes <paramref name="model"/> for <paramref name="target"/>.</summary>
    abstract Write:
        target: IrTarget * options: WriteOptions * model: FamilyModel -> Checked<byte array>

/// <summary>
/// A writer chosen for a requested version, with the exact target it serves. Only
/// <c>WriterRegistry.Resolve</c> builds one, so the target always names a version the writer declares.
/// </summary>
type ResolvedWriter =
    private
        {
            WriterValue: IIrWriter
            TargetValue: IrTarget
        }

    /// <summary>The chosen writer.</summary>
    member this.Writer = this.WriterValue

    /// <summary>The target: the writer's family and the requested version.</summary>
    member this.Target = this.TargetValue

    override this.ToString() =
        $"{this.WriterValue.Identity} for {this.TargetValue}"

/// <summary>The diagnostic codes of the registries.</summary>
[<RequireQualifiedAccess>]
module RegistryCodes =
    /// <summary>No writer declares the requested version.</summary>
    let UnsupportedIrVersion = DiagnosticCode.Create "compile.unsupported_ir_version"
    /// <summary>More than one writer declares the requested version.</summary>
    let AmbiguousIrWriter = DiagnosticCode.Create "compile.ambiguous_ir_writer"
    /// <summary>No builder produces the requested family.</summary>
    let UnsupportedIrFamily = DiagnosticCode.Create "compile.unsupported_ir_family"
    /// <summary>More than one builder produces the requested family.</summary>
    let AmbiguousIrBuilder = DiagnosticCode.Create "compile.ambiguous_ir_builder"
    /// <summary>No provider recognizes the project file.</summary>
    let UnknownProjectFile = DiagnosticCode.Create "sources.unknown_project_file"
    /// <summary>More than one provider recognizes the project file.</summary>
    let AmbiguousProjectFile = DiagnosticCode.Create "sources.ambiguous_project_file"

/// <summary>Selects the one writer that declares a requested format version.</summary>
[<Sealed>]
type WriterRegistry private (writers: IIrWriter list) =

    /// <summary>Builds a registry. Throws <c>ArgumentException</c> for a writer that declares no version.</summary>
    static member Create(writers: IEnumerable<IIrWriter>) =
        let writers = List.ofSeq writers

        match
            writers
            |> List.tryFind (fun writer -> writer.Versions.Count = 0)
        with
        | Some writer ->
            invalidArg (nameof writers) $"Writer {writer.Identity} declares no format version."
        | None -> WriterRegistry writers

    /// <summary>Every declared version, in registration then declaration order.</summary>
    member _.DeclaredVersions: IReadOnlyList<Versioning.FormatVersion> =
        [|
            for writer in writers do
                yield! writer.Versions
        |]
        :> IReadOnlyList<_>

    /// <summary>
    /// The writer whose declared versions contain <paramref name="requested"/> under the equivalence rule.
    /// No match fails with <c>compile.unsupported_ir_version</c>; more than one fails with
    /// <c>compile.ambiguous_ir_writer</c>.
    /// </summary>
    member this.Resolve(requested: Versioning.FormatVersion) : Checked<ResolvedWriter> =
        let text = Versioning.version requested

        let candidates =
            writers
            |> List.filter (fun writer ->
                writer.Versions
                |> Seq.exists (FormatVersion.sameVersion requested)
            )

        match candidates with
        | [ writer ] ->
            Checked.pass
                {
                    WriterValue = writer
                    TargetValue = {
                        Family = writer.Family
                        Version = requested
                    }
                }
        | [] ->
            let declared =
                this.DeclaredVersions
                |> Seq.map Versioning.version
                |> String.concat ", "

            Checked.fail [
                Diagnostic.error
                    RegistryCodes.UnsupportedIrVersion
                    $"No IR writer declares format version {text}. Declared versions: {declared}."
            ]
        | many ->
            let names =
                many
                |> List.map (fun writer -> writer.Identity.Value)
                |> String.concat ", "

            Checked.fail [
                Diagnostic.error
                    RegistryCodes.AmbiguousIrWriter
                    $"More than one IR writer declares format version {text}: {names}."
            ]

/// <summary>Why a source selection was refused.</summary>
[<RequireQualifiedAccess>]
type SourceSelectionFailure =
    /// <summary>The root is null or empty.</summary>
    | EmptyRoot
    /// <summary>There is no document.</summary>
    | NoDocuments
    /// <summary>A document path is not portable and root-relative.</summary>
    | NotPortable of path: string
    /// <summary>A document path appears more than once.</summary>
    | Repeated of path: string

    override this.ToString() =
        match this with
        | EmptyRoot -> "A source selection needs a root."
        | NoDocuments -> "A source selection needs at least one document."
        | NotPortable path -> $"The document path '{path}' is not portable and root-relative."
        | Repeated path -> $"The document path '{path}' repeats."

/// <summary>
/// Ordered source documents and the root they are relative to, independent of language and tool. It becomes
/// the MEP <c>SourceSet</c>.
/// </summary>
type SourceSelection =
    private
        {
            RootValue: string
            DocumentsValue: string list
            ProjectFileValue: string option
        }

    /// <summary>The root that every document is relative to.</summary>
    member this.Root = this.RootValue

    /// <summary>The documents, root-relative with '/' separators, in compilation order.</summary>
    member this.Documents: IReadOnlyList<string> =
        List.toArray this.DocumentsValue :> IReadOnlyList<_>

    /// <summary>The project file the documents came from, or null. For C# callers.</summary>
    member this.ProjectFileOrNull = Option.toObj this.ProjectFileValue

    override this.ToString() =
        $"{List.length this.DocumentsValue} document(s) under {this.RootValue}"

    /// <summary>
    /// Builds a selection from untrusted input. Refuses an empty root, an empty document list, and a document
    /// path that is not portable and root-relative or that repeats. For F# callers.
    /// </summary>
    static member Parse
        (root: string | null, documents: IEnumerable<string>, projectFile: string | null)
        : Result<SourceSelection, SourceSelectionFailure> =
        let documents = List.ofSeq documents

        let portable (path: string) =
            path.Length > 0
            && not (path.Contains '\\')
            && not (path.Contains ':')
            && not (path.StartsWith '/')
            && path.Split '/'
               |> Array.forall (fun segment ->
                   segment.Length > 0
                   && segment
                      <> "."
                   && segment
                      <> ".."
               )

        match root with
        | null
        | "" -> Error SourceSelectionFailure.EmptyRoot
        | root ->
            if List.isEmpty documents then
                Error SourceSelectionFailure.NoDocuments
            else
                match
                    documents
                    |> List.tryFind (
                        portable
                        >> not
                    )
                with
                | Some path -> Error(SourceSelectionFailure.NotPortable path)
                | None ->
                    match
                        documents
                        |> List.countBy id
                        |> List.tryFind (fun (_, count) -> count > 1)
                    with
                    | Some(path, _) -> Error(SourceSelectionFailure.Repeated path)
                    | None ->
                        Ok
                            {
                                RootValue = root
                                DocumentsValue = documents
                                ProjectFileValue = Option.ofObj projectFile
                            }

    /// <summary>Builds a selection from untrusted input. For C# callers.</summary>
    static member TryParse
        (root: string | null, documents: IEnumerable<string>, projectFile: string | null,
                                                                                    [<Out;
                                                                                      MaybeNullWhen(false)>] result:
                                                                                        byref<SourceSelection>,
                                                                                    [<Out;
                                                                                      MaybeNullWhen(true)>] failure:
                                                                                        byref<SourceSelectionFailure>)
        : bool =
        match SourceSelection.Parse(root, documents, projectFile) with
        | Ok selection ->
            result <- selection
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds a selection from literals in source code. Throws <c>ArgumentException</c> on invalid input.</summary>
    static member Create
        (root: string, documents: IEnumerable<string>, projectFile: string | null)
        : SourceSelection =
        match SourceSelection.Parse(root, documents, projectFile) with
        | Ok selection -> selection
        | Error error -> invalidArg (nameof documents) (error.ToString())

/// <summary>Expands a build-tool project file into ordered sources. Selected by file pattern, never by language.</summary>
type IProjectFileProvider =
    /// <summary>A stable identity, for example <c>msbuild</c>.</summary>
    abstract Identity: ComponentId
    /// <summary>File-name patterns with <c>*</c> and <c>?</c>, for example <c>*.*proj</c>.</summary>
    abstract Patterns: IReadOnlyList<string>

    /// <summary>Expands <paramref name="projectFile"/>, an absolute path.</summary>
    abstract ExpandAsync:
        projectFile: string * cancellationToken: CancellationToken -> Task<Checked<SourceSelection>>

/// <summary>Selects the one provider whose pattern matches a project file name.</summary>
[<Sealed>]
type ProjectFileProviderRegistry private (providers: IProjectFileProvider list) =

    static let rec matches (pattern: string) (name: string) (p: int) (n: int) =
        if p = pattern.Length then
            n = name.Length
        else
            match pattern[p] with
            | '*' ->
                matches pattern name (p + 1) n
                || (n < name.Length
                    && matches pattern name p (n + 1))
            | '?' ->
                n < name.Length
                && matches pattern name (p + 1) (n + 1)
            | c ->
                n < name.Length
                && Char.ToUpperInvariant c = Char.ToUpperInvariant name[n]
                && matches pattern name (p + 1) (n + 1)

    /// <summary>Builds a registry.</summary>
    static member Create(providers: IEnumerable<IProjectFileProvider>) =
        ProjectFileProviderRegistry(List.ofSeq providers)

    /// <summary>True when the file name of <paramref name="path"/> matches <paramref name="pattern"/>, ignoring case.</summary>
    static member Matches(pattern: string, path: string) =
        match IO.Path.GetFileName path with
        | null -> false
        | name -> matches pattern name 0 0

    /// <summary>
    /// The provider for <paramref name="projectFile"/>. No match fails with <c>sources.unknown_project_file</c>;
    /// more than one fails with <c>sources.ambiguous_project_file</c>. Both name the candidates.
    /// </summary>
    member _.Resolve(projectFile: string) : Checked<IProjectFileProvider> =
        let candidates =
            providers
            |> List.filter (fun provider ->
                provider.Patterns
                |> Seq.exists (fun pattern ->
                    ProjectFileProviderRegistry.Matches(pattern, projectFile)
                )
            )

        match candidates with
        | [ provider ] -> Checked.pass provider
        | [] ->
            let patterns =
                providers
                |> List.collect (fun p -> List.ofSeq p.Patterns)
                |> String.concat ", "

            Checked.fail [
                Diagnostic.error
                    RegistryCodes.UnknownProjectFile
                    $"No project-file provider recognizes '{projectFile}'. Registered patterns: {patterns}."
            ]
        | many ->
            let names =
                many
                |> List.map (fun provider -> provider.Identity.Value)
                |> String.concat ", "

            Checked.fail [
                Diagnostic.error
                    RegistryCodes.AmbiguousProjectFile
                    $"More than one project-file provider recognizes '{projectFile}': {names}."
            ]

// The builder registry is for F# frontends only, like the other builder contracts.
namespace Morphir.Compilation.Building

open System.Collections.Generic
open Morphir.IR.Model
open Morphir.Compilation

/// <summary>Selects the one builder for a family.</summary>
[<Sealed>]
type FamilyBuilderRegistry private (builders: IFamilyBuilder list) =

    /// <summary>Builds a registry.</summary>
    static member Create(builders: IEnumerable<IFamilyBuilder>) =
        FamilyBuilderRegistry(List.ofSeq builders)

    /// <summary>
    /// The builder for <paramref name="family"/>. No match fails with <c>compile.unsupported_ir_family</c>;
    /// more than one fails with <c>compile.ambiguous_ir_builder</c>.
    /// </summary>
    member _.Resolve(family: ModelFamily) : Checked<IFamilyBuilder> =
        let name =
            match family with
            | ModelFamily.Classic -> "classic"
            | ModelFamily.V4 -> "v4"

        match
            builders
            |> List.filter (fun builder -> builder.Family = family)
        with
        | [ builder ] -> Checked.pass builder
        | [] ->
            Checked.fail [
                Diagnostic.error
                    RegistryCodes.UnsupportedIrFamily
                    $"No IR builder produces the {name} family."
            ]
        | _ ->
            Checked.fail [
                Diagnostic.error
                    RegistryCodes.AmbiguousIrBuilder
                    $"More than one IR builder produces the {name} family."
            ]
