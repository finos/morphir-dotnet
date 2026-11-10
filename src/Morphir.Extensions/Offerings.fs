namespace Morphir.Extensions

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Morphir.Compilation

/// <summary>How a host runs: native code, or managed code that the .NET runtime compiles just in time.</summary>
/// <remarks>The wire values are those of the toolset recipe's <c>execution</c> member.</remarks>
[<RequireQualifiedAccess>]
type ExecutionMode =
    /// <summary>Native code compiled ahead of time; no .NET runtime and no dynamic code.</summary>
    | NativeAot
    /// <summary>Managed code on the .NET runtime.</summary>
    | Jit

    /// <summary>The wire value: <c>native-aot</c> or <c>jit</c>.</summary>
    member this.WireValue =
        match this with
        | NativeAot -> "native-aot"
        | Jit -> "jit"

    /// <summary>
    /// How this process runs. Native AOT code cannot compile dynamic code, so this reads
    /// <see cref="RuntimeFeature.IsDynamicCodeSupported"/>; it uses no reflection.
    /// </summary>
    static member OfThisProcess =
        if RuntimeFeature.IsDynamicCodeSupported then Jit else NativeAot

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>When an SDK prerequisite is needed: to install a delivery, or to run it.</summary>
[<RequireQualifiedAccess>]
type SdkPhase =
    /// <summary>Only to install the delivery, for example <c>dotnet tool install</c>.</summary>
    | Install
    /// <summary>Whenever the delivery runs.</summary>
    | Run

    /// <summary>The wire value: <c>install</c> or <c>run</c>.</summary>
    member this.WireValue =
        match this with
        | Install -> "install"
        | Run -> "run"

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>An installed .NET runtime that a delivery needs, with SemVer comparators for its versions.</summary>
/// <remarks>Only <see cref="Deployment.Prerequisites"/> declares one, so the representation is private.</remarks>
type RuntimePrerequisite =
    private
        {
            name: string
            versions: string array
        }

    /// <summary>The shared framework, for example <c>Microsoft.NETCore.App</c>.</summary>
    member this.Name = this.name

    /// <summary>Single SemVer comparators that all hold, for example <c>&gt;=10.0.0</c> and <c>&lt;11.0.0</c>.</summary>
    member this.Versions: IReadOnlyList<string> = this.versions

    /// <summary>The name and versions.</summary>
    override this.ToString() =
        $"""{this.Name} {String.Join(" ", this.Versions)}"""

/// <summary>A .NET SDK that a delivery needs, and when.</summary>
/// <remarks>Only <see cref="Deployment.Prerequisites"/> declares one, so the representation is private.</remarks>
type SdkPrerequisite =
    private
        {
            name: string
            versions: string array
            phase: SdkPhase
        }

    /// <summary>The SDK, for example <c>Microsoft.NET.Sdk</c>.</summary>
    member this.Name = this.name

    /// <summary>Single SemVer comparators that all hold.</summary>
    member this.Versions: IReadOnlyList<string> = this.versions

    /// <summary>When the SDK is needed.</summary>
    member this.Phase = this.phase

    /// <summary>The name, versions and phase.</summary>
    override this.ToString() =
        $"""{this.Name} {String.Join(" ", this.Versions)} ({this.Phase})"""

/// <summary>
/// What a delivery needs from the machine, in the toolset recipe's prerequisite kinds. Native-library prerequisites
/// are not declared here: no platform has established them yet.
/// </summary>
type Prerequisites =
    private
        {
            runtime: RuntimePrerequisite array
            sdk: SdkPrerequisite array
        }

    /// <summary>The .NET runtimes needed to run; empty when the delivery carries its runtime or is native code.</summary>
    member this.Runtime: IReadOnlyList<RuntimePrerequisite> = this.runtime

    /// <summary>The .NET SDKs needed to install or run.</summary>
    member this.Sdk: IReadOnlyList<SdkPrerequisite> = this.sdk

    /// <summary>Nothing from .NET.</summary>
    static member None: Prerequisites = { runtime = [||]; sdk = [||] }

    /// <summary>True when nothing is needed.</summary>
    member this.IsEmpty = this.Runtime.Count = 0 && this.Sdk.Count = 0

    /// <summary>The prerequisites, or <c>none</c>.</summary>
    override this.ToString() =
        if this.IsEmpty then
            "none"
        else
            String.Join("; ", Seq.append (this.Runtime |> Seq.map string) (this.Sdk |> Seq.map string))

