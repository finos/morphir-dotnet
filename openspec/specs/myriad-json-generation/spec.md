# myriad-json-generation Specification

## Purpose
Lets F# projects serialize their records, unions and enums with System.Text.Json in trimmed and Native AOT applications, through converters and contexts that a Myriad plugin generates at build time, with the encodings that F# JSON users already know.

## Requirements

### Requirement: Generated contexts work with System.Text.Json
For each configured context, the build SHALL generate a type that derives from `JsonSerializerContext`, with a `Default` instance and one `JsonTypeInfo<T>` member for each type in the context. `JsonSerializer` calls with that type info, and resolvers combined with `JsonTypeInfoResolver.Combine`, SHALL serialize and deserialize the types.

#### Scenario: Round trip through the context
- **WHEN** a project annotates a record `Person` for a context `AppJson` and calls `JsonSerializer.Serialize(person, AppJson.Default.Person)` and then `Deserialize` with the same type info
- **THEN** the result equals the original value

#### Scenario: Contexts combine
- **WHEN** a type in context `A` has a member whose type is in context `B`, and the options combine `A` and `B`
- **THEN** serialization of the outer type uses `B` for the member and succeeds

### Requirement: Generated code is trim-safe and AOT-safe
Generated code SHALL use no reflection, no `JsonSerializer` call without type info, and no suppression of trim or AOT warnings. An application that uses only generated contexts SHALL publish with Native AOT with no IL warning from generated code.

#### Scenario: AOT probe
- **WHEN** a probe application that serializes every supported shape through generated contexts is published with Native AOT
- **THEN** the publish has no IL warning, and the JSON output equals the JIT output byte for byte

### Requirement: Records follow the configured member rules
A record SHALL be a JSON object with members named by JsonName or the context naming policy. Non-option fields SHALL be required unless they declare a missing-member default or extension data. Explicit null SHALL retain configured null rules. Options SHALL be written as null or omitted as configured.

#### Scenario: Missing required member
- **WHEN** JSON for Person has no unannotated name member
- **THEN** deserialization fails with a JsonException naming name and Person

#### Scenario: Failure inside a nested value
- **WHEN** a member of a nested record has the wrong kind
- **THEN** the message names each member on the way to the failure

#### Scenario: Skippable option
- **WHEN** skippable options are enabled and Nickname is None
- **THEN** nickname is omitted and an absent nickname reads as None

### Requirement: Union encodings match the shared vocabulary
Unions SHALL support the base encodings `AdjacentTag`, `ExternalTag`, `InternalTag` and `Untagged`, the flags `NamedFields`, `UnwrapFieldlessTags`, `UnwrapSingleFieldCases`, `UnwrapSingleCaseUnions`, `UnwrapRecordCases`, `UnwrapOption`, `AllowUnorderedTag` and `UnionFieldNamesFromTypes`, case-insensitive tags, and the presets `Default`, `NewtonsoftLike`, `ThothLike` and `FSharpLuLike`, with the meanings that FSharp.SystemTextJson gives them.

#### Scenario: Same output as FSharp.SystemTextJson
- **WHEN** the compatibility corpus serializes each union and record shape with each base encoding, flag and preset, through the generated context and through FSharp.SystemTextJson with the same options
- **THEN** the two JSON outputs are equal, and each reads back through the generated context to the original value

#### Scenario: Fieldless cases as strings
- **WHEN** a union of fieldless cases uses `UnwrapFieldlessTags` and one case has `JsonName "validator"`
- **THEN** that case is written as the JSON string `"validator"`, and an unknown string fails with a `JsonException` that names the union

### Requirement: Tag and field names are configurable
The tag member name, the fields member name, the tag naming policy and the field naming policy SHALL be configurable for a context and for one type. A `JsonName` on a case SHALL set its tag, and a `JsonName` on a field SHALL set its member name. More than one name on input SHALL be accepted, and the first SHALL be written.

#### Scenario: Kind tag
- **WHEN** a union uses `InternalTag` with `NamedFields`, tag name `kind` and a camel-case tag policy
- **THEN** a case `TypeMismatch(expected, found)` is written as `{"kind":"typeMismatch","expected":…,"found":…}`

### Requirement: Parsed values are read through their parser
A type marked as a parsed value SHALL be written as its text member, and read only through its `TryParse` member. Invalid text SHALL fail with a `JsonException` that holds the parser's failure text and the JSON path, and SHALL NOT build a value.

