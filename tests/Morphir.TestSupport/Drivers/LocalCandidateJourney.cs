// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Morphir.TestSupport.Packaging;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The explicit inputs of the local candidate journey (docs/content/docs/contributing/local-morphirup-candidate.md):
/// the manager executable, the candidate manifest and archive, the declared SHA-256 of the archive, and an empty
/// evidence directory.
/// </summary>
public sealed record LocalCandidateInputs(string Manager, string Archive, string Manifest, string ArchiveSha256, string Evidence)
{
    /// <summary>The inputs from <c>MORPHIR_LOCAL_*</c>, or null when <c>MORPHIR_LOCAL_MANAGER_EXE</c> is not set: the journey is opt-in.</summary>
    public static LocalCandidateInputs? FromEnvironment()
    {
        var manager = Environment.GetEnvironmentVariable("MORPHIR_LOCAL_MANAGER_EXE");
        if (string.IsNullOrWhiteSpace(manager)) return null;
        return new LocalCandidateInputs(
            Path.GetFullPath(manager),
            LocalCandidateJourney.RequiredFile("MORPHIR_LOCAL_CANDIDATE_ARCHIVE"),
            LocalCandidateJourney.RequiredFile("MORPHIR_LOCAL_CANDIDATE_MANIFEST"),
            LocalCandidateJourney.Required("MORPHIR_LOCAL_CANDIDATE_SHA256"),
            Path.GetFullPath(LocalCandidateJourney.Required("MORPHIR_LOCAL_E2E_REPORT")));
    }
}

/// <summary>
/// The opt-in local candidate journey with real executables and the frozen workflow, never a fake manager: install
/// the candidate with morphirup, select it, compile and generate with it, then again offline with no system .NET. It
/// runs in the workspace of the test. A check that fails throws an <see cref="InvalidOperationException"/> with the
/// reason. The fixed workflow fixture is a copy of <c>Fixtures/Toolchain/workflows/fsharp-compile-generate</c> of the
/// test project in the workspace.
/// </summary>
public static class LocalCandidateJourney
{
    private const string Family = "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.1/";
    private const string SourceDigest = "f924e05d37b0517ac8777b495691ad86207f2b1dc7f5b6e038e6f6e370817d6c";
    private const string IrDigest = "b1035d065ecfddc557476d7203afe29c83a9340abc27300aaeeb911490c30cb7";
    private const string GeneratedDigest = "64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0";

    /// <summary>Runs the journey with <paramref name="inputs"/> in <paramref name="workspace"/>, with the workflow fixture <paramref name="workflow"/>.</summary>
    public static async Task RunAsync(LocalCandidateInputs inputs, IScenarioWorkspace workspace, WorkspaceDirectory workflow)
    {
        var manager = inputs.Manager;
        var archive = inputs.Archive;
        var manifest = inputs.Manifest;
        var expectedArchiveDigest = inputs.ArchiveSha256;
        File.Exists(manager).Should().BeTrue(manager);
        AssertX64Executable(manager);
        ReleaseFiles.Sha256(archive).Should().Be(expectedArchiveDigest);
        var candidateDigest = ReleaseFiles.Sha256(manifest);
        using var candidateDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(manifest));
        var candidate = candidateDocument.RootElement;
        Text(candidate, "$schema").Should().Be(Family + "candidate.schema.json");
        Text(candidate, "origin").Should().Be("local-developer");
        Text(candidate, "implementation").Should().Be("dotnet");
        Text(candidate, "sourceRepository").Should().Be("https://github.com/finos/morphir-dotnet");
        Text(candidate, "deployment").Should().Be("self-contained");
        Text(candidate, "platform").Should().Be("win-x64");
        Text(candidate.GetProperty("archive"), "sha256").Should().Be(expectedArchiveDigest);
        candidate.GetProperty("archive").GetProperty("bytes").GetInt64().Should().Be(new FileInfo(archive).Length);

