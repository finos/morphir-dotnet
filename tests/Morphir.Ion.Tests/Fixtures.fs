module Morphir.Ion.Tests.Fixtures

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text

type private Anchor = Anchor

let private assembly = typeof<Anchor>.Assembly
let private prefix = "Morphir.Ion.Fixtures."

/// Every vendored fixture, by its path below tests/Fixtures/MorphirIon with forward slashes.
let names =
    assembly.GetManifestResourceNames()
    |> Array.filter (fun name -> name.StartsWith prefix)
    |> Array.map (fun name -> name.Substring(prefix.Length).Replace('\\', '/'))
    |> Array.sort

/// The exact bytes of a vendored fixture.
let bytes (path: string) : byte array =
    let resource =
        assembly.GetManifestResourceNames()
        |> Array.tryFind (fun name -> name.StartsWith prefix && name.Substring(prefix.Length).Replace('\\', '/') = path)

    match resource with
    | None -> invalidArg (nameof path) $"Fixture '{path}' is not embedded."
    | Some resource ->
        use stream = assembly.GetManifestResourceStream resource
        use memory = new MemoryStream()
        stream.CopyTo memory
        memory.ToArray()

/// A vendored fixture as UTF-8 text.
let text path = Encoding.UTF8.GetString(bytes path)

/// Lowercase hexadecimal SHA-256.
let sha256 (data: byte array) =
    Convert.ToHexString(SHA256.HashData data).ToLowerInvariant()

/// Bytes for UTF-8 text.
let utf8 (value: string) = ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes value)
