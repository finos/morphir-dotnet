// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Configuration.Microsoft;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
ConfigValue At(DocumentResolution result, params string[] path) => ConfigTree.tryFind(ListModule.OfSeq(path), result.Effective).Value;
try
{
    foreach (var targetLiteral in new[] { "['winner']", "[]" })
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\n[codegen]\ntargets=['a','b','c']\n[custom]\ninteger=4\ntext='4'\n[custom.nested]\nenabled=true\n",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'\n[codegen]\ntargets=" + targetLiteral + "\n"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo/app");
        var canonical = ProjectSystem.Load(tree, discovery.Discovery, discovery.Selection.Projects.Single(), new ProjectLoadOptions { Isolated = true });
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["host:unrelated"] = "kept",
            ["codegen:targets:0"] = "old",
            ["codegen:targets:1"] = "old",
            ["codegen:targets:99"] = "old"
        });
        builder.AddMorphirConfiguration(canonical);
        using var configuration = (ConfigurationRoot)builder.Build();
        Check(configuration["host:unrelated"] == "kept", "Unrelated host configuration was replaced");
        Check(ReferenceEquals(configuration.GetMorphirConfiguration(), canonical), "Canonical result identity lost");
        Check(configuration["custom:integer"] == "4" && configuration["custom:text"] == "4", "Scalar projection changed");
        Check(At(canonical, "custom", "integer") is ConfigValue.Integer && At(canonical, "custom", "text") is ConfigValue.String, "Canonical scalar types lost");
        Check(configuration["custom:nested:enabled"] == "true", "Nested boolean lost");
        Check(canonical.Origins[ListModule.OfSeq(new[] { "codegen", "targets" })].Source.Path == "/repo/app/morphir.toml", "Array origin lost");
        var options = configuration.GetSection("codegen").Get<CodegenOptions>()!;
        var targets = ((ConfigValue.Array)At(canonical, "codegen", "targets")).Item;
        Check(options.Targets is not null && options.Targets.Length == targets.Length, "Generated binder changed array presence or length");
        Check(options.Missing is null, "Missing array changed to empty");
        Check(configuration.GetSection("codegen:targets").GetChildren().Count() == targets.Length, "Earlier host array children survived");
        Check(configuration["codegen:targets:99"] is null, "Earlier host array index survived direct lookup");
        Check(configuration["codegen:targets:1"] is null && configuration["codegen:targets:2"] is null, "Lower array indexes survived");
        Check(options.Targets!.SequenceEqual(targets.Select(item => ((ConfigValue.String)item).Item)), "Generated binder changed array values");
        // An explicit, typed binding path needs no runtime binder or reflection.
        var explicitOptions = new CodegenOptions { Targets = targets.Select(item => ((ConfigValue.String)item).Item).ToArray() };
        Check(explicitOptions.Targets.SequenceEqual(options.Targets!), "Explicit and generated options disagree");
    }

    foreach (var text in new[] { "{custom:{Foo:1,foo:2}}", "{custom:{'a:b':1,a:{b:2}}}", "{custom:{Foo:{x:1},foo:{y:2}}}", "{custom:{'':1}}" })
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.json"] = text });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        var canonical = ProjectSystem.Load(tree, discovery.Discovery, options: new ProjectLoadOptions { Isolated = true });
        var projection = MorphirConfigurationProjection.Create(canonical);
        Check(!projection.IsSuccess && projection.Values.Count == 0, "Collision was silently flattened");
        Check(projection.Diagnostics.Any(d => d.Code == "config.microsoft.collision"), "Typed collision diagnostic missing");
        Check(ReferenceEquals(projection.Canonical, canonical), "Collision lost canonical document");
    }

    var physicalRoot = Path.Combine(Path.GetTempPath(), "morphir-microsoft-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(physicalRoot);
    try
    {
        File.WriteAllText(Path.Combine(physicalRoot, "morphir.toml"), "[project]\nname='Native'\n[codegen]\ntargets=[]\n[custom]\nsecret={ command=['must-not-execute'] }\n");
        var tree = new FileSystemProjectTree(physicalRoot);
        var discovery = ProjectSystem.Discover(tree, physicalRoot);
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["custom:secret:command:0"] = "must-not-leak" })
            .AddMorphirConfiguration(tree, discovery.Discovery,
            options: new ProjectLoadOptions { Isolated = true }).Build();
        Check(configuration.GetSection("codegen").Get<CodegenOptions>()!.Targets is { Length: 0 }, "Native-tree empty array binding failed");
        Check(configuration["custom:secret"] == "[REDACTED]", "Secret reference leaked into standard view");
        Check(!configuration.GetSection("custom:secret").GetChildren().Any(), "Earlier host secret children leaked");
        Check(configuration["custom:secret:command:0"] is null, "Earlier host secret value leaked");
        Check(At(configuration.GetMorphirConfiguration(), "custom", "secret") is ConfigValue.SecretReference, "Opaque canonical reference lost");
    }
    finally { Directory.Delete(physicalRoot, true); }

    Console.WriteLine(JsonSerializer.Serialize(new SmokeResult(true, checks, true, true, true), SmokeJsonContext.Default.SmokeResult));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

public sealed class CodegenOptions
{
    public string[]? Targets { get; set; }
    public string[]? Missing { get; set; }
}
public sealed record SmokeResult(bool Success, int Checks, bool GeneratedBinding, bool ExplicitBinding, bool NativeTree);
[JsonSerializable(typeof(SmokeResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class SmokeJsonContext : JsonSerializerContext;