        var packaged = PackagedArtifact.Find(Path.GetDirectoryName(archive)!, "self-contained", "win-x64");
        Path.GetFullPath(packaged.ArchivePath).Should().Be(archive);
        packaged.Sha256.Should().Be(expectedArchiveDigest);
        packaged.Execution.Should().Be("managed");
        packaged.BundledRuntime.Should().BeTrue();
        packaged.InProcessFSharpFrontend.Should().BeTrue();
        packaged.LaunchPath.Should().Be(Text(candidate.GetProperty("entryPoint"), "path"));
        packaged.RequiredRuntime.Should().BeNull();
        packaged.RequiredSdk.Should().BeNull();
        // The archive release label and the host's full informational SemVer are recorded separately.
        using (var zip = ZipFile.OpenRead(archive))
            foreach (var file in new[] { "morphir.exe", "morphir.dll", "morphir.deps.json", "morphir.runtimeconfig.json",
                         "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "FSharp.Compiler.Service.dll", "FSharp.Core.dll" })
                zip.GetEntry(file).Should().NotBeNull($"the actual whole managed folder must contain {file}");

        var hiding = PackagedRun.HidingPlanForThisMachine();
        hiding.Mode.Should().Be(RuntimeHidingMode.Environment, hiding.Reason);
        var fixture = workflow.Path;
        var fixedSource = Path.Combine(fixture, "source", "Pricing.fs");
        var fixedIr = Path.Combine(fixture, "expected", "ir-4.0.0.json");
        var fixedGenerated = Path.Combine(fixture, "expected", "generated", "Pricing.fs");
        ReleaseFiles.Sha256(fixedSource).Should().Be(SourceDigest);
        ReleaseFiles.Sha256(fixedIr).Should().Be(IrDigest);
        ReleaseFiles.Sha256(fixedGenerated).Should().Be(GeneratedDigest);
        new FileInfo(fixedGenerated).Length.Should().Be(112);

        var evidence = inputs.Evidence;
        if (Directory.Exists(evidence) && Directory.EnumerateFileSystemEntries(evidence).Any())
            throw new InvalidOperationException("Use an empty evidence directory; previous raw results must be retained.");
        Directory.CreateDirectory(evidence);
        var journeyStarted = DateTimeOffset.UtcNow;
        var home = workspace.MorphirHome.Path;
        string PathOf(string name) => Path.Combine(workspace.Root, name);
        Path.Exists(home).Should().BeFalse();
        var acquisition = Directory.CreateDirectory(PathOf("acquisition inputs")).FullName;
        var localArchive = Path.Combine(acquisition, "candidate.zip");
        var localManifest = Path.Combine(acquisition, "candidate.json");
        File.Copy(archive, localArchive);
        File.Copy(manifest, localManifest);
        File.Copy(manifest, Path.Combine(evidence, "input-candidate.json"));
        foreach (var suffix in new[] { ".build-launch.json", ".build-contents.json" })
            File.Copy(archive + suffix, Path.Combine(evidence, "archive" + suffix));
        var installedFolder = Path.Combine(home, "store", "tools", "sha256", expectedArchiveDigest);
        var installedHost = Path.Combine(installedFolder, Text(candidate.GetProperty("entryPoint"), "path").Replace('/', Path.DirectorySeparatorChar));
        var generation = Path.Combine(home, "toolchains", "generations", candidateDigest);
        var receipt = Path.Combine(generation, "receipt.json");
        var selection = Path.Combine(home, "toolchains", "default.json");
        var receiptRelative = $"toolchains/generations/{candidateDigest}/receipt.json";
        var guardDirectory = Directory.CreateDirectory(PathOf("hostile path")).FullName;
        var guard = Path.Combine(guardDirectory, "dotnet.exe");
        // A real native Windows executable, not a script or a framework-dependent surrogate. --version must refuse.
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), guard);
        var emptyProgramFiles = Directory.CreateDirectory(PathOf("no system dotnet")).FullName;

