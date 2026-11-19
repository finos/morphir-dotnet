// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The state of the package steps of one scenario: the packages in the feed of the workspace, the package that a
/// check reads, the consumer project, the output of its last run and the files that a step remembered.
/// </summary>
public sealed class PackageRun
{
    private PackedPackage? _package;
    private ConsumerProject? _consumer;
    private CommandResult? _consumerOutput;

    /// <summary>The packages in the feed of the workspace, by id.</summary>
    public Dictionary<string, PackedPackage> InFeed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The package that the checks of a scenario read.</summary>
    public PackedPackage Package
    {
        get => _package ?? throw new InvalidOperationException("The scenario has no package before this step.");
        set => _package = value;
    }

    /// <summary>The consumer project of the scenario.</summary>
    public ConsumerProject Consumer
    {
        get => _consumer ?? throw new InvalidOperationException("The scenario has no consumer project before this step.");
        set => _consumer = value;
    }

    /// <summary>The result of the last run of the consumer.</summary>
    public CommandResult ConsumerOutput
    {
        get => _consumerOutput ?? throw new InvalidOperationException("The scenario did not run the consumer before this step.");
        set => _consumerOutput = value;
    }

    /// <summary>The files that a step remembered: their text and their time, by the path relative to the consumer.</summary>
    public Dictionary<string, (string Text, DateTime Time)> Remembered { get; } = new(StringComparer.Ordinal);

    /// <summary>The files that a step put beside the generated files: the path relative to the consumer, by the file name.</summary>
    public Dictionary<string, string> Placed { get; } = new(StringComparer.Ordinal);

    /// <summary>The folder into which a step installed a tool.</summary>
    public WorkspaceDirectory? ToolFolder { get; set; }

    /// <summary>The version of a package in the feed. A package that is not in the feed is an error.</summary>
    public string VersionOf(string id) =>
        InFeed.TryGetValue(id, out var package)
            ? package.Version
            : throw new InvalidOperationException($"The feed of the scenario has no package {id}. It has: {string.Join(", ", InFeed.Keys)}.");
}

/// <summary>One model input of a Myriad consumer: a <c>MorphirModel</c> item.</summary>
public sealed record ConsumerModel(string File, string? OutputDirectory = null);

/// <summary>
/// An F# consumer project in the workspace. The steps change its parts, and <see cref="Render"/> writes the project
/// file from them. It refers to the packages of the feed of the scenario by their versions.
/// </summary>
public sealed class ConsumerProject(WorkspaceDirectory folder)
{
    public const string ProjectFile = "Consumer.fsproj";

    public WorkspaceDirectory Folder { get; } = folder;

    /// <summary>The package references, in order: the id, the version and true for <c>PrivateAssets="all"</c>.</summary>
    public List<(string Id, string Version, bool Private)> Packages { get; } = [];

    /// <summary>The <c>MorphirModel</c> items of a Myriad consumer.</summary>
    public List<ConsumerModel> Models { get; } = [];

    /// <summary>The F# source files before <c>Program.fs</c>, in compile order.</summary>
    public List<string> Sources { get; } = [];

    /// <summary>The metadata of the <c>FSharpJsonContext</c> item on <c>Types.fs</c>, or null for no context.</summary>
    public List<(string Name, string Value)>? Context { get; set; }

    /// <summary>More properties of the project, for example <c>MorphirMyriadHostPath</c>.</summary>
    public List<(string Name, string Value)> Properties { get; } = [];

    /// <summary>True for <c>Nullable</c> and <c>TreatWarningsAsErrors</c>, as the consumers of the System.Text.Json plugin have.</summary>
    public bool Strict { get; init; }

    public WorkspaceFile File(string relative) => Folder.File(relative.Split('/'));

    /// <summary>The text of the project file.</summary>
    public string Render()
    {
        static string Escape(string text) => SecurityElement.Escape(text);

        var strict = Strict ? "<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>" : "";
        var references = string.Concat(Packages.Select(package =>
            $"""<PackageReference Include="{package.Id}" Version="[{package.Version}]"{(package.Private ? " PrivateAssets=\"all\"" : "")} />"""));
        var models = string.Concat(Models.Select(model =>
            $"""<MorphirModel Include="{Escape(model.File)}"{(model.OutputDirectory is { } output ? $" OutputDirectory=\"{Escape(output)}\"" : "")} />"""));
        var sources = string.Concat(Sources.Select(source =>
        {
            var compile = $"""<Compile Include="{Escape(source)}" />""";
            return source == "Types.fs" && Context is { } context
                ? compile + $"""<FSharpJsonContext Include="Types.fs"{string.Concat(context.Select(item => $" {item.Name}=\"{Escape(item.Value)}\""))} />"""
                : compile;
        }));
        var properties = Properties.Count == 0
            ? ""
            : "<PropertyGroup>" + string.Concat(Properties.Select(property => $"<{property.Name}>{Escape(property.Value)}</{property.Name}>")) + "</PropertyGroup>";

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>{strict}</PropertyGroup>
            <ItemGroup>{references}</ItemGroup>
            <ItemGroup>{models}{sources}<Compile Include="Program.fs" /></ItemGroup>
            {properties}
            </Project>
            """;
    }
}
