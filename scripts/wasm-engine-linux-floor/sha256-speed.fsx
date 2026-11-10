// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// EXPERIMENT (add-wasm-extension-host, the Linux floor measurement, point 4): how long the SHA-256 of one module
// takes with .NET's SHA256 (OpenSSL on Linux) and with BouncyCastle's managed Sha256Digest, the digest
// Morphir.Toolchain uses. JIT, after warm-up; the median of the timed rounds. Prints one JSON object on stdout.
//
//   dotnet fsi scripts/wasm-engine-linux-floor/sha256-speed.fsx <module.wasm>
#r "nuget: BouncyCastle.Cryptography, 2.7.0"

open System
open System.Diagnostics
open System.IO
open System.Text.Json

let bytes = File.ReadAllBytes fsi.CommandLineArgs[1]

let platform (data: byte array) =
    System.Security.Cryptography.SHA256.HashData data

let managed (data: byte array) =
    let digest = Org.BouncyCastle.Crypto.Digests.Sha256Digest()
    digest.BlockUpdate(ReadOnlySpan data)
    let result = Array.zeroCreate<byte> 32
    digest.DoFinal(result, 0) |> ignore
    result

let median (hash: byte array -> byte array) =
    for _ in 1..5 do hash bytes |> ignore
    let times =
        [| for _ in 1..21 ->
               let watch = Stopwatch.StartNew()
               hash bytes |> ignore
               watch.Elapsed.TotalMilliseconds |]
        |> Array.sort
    times[times.Length / 2]

let same = Convert.ToHexStringLower(platform bytes) = Convert.ToHexStringLower(managed bytes)
let platformMs = median platform
let managedMs = median managed

JsonSerializer.Serialize(
    {| file = Path.GetFileName fsi.CommandLineArgs[1]
       bytes = bytes.Length
       sameDigest = same
       platformMilliseconds = Math.Round(platformMs, 2)
       managedMilliseconds = Math.Round(managedMs, 2)
       managedOverPlatform = Math.Round(managedMs / platformMs, 2)
       managedMegabytesPerSecond = Math.Round(float bytes.Length / 1e6 / (managedMs / 1000.0), 1)
       os = Runtime.InteropServices.RuntimeInformation.OSDescription
       architecture = string Runtime.InteropServices.RuntimeInformation.ProcessArchitecture |})
|> printfn "%s"
