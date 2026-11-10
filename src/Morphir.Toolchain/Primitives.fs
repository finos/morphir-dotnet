// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Text
open System.Text.RegularExpressions
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

/// A provider identity: portable tokens joined by <c>.</c>, at most 238 characters, such as <c>morphir.fsharp</c>.
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
    let tryParse (text: string) =
        let mutable version = Unchecked.defaultof<SemVersion>

        if SemVersion.TryParse(text, SemVersionStyles.Strict, &version) && version.ToString() = text then
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

        if not (SemVersion.TryParse(text, SemVersionStyles.Strict, &version)) then
            Error ReaderCode.InvalidValue
        elif String.Equals(text, current, StringComparison.Ordinal) then
            Ok()
        else
            Error ReaderCode.UnsupportedContractVersion
/// One SemVer comparator, such as <c>&gt;=10.0.0</c>, parsed by the Semver library's npm range syntax.
[<CustomEquality; NoComparison>]
type Comparator =
    private
    | Comparator of text: string * range: SemVersionRange

    member this.Text =
        let (Comparator(text, _)) = this
        text

    /// The requirement as the Semver library models it.
    member this.Range =
        let (Comparator(_, range)) = this
        range

    override this.ToString() = this.Text

    override this.Equals(other) =
        match other with
        | :? Comparator as comparator -> String.Equals(this.Text, comparator.Text, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Text

module Comparator =
    // One comparator, never a range: an optional operator and one full or partial version, as the schemas state.
    let private single =
        Regex(
            @"^(?:=|>=|<=|>|<|~|\^)?(0|[1-9][0-9]*)(?:\.(0|[1-9][0-9]*|\*)(?:\.(0|[1-9][0-9]*|\*)(?:-[0-9A-Za-z.-]+)?)?)?$",
            RegexOptions.CultureInvariant
        )

    let tryParse (text: string) =
        let mutable range = Unchecked.defaultof<SemVersionRange>

        if single.IsMatch text && SemVersionRange.TryParseNpm(text, &range) then
            Ok(Comparator(text, range))
        else
            Error ReaderCode.InvalidValue

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

    /// The target path, or <c>non_portable_path</c>.
    let tryParse (text: string) =
        let hasForbiddenCharacter =
            text |> Seq.exists (fun c -> c <= '\u001F' || c = '\u2028' || c = '\u2029')

        if
            text.Length > 0
            && text.Length <= 240
            && Encoding.UTF8.GetByteCount text <= 512
            && not hasForbiddenCharacter
            && text.Split('/') |> Array.forall isPortableComponent
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

/// A MEP version a host speaks or a provider uses.
[<RequireQualifiedAccess>]
type ProtocolVersion =
    /// The frozen legacy MEP <c>0.1</c>, which never enables model transport.
    | Mep01
    /// MEP <c>0.2.0-draft.1</c>.
    | Mep020Draft1

module ProtocolVersion =
    let tryParse (text: string) =
        match text with
        | "0.1" -> Ok ProtocolVersion.Mep01
        | "0.2.0-draft.1" -> Ok ProtocolVersion.Mep020Draft1
        | _ -> Error ReaderCode.UnsupportedCase

    let text (version: ProtocolVersion) =
        match version with
        | ProtocolVersion.Mep01 -> "0.1"
        | ProtocolVersion.Mep020Draft1 -> "0.2.0-draft.1"

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

/// The model a built-in or MEP 0.1 provider reads.
type ModelInput = { Profile: Token; Codec: Token; IrVersion: SemVer }

/// A MEP 0.2.0-draft.1 model tuple; its five members are compared exactly.
type ModelTuple =
    { Operation: ModelOperation
      Profile: Token
      Codec: Token
      EnvelopeVersion: SemVer
      IrVersion: SemVer }

/// The protocol members of an external provider: the MEP version and exactly one compatibility member.
[<RequireQualifiedAccess>]
type Protocol =
    /// MEP 0.1 with the model it reads.
    | Mep01 of input: ModelInput
    /// MEP 0.2.0-draft.1 with its model tuple.
    | Mep020Draft1 of tuple: ModelTuple

module Protocol =
    let version (protocol: Protocol) =
        match protocol with
        | Protocol.Mep01 _ -> ProtocolVersion.Mep01
        | Protocol.Mep020Draft1 _ -> ProtocolVersion.Mep020Draft1

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