#### Scenario: Invalid extension id
- **WHEN** a record has a field of a parsed type `ExtensionId`, and the JSON value is `"A..b"`
- **THEN** deserialization fails with the `ExtensionId` failure text and the path of the member

### Requirement: Untagged unions can select a case by JSON token kind
An untagged union whose cases each have one field of a different JSON token kind (number, string, boolean, array, object, null) SHALL be read by the token kind of the value, and written as the field value alone.

#### Scenario: Request id
- **WHEN** a union `RequestId = Number of uint64 | Text of string` uses token-kind selection
- **THEN** `18446744073709551615` reads as `Number`, `"a"` reads as `Text`, each writes back as the same token, and `1.5` fails

### Requirement: Numbers keep their exact value
Integer types SHALL read and write their full range with no floating-point step. A JSON number with a fraction or out of range for the target type SHALL fail. Reading numbers from strings SHALL happen only when the context or member allows it.

#### Scenario: uint64 maximum
- **WHEN** a record field of type `uint64` holds `18446744073709551615`
- **THEN** the JSON has that exact number, and it reads back to the same value

### Requirement: Unknown and duplicate members follow the configured rule
Unknown members SHALL be ignored or refused as configured, or captured by an explicitly declared extension-data field. Duplicate members, including aliases of the same field, SHALL be refused with a JsonException naming the member and carrying the path reported by System.Text.Json.

#### Scenario: Refuse unknown members
- **WHEN** a type sets unknown members to refuse and JSON has an extra member
- **THEN** deserialization fails and names that member

#### Scenario: Duplicate member
- **WHEN** a record's name member appears twice
- **THEN** deserialization fails and names name

### Requirement: F# collections and options are supported
Generated converters SHALL support `option`, `voption`, `list`, arrays, `Set`, `ResizeArray`, `IReadOnlyList`, `IReadOnlyCollection`, `Map` and `IReadOnlyDictionary`, tuples, and nesting of these. A map SHALL use the configured map format (`Object`, `ArrayOfPairs` or `ObjectOrArrayOfPairs`); an object map SHALL need a string or parsed-value key.

#### Scenario: Map with parsed keys
- **WHEN** a field of type `Map<ExtensionId, Settings>` uses the `Object` map format
- **THEN** each key is written as the id text, and an invalid key fails through the `ExtensionId` parser

### Requirement: A user converter can replace one shape
A type or member SHALL accept a user-named converter type. The generated code SHALL create that converter with no reflection and use it for that type or member only.

#### Scenario: Converter on one member
- **WHEN** a record field is annotated with a user converter type
- **THEN** that field is read and written by the user converter, and the other fields by generated code

### Requirement: Configuration comes from the project and attributes
A context SHALL be declared in the project file with its name, namespace, input files and default options. Attributes on a type, case or field SHALL override the defaults for that item only. Generation SHALL need no change to a type other than attributes.

#### Scenario: Project default with a type override
- **WHEN** a context defaults to `ExternalTag`, and one union has an attribute that selects `InternalTag`
- **THEN** that union uses `InternalTag`, and every other union in the context uses `ExternalTag`

### Requirement: Unsupported or contradictory input fails the build
A type shape that the generator cannot encode, a reference to a type that no context or converter covers, or options that contradict each other SHALL fail the build with an error that names the type, member and reason. The generator SHALL NOT fall back to reflection.

#### Scenario: Ambiguous untagged union
- **WHEN** an untagged union has two cases whose fields have the same JSON token kind and no field names to tell them apart
- **THEN** the build fails with an error that names the union and both cases

### Requirement: Builds integrate as Morphir.Myriad does
The plugin SHALL register one Myriad generator named `morphir-system-text-json` for `.fs` inputs. The package SHALL generate during an ordinary .NET build, before handwritten files that use the generated code, without running a separate command. A failed generation SHALL block compilation. A repeat build with no input change SHALL NOT rewrite generated files.

#### Scenario: Clean package consumer
- **WHEN** a new F# project references the package, declares one context and builds
- **THEN** the build generates the context and converters, and handwritten code that calls the context compiles and runs

#### Scenario: Repository consumer
- **WHEN** a project in this repository declares a context and builds the solution
- **THEN** the plugin from the solution's build output generates the context, with no package feed