/// <summary>
/// One of the four .NET deployment offerings (approved DEP-1). The wire values are those of the approved change and
/// the toolset recipe's <c>deployment</c> member. C# builds one with the generated properties, for example
/// <c>Deployment.NativeAot</c>.
/// </summary>
[<RequireQualifiedAccess>]
type Deployment =
    /// <summary>The Native AOT <c>morphir</c>.</summary>
    | NativeAot
    /// <summary>The managed <c>morphir</c> with its own .NET runtime.</summary>
    | SelfContained
    /// <summary>The managed <c>morphir</c> apphost on an installed .NET runtime.</summary>
    | FrameworkDependent
    /// <summary>The <c>Morphir.Tool</c> package, command <c>dotnet-morphir</c>.</summary>
    | DotnetTool

    /// <summary>The wire value, for example <c>native-aot</c>.</summary>
    member this.WireValue =
        match this with
        | NativeAot -> "native-aot"
        | SelfContained -> "self-contained"
        | FrameworkDependent -> "framework-dependent"
        | DotnetTool -> "dotnet-tool"

    /// <summary>The four offerings in the fixed order of the approved change.</summary>
    static member All: IReadOnlyList<Deployment> = [| NativeAot; SelfContained; FrameworkDependent; DotnetTool |]

    /// <summary>How the offering runs.</summary>
    member this.Execution =
        match this with
        | NativeAot -> ExecutionMode.NativeAot
        | SelfContained
        | FrameworkDependent
        | DotnetTool -> ExecutionMode.Jit

    /// <summary>
    /// What the offering needs from the machine. The runtime is the shared framework that the projects' runtime
    /// configuration names, <c>Microsoft.NETCore.App</c> 10.0 with the default roll-forward, so any later 10.x serves.
    /// The tool needs the SDK only to install it.
    /// </summary>
    member this.Prerequisites: Prerequisites =
        let runtime: RuntimePrerequisite = { name = "Microsoft.NETCore.App"; versions = [| ">=10.0.0"; "<11.0.0" |] }

        match this with
        | NativeAot
        | SelfContained -> Prerequisites.None
        | FrameworkDependent -> { runtime = [| runtime |]; sdk = [||] }
        | DotnetTool ->
            {
                runtime = [| runtime |]
                sdk = [| { name = "Microsoft.NET.Sdk"; versions = [| ">=10.0.100" |]; phase = SdkPhase.Install } |]
            }

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>A first-delivery platform, by its .NET runtime identifier.</summary>
[<RequireQualifiedAccess>]
type Platform =
    /// <summary>Windows x64.</summary>
    | WinX64
    /// <summary>Windows ARM64.</summary>
    | WinArm64
    /// <summary>macOS x64.</summary>
    | OsxX64
    /// <summary>macOS ARM64.</summary>
    | OsxArm64
    /// <summary>Linux glibc x64.</summary>
    | LinuxX64
    /// <summary>Linux glibc ARM64.</summary>
    | LinuxArm64

    /// <summary>The runtime identifier, the platform id of the contract pages.</summary>
    member this.WireValue =
        match this with
        | WinX64 -> "win-x64"
        | WinArm64 -> "win-arm64"
        | OsxX64 -> "osx-x64"
        | OsxArm64 -> "osx-arm64"
        | LinuxX64 -> "linux-x64"
        | LinuxArm64 -> "linux-arm64"

    /// <summary>The six platforms in the order of the contract pages' platform table.</summary>
    static member All: IReadOnlyList<Platform> = [| WinX64; WinArm64; OsxX64; OsxArm64; LinuxX64; LinuxArm64 |]

    /// <summary>The runtime identifier.</summary>
    override this.ToString() = this.WireValue

