// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// morphirup needs no ICU: it runs in invariant globalization mode, its assemblies never call .NET's normalization,
/// and a morphirup that cannot load any ICU still produces the recorded plan digest. Each test fails if a
/// globalization dependency comes back.
/// </summary>
module Morphir.Toolchain.Tests.GlobalizationTests

open System
open System.Diagnostics
open System.IO
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Text.Json
open Expecto
open Morphir.TestSupport

let private output = AppContext.BaseDirectory

/// The members of System.String an assembly references by name.
let private stringMembers (assembly: string) =
    use stream = File.OpenRead(Path.Combine(output, assembly))
    use pe = new PEReader(stream)
    let metadata = pe.GetMetadataReader()

    [ for handle in metadata.MemberReferences do
          let reference = metadata.GetMemberReference handle

          if reference.Parent.Kind = HandleKind.TypeReference then
              let parent = metadata.GetTypeReference(TypeReferenceHandle.op_Explicit reference.Parent)

              if metadata.GetString parent.Namespace = "System" && metadata.GetString parent.Name = "String" then
                  yield metadata.GetString reference.Name ]
    |> set

/// The assemblies and type namespaces an assembly references.
let private references (assembly: string) =
    use stream = File.OpenRead(Path.Combine(output, assembly))
    use pe = new PEReader(stream)
    let metadata = pe.GetMetadataReader()

    let assemblies =
        [ for handle in metadata.AssemblyReferences -> metadata.GetString((metadata.GetAssemblyReference handle).Name) ]

    let namespaces =
        [ for handle in metadata.TypeReferences -> metadata.GetString((metadata.GetTypeReference handle).Namespace) ]

    set assemblies, set namespaces

let private probe = Path.Combine(__SOURCE_DIRECTORY__, "AotProbe")

[<Tests>]
let tests =
    testList "Globalization" [
        testCase "morphirup runs in invariant globalization mode" (fun () ->
            use config = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "morphirup.runtimeconfig.json")))

            let invariant =
                config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties").GetProperty("System.Globalization.Invariant")

            Expect.isTrue (invariant.GetBoolean()) "System.Globalization.Invariant")

        testCase "neither morphirup nor Morphir.Toolchain calls .NET's Unicode normalization" (fun () ->
            for assembly in [ "Morphir.Toolchain.dll"; "morphirup.dll" ] do
                let members = stringMembers assembly
                Expect.isFalse (members.Contains "Normalize") $"{assembly} references String.Normalize"
                Expect.isFalse (members.Contains "IsNormalized") $"{assembly} references String.IsNormalized")

        testCase "neither morphirup nor Morphir.Toolchain uses the platform's cryptography library" (fun () ->
            // On Linux, System.Security.Cryptography calls into the machine's OpenSSL, and a machine without libssl
            // kills the process. SHA-256 comes from a managed digest instead.
            for assembly in [ "Morphir.Toolchain.dll"; "morphirup.dll" ] do
                let assemblies, namespaces = references assembly
                Expect.isFalse (assemblies.Contains "System.Security.Cryptography") $"{assembly} references System.Security.Cryptography"

                Expect.isEmpty
                    (namespaces |> Set.filter (fun ns -> ns.StartsWith("System.Security.Cryptography", StringComparison.Ordinal)))
                    $"{assembly} uses System.Security.Cryptography types")

        testCase "neither morphirup nor Morphir.Toolchain uses regular expressions" (fun () ->
            // .NET's $ also matches before a final line feed, so an anchored pattern accepted "<128 hex>\n" and the
            // decoder after it threw (review of #581, T1). Spellings are checked character by character instead.
            for assembly in [ "Morphir.Toolchain.dll"; "morphirup.dll" ] do
                let _, namespaces = references assembly

                Expect.isFalse (namespaces.Contains "System.Text.RegularExpressions") $"{assembly} uses System.Text.RegularExpressions")
        testCase "a morphirup that can load no ICU still plans the recorded digest" (fun () ->
            // An app-local ICU version that does not exist: a morphirup that used ICU would fail at its first
            // globalization call. In invariant mode the setting is never read.
            let start = ProcessStartInfo("dotnet", WorkingDirectory = probe)

            for argument in
                [ "exec"
                  Path.Combine(output, "morphirup.dll")
                  "setup"
                  "--plan"
                  "--platform"
                  "linux-x64"
                  "--request"
                  "toolchain.toml"
                  "--recipe"
                  "recipe.json"
                  "--repository-id"
                  String.replicate 64 "b"
                  "--artifact-sizes"
                  "artifact-sizes.json"
                  "--expect-plan"
                  File.ReadAllText(Path.Combine(probe, "plan-sha256.txt")).Trim() ] do
                start.ArgumentList.Add argument

            start.Environment["DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU"] <- "0.0.1"
            start.Environment.Remove "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" |> ignore
            let result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 2.0).GetAwaiter().GetResult()
            Expect.equal result.ExitCode 0 $"the recorded digest without ICU: {result.CombinedOutput}")
    ]
