// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ConfigurationEditingTests
{
    [Test]
    public void InMemoryHostPreviewsWithoutInvokingAnyWriteOperation()
    {
        const string path = "/repo/morphir.toml";
        var files = new Dictionary<string, string> { [path] = "# keep\n[project]\nname='App'\n[custom]\nvalue='before'\n" };
        var tree = new InMemoryProjectTree("/repo", files);
        var host = new ReadOnlySnapshotHost(files);
        var preview = new ConfigurationEditService(tree, host).Plan("/repo", new EditRequest(EditOperation.NewSet("after", EditValueType.String), "custom.value"));
        preview.Failure.Should().BeNull();
        preview.Plan!.After!.Value.Should().Be(ConfigValue.NewString("after"));
        preview.Plan.Effective.Value!.Value.Should().Be(ConfigValue.NewString("after"));
        preview.Transaction!.Observations.Should().Contain(o => o.Path == "/repo/.morphir/morphir.toml" && o.Kind == ConfigurationObservationKind.Missing);
        files[path].Should().Contain("value='before'");
    }

    [Test]
    public void FileEvidenceAndParsedContentUseTheSameStorageBuffer()
    {
        const string path = "/repo/morphir.toml";
        var files = new Dictionary<string, string> { [path] = "[project]\nname='App'\n[custom]\nvalue='old'\n" };
        var tree = new InMemoryProjectTree("/repo", files);
        files[path] = files[path].Replace("'old'", "'new'", StringComparison.Ordinal);
        var preview = new ConfigurationEditService(tree, new ReadOnlySnapshotHost(files)).Plan("/repo", Set("custom.value", "after"));
        preview.Failure.Should().BeNull();
        preview.Plan!.Before!.Value.Should().Be(ConfigValue.NewString("new"));
    }

    [Test]
    public void ExplicitFilesCannotBorrowMachineVolumeAuthorization()
    {
        var files = new Dictionary<string, string> { ["/repo/morphir.toml"] = "[project]\nname='App'\n" };
        var tree = new InMemoryProjectTree("/repo", files);
        var options = new ProjectLoadOptions { MachineTrees = [new InMemoryProjectTree("/", new Dictionary<string, string>())] };
        var preview = new ConfigurationEditService(tree, new ReadOnlySnapshotHost(files)).Plan("/repo", Set("custom.value", "after"), [EditScope.NewFile("/outside/config.toml")], options: options);
        preview.Failure.Should().NotBeNull();
        preview.Failure!.Code.Should().Be("config.edit_boundary");
    }

    [Test]
    public void IsolatedPlanningDoesNotObserveUnselectedPersonalSources()
    {
        var files = new Dictionary<string, string> { ["/repo/morphir.toml"] = "[project]\nname='App'\n", ["/repo/morphir.user.toml"] = "[custom]\nvalue='personal'\n" };
        var tree = new InMemoryProjectTree("/repo", files);
        var preview = new ConfigurationEditService(tree, new ReadOnlySnapshotHost(files)).Plan("/repo", Set("custom.value", "after"), options: new ProjectLoadOptions { Isolated = true });
        preview.Failure.Should().BeNull();
        tree.ObservedPaths.Should().NotContain("/repo/morphir.user.toml");
    }

    [Test]
    public void AmbiguousAutomaticProjectsAndInvalidDefaultsFailWithoutAWritePlan()
    {
        var files = new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['a','b']\n",
            ["/repo/a/morphir.toml"] = "[project]\nname='A'\n",
            ["/repo/b/morphir.toml"] = "[project]\nname='B'\n"
        };
        var tree = new InMemoryProjectTree("/repo", files, ["/repo/tools"]);
        var ambiguous = new ConfigurationEditService(tree, new ReadOnlySnapshotHost(files)).Plan("/repo/tools", Set("custom.value", "after"));
        ambiguous.Failure.Should().NotBeNull();
        ambiguous.Transaction.Should().BeNull();
        files["/repo/morphir.toml"] += "default_member='missing'\n";
        var invalidTree = new InMemoryProjectTree("/repo", files, ["/repo/tools"]);
        var invalid = new ConfigurationEditService(invalidTree, new ReadOnlySnapshotHost(files)).Plan("/repo/a", Set("custom.value", "after"));
        invalid.Failure.Should().NotBeNull();
        invalid.Failure!.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.invalid_default");
        invalid.Transaction.Should().BeNull();
    }

    [Test]
    [Arguments("morphir.json", "{name:'App',exposedModules:[],custom:{value:'lower'}}")]
    [Arguments("morphir.json5", "{project:{name:'App'},custom:{value:'lower'}}")]
    [Arguments("morphir.yaml", "project: {name: App}\ncustom: {value: lower}\n")]
    [Arguments(".config/morphir/config.yml", "project: {name: App}\ncustom: {value: lower}\n")]
    public void ScopedPromotionRetainsLowerBytesAndUnsetRevealsTheirOrigin(string name, string text)
    {
        using var fixture = new NativeFixture();
        var lower = fixture.Write(name, text);
        var service = fixture.Service;
        var preview = service.Plan(fixture.Root, Set("custom.value", "written"));
        preview.Failure.Should().BeNull();
        preview.Plan!.Target.Path.Should().Be(Path.ChangeExtension(lower, ".toml"));
        service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllText(lower).Should().Be(text);
        var unset = service.Plan(fixture.Root, new EditRequest(EditOperation.Unset, "custom.value"));
        unset.Failure.Should().BeNull();
        unset.Plan!.Effective.Value!.Value.Should().Be(ConfigValue.NewString("lower"));
        unset.Plan.Effective.Contributions.ToArray().Should().Contain(c => c.Winner && c.Source.Path == lower);
        service.Apply(unset).Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllText(lower).Should().Be(text);
    }

    [Test]
    public void CandidateTopologyIsResolvedAgainThroughCanonicalDiscovery()
    {
        using var fixture = new NativeFixture();
        var workspace = fixture.Write("morphir.toml", "[workspace]\nmembers=['app']\n");
        fixture.Write("app/morphir.toml", "[project]\nname='App'\n");
        var preview = fixture.Service.Plan(Path.Combine(fixture.Root, "app"),
            new EditRequest(EditOperation.NewSet("[]", EditValueType.Array), "workspace.members"), [EditScope.NewFile(workspace)]);
        preview.Failure.Should().BeNull();
        preview.Plan!.Effective.Value.Should().NotBeNull("the removed member no longer hides root workspace configuration");
        preview.Plan.Effective.Value!.Value.Should().Be(ConfigValue.NewArray(Microsoft.FSharp.Collections.FSharpList<ConfigValue>.Empty));
    }

    [Test]
    public void NewAlternatePrimaryConflictsAndIdenticalWritesRetainTimestamp()
    {
        using var fixture = new NativeFixture();
        var target = fixture.Write("morphir.toml", "[project]\nname='App'\n[custom]\nvalue='same'\n");
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, timestamp);
        var noop = fixture.Service.Plan(fixture.Root, Set("custom.value", "same"));
        fixture.Service.Apply(noop).Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        File.GetLastWriteTimeUtc(target).Should().Be(timestamp);
        var stale = fixture.Service.Plan(fixture.Root, Set("custom.value", "after"));
        fixture.Write(".morphir/morphir.toml", "[custom]\nvalue='other'\n");
        fixture.Service.Apply(stale).Status.Should().Be(ConfigurationTransactionStatus.Conflict);
        File.ReadAllText(target).Should().Contain("value='same'");
    }

    [Test]
    public void EditsRetainAcceptedBomEncodingsAndEveryUnrelatedByte()
    {
        Encoding[] encodings = [new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true)];
        const string text = "# keep é € 🐦\r\n[project]\r\nname='App'\r\n[custom]\r\nvalue = 'before' # keep inline\r\nother='é € 🐦'\r\n";
        foreach (var encoding in encodings)
        {
            using var fixture = new NativeFixture();
            var path = ProjectPath.join(fixture.Root, "morphir.toml");
            File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
            var preview = fixture.Service.Plan(fixture.Root, Set("custom.value", "after"));
            preview.Failure.Should().BeNull(encoding.WebName);
            fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
            File.ReadAllBytes(path).Should().Equal([.. encoding.GetPreamble(), .. encoding.GetBytes(text.Replace("'before'", "\"after\"", StringComparison.Ordinal))]);
            var unchanged = fixture.Service.Plan(fixture.Root, Set("custom.value", "after"));
            fixture.Service.Apply(unchanged).Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void WorkspaceWritesExplainSurvivingMemberOverrides(bool automaticAtRoot)
    {
        using var fixture = new NativeFixture();
        var workspace = fixture.Write("morphir.toml", "[workspace]\nmembers=['app']\ndefault_member='app'\n[custom]\nvalue='workspace'\n");
        var member = fixture.Write("app/morphir.toml", "[project]\nname='App'\n[custom]\nvalue='member'\n");
        var originalMember = File.ReadAllBytes(member);
        var preview = fixture.Service.Plan(automaticAtRoot ? fixture.Root : ProjectPath.join(fixture.Root, "app"),
            Set("custom.value", "written"), automaticAtRoot ? [] : [EditScope.Workspace]);
        preview.Failure.Should().BeNull();
        preview.Plan!.Target.Path.Should().Be(workspace);
        preview.Plan.After!.Value.Should().Be(ConfigValue.NewString("written"));
        preview.Plan.Effective.Value!.Value.Should().Be(ConfigValue.NewString("member"));
        preview.Plan.Effective.Contributions.ToArray().Should().Contain(c => c.Winner && c.Source.Role == SourceRole.MemberPrimary);
        fixture.Service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllBytes(member).Should().Equal(originalMember);
    }

    [Test]
    [Arguments("read")]
    [Arguments("mismatch")]
    [Arguments("stale")]
    public void PlanningReadFaultsAndUnstableEvidenceReturnTypedFailures(string mode)
    {
        var files = new Dictionary<string, string> { ["/repo/morphir.toml"] = "[project]\nname='App'\n[custom]\nvalue='before'\n" };
        var host = new FaultingSnapshotHost(new ReadOnlySnapshotHost(files), mode);
        var preview = new ConfigurationEditService(new InMemoryProjectTree("/repo", files), host).Plan("/repo", Set("custom.value", "after"));
        preview.Failure.Should().NotBeNull();
        preview.Failure!.Code.Should().Be(mode == "read" ? "config.edit_io" : "config.edit_conflict");
        preview.Failure.ExitCode.Should().Be(2);
        preview.Transaction.Should().BeNull();
        files["/repo/morphir.toml"].Should().Contain("value='before'");
    }

    [Test]
    public void MemberPersonalAndSelectedPersonalEditsKeepTheirOwnership()
    {
        using var fixture = new NativeFixture();
        fixture.Write("morphir.toml", "[workspace]\nmembers=['app']\nout_dir='root-out'\n");
        fixture.Write("app/.morphir/morphir.yaml", "project: {name: App}\ncustom: {value: member}\n");
        var start = ProjectPath.join(fixture.Root, "app");
        var user = fixture.Service.Plan(start, Set("custom.value", "personal"), [EditScope.User]);
        user.Failure.Should().BeNull();
        var target = ProjectPath.join(start, ".morphir/morphir.user.toml");
        user.Plan!.Target.Path.Should().Be(target);
        fixture.Service.Apply(user).Status.Should().Be(ConfigurationTransactionStatus.Written);
        var selected = fixture.Service.Plan(start, Set("workspace.out_dir", "ignored-member-output"), [EditScope.NewFile(target)], options: new ProjectLoadOptions { Isolated = true });
        selected.Failure.Should().BeNull();
        selected.Plan!.Target.Role.Should().Be(SourceRole.UserOverride);
        selected.Plan.CandidateInput.OwningWorkspace!.Value.OutputDirectory!.Value.Should().Be(ProjectPath.join(fixture.Root, "root-out"));
        selected.Plan.Effective.Value!.Value.Should().NotBe(ConfigValue.NewString("ignored-member-output"));
        selected.Plan.Effective.Diagnostics.ToArray().Should().Contain(d => d.Code == "config.member_output");
    }

    [Test]
    public void ExplicitAlternateTomlPrimaryCannotIntroduceAmbiguity()
    {
        using var fixture = new NativeFixture();
        fixture.Write("morphir.toml", "[project]\nname='App'\n");
        var alternate = ProjectPath.join(fixture.Root, ".morphir/morphir.toml");
        var preview = fixture.Service.Plan(fixture.Root, Set("custom.value", "after"), [EditScope.NewFile(alternate)]);
        preview.Failure.Should().NotBeNull();
        preview.Failure!.Code.Should().Be("config.edit_ambiguous");
        File.Exists(alternate).Should().BeFalse();
    }

    private sealed class FaultingSnapshotHost(IConfigurationTransactionHost inner, string mode) : IConfigurationTransactionHost
    {
        private int targetObservations;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
        {
            var result = inner.Observe(path, excludedPaths);
            if (path == "/repo/morphir.toml" && ++targetObservations > 1 && mode == "stale") return result with { Identity = new string('0', 64) };
            return result;
        }
        public byte[] ReadFile(string path) => mode == "read" ? throw new IOException("injected read failure") : mode == "mismatch" ? Encoding.UTF8.GetBytes("custom='changed'\n") : inner.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => inner.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => inner.CreateParentDirectories(destination);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => inner.Stage(destination, content, transactionId, role);
        public void Install(string stagedPath, string destination, bool overwrite) => inner.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
    }

    private static EditRequest Set(string key, string value) => new(EditOperation.NewSet(value, EditValueType.String), key);

    private sealed class NativeFixture : IDisposable
    {
        public string Root { get; } = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "morphir-edit-service-" + Guid.NewGuid().ToString("N")));
        public ConfigurationEditService Service { get; }
        public NativeFixture() { Directory.CreateDirectory(Root); Service = new(new FileSystemProjectTree(Root), new NativeConfigurationTransactionHost()); }
        public string Write(string relative, string text)
        {
            var path = ProjectPath.join(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class ReadOnlySnapshotHost(IReadOnlyDictionary<string, string> files) : IConfigurationTransactionHost
    {
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
        {
            if (files.TryGetValue(path, out var text)) return new(path, ConfigurationObservationKind.File, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
            if (path == "/repo") return new(path, ConfigurationObservationKind.Directory, "root");
            return new(path, ConfigurationObservationKind.Missing, "");
        }
        public byte[] ReadFile(string path) => Encoding.UTF8.GetBytes(files[path]);
        public IDisposable Acquire(IReadOnlyList<string> paths) => throw new InvalidOperationException("Preview must not acquire a writer");
        public IReadOnlyList<string> CreateParentDirectories(string destination) => throw new InvalidOperationException("Preview must not create directories");
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => throw new InvalidOperationException("Preview must not stage");
        public void Install(string stagedPath, string destination, bool overwrite) => throw new InvalidOperationException("Preview must not install");
        public void DeleteFile(string path) => throw new InvalidOperationException("Preview must not delete");
        public void DeleteDirectory(string path) => throw new InvalidOperationException("Preview must not delete");
    }
}
