# Morphir.Configuration.IO

Reusable .NET 10 readers for caller-supplied Morphir configuration. The adapter
references the pure `Morphir.Configuration` library, CsToml 1.8.3 and YamlDotNet
16.3.0. It has no CLI or hosting dependency.

```csharp
var supplied = ConfigurationReader.ReadText("morphir.json5",
    "{project:{name:'Acme.Orders',source_directory:'src'}}");
```

`ReadText` accepts a path identity and text, or a complete `SourceIdentity`.
`ReadFile` reads exactly the supplied path. Both return `SourceInput`; required
and missing states remain available for source reports. Parsing retains an
immutable raw tree, canonical tree, diagnostics, positions and legacy field
accounting. File and text readers use the same normalization function.

The pure `Normalization.normalize(source, raw, positions)` entry point lets an
embedded host supply a typed raw tree directly. No parser evaluates secrets,
runs commands, discovers roots, changes process state or writes configuration.

## Reader profile

- `.json` and `.json5` both accept JSON5 1.0. This includes comments, Unicode
  identifier keys and escapes, quoted keys, single-quoted strings, hexadecimal
  numbers, signed values, leading/trailing decimal points, exponents, extended
  whitespace, line continuations, string escapes, infinity, NaN and trailing
  commas. Equivalent decoded duplicate keys fail.
- Integral JSON5 values retain signed 64-bit precision and larger integers use
  `ConfigValue.BigInteger`. Fractional and nonfinite numbers use IEEE doubles.
  Negative zero retains its IEEE sign for decimal and hexadecimal syntax.
  Strings remain different from numbers and booleans. Nulls remain in raw trees
  for control validation and later ordinary-value merging.
- `.toml` uses the direct CsToml document/node APIs, with TOML 1.0 syntax and no
  reflection-based object binding. Duplicate declarations fail. Typed temporal
  values retain their kind and ISO representation. Successful CsToml nodes do
  not expose original source positions; parser errors retain their available
  line number. JSON5 and YAML value positions are one-based.
- `.yaml` and `.yml` use YamlDotNet parser events, without object deserialization.
  They match the pinned Moon-Lander 0.8 Default profile. Null is accepted,
  anchors and aliases expand, and custom scalar tags retain strings. Tags on
  collections preserve the underlying map/array shape. Duplicate decoded keys,
  merge keys, multiple documents, non-string keys and nonmapping roots fail.
  `yes`, dates, `Null`, `NaN` and `Infinity` remain strings. Quoted numeric values
  stay strings even with a numeric tag. Unsigned hexadecimal/octal and signed
  decimal integers retain exact precision; signed radix forms remain strings
  unless explicitly tagged `!!int`. `.inf`, `-.inf` and `.NaN` are doubles.
  Alias descendants identify the alias-use position. Cycles and expansions over
  depth 128 or 100,000 alias-expanded nodes fail with structured diagnostics.

Configuration roots must be objects. Nested documents are bounded to 128 levels.
Unsupported extensions and malformed inputs return structured diagnostics naming
their source and key path, with positions when available. Field names preserve
case and unknown ordinary settings remain in the typed tree.

## Classic normalization

A classic marker is any of `name`, `sourceDirectory` or `exposedModules`.
The fields map to `project.name`, `project.source_directory` and
`project.exposed_modules`. A classic document defaults an absent name to `user`,
source directory to `src`, and frontend language to `elm`. In the pinned classic
profile, top-level `exposedModules` is required as an array of strings whenever
any classic marker appears. A canonical `project.exposed_modules` alone does not
satisfy that compatibility requirement. Same-document legacy/canonical
equivalents conflict even when equal. Invalid legacy types or scalar canonical
containers fail before defaults can hide them.

Checkpoint backend and `morphir-json` checkpoint format are recorded as separate
low-priority default notes. The resolver applies them below system/global
settings. Unsupported nonempty legacy dependency, local dependency and decoration
shapes warn. Canonical dependency objects remain available. Every original field
remains in `Raw`, and `LegacyFields` records consumed, derived and unsupported
values for later loss-checked migration. This reader does not perform migration.

## Explicit profile loading

`ConfigurationLoader.LoadAndResolve(input, candidates)` accepts an inventory of
explicit source identities and supplied reader delegates. It reads ordinary
sources, calls pure `Profiles.plan`, then invokes readers only for active profile
names. Disabled profiles and whitespace-only environment selection do not invoke
profile readers. Invalid profile controls prevent those reads. The caller owns
the source inventory, source roles, required flags and workspace context.

Read [the profile guide](../../docs/content/docs/configuration/profiles.md) and
the pinned [fixture provenance](../../tests/Fixtures/Configuration/PROVENANCE.md).
The [validation journal](VALIDATION.md) records actual commands and checkpoint
status. Qualification is complete only when the full reader suite and both the
trimmed and native smoke hosts pass.

The existing cache-only `MorphirConfig`, merge functions, `CiProfileMode` and
Git-first tooling resolver keep their old entry points. Hosts opt into these new
document APIs explicitly.

Standards used by the readers are the [JSON5 1.0 grammar](https://spec.json5.org/),
[TOML 1.0](https://toml.io/en/v1.0.0), and
[YAML 1.2.2 scalar model](https://yaml.org/spec/1.2.2/).

The [official JSON5 grammar corpus](../../tests/Fixtures/Configuration/json5-tests/PROVENANCE.md)
adds 114 syntax cases under both file extensions, including Unicode identifier
cases and CR/CRLF/LF strings/comments. Per-file hashes and the upstream MIT
license are retained. Duplicate-key acceptance is an intentional difference.