        async Task<ChildProcessResult> Invoke(string label, string executable, string[] arguments, bool hidden = false, bool success = true,
            string? workingDirectory = null)
        {
            var start = ChildProcess.StartInfo(executable, arguments);
            workspace.Contain(start);
            start.WorkingDirectory = workingDirectory ?? workspace.Work.Path;
            if (hidden)
            {
                PackagedRun.HideRuntime(start, emptyProgramFiles);
                start.Environment["PATH"] = guardDirectory;
                start.Environment["DOTNET_HOST_PATH"] = guard;
                start.Environment["HTTP_PROXY"] = "http://127.0.0.1:9";
                start.Environment["HTTPS_PROXY"] = "http://127.0.0.1:9";
                start.Environment["ALL_PROXY"] = "http://127.0.0.1:9";
                start.Environment["NO_PROXY"] = "";
            }
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
            start.Environment["MORPHIR_TEST_MAX_PROCESSES"] = "2";
            start.Environment["DOTNET_HOST_TRACE"] = "1";
            start.Environment["DOTNET_HOST_TRACEFILE"] = Path.Combine(evidence, label + ".hosttrace.log");
            start.Environment["COREHOST_TRACE"] = "1";
            start.Environment["COREHOST_TRACEFILE"] = start.Environment["DOTNET_HOST_TRACEFILE"];
            await File.WriteAllTextAsync(Path.Combine(evidence, label + ".invocation.json"), JsonSerializer.Serialize(new
            {
                executable,
                arguments,
                workingDirectory = start.WorkingDirectory,
                hidden,
                path = start.Environment.TryGetValue("PATH", out var searchPath) ? searchPath : null,
                home,
                guardSha256 = ReleaseFiles.Sha256(guard)
            }));
            try
            {
                var remaining = TimeSpan.FromMinutes(3) - (DateTimeOffset.UtcNow - journeyStarted);
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("No new child admitted after the journey deadline.");
                var timeout = remaining < TimeSpan.FromMinutes(2) ? remaining : TimeSpan.FromMinutes(2);
                var result = await ChildProcess.RunAsync(start, timeout);
                await File.WriteAllTextAsync(Path.Combine(evidence, label + ".stdout.log"), result.StandardOutput);
                await File.WriteAllTextAsync(Path.Combine(evidence, label + ".stderr.log"), result.StandardError);
                await File.WriteAllTextAsync(Path.Combine(evidence, label + ".result.json"), JsonSerializer.Serialize(new
                { result.ExitCode, result.StartedAt, result.ExitedAt }));
                if (success) result.ExitCode.Should().Be(0, result.CombinedOutput);
                return result;
            }
            catch (ChildProcessTimeoutException error)
            {
                await File.WriteAllTextAsync(Path.Combine(evidence, label + ".stdout.log"), error.StandardOutput);
                await File.WriteAllTextAsync(Path.Combine(evidence, label + ".stderr.log"), error.StandardError);
                throw;
            }
        }

        async Task<ChildProcessResult> Manager(string label, string[] arguments, bool hidden = false) =>
            await Invoke(label, manager, arguments, hidden);
        async Task<ChildProcessResult> Host(string label, string[] arguments, bool hidden = false, string? workingDirectory = null) =>
            await Invoke(label, manager, ["run", "--home", home, "--", .. arguments], hidden, workingDirectory: workingDirectory);
        void Result(ChildProcessResult output, string command, string status)
        {
            using var json = JsonDocument.Parse(output.StandardOutput);
            Text(json.RootElement, "$schema").Should().Be(Family + "result.schema.json");
            Text(json.RootElement, "origin").Should().Be("local-developer");
            Text(json.RootElement, "command").Should().Be(command);
            Text(json.RootElement, "status").Should().Be(status);
            Text(json.RootElement, "candidateSha256").Should().Be(candidateDigest);
            if (status != "planned") Text(json.RootElement, "receiptPath").Should().Be(receiptRelative);
            if (json.RootElement.TryGetProperty("selectionPath", out var selectionPath))
                selectionPath.GetString().Should().Be("toolchains/default.json");
        }

