namespace Morphir.Configuration

/// Typed configuration trees. Secret references are opaque, indivisible values.
[<RequireQualifiedAccess>]
type ConfigValue =
    | Null
    | String of string
    | Boolean of bool
    | Integer of int64
    | Number of double
    | Array of ConfigValue list
    | Object of Map<string, ConfigValue>
    | SecretReference of Map<string, ConfigValue>

[<RequireQualifiedAccess>]
type DocumentFormat =
    | Json = 0
    | Yaml = 1
    | Toml = 2
    | Memory = 3

[<RequireQualifiedAccess>]
type SourceRole =
    | Defaults = 0
    | System = 1
    | Global = 2
    | RootPrimary = 3
    | Selected = 4
    | MemberPrimary = 5
    | WorkspaceProfile = 6
    | MemberProfile = 7
    | UserOverride = 8
    | Environment = 9
    | Explicit = 10

type SourceIdentity =
    { Path: string
      Format: DocumentFormat
      Role: SourceRole
      Order: int
      Profile: string option }

type SourcePosition = { Line: int; Column: int }

[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    | Warning = 0
    | Error = 1

type ConfigDiagnostic =
    { Code: string
      Message: string
      Severity: DiagnosticSeverity
      Source: SourceIdentity
      KeyPath: string list
      Position: SourcePosition option }

/// Every legacy field is accounted for, including fields not projected into canonical values.
type LegacyField =
    { RawPath: string list
      CanonicalPath: string list option
      Value: ConfigValue
      Disposition: string }

type ConfigurationDocument =
    { Source: SourceIdentity
      Raw: ConfigValue
      Values: ConfigValue
      Positions: Map<string list, SourcePosition>
      Diagnostics: ConfigDiagnostic list
      LegacyFields: LegacyField list }

[<RequireQualifiedAccess>]
type SourceContent =
    | Available of ConfigurationDocument
    | Missing
    | Invalid of ConfigDiagnostic list

type SourceInput =
    { Source: SourceIdentity
      Content: SourceContent
      Required: bool }

[<RequireQualifiedAccess>]
type ProfileSelection =
    | Automatic
    | Disabled
    | Names of string list

type ProfileOptions =
    { Selection: ProfileSelection
      MaxDepth: int
      MaxExpansions: int }

type ProfilePlan =
    { Enabled: bool
      Active: string list
      Diagnostics: ConfigDiagnostic list }

[<RequireQualifiedAccess>]
type ProjectScope =
    | Root
    | Member

/// Host-supplied ownership context. Resolution never discovers or changes it.
type OwningWorkspace =
    { Root: string
      OutputDirectory: string option }

type ResolutionInput =
    { Sources: SourceInput list
      Environment: Map<string, string>
      Explicit: ConfigValue
      Profiles: ProfileOptions
      Scope: ProjectScope
      OwningWorkspace: OwningWorkspace option
      SuppressWarnings: string list }

type ValueOrigin =
    { Source: SourceIdentity
      Position: SourcePosition option }

[<RequireQualifiedAccess>]
type SourceStatus =
    | Loaded
    | Missing
    | Skipped
    | Failed

type SourceReport =
    { Source: SourceIdentity
      Status: SourceStatus
      Contributed: bool
      Detail: string }

type DocumentResolution =
    { Effective: ConfigValue
      Origins: Map<string list, ValueOrigin>
      Sources: SourceReport list
      ActiveProfiles: string list
      OwningWorkspace: OwningWorkspace option
      Diagnostics: ConfigDiagnostic list }

module DocumentDefaults =
    let emptyValue = ConfigValue.Object Map.empty

    let profileOptions =
        { Selection = ProfileSelection.Automatic
          MaxDepth = 32
          MaxExpansions = 4096 }

    let resolutionInput =
        { Sources = []
          Environment = Map.empty
          Explicit = emptyValue
          Profiles = profileOptions
          Scope = ProjectScope.Root
          OwningWorkspace = None
          SuppressWarnings = [] }

/// Tree operations shared by embedded hosts and adapters.
module ConfigTree =
    let rec tryFind path value =
        match path, value with
        | [], _ -> Some value
        | key :: rest, ConfigValue.Object fields ->
            Map.tryFind key fields |> Option.bind (tryFind rest)
        | _ -> None

    let rec set path value tree =
        match path with
        | [] -> value
        | key :: rest ->
            let fields = match tree with ConfigValue.Object fields -> fields | _ -> Map.empty
            let child = Map.tryFind key fields |> Option.defaultValue DocumentDefaults.emptyValue
            ConfigValue.Object (Map.add key (set rest value child) fields)

    let remove key tree =
        match tree with
        | ConfigValue.Object fields -> ConfigValue.Object (Map.remove key fields)
        | _ -> tree

module ValueMerge =
    /// Higher arrays and incompatible shapes replace complete lower values.
    let rec merge lower higher =
        match lower, higher with
        | ConfigValue.Object left, ConfigValue.Object right ->
            right
            |> Map.fold (fun result key value ->
                let merged = Map.tryFind key result |> Option.map (fun old -> merge old value) |> Option.defaultValue value
                Map.add key merged result) left
            |> ConfigValue.Object
        | _ -> higher