### Requirement: The packages are published with each release
Each morphir-dotnet release SHALL publish the plugin package `Morphir.Myriad.SystemTextJson` and the annotations package `Morphir.Myriad.SystemTextJson.Annotations` to NuGet.org, with the release version, a README, the license, the third-party notices and repository metadata. The release SHALL validate the plugin package layout before it publishes, and SHALL publish the annotations package before the plugin package.

#### Scenario: Release publishes both packages
- **WHEN** the release workflow packs and publishes a release
- **THEN** both packages are on NuGet.org with the release version, and a new project restores them from NuGet.org and builds a context

#### Scenario: Broken plugin package
- **WHEN** the plugin package has no Myriad host or no build targets
- **THEN** package validation fails, and the release publishes no package

### Requirement: Record fields can declare wire defaults
A record field MAY declare a typed scalar, empty collection, static provider or explicit JSON-literal default for an absent member. Scalar and JSON defaults SHALL use the generated field codec. Explicit null SHALL retain the field's null rules. Conflicting declarations and malformed JSON defaults SHALL fail generation. Unannotated fields SHALL remain required.

#### Scenario: Typed scalar defaults
- **WHEN** string, boolean and numeric fields declare ordinary literal defaults
- **THEN** absent members use their values without callers encoding JSON strings, and incompatible scalar kinds fail generation

#### Scenario: Absent capability flag
- **WHEN** a boolean field declares the default `false` and its JSON member is absent
- **THEN** it reads as false, while a null member is refused

### Requirement: Default providers are typed direct calls
Provider calls SHALL be emitted directly and checked by the F# compiler against the field type, without reflection or build-time execution. Providers SHALL run once per absent member, never for explicit values or null. Results SHALL respect null, canonical-version and critical-path rules.

#### Scenario: Decimal provider
- **WHEN** a decimal field names an accessible static no-argument provider method using typeof and nameof
- **THEN** an absent member calls the provider directly, and an incompatible return type or method signature fails compilation

#### Scenario: Mutable default isolation
- **WHEN** two absent collection members use a provider that creates a mutable collection
- **THEN** their collections are distinct and mutations do not affect the other record

### Requirement: Empty collections can be omitted
A collection field MAY opt into omission when empty. Nonempty collections SHALL retain their configured encoding. Unsupported shapes SHALL fail generation.

#### Scenario: Optional diagnostic related information
- **WHEN** a related-information list declares an empty default and empty omission
- **THEN** an absent list reads as empty and an empty list is not written

### Requirement: Booleans can be omitted when equal to their default
A Boolean field with a missing-member default MAY opt into omission when its value equals that default. Another value SHALL be written. A field without a Boolean type or without a Boolean default SHALL fail generation.

#### Scenario: Multi-document frontend capability
- **WHEN** `multiDocument` declares the default false and default omission
- **THEN** an absent member reads as false, false is not written, and true is written

### Requirement: Extension data is preserved without a wrapper
A record MAY have one extension-data field of a string-keyed JsonElement map type. Unknown members SHALL be captured and written beside named members. A repeated member or a write collision with a named member or its alias SHALL fail. An absent extension-data set SHALL be empty.

#### Scenario: Future capability
- **WHEN** a capability object includes an unknown nested member
- **THEN** its JSON value survives a read/write cycle without an extra wrapper member

### Requirement: Annotated version aliases migrate canonically
A record MAY declare a legacy alias and exact legacy/current versions. Readers SHALL require a matching name/version pair, refuse both aliases together, and normalize legacy values and paths. Writers SHALL use the current member and version.

#### Scenario: MEP claims migration
- **WHEN** a claim set uses statementVersion draft.1 and lists statementVersion as critical
- **THEN** it reads as claimsVersion draft.2 and writes the canonical current version and path

#### Scenario: Version-alias defaults are canonical
- **WHEN** a version-alias field also declares a missing-member default
- **THEN** literal defaults are accepted only when they encode the exact current-version string, and provider results are checked at deserialization, so an absent member cannot bypass the canonical-version invariant

#### Scenario: Unknown critical child
- **WHEN** a critical list includes a child path not present in its exact allowlist
- **THEN** the reader refuses it even if its parent is allowed

### Requirement: Critical paths are checked exactly
A critical-path field MAY declare an exact allowlist. Readers and writers SHALL refuse an unknown path. A legacy version alias SHALL refuse a critical path naming the current version member.

#### Scenario: Current path on legacy input
- **WHEN** a draft.1 statement lists claimsVersion as critical
- **THEN** the reader refuses it instead of treating it as a migrated legacy path
