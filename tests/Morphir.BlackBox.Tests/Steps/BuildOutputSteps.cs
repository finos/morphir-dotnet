// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps that check a file of the build output of a program and its metadata.</summary>
[Binding]
public sealed class BuildOutputSteps(IArtifactInspectionDriver builtFiles)
{
    private string? _program;

    private string Program => _program ?? throw new InvalidOperationException("The scenario named no build output before this step.");

    [Given("the build output of the program {string}")]
    public void TheBuildOutputOfTheProgram(string program) => _program = program;

    [Then("the runtime configuration {string} sets {string} to true")]
    public void TheRuntimeConfigurationSets(string file, string property)
    {
        var configuration = builtFiles.BuiltFile(Program, file).ReadJson();
        if (!configuration.GetProperty("runtimeOptions").TryGetProperty("configProperties", out var properties)
            || !properties.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException($"{file} of {Program} does not set {property} to true.");
        }
    }

    [Then("the assembly {string} references no member {string} of {string}")]
    public void TheAssemblyReferencesNoMember(string assembly, string member, string type)
    {
        var separator = type.LastIndexOf('.');
        var members = builtFiles.BuiltFile(Program, assembly).ReadAssembly().MembersOf(type[..separator], type[(separator + 1)..]);
        if (members.Contains(member))
            throw new InvalidOperationException($"{assembly} of {Program} references {type}.{member}.");
    }

    [Then("the assembly {string} references no assembly {string} and no type of its namespace")]
    public void TheAssemblyReferencesNoAssembly(string assembly, string referenced)
    {
        var facts = builtFiles.BuiltFile(Program, assembly).ReadAssembly();
        if (facts.AssemblyReferences.Contains(referenced) || facts.UsesNamespace(referenced))
            throw new InvalidOperationException($"{assembly} of {Program} uses {referenced}.");
    }

    [Then("the assembly {string} uses no type of the namespace {string}")]
    public void TheAssemblyUsesNoTypeOfTheNamespace(string assembly, string @namespace)
    {
        if (builtFiles.BuiltFile(Program, assembly).ReadAssembly().TypeReferenceNamespaces.Contains(@namespace))
            throw new InvalidOperationException($"{assembly} of {Program} uses types of {@namespace}.");
    }
}
