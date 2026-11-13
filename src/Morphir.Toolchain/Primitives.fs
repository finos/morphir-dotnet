// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Text
open Semver

/// A lowercase portable token: <c>[a-z]</c>, then <c>[a-z0-9-]</c>, ending in a letter or digit, at most 64
/// characters. Implementations, deployments, presets, languages, targets, ABIs, generation ids, workflows, package
/// managers and commands are tokens.
[<CustomEquality; CustomComparison>]
type Token =
    private
    | Token of string

    member this.Text =
        let (Token text) = this
        text

    override this.ToString() = this.Text
    override this.Equals(other) =
        match other with
        | :? Token as token -> String.Equals(this.Text, token.Text, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Text

    interface IComparable with
        member this.CompareTo(other) =
            match other with
            | :? Token as token -> String.CompareOrdinal(this.Text, token.Text)
            | _ -> invalidArg (nameof other) "not a token"

module Token =
    let internal isToken (maximum: int) (text: string) =
        text.Length >= 1
        && text.Length <= maximum
        && isAsciiLower text[0]
        && Seq.forall (fun c -> isAsciiLower c || isAsciiDigit c || c = '-') text
        && text[text.Length - 1] <> '-'

    /// The token, or <c>invalid_value</c>.
    let tryParse (text: string) =
        if isToken 64 text then Ok(Token text) else Error ReaderCode.InvalidValue

    let text (token: Token) = token.Text

/// A provider identity: lowercase portable segments joined by <c>.</c>, at most
/// 238 characters. Each document reader selects its family's segment rules.
type ExtensionId =
    private
    | ExtensionId of string

    member this.Text =
        let (ExtensionId text) = this
        text

    override this.ToString() = this.Text

module ExtensionId =
    let tryParse (text: string) =
        if text.Length <= 238 && text.Split('.') |> Array.forall (Token.isToken 64) then
            Ok(ExtensionId text)
        else
            Error ReaderCode.InvalidValue

    /// The published extension-family grammar used by preview documents. The
    /// historical parser above retains its original segment bound and rules.
    let internal tryParsePreview (text: string) =
        let segments = text.Split '.'
        let first = segments[0]
        let device =
            [ "con"; "prn"; "aux"; "nul" ] |> List.contains first
            || (first.Length = 4
                && (first.StartsWith("com", StringComparison.Ordinal) || first.StartsWith("lpt", StringComparison.Ordinal))
                && first[3] >= '1' && first[3] <= '9')

        if text.Length <= 238 && not device && segments |> Array.forall (Token.isToken 238) then
            Ok(ExtensionId text)
        else
            Error ReaderCode.InvalidValue

/// A NuGet package id: segments of <c>[A-Za-z0-9_]</c> joined by <c>.</c> or <c>-</c>, at most 100 characters.
type PackageId =
    private
    | PackageId of string

    member this.Text =
        let (PackageId text) = this
        text

    override this.ToString() = this.Text

module PackageId =
    let private isSegmentChar (c: char) =
        (c >= 'A' && c <= 'Z') || isAsciiLower c || isAsciiDigit c || c = '_'

    let tryParse (text: string) =
        let separated = text.Split([| '.'; '-' |])

        if text.Length <= 100 && separated |> Array.forall (fun segment -> segment.Length > 0 && Seq.forall isSegmentChar segment) then
            Ok(PackageId text)
        else
            Error ReaderCode.InvalidValue

/// A tool release metadata v1 tool id: <c>[a-z]</c>, then <c>[a-z0-9-]</c>, ending in a letter or digit, at most 182
/// characters.
type ToolId =
    private
    | ToolId of string

    member this.Text =
        let (ToolId text) = this
        text

    override this.ToString() = this.Text

module ToolId =
    let tryParse (text: string) =
        if Token.isToken 182 text then Ok(ToolId text) else Error ReaderCode.InvalidValue

/// 64 lowercase hexadecimal digits of SHA-256 over exact bytes.
type Sha256 =
    private
    | Sha256 of string

    member this.Text =
        let (Sha256 text) = this
        text

    override this.ToString() = this.Text

module Sha256 =
    let tryParse (text: string) =
        if text.Length = 64 && Seq.forall (fun c -> isAsciiDigit c || (c >= 'a' && c <= 'f')) text then
            Ok(Sha256 text)
        else
            Error ReaderCode.InvalidValue

    /// The SHA-256 of exact bytes.
    let ofBytes (bytes: byte array) = Sha256(Digest.ofBytes bytes)

/// <summary>
/// A canonical SemVer 2.0.0 version, parsed strictly by the Semver library. Two versions are the same identity only
/// when their text is equal, so <c>1.2.3</c> and <c>1.2.3+build</c> are different releases.
/// </summary>
[<CustomEquality; NoComparison>]
type SemVer =
    private
    | SemVer of SemVersion

    member this.SemVersion =
        let (SemVer version) = this
        version

    member this.Text = this.SemVersion.ToString()
    override this.ToString() = this.Text

    override this.Equals(other) =
        match other with
        | :? SemVer as version -> String.Equals(this.Text, version.Text, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Text

module SemVer =
    /// <summary>The longest version text read: npm semver's MAX_LENGTH (index.md, Versions).</summary>
    [<Literal>]
    let MaxLength = 256

    /// <summary>The most digits in a numeric identifier (major, minor, patch, or an all-digit prerelease identifier).
    /// Every 15-digit number is a JavaScript safe integer and fits a signed 64-bit integer, so every reader can hold it
    /// exactly (index.md, Versions).</summary>
    [<Literal>]
    let MaxDigits = 15

    /// <summary>Whether text is within the bounds, checked on the text before any parser runs: Semver
    /// holds major, minor and patch as BigInteger, and parsing a million digits took about a minute. Build metadata
    /// identifiers are not numeric and are bounded only by the length.</summary>
    let withinBounds (text: string) =
        if text.Length > MaxLength then
            false
        else
            let core =
                match text.IndexOf '+' with
                | -1 -> text
                | at -> text.Substring(0, at)

            let identifiers =
                match core.IndexOf '-' with
                | -1 -> core.Split '.'
                | at -> Array.append (core.Substring(0, at).Split '.') (core.Substring(at + 1).Split '.')

            identifiers
            |> Array.forall (fun identifier -> identifier.Length <= MaxDigits || not (identifier |> Seq.forall Char.IsAsciiDigit))

    /// <summary>A strict, canonical SemVer version within the bounds, or <c>invalid_value</c>.</summary>
    let tryParse (text: string) =
        let mutable version = Unchecked.defaultof<SemVersion>

        if
            withinBounds text
            && SemVersion.TryParse(text, SemVersionStyles.Strict, &version, MaxLength)
            && version.ToString() = text
        then
            Ok(SemVer version)
        else
            Error ReaderCode.InvalidValue

/// <summary>The contract version a reader supports. A <c>0.1.0-draft.1</c> reader supports exactly that text: the
/// schemas say <c>"const": "0.1.0-draft.1"</c>, so even build metadata, which SemVer precedence ignores, makes another
/// version.</summary>
module ContractVersion =
    /// The one draft this reader supports.
    let current = "0.1.0-draft.1"

    /// <c>invalid_value</c> for text that is not strict SemVer, <c>unsupported_contract_version</c> for any other
    /// version.
    let check (text: string) =
        let mutable version = Unchecked.defaultof<SemVersion>

        if not (SemVer.withinBounds text && SemVersion.TryParse(text, SemVersionStyles.Strict, &version)) then
            Error ReaderCode.InvalidValue
        elif String.Equals(text, current, StringComparison.Ordinal) then
            Ok()
        else
            Error ReaderCode.UnsupportedContractVersion
/// The operator of a version comparator.
[<RequireQualifiedAccess>]
type ComparatorOperator =
    | Equal
    | Greater
    | GreaterOrEqual
    | Less
    | LessOrEqual

/// <summary>
/// One version comparator: exactly one of <c>=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c> or <c>&lt;=</c>, then a complete
/// version in canonical SemVer spelling, with no space. <c>^</c>, <c>~</c>, wildcards, partial versions and combined
/// ranges are <c>invalid_value</c>, because Rust, TypeScript and .NET read them differently.
/// </summary>
[<CustomEquality; NoComparison>]
type Comparator =
    private
    | Comparator of operator: ComparatorOperator * version: SemVer

    member this.Operator =
        let (Comparator(operator, _)) = this
        operator

    member this.Version =
        let (Comparator(_, version)) = this
        version

    member this.Text =
        let (Comparator(operator, version)) = this

        let symbol =
            match operator with
            | ComparatorOperator.Equal -> "="
            | ComparatorOperator.Greater -> ">"
            | ComparatorOperator.GreaterOrEqual -> ">="
            | ComparatorOperator.Less -> "<"
            | ComparatorOperator.LessOrEqual -> "<="

        symbol + version.Text

    override this.ToString() = this.Text

    override this.Equals(other) =
        match other with
        | :? Comparator as comparator -> String.Equals(this.Text, comparator.Text, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Text

module Comparator =
    // Two-character operators first, so ">=1.0.0" is never read as ">" and "=1.0.0".
    let private operators =
        [ ">=", ComparatorOperator.GreaterOrEqual
          "<=", ComparatorOperator.LessOrEqual
          ">", ComparatorOperator.Greater
          "<", ComparatorOperator.Less
          "=", ComparatorOperator.Equal ]

    let tryParse (text: string) =
        operators
        |> List.tryFind (fun (symbol, _) -> text.StartsWith(symbol, StringComparison.Ordinal))
        |> Option.map (fun (symbol, operator) ->
            SemVer.tryParse (text.Substring symbol.Length) |> Result.map (fun version -> Comparator(operator, version)))
        |> Option.defaultValue (Error ReaderCode.InvalidValue)
        |> Result.mapError (fun _ -> ReaderCode.InvalidValue)

    /// Whether a version satisfies one comparator by SemVer precedence.
    let satisfies (version: SemVer) (comparator: Comparator) =
        let order = SemVersion.ComparePrecedence(version.SemVersion, comparator.Version.SemVersion)

        match comparator.Operator with
        | ComparatorOperator.Equal -> order = 0
        | ComparatorOperator.Greater -> order > 0
        | ComparatorOperator.GreaterOrEqual -> order >= 0
        | ComparatorOperator.Less -> order < 0
        | ComparatorOperator.LessOrEqual -> order <= 0

    /// <summary>Whether an installed version meets a comparator list: it satisfies every comparator by SemVer
    /// precedence and, if it is a prerelease, at least one comparator names its own major, minor and patch with a
    /// prerelease. This is the rule of the Rust <c>semver</c> crate and of npm, so <c>[">=10.0.0", "&lt;11.0.0"]</c>
    /// is not met by <c>11.0.0-preview.1</c>. An empty list is always met.</summary>
    let allMet (comparators: Comparator list) (installed: SemVer) =
        let version = installed.SemVersion

        let namesThisPrerelease (comparator: Comparator) =
            let named = comparator.Version.SemVersion

            named.IsPrerelease
            && named.Major = version.Major
            && named.Minor = version.Minor
            && named.Patch = version.Patch

        List.forall (satisfies installed) comparators
        && (not version.IsPrerelease || comparators.IsEmpty || List.exists namesThisPrerelease comparators)

/// <summary>The toolchain contract's CLI requirement grammar (toolset recipe page, "CLI requirement grammar", at
/// finos/morphir <c>837164580</c>): one or more single comparators, each an operator directly followed by a version
/// within the version bound, separated by exactly a comma and one space, such as <c>&gt;=0.4.0-alpha.5, &lt;0.5.0</c>.
/// No other space, separator, range or wildcard is part of it, so <c>&gt;=0.4.0,&lt;0.5.0</c>, a leading space and
/// <c>^0.4.0</c> are refused. A requirement is met by the npm and Rust <c>semver</c> prerelease rule
/// (<see cref="Comparator.allMet"/>). This is a toolchain rule, stricter than the tool release metadata v1
/// profile.</summary>
module CliRequirement =
    /// The comparators of a requirement in the grammar, or None.
    let tryParse (text: string) : Comparator list option =
        if text.Length = 0 then
            None
        else
            let parsed =
                text.Split ", "
                |> Array.map (fun part ->
                    if part.Length = 0 || part.Contains ' ' || part.Contains ',' then
                        None
                    else
                        Comparator.tryParse part |> Result.toOption)

            if Array.forall Option.isSome parsed then Some(parsed |> Array.choose id |> List.ofArray) else None

/// A platform, in .NET runtime identifier spelling.
[<RequireQualifiedAccess>]
type Platform =
    | WinX64
    | WinArm64
    | OsxX64
    | OsxArm64
    | LinuxX64
    | LinuxArm64
    | LinuxMuslX64
    | LinuxMuslArm64

module Platform =
    /// Every platform in the order of the overview page's platform table, which is also the order in which readers
    /// and checks visit platforms.
    let all =
        [ Platform.WinX64, "win-x64"
          Platform.WinArm64, "win-arm64"
          Platform.OsxX64, "osx-x64"
          Platform.OsxArm64, "osx-arm64"
          Platform.LinuxX64, "linux-x64"
          Platform.LinuxArm64, "linux-arm64"
          Platform.LinuxMuslX64, "linux-musl-x64"
          Platform.LinuxMuslArm64, "linux-musl-arm64" ]

    let name (platform: Platform) =
        all |> List.find (fun (candidate, _) -> candidate = platform) |> snd

    let tryParse (text: string) =
        match all |> List.tryFind (fun (_, name) -> name = text) with
        | Some(platform, _) -> Ok platform
        | None -> Error ReaderCode.InvalidValue

    /// The position in platform table order.
    let order (platform: Platform) =
        all |> List.findIndex (fun (candidate, _) -> candidate = platform)

    let isMusl (platform: Platform) =
        match platform with
        | Platform.LinuxMuslX64
        | Platform.LinuxMuslArm64 -> true
        | _ -> false

    let isWindows (platform: Platform) =
        match platform with
        | Platform.WinX64
        | Platform.WinArm64 -> true
        | _ -> false

    /// The platforms of a preview.5 local toolchain: the two Windows platforms and macOS ARM64.
    let isLocalToolchain (platform: Platform) =
        match platform with
        | Platform.WinX64
        | Platform.WinArm64
        | Platform.OsxArm64 -> true
        | _ -> false

    /// The platform of the running process, if it is one of the eight.
    let current () =
        let architecture = Runtime.InteropServices.RuntimeInformation.OSArchitecture

        let pick x64 arm64 =
            match architecture with
            | Runtime.InteropServices.Architecture.X64 -> Some x64
            | Runtime.InteropServices.Architecture.Arm64 -> Some arm64
            | _ -> None

        if OperatingSystem.IsWindows() then
            pick Platform.WinX64 Platform.WinArm64
        elif OperatingSystem.IsMacOS() then
            pick Platform.OsxX64 Platform.OsxArm64
        elif OperatingSystem.IsLinux() then
            // .NET names the C library in its runtime identifier: linux-musl-* on musl.
            if Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier.StartsWith("linux-musl", StringComparison.Ordinal) then
                pick Platform.LinuxMuslX64 Platform.LinuxMuslArm64
            else
                pick Platform.LinuxX64 Platform.LinuxArm64
        else
            None

/// <summary>A target path naming an authenticated TUF target: relative, <c>/</c>-separated, at most 512 UTF-8 bytes
/// and 240 UTF-16 code units, every segment storable on every first-delivery platform.</summary>
type TargetPath =
    private
    | TargetPath of string

    member this.Text =
        let (TargetPath text) = this
        text

    override this.ToString() = this.Text

module TargetPath =
    let private forbidden = set [ '\\'; '<'; '>'; ':'; '"'; '|'; '?'; '*' ]

    let private isDeviceName (segment: string) =
        let stem =
            match segment.IndexOf '.' with
            | -1 -> segment
            | dot -> segment.Substring(0, dot)

        let stem = stem.TrimEnd(' ')
        let fixedNames = [ "CON"; "PRN"; "AUX"; "NUL"; "CONIN$"; "CONOUT$" ]

        let numbered prefix =
            stem.Length = 4
            && equalsAsciiIgnoreCase (stem.Substring(0, 3)) prefix
            && "123456789¹²³".Contains(stem[3])

        fixedNames |> List.exists (equalsAsciiIgnoreCase stem) || numbered "COM" || numbered "LPT"

    let private isPortableComponent (segment: string) =
        segment.Length > 0
        && segment <> "."
        && segment <> ".."
        && not (segment.EndsWith('.'))
        && not (segment.EndsWith(' '))
        && not (segment |> Seq.exists forbidden.Contains)
        && Encoding.UTF8.GetByteCount segment <= 255
        && segment.Length <= 255
        && not (isDeviceName segment)

    let private strictUtf8 = UTF8Encoding(false, true)

    /// <summary>Whether text is well-formed (no lone surrogate) and in Unicode Normalization Form C.</summary>
    let private isNfc (text: string) =
        try
            strictUtf8.GetByteCount text |> ignore
            Nfc.isNormalized text
        with :? ArgumentException ->
            false

    /// <summary>The target path, or <c>non_portable_path</c>. A target path is also NFC: canonical encoding normalizes
    /// the keys of signed targets metadata, so a path in another form would be signed under one key and looked up
    /// under another.</summary>
    let tryParse (text: string) =
        let hasForbiddenCharacter =
            text |> Seq.exists (fun c -> c <= '\u001F' || c = '\u2028' || c = '\u2029')

        if
            text.Length > 0
            && text.Length <= 240
            && Encoding.UTF8.GetByteCount text <= 512
            && not hasForbiddenCharacter
            && text.Split('/') |> Array.forall isPortableComponent
            && isNfc text
        then
            Ok(TargetPath text)
        else
            Error ReaderCode.NonPortablePath

/// Which absolute path syntax a local path must use.
[<RequireQualifiedAccess>]
type PathSyntax =
    /// A drive letter or a UNC path.
    | Windows
    /// A path starting with <c>/</c>.
    | Posix
    /// Either, as a result's project path may use.
    | Any

/// <summary>An absolute local path, as receipts and <c>show</c> results hold. No <c>\</c>- or <c>/</c>-separated
/// segment is <c>.</c> or <c>..</c>, and the path has no NUL.</summary>
type LocalPath =
    private
    | LocalPath of string

    member this.Text =
        let (LocalPath text) = this
        text

    override this.ToString() = this.Text

module LocalPath =
    let private isDrive (text: string) =
        text.Length >= 4
        && Char.IsAsciiLetter text[0]
        && text[1] = ':'
        && (text[2] = '\\' || text[2] = '/')

    let private isUnc (text: string) = text.Length >= 3 && text.StartsWith(@"\\", StringComparison.Ordinal)
    let private isPosix (text: string) = text.Length >= 2 && text[0] = '/'

    /// The local path in the given syntax, or <c>invalid_value</c>.
    let tryParse (syntax: PathSyntax) (text: string) =
        let absolute =
            match syntax with
            | PathSyntax.Windows -> isDrive text || isUnc text
            | PathSyntax.Posix -> isPosix text
            | PathSyntax.Any -> isDrive text || isUnc text || isPosix text

        let traverses =
            text.Split([| '\\'; '/' |]) |> Array.exists (fun segment -> segment = "." || segment = "..")

        if absolute && not traverses && not (text.Contains '\000') then
            Ok(LocalPath text)
        else
            Error ReaderCode.InvalidValue

    /// The syntax a receipt for this platform uses.
    let syntaxFor (platform: Platform) =
        if Platform.isWindows platform then PathSyntax.Windows else PathSyntax.Posix

/// A host acquisition channel, as in tool release metadata v1.
[<RequireQualifiedAccess>]
type Channel =
    | Stable
    | Preview
    | Insiders
    /// <c>preview/&lt;segment&gt;</c>.
    | NamedPreview of segment: string

module Channel =
    let private isSegment (segment: string) =
        segment.Length > 0
        && isAsciiLower segment[0]
        && Seq.forall (fun c -> isAsciiLower c || isAsciiDigit c || c = '-') segment
        && segment[segment.Length - 1] <> '-'

    let tryParse (text: string) =
        match text with
        | "stable" -> Ok Channel.Stable
        | "preview" -> Ok Channel.Preview
        | "insiders" -> Ok Channel.Insiders
        | _ when text.StartsWith("preview/", StringComparison.Ordinal) && isSegment (text.Substring 8) ->
            Ok(Channel.NamedPreview(text.Substring 8))
        | _ -> Error ReaderCode.InvalidValue

    let text (channel: Channel) =
        match channel with
        | Channel.Stable -> "stable"
        | Channel.Preview -> "preview"
        | Channel.Insiders -> "insiders"
        | Channel.NamedPreview segment -> "preview/" + segment

/// An operation a toolset requires: compiling a language or generating a target.
[<RequireQualifiedAccess>]
type Operation =
    | Compile of language: Token
    | Generate of target: Token

module Operation =
    let kindName (operation: Operation) =
        match operation with
        | Operation.Compile _ -> "compile"
        | Operation.Generate _ -> "generate"

    /// Text for messages, such as <c>compile fsharp</c>.
    let describe (operation: Operation) =
        match operation with
        | Operation.Compile language -> $"compile {language.Text}"
        | Operation.Generate target -> $"generate {target.Text}"

/// <summary>
/// A MEP version on the supported 0.2 line (overview page, MEP versions): in canonical SemVer spelling without build
/// metadata, major <c>0</c> and minor <c>2</c>, with no prerelease or a prerelease of <c>0.2.0</c>. So
/// <c>0.2.0-draft.1</c>, <c>0.2.0</c> and <c>0.2.3</c> are on it, and <c>0.1</c>, <c>0.2.1-rc.1</c> and <c>0.3.0</c>
/// are not. The line is defined directly, not through a range library, because range libraries differ on
/// prereleases. Membership is exact: two versions on the line are different versions.
/// </summary>
[<CustomEquality; NoComparison>]
type ProtocolVersion =
    private
    | ProtocolVersion of SemVer

    member this.Text =
        let (ProtocolVersion version) = this
        version.Text

    override this.ToString() = this.Text

    override this.Equals(other) =
        match other with
        | :? ProtocolVersion as version -> String.Equals(this.Text, version.Text, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Text

module ProtocolVersion =
    /// <summary>The version on the line. Text that is not a canonical SemVer version within the bound fails its type and
    /// is <c>invalid_value</c>, even when it would be off the line; a SemVer version off the line, build metadata
    /// included, is <c>unsupported_case</c> (index.md, MEP versions).</summary>
    let tryParse (text: string) =
        match SemVer.tryParse text with
        | Ok version ->
            let v = version.SemVersion

            if
                v.Major.IsZero
                && v.Minor = Numerics.BigInteger 2
                && v.MetadataIdentifiers.Count = 0
                && (not v.IsPrerelease || v.Patch.IsZero)
            then
                Ok(ProtocolVersion version)
            else
                Error ReaderCode.UnsupportedCase
        | Error code -> Error code

    let text (version: ProtocolVersion) = version.Text
/// The MEP model operation of a model tuple.
[<RequireQualifiedAccess>]
type ModelOperation =
    | CompileModel
    | GenerateModel

module ModelOperation =
    let tryParse (text: string) =
        match text with
        | "morphir.frontend.compileModel" -> Ok ModelOperation.CompileModel
        | "morphir.backend.generateModel" -> Ok ModelOperation.GenerateModel
        | _ -> Error ReaderCode.UnsupportedCase

    let text (operation: ModelOperation) =
        match operation with
        | ModelOperation.CompileModel -> "morphir.frontend.compileModel"
        | ModelOperation.GenerateModel -> "morphir.backend.generateModel"

    /// Whether a tuple with this model operation performs the operation: compile for compileModel, generate for
    /// generateModel.
    let performs (operation: Operation) (model: ModelOperation) =
        match operation, model with
        | Operation.Compile _, ModelOperation.CompileModel
        | Operation.Generate _, ModelOperation.GenerateModel -> true
        | _ -> false

/// <summary>The model a built-in exchanges with its host in process: the model a compile built-in writes or a
/// generate built-in reads. Readiness compares its IR version with what the staged host reports.</summary>
type ModelFormat = { Profile: Token; Codec: Token; IrVersion: SemVer }

/// A MEP 0.2 model tuple; its five members are compared exactly.
type ModelTuple =
    { Operation: ModelOperation
      Profile: Token
      Codec: Token
      EnvelopeVersion: SemVer
      IrVersion: SemVer }

/// <summary>The protocol members of an external provider: a MEP version on the 0.2 line and one model tuple. MEP
/// <c>0.1</c> is not supported, and an external provider never has <c>modelInput</c> or <c>modelOutput</c>.</summary>
type Protocol =
    { Version: ProtocolVersion
      Tuple: ModelTuple }
/// Text with at least one character that is not white space.
type NonBlankText =
    private
    | NonBlankText of string

    member this.Text =
        let (NonBlankText text) = this
        text

    override this.ToString() = this.Text

module NonBlankText =
    let tryParse (text: string) =
        if String.IsNullOrWhiteSpace text then Error ReaderCode.InvalidValue else Ok(NonBlankText text)

/// When an SDK prerequisite is needed.
[<RequireQualifiedAccess>]
type SdkPhase =
    | Install
    | Run

/// The kind of a prerequisite, with the members each kind requires.
[<RequireQualifiedAccess>]
type PrerequisiteKind =
    | Runtime of versions: Comparator list
    | Sdk of versions: Comparator list * phase: SdkPhase
    | Native of versions: Comparator list option

/// A declared runtime, SDK or native prerequisite.
type Prerequisite =
    { Kind: PrerequisiteKind
      Name: NonBlankText
      /// The platforms a prerequisite is limited to; absent means every platform.
      Platforms: Platform list option }

/// A non-empty argument vector, such as a diagnostic's instruction.
type Argv =
    private
    | Argv of string list

    member this.Items =
        let (Argv items) = this
        items

module Argv =
    /// The vector, or <c>inconsistent_document</c> when it is empty.
    let tryCreate (items: string list) =
        if List.isEmpty items then Error ReaderCode.InconsistentDocument else Ok(Argv items)

/// A launch's fixed arguments; none contains NUL.
type LaunchArguments =
    private
    | LaunchArguments of string list

    member this.Items =
        let (LaunchArguments items) = this
        items

module LaunchArguments =
    /// The arguments, or the index of the first that contains NUL.
    let tryCreate (items: string list) =
        match items |> List.tryFindIndex (fun item -> item.Contains '\000') with
        | Some index -> Error index
        | None -> Ok(LaunchArguments items)