        await Manager("manager-version", ["--version"]);
        Result(await Manager("setup-dry-run", ["setup", "--candidate", localManifest, "--local-archive", localArchive,
            "--home", home, "--local-developer", "--dry-run"]), "setup", "planned");
        Path.Exists(home).Should().BeFalse("dry-run must not create even a lease or stage");
        Result(await Manager("setup", ["setup", "--candidate", localManifest, "--local-archive", localArchive,
            "--home", home, "--local-developer", "--yes"]), "setup", "installed");
        File.Exists(selection).Should().BeFalse("installation must not silently select");
        File.ReadAllBytes(Path.Combine(generation, "candidate.json")).Should().Equal(File.ReadAllBytes(manifest));
        Result(await Manager("select", ["select", "--generation", candidateDigest, "--home", home]), "select", "selected");
        Result(await Manager("show", ["show", "--explain", "--json", "--home", home]), "show", "shown");
        AssertX64Executable(installedHost);
        ReleaseFiles.Sha256(installedHost).Should().Be(Text(candidate.GetProperty("entryPoint"), "sha256"));
        await File.WriteAllTextAsync(Path.Combine(evidence, "folder-installed.json"), JsonSerializer.Serialize(AssertFullFolder(candidate, installedFolder)));
        using (var json = JsonDocument.Parse(await File.ReadAllBytesAsync(receipt)))
        {
            var value = json.RootElement;
            Text(value, "$schema").Should().Be(Family + "receipt.schema.json");
            foreach (var member in new[] { "origin", "implementation", "deployment", "platform", "version", "sourceCommit", "buildIdentity" })
                Text(value, member).Should().Be(Text(candidate, member));
            Text(value, "candidateSha256").Should().Be(candidateDigest);
            Text(value, "archiveSha256").Should().Be(expectedArchiveDigest);
            Text(value, "installationPath").Should().Be($"store/tools/sha256/{expectedArchiveDigest}");
            Text(value, "owner").Should().Be("manager-owned");
            Text(value, "readiness").Should().Be("launch-verified");
            JsonNode.DeepEquals(JsonNode.Parse(value.GetProperty("entryPoint").GetRawText()),
                JsonNode.Parse(candidate.GetProperty("entryPoint").GetRawText())).Should().BeTrue();
        }
        using (var json = JsonDocument.Parse(await File.ReadAllBytesAsync(selection)))
        {
            Text(json.RootElement, "$schema").Should().Be(Family + "selection.schema.json");
            Text(json.RootElement, "origin").Should().Be("local-developer");
            Text(json.RootElement, "candidateSha256").Should().Be(candidateDigest);
            Text(json.RootElement, "receiptPath").Should().Be(receiptRelative);
            Text(json.RootElement, "receiptSha256").Should().Be(ReleaseFiles.Sha256(receipt));
        }
        File.Copy(receipt, Path.Combine(evidence, "installed-receipt.json"));
        File.Copy(selection, Path.Combine(evidence, "selected.json"));
        var receiptBytes = await File.ReadAllBytesAsync(receipt);
        var selectionBytes = await File.ReadAllBytesAsync(selection);
        (await Host("host-version", ["--version"])).StandardOutput.Trim().Should().Be(Text(candidate, "version"));
        foreach (var (command, option) in new[] { ("compile", "--language"), ("generate", "--target") })
        {
            var help = await Host(command + "-help", [command, "--help"]);
            help.CombinedOutput.Should().Contain(option).And.Contain("--input").And.Contain("--output");
        }

