using FluentAssertions;

namespace Morphir.Configuration.IO.Tests;

public sealed class NativeDiscoveryTests
{
    [Test]
    public void NativeAndMemoryHaveStructurallyEqualDiscoveryAndSelection()
    {
        var root = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "morphir-discovery-parity-" + Guid.NewGuid().ToString("N")));
        var files = new Dictionary<string, string>
        {
            [root + "/morphir.toml"] = "[workspace]\nmembers=['packages/*']\ndefault_member='packages/a'",
            [root + "/packages/a/.config/morphir/config.yml"] = "project: {name: Alpha}",
            [root + "/packages/b/morphir.json5"] = "{project:{name:'Beta'}}",
            [root + "/packages/a/src/input.elm"] = "module Input exposing (..)"
        };
        try
        {
            foreach (var (path, text) in files) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
            var memory = ProjectSystem.Discover(new InMemoryProjectTree(root, files), root + "/packages/a/src/input.elm");
            var native = ProjectSystem.Discover(new FileSystemProjectTree(root), root + "/packages/a/src/input.elm");
            native.Discovery.Should().Be(memory.Discovery);
            native.Selection.Should().Be(memory.Selection);
            ProjectSystem.Load(new FileSystemProjectTree(root), native.Discovery, native.Selection.Projects.Single())
                .Should().Be(ProjectSystem.Load(new InMemoryProjectTree(root, files), memory.Discovery, memory.Selection.Projects.Single()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void DiscoveredFileAndDirectoryLinksAreNeverReadOrTraversed()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symbolic links requires machine-specific privileges on Windows.
        var root = Path.Combine(Path.GetTempPath(), "morphir-discovery-links-" + Guid.NewGuid().ToString("N"));
        var outside = root + "-outside";
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(root, "morphir.toml"), "[workspace]\nmembers=['**','linked']");
            File.WriteAllText(Path.Combine(outside, "morphir.toml"), "[project]\nname='Outside'");
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
            File.CreateSymbolicLink(Path.Combine(root, "morphir.json"), Path.Combine(outside, "morphir.toml"));
            var tree = new FileSystemProjectTree(root);
            var result = ProjectSystem.Discover(tree, root);
            result.Discovery.Projects.ToArray().Should().OnlyContain(p => !p.IsValid);
            result.Discovery.Sources.ToArray().Should().Contain(s => s.Source.Path.EndsWith("morphir.json") && s.Status.IsSkipped);
            tree.Observe(ProjectPath.normalize(Path.Combine(root, "linked", "morphir.toml"))).Should().Be(ProjectEntry.Link);
            tree.Observe(ProjectPath.normalize(Path.Combine(outside, "morphir.toml"))).IsFailure.Should().BeTrue();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(outside)) Directory.Delete(outside, true); }
    }
}
