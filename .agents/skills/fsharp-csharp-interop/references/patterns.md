# F# and C# interop patterns

Code for each rule in [../SKILL.md](../SKILL.md). The examples compile with the .NET 10 SDK (F# 10, C# 14), with `<Nullable>enable</Nullable>` and warnings as errors on both sides.

## Rule 1: project settings

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsTrimmable>true</IsTrimmable>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <IsAotCompatible>true</IsAotCompatible>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

With F# nullness on, this C# call is a compile error (CS8625) in a project with nullable warnings as errors:

```csharp
ExtensionId.Create(null);   // error CS8625: Cannot convert null literal to non-nullable reference type.
```

With F# nullness off, the same call compiles with no warning, because C# reads the F# assembly as "oblivious".

## Rule 2: a parsed value

```fsharp
namespace Morphir.Extensions

open System
open System.Diagnostics.CodeAnalysis // MaybeNullWhen
open System.Runtime.InteropServices // Out

/// Why a text value was refused. Turned into a diagnostic only at the output edge.
[<RequireQualifiedAccess>]
type ParseFailure =
    | Empty of field: string
    | TooLong of field: string * limit: int
    | InvalidSegment of field: string * segment: int

    override this.ToString() =
        match this with
        | Empty field -> $"{field} is empty"
        | TooLong(field, limit) -> $"{field} is longer than {limit} characters"
        | InvalidSegment(field, segment) -> $"{field} segment {segment} is not a lowercase portable token"

/// An extension id: lowercase portable tokens joined by dots.
[<CustomEquality; CustomComparison>]
type ExtensionId =
    private
    | ExtensionId of string

    /// The canonical text.
    member this.Value = let (ExtensionId value) = this in value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? ExtensionId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ExtensionId as id -> String.CompareOrdinal(this.Value, id.Value)
            | _ -> invalidArg (nameof other) "Not an ExtensionId."

    /// Parses untrusted text. For F# callers.
    static member Parse(text: string | null) : Result<ExtensionId, ParseFailure> =
        match text with
        | null -> Error(ParseFailure.Empty "extension id")      // after this case, F# knows the value is not null
        | value when value.Length = 0 -> Error(ParseFailure.Empty "extension id")
        | value when value.Length > 238 -> Error(ParseFailure.TooLong("extension id", 238))
        | value ->
            let isToken (s: string) =
                s.Length > 0 && Char.IsAsciiLetterLower s[0] && Char.IsAsciiLetterOrDigit s[s.Length - 1]
                && s |> Seq.forall (fun c -> Char.IsAsciiLetterLower c || Char.IsAsciiDigit c || c = '-')
            match value.Split '.' |> Array.tryFindIndex (isToken >> not) with
            | Some index -> Error(ParseFailure.InvalidSegment("extension id", index))
            | None -> Ok(ExtensionId value)

    /// Parses untrusted text. For C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<ExtensionId>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) : bool =
        match ExtensionId.Parse text with
        | Ok id ->
            result <- id
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Builds a value from a literal in source code. Throws on invalid text.
    static member Create(literal: string) : ExtensionId =
        match ExtensionId.Parse literal with
        | Ok id -> id
        | Error error -> invalidArg (nameof literal) (error.ToString())
```

C# use:

```csharp
if (!ExtensionId.TryParse(text, out var id, out var failure))
    return Fail(failure.ToString());   // failure is not null here
Use(id);                               // id is not null here

// Reading id inside the if, or failure after it, gives CS8602 (possibly null). With byref<T | null> instead of
// MaybeNullWhen, C# gives no warning there, because F# does not write byref element nullness into the metadata.

var builtIn = ExtensionId.Create("morphir.csharp");
```

A record with an invariant hides its representation, as `SourceFormatVersion` in `src/Morphir.Models/IR/VersionedModel.fs` does:

```fsharp
type SourceFormatVersion =
    private { Declared: string; Parsed: Versioning.FormatVersion }
    member this.Text = this.Declared
```

## Rule 3: names and calls

```fsharp
// Good: a static member with tupled parameters and a Func.
type ExtensionCatalog =
    static member Select(entries: IReadOnlyList<ExtensionEntry>, keep: Func<ExtensionEntry, bool>) : IReadOnlyList<ExtensionEntry> =
        entries |> Seq.filter keep.Invoke |> Seq.toArray :> IReadOnlyList<_>

// Bad on the C# surface: curried, and an F# function type.
let select (keep: ExtensionEntry -> bool) (entries: ExtensionEntry list) = List.filter keep entries

// A module function that C# must call.
[<RequireQualifiedAccess>]
module Catalogs =
    [<CompiledName("Empty")>]
    let empty host = ExtensionCatalog.Create(host, Array.empty)

// Asynchronous work for C#.
type Loader =
    static member LoadAsync(path: string, ct: CancellationToken) : Task<Catalog> =
        task { return! load path ct }
```

## Rule 4: option and null

```fsharp
type ExtensionIdentity =
    { Id: ExtensionId; Description: ExtensionDescription option }   // F# inside

    /// For C# callers: null when there is no description.
    member this.DescriptionOrNull : ExtensionDescription | null = Option.toObj this.Description
```

## Rule 5: unions seen from C#

```fsharp
[<RequireQualifiedAccess>]
type HostAvailability =
    | AllHosts
    | Only of MorphirHost

    /// The F# decision. C# calls this and does not branch.
    member this.IsAvailableIn(host: MorphirHost) =
        match this with
        | AllHosts -> true
        | Only h -> h = host

    /// For C# code that must branch: every case needs a function.
    member this.Match<'R>(allHosts: Func<'R>, only: Func<MorphirHost, 'R>) : 'R =
        match this with
        | AllHosts -> allHosts.Invoke()
        | Only h -> only.Invoke h

[<RequireQualifiedAccess>]
type MorphirHost =
    | Native
    | DotnetTool

    /// The text that the output edge writes.
    member this.WireValue =
        match this with
        | Native -> "morphir"
        | DotnetTool -> "dotnet-morphir"

    override this.ToString() = this.WireValue
```

C# use:

```csharp
var host = MorphirHost.Native;                         // generated static property for a fieldless case
var text = entry.Hosts.Match(
    allHosts: () => "all hosts",
    only: h => h.WireValue);                            // no switch on Tag or case types
```

## Rule 6: collections

```fsharp
type Catalog =
    member this.Entries : IReadOnlyList<ExtensionEntry> = entries :> IReadOnlyList<_>   // an array inside

// Bad on the C# surface:
//   member this.Entries : ExtensionEntry list
```

## Rule 7: equality, order and text

- Use `[<CustomEquality; CustomComparison>]` with ordinal comparison for text identities, as in rule 2.
- Override `ToString()` on every record and union on the C# surface. Do not leave the default, which formats through reflection.
- In shipped code, use `$"{value.Field}"` over explicit members. Do not use `%A`.

## A C# test for the nullness contract

Put the null-literal call in a project or Roslyn test that is expected to fail to build. The repository test fails if the call compiles, which shows that F# nullness metadata is missing.
