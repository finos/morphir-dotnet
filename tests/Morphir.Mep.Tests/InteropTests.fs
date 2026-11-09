// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.InteropTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Morphir.Mep
open Morphir.TestSupport

[<Tests>]
let tests =
    testTask "C# sees the protocol parser's nullness and TryParse flow metadata" {
        use sandbox = TestSandbox.Create("mep-interop-")

        let project =
            Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../src/Morphir.Mep/Morphir.Mep.fsproj"))

        let xml =
            $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><ProjectReference Include="{project}" /></ItemGroup>
</Project>"""

        File.WriteAllText(Path.Combine(sandbox.Work, "Consumer.csproj"), xml)

        File.WriteAllText(
            Path.Combine(sandbox.Work, "BadLiteral.cs"),
            """using Morphir.Mep;
static class BadLiteral { static void Check() { ProtocolVersion.Create(null); } }
        """
        )

        File.WriteAllText(
            Path.Combine(sandbox.Work, "BadResult.cs"),
            """using System;
using Morphir.Mep;
static class BadResult { static void Check(string? text) {
    if (!ProtocolVersion.TryParse(text, out var result, out var failure)) Console.WriteLine(result.Value);
} }
        """
        )

        File.WriteAllText(
            Path.Combine(sandbox.Work, "BadFailure.cs"),
            """using System;
using Morphir.Mep;
static class BadFailure { static void Check(string? text) {
    if (ProtocolVersion.TryParse(text, out var result, out var failure)) Console.WriteLine(failure.ToString());
} }
        """
        )

        File.WriteAllText(
            Path.Combine(sandbox.Work, "BadMutation.cs"),
            "using Morphir.Mep; static class BadMutation { static void Check(DescribeParams p) { p.ProtocolVersions[0] = ProtocolVersion.Create(\"0.1\"); } }"
        )

        let start =
            ChildProcess.StartInfo(
                "dotnet",
                "build",
                "Consumer.csproj",
                "-c",
                "Release",
                "-p:BuildProjectReferences=false",
                "--nologo"
            )

        start.WorkingDirectory <- sandbox.Work
        sandbox.Apply start
        let! (result: ChildProcessResult) = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 2.0)
        Expect.notEqual result.ExitCode 0 "Unsafe C# use must not compile"

        for file, diagnostic in
            [ "BadLiteral", "CS8625"
              "BadResult", "CS8602"
              "BadFailure", "CS8602"
              "BadMutation", "CS0200" ] do
            Expect.isTrue
                (Regex.IsMatch(result.CombinedOutput, file + @"\.cs\([^\r\n]*" + diagnostic))
                $"The compiler reports {diagnostic} for {file}: {result.CombinedOutput}"
    }


[<Tests>]
let consumer =
    testTask "C# consumes generated MEP codecs and framing with reflection serialization disabled" {
        let! (output: string) = Morphir.Mep.AotProbe.MepConsumer.RunAsync()

        Expect.stringContains
            output
            "describe {\"protocolVersions\":[\"0.2.0-draft.1\"]}"
            "The C# consumer used the generated describe codec"
    }