/// <summary>
/// What built the running executable: a named release profile, which states its deployment and, unless the artifact is
/// portable, its platform; or no profile, as in a development build, a test host or a build that no named profile
/// produced. It is fixed at build time, never inferred from how the process runs.
/// </summary>
[<RequireQualifiedAccess>]
type BuildProfile =
    /// <summary>A named release profile built this executable.</summary>
    | Named of deployment: Deployment * platform: Platform option
    /// <summary>No named release profile built this executable; it states no deployment.</summary>
    | Unnamed

    /// <summary>For C# callers: a profile for one platform.</summary>
    static member Of(deployment: Deployment, platform: Platform) = Named(deployment, Some platform)

    /// <summary>For C# callers: a profile of a portable artifact, such as the tool package.</summary>
    static member Portable(deployment: Deployment) = Named(deployment, None)

    /// <summary>For C# callers: no profile.</summary>
    static member NoProfile = Unnamed

    /// <summary>The deployment, or none. For F# in this assembly; C# reads <c>HostIdentity.DeploymentOrNull</c>.</summary>
    member internal this.Deployment =
        match this with
        | Named(deployment, _) -> Some deployment
        | Unnamed -> None

    /// <summary>The platform, or none. For F# in this assembly; C# reads <c>HostIdentity.PlatformOrNull</c>.</summary>
    member internal this.Platform =
        match this with
        | Named(_, platform) -> platform
        | Unnamed -> None

    /// <summary>The deployment and platform, or <c>no profile</c>.</summary>
    override this.ToString() =
        match this with
        | Named(deployment, Some platform) -> $"{deployment} {platform}"
        | Named(deployment, None) -> deployment.WireValue
        | Unnamed -> "no profile"

