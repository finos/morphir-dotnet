# Tasks

Every behavior task follows red/green/refactor: write the failing test first. Each group lands its own tests and documentation. Expected JSON is written by hand for each case; FSharp.SystemTextJson output is a compatibility check, not the only reference. The F# surface that C# can call follows the `fsharp-csharp-interop` skill.

## 1. Tracking and shared build infrastructure

- [x] 1.1 Open the tracking issue (link this change, `add-mep-core` and the extension model PRD) and link it from `proposal.md`. Verify that the issue and the proposal link each other.
- [x] 1.2 Move the private Myriad host and the coordinator of `Morphir.Myriad` into a shared build component, with no behavior change for `Morphir.Myriad`. Verify that `./build.sh --target TestMyriadPackages` and the car rental example pass unchanged.
- [x] 1.3 Create `src/Morphir.Myriad.SystemTextJson.Annotations` (`netstandard2.0`, no dependency): `JsonUnionEncoding` flags and presets, naming policy, map format, unknown-member and skippable-option settings, and the attributes `JsonUnion`, `JsonName`, `JsonParsedValue`, `JsonIgnoreType` and `JsonRecord`. Verify XML documentation on every public member and that the package has no dependency.
- [x] 1.4 Create `src/Morphir.Myriad.SystemTextJson` (the plugin with the `morphir-system-text-json` generator for `.fs` inputs), its build props and targets with the `FSharpJsonContext` item, and `tests/Morphir.Myriad.SystemTextJson.Tests` with a sample consumer project in the repository that uses the plugin from its build output. Verify that the sample builds an empty context and that a failing generator blocks its compilation.

## 2. Context and records

- [x] 2.1 Generate the context type (`JsonSerializerContext`, `Default`, a `JsonTypeInfo<T>` member for each type, `GetTypeInfo`, `Verify(options)`) and a converter for a record of primitive fields. Verify a round trip through `JsonSerializer` with the type info, and `JsonTypeInfoResolver.Combine` with a second generated context.
- [x] 2.2 Add the record member rules: `JsonName` and `JsonPropertyName`, naming policies, required non-option fields, refusal of `null`, skippable options, duplicate refusal and the unknown-member rule. Verify one hand-written expected JSON case for each rule, and the `JsonException` text and path for each failure.
- [x] 2.3 Add exact numbers for every integer type, `decimal`, floating point, `bool`, `string`, `Guid`, `DateTimeOffset` and `JsonElement`, and the number-from-string option. Verify the range ends of each type, a fraction for an integer, and `uint64` maximum.
- [x] 2.4 Add the collections: `option`, `voption`, `list`, arrays, `Set`, `ResizeArray`, `IReadOnlyList`, `IReadOnlyCollection`, tuples, `Map` and `IReadOnlyDictionary` with the three map formats, and nesting. Verify each shape with expected JSON and with the FSharp.SystemTextJson compatibility check.
- [x] 2.5 Add build errors for unsupported input: generic user types, a reserved built-in name used by a user type, an unknown attribute argument and contradictory options. Verify that each error names the file, type, member and reason.

## 3. Unions

- [x] 3.1 Add fieldless unions with `UnwrapFieldlessTags`, `JsonName` on cases, the tag naming policy and `TagCaseInsensitive`. Verify the MEP `ExtensionType` shape (`"validator"`) and the failure for an unknown string.
- [x] 3.2 Add `AdjacentTag` and `ExternalTag` with positional and `NamedFields` fields, `UnwrapSingleFieldCases`, the tag and fields names, and `AllowUnorderedTag` (with the reader look-ahead). Verify each combination against expected JSON and the compatibility check, including a tag after the fields.
- [x] 3.3 Add `InternalTag` (array form and `NamedFields` object form), `UnwrapRecordCases` and `UnionFieldNamesFromTypes`. Verify the classic IR array shape and a `kind`-tagged object shape.
- [x] 3.4 Add `Untagged` with unique field names, `SelectByTokenKind`, `UnwrapSingleCaseUnions` and `UnwrapOption`. Verify the `RequestId` scenario, the untyped JSON value, and the build error for an ambiguous untagged union.
- [x] 3.5 Add the presets `Default`, `NewtonsoftLike`, `ThothLike` and `FSharpLuLike`, and recursive types. Verify the whole compatibility corpus for each preset.

## 4. Parsed values and user converters

- [x] 4.1 Add `JsonParsedValue` with `Parse` (result) and `TryParse` members, as a value and as an object-map key. Verify with `Morphir.Extensions` `ExtensionId` and `ReleaseVersion`: valid text round-trips, and invalid text fails with the parser's text and no value built.
- [x] 4.2 Add the System.Text.Json `JsonConverter` attribute on a type and on a field. Verify that the user converter handles only that type or field, with no reflection in the generated code.

## 5. AOT, packaging and documentation

- [x] 5.1 Add an AOT probe that serializes every supported shape through generated contexts, and add it to the AOT checks. Verify zero IL warnings and output equal to JIT, byte for byte, and record the probe size.
- [x] 5.2 Pack both packages and add package-consumer tests (a clean project with an empty NuGet cache, as for `Morphir.Myriad`). Verify a clean build, a repeat build that rewrites no generated file, and a failure that blocks compilation.
- [x] 5.3 Add snapshot tests of the generated code for the sample context. Verify that a change to the generator shows as a snapshot diff.
- [x] 5.4 Remove the stub `json-codec` generator, `GenerateJsonCodecAttribute`, `JsonCodecConfig` and their tests from `Morphir.Internal.CodeGeneration`; update its README and `IMPLEMENTATION_STATUS.md`, and the `elm-to-fsharp-guru` skill template. Verify that the solution builds and that no reference to `json-codec` is left outside the archive.
- [x] 5.5 Write the guide `docs/content/docs/guides/myriad-json.md`: setup, the context item, the attribute reference, the encoding table with JSON examples, the additions, AOT use, errors, and moving from FSharp.SystemTextJson. Verify that each code sample compiles in the sample consumer.
- [x] 5.6 Add `Morphir.Myriad` and both new packages to `LibraryPackages` (`Morphir.Myriad` after its backend, annotations before the plugin) so that `PackLibs` packs them and `PublishLibs` and `deployment.yml` publish them to NuGet.org with each release. Add a plugin-package check to `PackageValidator` (plugin and dependencies under `tools/`, the Myriad host, build props and targets, README, license, `ThirdPartyNotices.txt`). Verify that `./build.sh --target PackLibs` produces the three packages, that the check passes for both plugin packages and fails for a package with the host removed, and that a clean project restores them from the packed output only and builds a context and a Morphir model.
- [x] 5.7 Move the `Morphir.Myriad` getting-started article, the car rental example and its README from the local feed and `0.1.0-local.5` to the published package and the release version; keep the local feed only for the package tests. Verify that the example builds from a packed release version with no local feed in its `NuGet.config`, and that no document asks for the local pack step.
- [ ] 5.8 Write the package READMEs (they are the NuGet.org pages), the changelog entry and the release notes. Confirm with the release manager that the NuGet.org publishing account can create the new package ids, and record the result in the tracking issue. Verify that each README links the guide and that the tracking issue records the account check.

## 6. Integration check

- [ ] 6.1 Run `./build.sh --target Test`, `dotnet format --verify-no-changes`, the AOT checks and the Myriad package tests. Verify that all pass.

## Workflow follow-up

- Follow AGENTS.md for review, approval and landing through a pull request.
- Archive the change after it lands, and sync the `myriad-json-generation` spec.
- `add-mep-core` starts its group 2 when this change lands.