        async Task Workflow(string phase, bool hidden)
        {
            var project = Directory.CreateDirectory(Path.Combine(workspace.Work.Path, phase)).FullName;
            File.Copy(fixedSource, Path.Combine(project, "Pricing.fs"));
            var irDirectory = Path.Combine(project, "compiled");
            var compiled = await Host(phase + "-compile", ["compile", "--language", "fsharp", "--input", "Pricing.fs",
                "--package-name", "acme/demo", "--ir-version", "4", "--output", "compiled", "--out-dir", "out", "--json"], hidden, project);
            var ir = Path.Combine(irDirectory, "morphir-ir.json");
            File.Copy(ir, Path.Combine(evidence, phase + "-ir.json"));
            using (var report = JsonDocument.Parse(compiled.StandardOutput))
                report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(compiled.StandardOutput);
            JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllTextAsync(fixedIr)), JsonNode.Parse(await File.ReadAllTextAsync(ir)))
                .Should().BeTrue("the frozen IR, with object member order ignored, is the shared expectation");
            var generated = Path.Combine(project, "generated");
            await Host(phase + "-generate", ["generate", "--target", "fsharp", "--input", "compiled/morphir-ir.json",
                "--output", "generated", "--option", "format=false", "--json"], hidden, project);
            var pricing = Path.Combine(generated, "Pricing.fs");
            File.Copy(pricing, Path.Combine(evidence, phase + "-Pricing.fs"));
            Directory.GetFiles(generated, "*.fs", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(generated, path))
                .Should().Equal("Pricing.fs");
            (await File.ReadAllBytesAsync(pricing)).Should().Equal(await File.ReadAllBytesAsync(fixedGenerated));
            new FileInfo(pricing).Length.Should().Be(112);
            ReleaseFiles.Sha256(pricing).Should().Be(GeneratedDigest);
            var trace = await File.ReadAllTextAsync(Path.Combine(evidence, phase + "-compile.hosttrace.log"));
            HostTrace.BundledRuntimeViolations(trace, installedFolder, []).Should().BeEmpty();
        }

        await Workflow("online", hidden: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "folder-before-offline.json"), JsonSerializer.Serialize(AssertFullFolder(candidate, installedFolder)));
        File.Move(localArchive, localArchive + ".withdrawn");
        File.Move(localManifest, localManifest + ".withdrawn");
        (await Invoke("hostile-dotnet-control", guard, ["--version"], hidden: true, success: false)).ExitCode.Should().NotBe(0);
        Result(await Manager("offline-show", ["show", "--explain", "--json", "--home", home], hidden: true), "show", "shown");
        await Workflow("offline", hidden: true);
        await File.WriteAllTextAsync(Path.Combine(evidence, "folder-after-offline.json"), JsonSerializer.Serialize(AssertFullFolder(candidate, installedFolder)));
        (await File.ReadAllBytesAsync(receipt)).Should().Equal(receiptBytes);
        (await File.ReadAllBytesAsync(selection)).Should().Equal(selectionBytes);
        ReleaseFiles.Sha256(archive).Should().Be(expectedArchiveDigest);
        await File.WriteAllTextAsync(Path.Combine(evidence, "identity.json"), JsonSerializer.Serialize(new
        {
            manager,
            managerSha256 = ReleaseFiles.Sha256(manager),
            archive,
            archiveSha256 = expectedArchiveDigest,
            candidateDigest,
            installedHostSha256 = ReleaseFiles.Sha256(installedHost),
            coreClrSha256 = ReleaseFiles.Sha256(Path.Combine(installedFolder, "coreclr.dll")),
            version = Text(candidate, "version"),
            archiveRelease = packaged.Release,
            sourceCommit = Text(candidate, "sourceCommit"),
            buildIdentity = Text(candidate, "buildIdentity"),
            provenance = "local-developer",
            hiding = hiding.Reason,
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            testProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            qualification = "local-windows-x64-only"
        }));
    }

    internal static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Supply {name}; the actual-artifact test has no fallback.");
    internal static string RequiredFile(string name)
    {
        var path = Path.GetFullPath(Required(name));
        File.Exists(path).Should().BeTrue(path);
        return path;
    }
    private static string Text(JsonElement value, string member) => value.GetProperty(member).GetString()
        ?? throw new InvalidOperationException($"Expected text for {member}.");
    private sealed record ObservedFile(string Path, string Sha256, long Bytes);
    private static IReadOnlyList<ObservedFile> AssertFullFolder(JsonElement candidate, string installedFolder)
    {
        File.GetAttributes(installedFolder).HasFlag(FileAttributes.ReparsePoint).Should().BeFalse(installedFolder);
        var files = candidate.GetProperty("files").EnumerateArray().ToDictionary(file => Text(file, "path"), StringComparer.Ordinal);
        files.Count.Should().BeInRange(1, 8192);
        var actual = new List<ObservedFile>();
        var pending = new Stack<string>();
        pending.Push(installedFolder);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                attributes.HasFlag(FileAttributes.ReparsePoint).Should().BeFalse(entry);
                var relative = Path.GetRelativePath(installedFolder, entry).Replace('\\', '/');
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    files.Keys.Any(path => path.StartsWith(relative + "/", StringComparison.Ordinal)).Should().BeTrue(entry);
                    pending.Push(entry);
                }
                else
                {
                    Check(files.ContainsKey(relative), relative);
                    var expected = files[relative];
                    var bytes = new FileInfo(entry).Length;
                    var digest = ReleaseFiles.Sha256(entry);
                    bytes.Should().Be(expected.GetProperty("bytes").GetInt64(), relative);
                    digest.Should().Be(Text(expected, "sha256"), relative);
                    actual.Add(new(relative, digest, bytes));
                }
            }
        actual.Select(file => file.Path).Order(StringComparer.Ordinal).Should().Equal(files.Keys.Order(StringComparer.Ordinal));
        return actual.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }
    private static void AssertX64Executable(string path)
    {
        using var input = File.OpenRead(path);
        using var pe = new PEReader(input);
        pe.PEHeaders.CoffHeader.Machine.Should().Be(Machine.Amd64);
    }

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}