/// <summary>Why an extension is not available in the running offering, for people. Never empty.</summary>
[<CustomEquality; CustomComparison>]
type UnavailableReason =
    private
    | UnavailableReason of string

    /// <summary>The text.</summary>
    member this.Value =
        let (UnavailableReason text) = this
        text

    /// <summary>
    /// Parses untrusted text: not empty, at most 1024 characters, no white space at its ends and no control character.
    /// For F# callers.
    /// </summary>
    static member Parse(text: string | null) : Result<UnavailableReason, ParseFailure> =
        Text.display "unavailable reason" 1024 text |> Result.map UnavailableReason

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<UnavailableReason>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) : bool =
        match UnavailableReason.Parse text with
        | Ok reason ->
            result <- reason
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>A reason from a literal in source code. Throws <see cref="ArgumentException"/> on invalid text.</summary>
    static member Create(literal: string) : UnavailableReason =
        match UnavailableReason.Parse literal with
        | Ok reason -> reason
        | Error failure -> invalidArg (nameof literal) (Text.refusedLiteral "unavailable reason" literal failure)

    override this.Equals(other: obj | null) =
        match other with
        | :? UnavailableReason as reason -> String.Equals(this.Value, reason.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? UnavailableReason as reason -> String.CompareOrdinal(this.Value, reason.Value)
            | _ -> invalidArg (nameof other) "Not an unavailable reason."

    /// <summary>The text.</summary>
    override this.ToString() = this.Value

/// <summary>
/// The offerings of a release in which an extension is available. An extension that some offering lacks always says
/// why, so a list without a reason cannot exist.
/// </summary>
[<RequireQualifiedAccess>]
type EntryDeployments =
    /// <summary>All four offerings.</summary>
    | Every
    /// <summary>Only these offerings; <c>reason</c> explains the others.</summary>
    | Only of deployments: NonEmptySet<Deployment> * reason: UnavailableReason

    /// <summary>For C# callers: only these offerings, with the reason for the others.</summary>
    static member OnlyIn(deployments: NonEmptySet<Deployment>, reason: UnavailableReason) = Only(deployments, reason)

    /// <summary>The offerings, in the fixed order of <see cref="Deployment.All"/>.</summary>
    member this.Deployments: IReadOnlyList<Deployment> =
        match this with
        | Every -> Deployment.All
        | Only(deployments, _) -> [| for deployment in Deployment.All do if deployments.Contains deployment then deployment |]

    /// <summary>True when the extension is available in the offering.</summary>
    member this.Contains(deployment: Deployment) =
        match this with
        | Every -> true
        | Only(deployments, _) -> deployments.Contains deployment

    /// <summary>The reason for the offerings that lack the extension, or none when every offering has it. For F#; C# reads <see cref="ReasonOrNull"/>.</summary>
    member this.Reason =
        match this with
        | Every -> None
        | Only(_, reason) -> Some reason

    /// <summary>For C# callers: the reason for the offerings that lack the extension, or null when every offering has it.</summary>
    member this.ReasonOrNull: UnavailableReason | null = Option.toObj this.Reason

    /// <summary>The offerings.</summary>
    override this.ToString() =
        String.Join(", ", this.Deployments |> Seq.map (fun deployment -> deployment.WireValue))

/// <summary>A language that a frontend reads, with its file extensions in their declared spelling.</summary>
[<CustomEquality; CustomComparison>]
type FrontendLanguage =
    private
        {
            language: LanguageId
            fileExtensions: string array
        }

    /// <summary>The language id.</summary>
    member this.Language = this.language

    /// <summary>The file extensions, each with its leading dot, for example <c>.fs</c>.</summary>
    member this.FileExtensions: IReadOnlyList<string> = this.fileExtensions

    /// <summary>
    /// Parses file extensions that may come from outside: at least one, each a dot followed by at least one character.
    /// For F# callers.
    /// </summary>
    static member Parse(language: LanguageId, fileExtensions: (string | null) seq) : Result<FrontendLanguage, ParseFailure> =
        let extensions = Seq.toArray fileExtensions

        if extensions.Length = 0 then
            Error(ParseFailure.Empty "frontend file extensions")
        else
            let valid (extension: string | null) =
                match extension with
                | null -> false
                | text -> text.Length >= 2 && text[0] = '.'

            match extensions |> Array.tryFindIndex (valid >> not) with
            | Some index -> Error(ParseFailure.InvalidFileExtension("frontend file extension", index))
            | None ->
                Ok
                    {
                        language = language
                        fileExtensions = extensions |> Array.map (fun extension -> nonNull extension)
                    }

    /// <summary>Parses file extensions that may come from outside. For C# callers.</summary>
    static member TryParse
        (
            language: LanguageId,
            fileExtensions: IEnumerable<string | null>,
            [<Out; MaybeNullWhen(false)>] result: byref<FrontendLanguage>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) : bool =
        match FrontendLanguage.Parse(language, fileExtensions) with
        | Ok parsed ->
            result <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>
    /// A language from literals in source code. Throws <see cref="ArgumentException"/> when there is no file extension
    /// or one is not a dot followed by at least one character.
    /// </summary>
    static member Create(language: LanguageId, fileExtensions: string array) : FrontendLanguage =
        match FrontendLanguage.Parse(language, fileExtensions |> Seq.map (fun extension -> extension: string | null)) with
        | Ok parsed -> parsed
        | Error failure ->
            invalidArg (nameof fileExtensions) $"""Invalid frontend language literal {language} ({String.Join(", ", fileExtensions)}): {failure}"""

    member private this.Key = $"""{this.language.Value}:{String.Join("|", this.fileExtensions)}"""

    override this.Equals(other: obj | null) =
        match other with
        | :? FrontendLanguage as language -> String.Equals(this.Key, language.Key, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Key

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? FrontendLanguage as language -> String.CompareOrdinal(this.Key, language.Key)
            | _ -> invalidArg (nameof other) "Not a frontend language."

    /// <summary>The language and its file extensions.</summary>
    override this.ToString() =
        $"""{this.language} ({String.Join(", ", this.fileExtensions)})"""

/// <summary>What a frontend compiles: at least one language, and the IR format versions it produces.</summary>
type FrontendClaim =
    {
        /// <summary>The languages the frontend reads.</summary>
        Languages: NonEmptySet<FrontendLanguage>
        /// <summary>The IR format versions the frontend produces.</summary>
        IrVersions: NonEmptySet<Morphir.IR.Versioning.FormatVersion>
    }

    /// <summary>For C# callers: a claim of the languages and IR versions.</summary>
    static member Create
        (
            languages: NonEmptySet<FrontendLanguage>,
            irVersions: NonEmptySet<Morphir.IR.Versioning.FormatVersion>
        ) : FrontendClaim =
        { Languages = languages; IrVersions = irVersions }

    /// <summary>The IR versions as the model readers write them, in order.</summary>
    member this.IrVersionTexts: IReadOnlyList<string> = [|
        for version in this.IrVersions -> Morphir.IR.Versioning.version version
    |]

    /// <summary>The languages and IR versions.</summary>
    override this.ToString() =
        $"""languages {String.Join(", ", this.Languages |> Seq.map string)}; IR versions {String.Join(", ", this.IrVersionTexts)}"""
