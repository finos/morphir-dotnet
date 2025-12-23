using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Morphir.Configuration;
using Morphir.Tooling.Configuration;

namespace Morphir.Tooling.Tests.Configuration;

public class ConfigResolverTests
{
    private readonly string _testRoot;

    public ConfigResolverTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"morphir-resolver-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
    }

    private ConfigResolver CreateResolver()
    {
        var workspaceDiscovery = new WorkspaceDiscovery(NullLogger<WorkspaceDiscovery>.Instance);
        return new ConfigResolver(NullLogger<ConfigResolver>.Instance, workspaceDiscovery);
    }

    [Test]
    public async Task ResolveConfigAsync_ShouldReturnDefaults_WhenNoWorkspace()
    {
        // Arrange
        var resolver = CreateResolver();

        try
        {
            // Act
            var result = await resolver.ResolveConfigAsync(startPath: _testRoot);

            // Assert
            result.Should().NotBeNull();
            result.WorkspaceRoot.Should().Be(Microsoft.FSharp.Core.FSharpOption<string>.None);
            result.CiProfileApplied.Should().BeFalse();
            Microsoft.FSharp.Collections.ListModule.IsEmpty(result.Layers).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task ResolveConfigAsync_ShouldFindWorkspaceConfig()
    {
        // Arrange
        var resolver = CreateResolver();
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        var configPath = Path.Combine(morphirDir, "morphir.toml");
        File.WriteAllText(configPath, @"
[morphir.cache]
workspace = ""/test/workspace/cache""
");

        try
        {
            // Act
            var result = await resolver.ResolveConfigAsync(startPath: _testRoot);

            // Assert
            result.Should().NotBeNull();
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.WorkspaceRoot).Should().BeTrue();
            result.WorkspaceRoot.Value.Should().Be(_testRoot);
            Microsoft.FSharp.Collections.ListModule.Length(result.Layers).Should().BeGreaterThanOrEqualTo(1);

            var workspaceLayer = result.Layers.FirstOrDefault(l => l.Path.Contains("morphir.toml"));
            workspaceLayer.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task ResolveConfigAsync_ShouldApplyUserOverride()
    {
        // Arrange
        var resolver = CreateResolver();
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        var workspaceConfigPath = Path.Combine(morphirDir, "morphir.toml");
        File.WriteAllText(workspaceConfigPath, @"
[morphir.cache]
workspace = ""/workspace/cache""
global = ""/workspace/global""
");

        var userConfigPath = Path.Combine(morphirDir, "morphir.user.toml");
        File.WriteAllText(userConfigPath, @"
[morphir.cache]
workspace = ""/user/cache""
");

        try
        {
            // Act
            var result = await resolver.ResolveConfigAsync(startPath: _testRoot);

            // Assert
            result.Should().NotBeNull();
            Microsoft.FSharp.Collections.ListModule.Length(result.Layers).Should().Be(2, "workspace and user layers should be loaded");

            // User override should take precedence for workspace cache
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Effective.Cache.WorkspaceCache).Should().BeTrue();
            result.Effective.Cache.WorkspaceCache.Value.Should().Be("/user/cache", "user override should take precedence");

            // Global cache should come from workspace config
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Effective.Cache.GlobalCache).Should().BeTrue();
            result.Effective.Cache.GlobalCache.Value.Should().Be("/workspace/global", "should use workspace value when user doesn't override");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task ResolveConfigAsync_ShouldApplyCiOverride_WhenModeIsOn()
    {
        // Arrange
        var resolver = CreateResolver();
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        var workspaceConfigPath = Path.Combine(morphirDir, "morphir.toml");
        File.WriteAllText(workspaceConfigPath, @"
[morphir.cache]
workspace = ""/workspace/cache""
");

        var ciConfigPath = Path.Combine(morphirDir, "morphir.ci.toml");
        File.WriteAllText(ciConfigPath, @"
[morphir.cache]
workspace = ""/ci/cache""
");

        try
        {
            // Act
            var result = await resolver.ResolveConfigAsync(ciMode: CiProfileMode.On, startPath: _testRoot);

            // Assert
            result.Should().NotBeNull();
            result.CiProfileApplied.Should().BeTrue("CI mode is On");
            Microsoft.FSharp.Collections.ListModule.Length(result.Layers).Should().Be(2, "workspace and CI layers should be loaded");

            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Effective.Cache.WorkspaceCache).Should().BeTrue();
            result.Effective.Cache.WorkspaceCache.Value.Should().Be("/ci/cache", "CI override should be applied");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task ResolveConfigAsync_ShouldNotApplyCiOverride_WhenModeIsOff()
    {
        // Arrange
        var resolver = CreateResolver();
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        var workspaceConfigPath = Path.Combine(morphirDir, "morphir.toml");
        File.WriteAllText(workspaceConfigPath, @"
[morphir.cache]
workspace = ""/workspace/cache""
");

        var ciConfigPath = Path.Combine(morphirDir, "morphir.ci.toml");
        File.WriteAllText(ciConfigPath, @"
[morphir.cache]
workspace = ""/ci/cache""
");

        try
        {
            // Act
            var result = await resolver.ResolveConfigAsync(ciMode: CiProfileMode.Off, startPath: _testRoot);

            // Assert
            result.Should().NotBeNull();
            result.CiProfileApplied.Should().BeFalse("CI mode is Off");
            Microsoft.FSharp.Collections.ListModule.Length(result.Layers).Should().Be(1, "only workspace layer should be loaded");

            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Effective.Cache.WorkspaceCache).Should().BeTrue();
            result.Effective.Cache.WorkspaceCache.Value.Should().Be("/workspace/cache", "CI override should not be applied");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task ResolveConfigAsync_ShouldRespectLayerPrecedence()
    {
        // Arrange
        var resolver = CreateResolver();
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        var workspaceConfigPath = Path.Combine(morphirDir, "morphir.toml");
        File.WriteAllText(workspaceConfigPath, @"
[morphir.cache]
workspace = ""/workspace/cache""
global = ""/workspace/global""
");

        var userConfigPath = Path.Combine(morphirDir, "morphir.user.toml");
        File.WriteAllText(userConfigPath, @"
[morphir.cache]
workspace = ""/user/cache""
");

        var ciConfigPath = Path.Combine(morphirDir, "morphir.ci.toml");
        File.WriteAllText(ciConfigPath, @"
[morphir.cache]
global = ""/ci/global""
");

        try
        {
            // Act - with CI mode On
            var result = await resolver.ResolveConfigAsync(ciMode: CiProfileMode.On, startPath: _testRoot);

            // Assert
            result.Should().NotBeNull();
            Microsoft.FSharp.Collections.ListModule.Length(result.Layers).Should().Be(3, "workspace, user, and CI layers should be loaded");
            result.CiProfileApplied.Should().BeTrue();

            // User should override workspace for workspace cache
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Effective.Cache.WorkspaceCache).Should().BeTrue();
            result.Effective.Cache.WorkspaceCache.Value.Should().Be("/user/cache", "user has highest precedence for workspace cache");

            // CI should override workspace for global cache
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Effective.Cache.GlobalCache).Should().BeTrue();
            result.Effective.Cache.GlobalCache.Value.Should().Be("/ci/global", "CI has highest precedence for global cache");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