/// <summary>
/// The checks of <see cref="LocalCandidateJourney"/>, with the names of the assertions of the test that it came
/// from. A failed check throws an <see cref="InvalidOperationException"/>.
/// </summary>
internal static class JourneyExpectations
{
    public static Expectation<T> Should<T>(this T value) => new(value);

    internal readonly record struct Expectation<T>(T Value)
    {
        public Expectation<T> And => this;

        public Expectation<T> Be(T expected, string because = "") =>
            EqualityComparer<T>.Default.Equals(Value, expected) ? this : throw Failed($"is {Value}, not {expected}", because);

        public Expectation<T> NotBe(T unexpected, string because = "") =>
            !EqualityComparer<T>.Default.Equals(Value, unexpected) ? this : throw Failed($"is {Value}", because);

        public Expectation<T> BeNull(string because = "") => Value is null ? this : throw Failed($"is {Value}, not null", because);

        public Expectation<T> NotBeNull(string because = "") => Value is not null ? this : throw Failed("is null", because);

        public Expectation<T> BeTrue(string because = "") => Value is true ? this : throw Failed("is false", because);

        public Expectation<T> BeFalse(string because = "") => Value is false ? this : throw Failed("is true", because);

        public Expectation<T> BeEmpty(string because = "") =>
            Items().Count == 0 ? this : throw Failed($"has {Items().Count} items", because);

        /// <summary>The items equal <paramref name="expected"/>. A string is one item, not a sequence of characters.</summary>
        public Expectation<T> Equal(System.Collections.IEnumerable expected, string because = "")
        {
            object?[] items = expected is string text ? [text] : [.. expected.Cast<object?>()];
            return Items().SequenceEqual(items) ? this : throw Failed($"is [{string.Join(", ", Items())}], not [{string.Join(", ", items)}]", because);
        }

        public Expectation<T> Equal(params object[] expected) => Equal((System.Collections.IEnumerable)expected);

        public Expectation<T> Contain(string text, string because = "") =>
            Value is string value && value.Contains(text, StringComparison.Ordinal) ? this : throw Failed($"does not contain '{text}'", because);

        public Expectation<T> BeInRange(long minimum, long maximum, string because = "") =>
            Convert.ToInt64(Value, System.Globalization.CultureInfo.InvariantCulture) is var number && number >= minimum && number <= maximum
                ? this : throw Failed($"is {Value}, not in [{minimum}, {maximum}]", because);

        private List<object?> Items() => Value is System.Collections.IEnumerable items ? [.. items.Cast<object?>()] : throw Failed("is not a sequence", "");

        private static InvalidOperationException Failed(string problem, string because) =>
            new($"The value {problem}{(because.Length == 0 ? "." : $": {because}")}");
    }
}
