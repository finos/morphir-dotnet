// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Compilation.Mep;
using Morphir.Extensions;
using Morphir.Extensions.Installation;
using Morphir.Extensions.Wasm;
using Morphir.IR.Trees;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Management;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.TaskRecords;
using ModelOperation = Morphir.Mep.ModelOperation;
using Versioning = Morphir.IR.Versioning;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class MepDocumentTreeGenerationTests
{
    private static string FakeWorkerAssembly => Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");

    [Test]
    [Arguments("v4", "contextual-json", "4.0.0", IrStorageFormat.Json)]
    [Arguments("v4", "contextual-yaml", "4.0.0", IrStorageFormat.Yaml)]
    [Arguments("v3.1", "contextual-json", "3.1.0", IrStorageFormat.Json)]
    [Arguments("v3.1", "contextual-yaml", "3.1.0", IrStorageFormat.Yaml)]
    public async Task RecordedBoundedGenerationNegotiatesContextualJsonAndYamlTrees(
        string versionDir, string profile, string irVersion, IrStorageFormat storageFormat)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("orders.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [frontend]
            language = "fsharp"

            [codegen.text]
            extension = "orders"

            [extensions.orders]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
        var taskOptions = new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) };
        var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                storageFormat, Versioning.parse(irVersion).ResultValue);
            CopyOrdersFixture(versionDir, profile, descriptor.Path.Under(run.Dest));
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }

        var guest = new OrdersChannelGuest(irVersion, storageFormat == IrStorageFormat.Json ? ModelAccessCodec.MorphirJson : ModelAccessCodec.MorphirYaml,
            ModelAccessEncoding.ContextualDraft1);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Backend.Should().Be("org.finos.morphir.orders");
        success.BackendTransport.Should().Be("wasm");
        success.Representation.Should().Be("DocumentTree");
        success.Publication.Artifacts.Should().Equal("order.txt");
        File.ReadAllText(Path.Combine(outputDir, "order.txt")).Should().Be("73\n");
        guest.SelectedTuple.Should().NotBeNull();
        guest.SelectedTuple!.ContextualEncoding.Should().Be(ModelAccessEncoding.ContextualDraft1);
        guest.SelectedTuple.Codec.Should().Be(storageFormat == IrStorageFormat.Json ? ModelAccessCodec.MorphirJson : ModelAccessCodec.MorphirYaml);
        guest.SelectedTuple.IrVersion.Value.Should().Be(irVersion);
        guest.DeliveredContextualEnvelope.Should().BeTrue();
    }

    [Test]
    public async Task OrdersScaleRefusesContextualJsonRecordedTree()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("orders.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [frontend]
            language = "fsharp"

            [codegen.text]
            extension = "orders"

            [extensions.orders]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
        var taskOptions = new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) };
        var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                IrStorageFormat.Json, Versioning.parse("4").ResultValue);
            CopyOrdersFixture("v4", "contextual-json", descriptor.Path.Under(run.Dest));
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }

        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);
        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir)
            { OutDir = outDir, BoundedProfile = "orders-scale" },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), new MepBackendExtensionConnector(), [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Diagnostics.Should().Contain(d => d.Code == "generate.invalid_bounded_profile");
    }

    [Test]
    [Arguments("v4", "plain-json", "4.0.0", false)]
    [Arguments("v4", "contextual-json", "4.0.0", true)]
    [Arguments("v4", "plain-yaml", "4.0.0", false)]
    [Arguments("v4", "contextual-yaml", "4.0.0", true)]
    public async Task ExplicitInputJsonOrYamlDocumentTreeRoutesThroughBoundedGenerationAndPublishes(
        string versionDir, string profile, string irVersion, bool contextual)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture(versionDir, profile, treeRoot);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("orders.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [codegen.text]
            extension = "orders"

            [extensions.orders]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var codec = profile.EndsWith("yaml", StringComparison.Ordinal) ? ModelAccessCodec.MorphirYaml : ModelAccessCodec.MorphirJson;
        var encoding = contextual ? ModelAccessEncoding.ContextualDraft1 : ModelAccessEncoding.Plain;
        var guest = new OrdersChannelGuest(irVersion, codec, encoding);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Representation.Should().Be("DocumentTree");
        success.Publication.Artifacts.Should().Equal("order.txt");
        File.ReadAllText(Path.Combine(outputDir, "order.txt")).Should().Be("73\n");
        File.Exists(Path.Combine(outputDir, GenerationDefaults.OwnershipManifest)).Should().BeTrue();
        guest.SelectedTuple.Should().NotBeNull();
        guest.SelectedTuple!.ContextualEncoding.Should().Be(encoding);
        guest.SelectedTuple.Codec.Should().Be(codec);
    }

    [Test]
    public async Task ExplicitInputJsonDocumentTreeWorksWithProcessExtension()
    {
        using var sandbox = TestSandbox.Create();
        var workDir = sandbox.PathOf("work");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "bounded-orders.mode"), "orders");
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v4", "plain-json", treeRoot);
        var outputDir = sandbox.PathOf("generated");

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(
                new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Extension = FakeWorkerAssembly },
                new InvocationContext(workDir) { Environment = SandboxEnvironment(sandbox) }),
            new CliGenerationSettingsResolver(), new RejectingLoader(),
            new RegisteredBackendResolver([]), new GenerationPublisher(), new MepBackendExtensionConnector(), [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Backend.Should().Be("org.finos.morphir.orders");
        success.BackendTransport.Should().Be("mep");
        success.Representation.Should().Be("DocumentTree");
        File.ReadAllText(Path.Combine(outputDir, "order.txt")).Should().Be("73\n");
        File.Exists(Path.Combine(outputDir, GenerationDefaults.OwnershipManifest)).Should().BeTrue();
    }

    [Test]
    public async Task ExplicitInputTreeRefusesSymlinkAndPublishesNothing()
    {
        if (OperatingSystem.IsWindows()) Skip.Test("Symlink creation requires elevated privileges on Windows.");
        using var sandbox = TestSandbox.Create();
        var realTree = sandbox.PathOf("real-tree");
        CopyOrdersFixture("v4", "plain-json", realTree);
        var linkedTree = sandbox.PathOf("linked-tree");
        Directory.CreateSymbolicLink(linkedTree, realTree);
        var outputDir = sandbox.PathOf("generated");

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(
                new GenerateOverrides(Input: linkedTree, Target: "text", Output: outputDir) { Extension = FakeWorkerAssembly },
                new InvocationContext(sandbox.Work) { Environment = SandboxEnvironment(sandbox) }),
            new CliGenerationSettingsResolver(), new RejectingLoader(),
            new RegisteredBackendResolver([]), new GenerationPublisher(), new MepBackendExtensionConnector(), [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Diagnostics.Should().Contain(d => d.Code == "unsupported-input-entry");
        Directory.Exists(outputDir).Should().BeFalse();
    }

    [Test]
    public async Task ExplicitInputTreeCleansUpStagingOnFailureAndCancellation()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v4", "plain-json", treeRoot);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("orders.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [codegen.text]
            extension = "orders"

            [extensions.orders]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var tempRoot = Morphir.Tooling.Features.Compile.CompilePaths.RealPath(sandbox.PathOf("stage-tmp"));
        Directory.CreateDirectory(tempRoot);
        var env = new Dictionary<string, string>(SandboxEnvironment(sandbox), StringComparer.OrdinalIgnoreCase)
        {
            ["TMPDIR"] = tempRoot,
            ["TEMP"] = tempRoot,
            ["TMP"] = tempRoot,
        };

        // Failure case: guest returns failed step after staging a partial chunk
        string? observedFailureStage = null;
        var failingGuest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain, failOnRound: 2,
            onRound: round =>
            {
                if (round == 1)
                    observedFailureStage = Directory.GetDirectories(tempRoot, "morphir-bounded-stage-*").SingleOrDefault();
            });
        var connector = new MepBackendExtensionConnector(null, (_, _) => (failingGuest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var failed = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = env }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        failed.Should().BeOfType<Outcome<GenerationSuccess>.Failure>();
        Directory.Exists(outputDir).Should().BeFalse();
        observedFailureStage.Should().NotBeNull("the private staging directory should exist during the bounded session");
        Directory.Exists(observedFailureStage!).Should().BeFalse("the private staging directory itself must be removed on failure");

        // Cancellation case
        string? observedCancelStage = null;
        using var cts = new CancellationTokenSource();
        var cancellingGuest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain,
            onRound: round =>
            {
                if (round == 1)
                    observedCancelStage = Directory.GetDirectories(tempRoot, "morphir-bounded-stage-*").SingleOrDefault();
                cts.Cancel();
            });
        var cancelConnector = new MepBackendExtensionConnector(null, (_, _) => (cancellingGuest, "", ""));
        var cancelled = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = env }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), cancelConnector, [], cts.Token);

        cancelled.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error.Kind
            .Should().Be(GenerationFailureKind.Cancelled);
        Directory.Exists(outputDir).Should().BeFalse();
        observedCancelStage.Should().NotBeNull("the private staging directory should exist during the bounded session");
        Directory.Exists(observedCancelStage!).Should().BeFalse("the private staging directory itself must be removed on cancellation");
    }

    [Test]
    public async Task ExplicitInputTreeImmutableSnapshotIgnoresDiskMutationMidSession()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v4", "plain-json", treeRoot);
        var orderFile = Path.Combine(treeRoot, "pkg", "acme", "orders", "domain", "order.value.json");
        var originalBytes = File.ReadAllText(orderFile);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("orders.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [codegen.text]
            extension = "orders"

            [extensions.orders]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain,
            onRound: round =>
            {
                // Mutate the member file on disk after inventory (round 1) and before delivery (round 3).
                if (round == 1)
                    File.WriteAllText(orderFile, """{"formatVersion":4,"name":"order","def":{"mutated":true}}""");
            });
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
        guest.ReceivedOrderContent.Should().NotBeNull();
        guest.ReceivedOrderContent.Should().Be(originalBytes);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task InstalledExtensionIdRunsBoundedGenerationWithProviderObservationAndLifetimeDisposal(bool recorded)
    {
        using var sandbox = TestSandbox.Create();
        var home = sandbox.MorphirHome;
        Directory.CreateDirectory(home);
        var limits = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: true);
        var tuple = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
            SemanticVersion.Create("4.0.0"), "morphir-ir", ModelAccessCodec.MorphirJson, "document-tree", ModelAccessEncoding.ContextualDraft1);
        await InstallFakeWasmExtensionAsync(sandbox, home, "org.finos.morphir.orders", "Orders generator", "0.1.0",
            new ModelAccessSupport([tuple], limits));

        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
        var treeRoot = sandbox.PathOf("input-tree");
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"), "[project]\nname = \"acme/orders\"\n\n[frontend]\nlanguage = \"fsharp\"\n");

        var taskOptions = new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) };
        if (recorded)
        {
            var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
            var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
            using var run = (await store.BeginAsync(default)).ShouldSucceed();
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                IrStorageFormat.Json, Versioning.parse("4").ResultValue);
            CopyOrdersFixture("v4", "contextual-json", descriptor.Path.Under(run.Dest));
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }
        else
        {
            CopyOrdersFixture("v4", "contextual-json", treeRoot);
        }

        var guest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.ContextualDraft1);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);
        var env = new Dictionary<string, string>(SandboxEnvironment(sandbox), StringComparer.OrdinalIgnoreCase)
        {
            ["MORPHIR_HOME"] = home
        };
        var overrides = recorded
            ? new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir, ExtensionId = "org.finos.morphir.orders" }
            : new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { ExtensionId = "org.finos.morphir.orders" };

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(overrides, new InvocationContext(projectRoot) { Environment = env }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Backend.Should().Be("org.finos.morphir.orders");
        success.BackendTransport.Should().Be("wasm");
        success.Representation.Should().Be("DocumentTree");
        success.Provider.Should().NotBeNull();
        success.Provider!.Value.GetProperty("role").GetString().Should().Be("backend");
        success.Provider.Value.GetProperty("extensionId").GetString().Should().Be("org.finos.morphir.orders");
        success.Provider.Value.GetProperty("mepVersion").GetString().Should().Be("0.2.0-draft.2");
        success.Provider.Value.GetProperty("modelTuple").GetProperty("contextualEncoding").GetString()
            .Should().Be("https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json");
        File.ReadAllText(Path.Combine(outputDir, "order.txt")).Should().Be("73\n");
        guest.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task InstalledExtensionIdRefusesSessionClaimsMismatchAndDisposesLifetime()
    {
        using var sandbox = TestSandbox.Create();
        var home = sandbox.MorphirHome;
        Directory.CreateDirectory(home);
        var limits = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: true);
        // Installed static claims only allow Plain encoding, while the live guest negotiates ContextualDraft1.
        var claimedTuple = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
            SemanticVersion.Create("4.0.0"), "morphir-ir", ModelAccessCodec.MorphirJson, "document-tree", ModelAccessEncoding.Plain);
        await InstallFakeWasmExtensionAsync(sandbox, home, "org.finos.morphir.orders", "Orders generator", "0.1.0",
            new ModelAccessSupport([claimedTuple], limits));

        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v4", "contextual-json", treeRoot);
        var outputDir = sandbox.PathOf("generated");
        var guest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.ContextualDraft1);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var env = new Dictionary<string, string>(SandboxEnvironment(sandbox), StringComparer.OrdinalIgnoreCase)
        {
            ["MORPHIR_HOME"] = home
        };

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(
                new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { ExtensionId = "org.finos.morphir.orders" },
                new InvocationContext(sandbox.Work) { Environment = env }),
            new CliGenerationSettingsResolver(), new RejectingLoader(),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Diagnostics.Should().Contain(d => d.Code == "extension.claims_mismatch");
        Directory.Exists(outputDir).Should().BeFalse();
        guest.Disposed.Should().BeTrue();
    }

    [Test]
    [Arguments("4.0.0", "tree")]
    [Arguments("4.0.0", "text")]
    [Arguments("4.0.0", "binary")]
    [Arguments("3.0.0", "tree")]
    public async Task IonInputsConvertLosslesslyToSingleDocumentJsonForDraft1OnlyExtension(string version, string format)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.DailyRate(sandbox.PathOf("input"), version, format);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "car/rental"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: inputPath, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Publication.Artifacts.Should().Equal("converted.txt");
        File.ReadAllText(Path.Combine(outputDir, "converted.txt")).Should().Be("ok\n");
        Directory.GetFiles(outputDir).Select(Path.GetFileName).Should().BeEquivalentTo(["converted.txt", GenerationDefaults.OwnershipManifest]);

        guest.ReceivedPayloadLayout.Should().Be("single-document");
        guest.ReceivedPayloadCodec.Should().Be("morphir-json");
        guest.ReceivedRevision.Should().Be("sha256:" + guest.ReceivedPayloadSha256);

        var converted = success.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted").Which;
        converted.Severity.Should().Be("info");
        converted.Data.Should().NotBeNull();
        converted.Data!.Value.GetProperty("to").GetProperty("codec").GetString().Should().Be("morphir-json");
        converted.Data.Value.GetProperty("to").GetProperty("revision").GetString().Should().Be(guest.ReceivedRevision);
        converted.Data.Value.GetProperty("to").GetProperty("payloadSha256").GetString().Should().Be(guest.ReceivedPayloadSha256);
        converted.Data.Value.GetProperty("dropped").GetArrayLength().Should().Be(0);
    }

    [Test]
    [Arguments("plain-json", false)]
    [Arguments("contextual-yaml", false)]
    [Arguments("contextual-json", true)]
    public async Task DocumentTreeFallsBackToDraft1LosslessConversionWhenGuestLacksDraft2ModelAccess(
        string profile, bool recorded)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
        var treeRoot = sandbox.PathOf("input-tree");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [frontend]
            language = "fsharp"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var taskOptions = new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) };
        if (recorded)
        {
            var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
            var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
            using var run = (await store.BeginAsync(default)).ShouldSucceed();
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                profile.EndsWith("yaml", StringComparison.Ordinal) ? IrStorageFormat.Yaml : IrStorageFormat.Json,
                Versioning.parse("4").ResultValue);
            CopyOrdersFixture("v4", profile, descriptor.Path.Under(run.Dest));
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }
        else
        {
            CopyOrdersFixture("v4", profile, treeRoot);
        }

        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);
        var overrides = recorded
            ? new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir }
            : new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot };

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(overrides, new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Publication.Artifacts.Should().Equal("converted.txt");
        File.ReadAllText(Path.Combine(outputDir, "converted.txt")).Should().Be("ok\n");
        guest.ReceivedPayloadLayout.Should().Be("single-document");
        guest.ReceivedModelId.Should().Be("acme/orders");
        guest.ReceivedRevision.Should().Be("sha256:" + guest.ReceivedPayloadSha256);
        var converted = success.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted").Which;
        converted.Severity.Should().Be("info");
        var expectedFromCodec = profile.EndsWith("yaml", StringComparison.Ordinal) ? "morphir-yaml" : "morphir-json";
        converted.Data!.Value.GetProperty("from").GetProperty("codec").GetString().Should().Be(expectedFromCodec);
        converted.Data.Value.GetProperty("to").GetProperty("codec").GetString().Should().Be("morphir-json");
        if (recorded)
        {
            File.Exists(Path.Combine(outDir, "generate", "text.json")).Should().BeTrue("recorded fallback commits via task ledger");
            File.Exists(Path.Combine(outputDir, GenerationDefaults.OwnershipManifest)).Should().BeFalse("recorded fallback does not write .morphir-generated.json");
        }
    }

    [Test]
    public async Task Classic31DocumentTreeFallsBackToDraft1GuestSupporting31()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v3.1", "contextual-json", treeRoot);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        guest.ReceivedPayloadLayout.Should().Be("single-document");
        guest.ReceivedIrVersion.Should().Be("3.1.0");
        guest.ReceivedModelId.Should().Be("acme/orders");
        guest.ReceivedRevision.Should().Be("sha256:" + guest.ReceivedPayloadSha256);
        var converted = success.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted").Which;
        converted.Data!.Value.GetProperty("from").GetProperty("codec").GetString().Should().Be("morphir-json");
    }

    [Test]
    public async Task Draft1HostConversionRefusesLossyIonHeaderWithoutCallingGuest()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var ionPath = GenerationInputFixtures.DailyRate(sandbox.PathOf("input"), "4.0.0", "text");
        var originalText = File.ReadAllText(ionPath);
        var idx = originalText.IndexOf("morphir::{", StringComparison.Ordinal);
        idx.Should().BeGreaterThanOrEqualTo(0);
        var lossyPath = sandbox.PathOf("lossy.ion");
        File.WriteAllText(lossyPath, originalText.Insert(idx + "morphir::{".Length, "customMeta: \"dropped-by-json\", "));
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "car/rental"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: lossyPath, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    public async Task Draft1FallbackRefusesTreeWithDroppedManifestMetadataWithoutCallingGuest()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v4", "plain-json", treeRoot);
        var manifestPath = Path.Combine(treeRoot, "manifest.json");
        var manifestJson = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, manifestJson.Replace("\"formatVersion\": 4,", "\"formatVersion\": 4, \"version\": \"1.2.3\",", StringComparison.Ordinal));
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    public async Task Draft1ConversionPreservesOriginalModelIdAndPropagatesDiagnosticOnGuestFailure()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.DailyRate(sandbox.PathOf("input"), "4.0.0", "text");
        var expectedOriginalSha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(inputPath)));
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "car/rental"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new Draft1OnlyChannelGuest(failGenerate: true);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: inputPath, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        guest.ReceivedModelId.Should().Be("sha256:" + expectedOriginalSha);
        guest.ReceivedRevision.Should().Be("sha256:" + guest.ReceivedPayloadSha256);
        guest.ReceivedModelId.Should().NotBe(guest.ReceivedRevision);
        failure.Diagnostics.Should().Contain(d => d.Code == "extension.model_converted" && d.Severity == "info");
        failure.Diagnostics.Should().Contain(d => d.Code == "guest.failed");
    }

    [Test]
    public async Task RecordedCompileSwitchesCleanlyBetweenDraft1FallbackAndDraft2BoundedOnSameOutputDir()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("ext.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [frontend]
            language = "fsharp"

            [codegen.text]
            extension = "ext"

            [extensions.ext]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var taskOptions = new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) };
        var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
        var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                IrStorageFormat.Json, Versioning.parse("4").ResultValue);
            CopyOrdersFixture("v4", "plain-json", descriptor.Path.Under(run.Dest));
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }

        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);

        // Run 1: draft.1 fallback publishes via RecordedGenerationTask ledger
        var draft1Guest = new Draft1OnlyChannelGuest();
        var draft1Connector = new MepBackendExtensionConnector(null, (_, _) => (draft1Guest.OpenChannel(), "", ""));
        var first = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), draft1Connector, [], default);
        first.Should().BeOfType<Outcome<GenerationSuccess>.Success>(first.ToString());
        File.Exists(Path.Combine(outputDir, "converted.txt")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "generate", "text.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, GenerationDefaults.OwnershipManifest)).Should().BeFalse();

        // Run 2: draft.2 bounded replaces converted.txt with order.txt cleanly via the same ledger
        var draft2Guest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain);
        var draft2Connector = new MepBackendExtensionConnector(null, (_, _) => (draft2Guest, "", ""));
        var second = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(),
            new RegisteredBackendResolver([]), new GenerationPublisher(), draft2Connector, [], default);
        second.Should().BeOfType<Outcome<GenerationSuccess>.Success>(second.ToString());
        File.Exists(Path.Combine(outputDir, "order.txt")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "converted.txt")).Should().BeFalse("previous draft.1 artifact removed by shared task ledger");

        // Run 3: switching back to draft.1 removes order.txt and installs converted.txt cleanly
        var draft1Again = new Draft1OnlyChannelGuest();
        var draft1AgainConnector = new MepBackendExtensionConnector(null, (_, _) => (draft1Again.OpenChannel(), "", ""));
        var third = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), draft1AgainConnector, [], default);
        third.Should().BeOfType<Outcome<GenerationSuccess>.Success>(third.ToString());
        File.Exists(Path.Combine(outputDir, "converted.txt")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "order.txt")).Should().BeFalse("previous draft.2 artifact removed by shared task ledger");
    }

    [Test]
    public async Task ExplicitContextualYamlTreeFallsBackToProcessDraft1Guest()
    {
        using var sandbox = TestSandbox.Create();
        var workDir = sandbox.PathOf("work");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "bounded-orders.mode"), "orders");
        var treeRoot = sandbox.PathOf("input-tree");
        CopyOrdersFixture("v4", "contextual-yaml", treeRoot);
        var outputDir = sandbox.PathOf("generated");

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(
                new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Extension = FakeWorkerAssembly },
                new InvocationContext(workDir) { Environment = SandboxEnvironment(sandbox) }),
            new CliGenerationSettingsResolver(), new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), new MepBackendExtensionConnector(), [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        success.Backend.Should().Be("org.finos.morphir.orders");
        success.BackendTransport.Should().Be("mep");
        File.ReadAllText(Path.Combine(outputDir, "order.txt")).Should().Be("73\n");
        success.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted" && d.Severity == "info");
    }

    [Test]
    public async Task Draft1HostConversionRefusesOverLimitPayloadBeforeSending()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.DailyRate(sandbox.PathOf("input"), "4.0.0", "tree");
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "car/rental"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var guest = new Draft1OnlyChannelGuest(maxDecodedBytes: 16);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: inputPath, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Diagnostics.Should().Contain(d => d.Code == "extension.not_admitted");
        guest.GenerateCalls.Should().Be(0);
    }

    private static async Task InstallFakeWasmExtensionAsync(TestSandbox sandbox, string home, string extensionId,
        string name, string version, ModelAccessSupport claimedAccess)
    {
        var wasmBytes = new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
        var wasmSha256 = Convert.ToHexStringLower(SHA256.HashData(wasmBytes));
        var accessJson = JsonSerializer.Serialize(claimedAccess, MepJson.Default.ModelAccessSupport);
        var claimsJson = $$"""
            {
              "protocolVersions": ["0.2.0-draft.2"],
              "extension": { "id": "{{extensionId}}", "name": "{{name}}", "version": "{{version}}", "types": ["backend"] },
              "capabilities": {
                "backend": { "targets": ["text"], "irVersions": ["4.0.0"], "generate": false },
                "modelAccess": {{accessJson}},
                "streaming": false, "incremental": false, "cancellation": false, "progress": false
              }
            }
            """;
        var releaseJson = $$"""
            {
              "$schema": "https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json",
              "extensionId": "{{extensionId}}",
              "shortId": "orders",
              "version": "{{version}}",
              "platformDifferences": "none",
              "artifacts": [{
                "runtime": "wasm",
                "filename": "orders.wasm",
                "sha256": "{{wasmSha256}}",
                "claims": {{claimsJson}}
              }]
            }
            """;

        var feed = sandbox.PathOf("feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feed);
        var nupkgPath = Path.Combine(feed, $"morphir.orders.{version}.nupkg");
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Add(string entry, byte[] bytes)
                {
                    using var stream = zip.CreateEntry(entry).Open();
                    stream.Write(bytes);
                }

                Add("Morphir.Orders.nuspec", Encoding.UTF8.GetBytes(
                    $"""<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>Morphir.Orders</id><version>{version}</version><authors>FINOS</authors><description>Orders test extension.</description><packageTypes><packageType name="MorphirExtension" /></packageTypes></metadata></package>"""));
                Add("morphir/release.json", Encoding.UTF8.GetBytes(releaseJson));
                Add("morphir/orders.wasm", wasmBytes);
            }
            await File.WriteAllBytesAsync(nupkgPath, memory.ToArray());
        }

        using var http = new HttpClient();
        var installed = await ExtensionHandlers.InstallFromSourceAsync(
            new ExtensionSourceInstall($"nuget:Morphir.Orders@{version}", home, NoProbe: true, DryRun: false, NuGetSource: feed), http, default);
        installed.Success.Should().BeTrue($"{installed.Code}: {installed.Message}");
    }

    private static IReadOnlyDictionary<string, string> SandboxEnvironment(TestSandbox sandbox)
    {
        var inherited = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);
        return sandbox.EnvironmentSnapshot(inherited);
    }

    private static void CopyOrdersFixture(string versionDir, string profile, string destination)
    {
        var prefix = FixtureCatalog.BoundedOrders.Directory + versionDir + "/" + profile + "/";
        foreach (var entry in FixtureCatalog.BoundedOrders.Files.Where(f => f.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = entry.RelativePath[prefix.Length..];
            var path = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, FixtureCatalog.ReadBytes(entry));
        }
    }

    private sealed class RejectingLoader : IModelLoader
    {
        public ValueTask<Outcome<LoadedModel>> LoadAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct) =>
            throw new InvalidOperationException("Whole-model loader must not be called on the bounded document-tree route.");
    }

    private sealed class OrdersChannelGuest : ISingleMessageChannel, IDisposable
    {
        private readonly ModelAccessTuple _supported;
        private readonly int? _failOnRound;
        private readonly Action<int>? _onRound;
        private readonly Guest _guest;
        private string? _sessionId;
        private string? _orderDocId;
        private string? _finalDocId;
        private string? _domainSelection;
        private int _round = 1;

        public ModelAccessTuple? SelectedTuple { get; private set; }
        public bool DeliveredContextualEnvelope { get; private set; }
        public string? ReceivedOrderContent { get; private set; }
        public bool Disposed { get; private set; }

        public OrdersChannelGuest(string irVersion, ModelAccessCodec codec, ModelAccessEncoding encoding,
            int? failOnRound = null, Action<int>? onRound = null)
        {
            _failOnRound = failOnRound;
            _onRound = onRound;
            _supported = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
                SemanticVersion.Create(irVersion), "morphir-ir", codec, "document-tree", encoding);

            var limits = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: true);
            var support = new ModelAccessSupport([_supported], limits);
            const string claimsJson = """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.2"],"extension":{"id":"org.finos.morphir.orders","name":"Orders generator","version":"0.1.0","types":["backend"]},"capabilities":{}}""";
            var claims = JsonSerializer.Deserialize(claimsJson, MepJson.Default.CapabilityClaimSet)!;
            var supportJson = JsonSerializer.Serialize(support, MepJson.Default.ModelAccessSupport);
            var capabilities = JsonSerializer.Deserialize(
                $$"""{"backend":{"targets":["text"],"irVersions":["{{irVersion}}"],"generate":false},"modelAccess":{{supportJson}}}""",
                MepJson.Default.ExtensionCapabilities)!;

            var handlers = new Dictionary<string, MethodHandler>
            {
                [Method.GenerateModelStart.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessStart, MepJson.Default.ModelAccessStep,
                    (request, _) =>
                    {
                        SelectedTuple = request.Input;
                        _sessionId = request.SessionId;
                        _round = 1;
                        return Task.FromResult(ModelAccessStep.NewContinue(request.SessionId, 1,
                            [ModelAccessRequest.NewInventory("root", "root", 10, null)], [], []));
                    }),
                [Method.GenerateModelAdvance.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessAdvance, MepJson.Default.ModelAccessStep,
                    (advance, _) => Task.FromResult(HandleAdvance(advance))),
                [Method.GenerateModelCancel.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessCancel, MepJson.Default.ModelAccessCancel,
                    (cancel, _) => Task.FromResult(cancel)),
            };
            _guest = new Guest(new GuestDefinition(claims, capabilities, handlers));
        }

        private ModelAccessStep HandleAdvance(ModelAccessAdvance advance)
        {
            _onRound?.Invoke(advance.Round);
            if (_failOnRound == advance.Round)
            {
                return ModelAccessStep.NewFailed(_sessionId!,
                    [new Morphir.Mep.Diagnostic(Morphir.Mep.DiagnosticSeverity.Error, "orders.failed", "Simulated guest failure.", null, [], null)]);
            }

            _round++;
            var delivery = JsonSerializer.SerializeToElement(advance.Deliveries.Single(), MepJson.Default.ModelAccessDelivery);
            if (advance.Round == 1)
            {
                var module = delivery.GetProperty("entries").EnumerateArray()
                    .Select(e => e.GetProperty("module"))
                    .First(m => m.GetProperty("modulePath").GetString() == "domain");
                _domainSelection = module.GetProperty("memberSelection").GetString()!;
                var partial = Encoding.UTF8.GetBytes("partial\n");
                ModelArtifactChunk[] chunks = _failOnRound.HasValue
                    ? [new ModelArtifactChunk("partial.txt", 0, partial.Length, Sha256Digest.Compute(partial), Convert.ToBase64String(partial), false)]
                    : [];
                return ModelAccessStep.NewContinue(_sessionId!, _round,
                    [ModelAccessRequest.NewInventory("members", _domainSelection, 10, null)], chunks, []);
            }
            if (advance.Round == 2)
            {
                foreach (var entry in delivery.GetProperty("entries").EnumerateArray())
                {
                    if (!entry.TryGetProperty("value", out var value)) continue;
                    var name = value.GetProperty("name").GetString();
                    var docId = value.GetProperty("documentId").GetString()!;
                    if (name == "order") _orderDocId = docId;
                    if (name == "total-in-USD") _finalDocId = docId;
                }
                return ModelAccessStep.NewContinue(_sessionId!, _round,
                    [ModelAccessRequest.NewDocument("order-doc", _orderDocId!)], [], []);
            }
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(delivery.GetProperty("content").GetString()!));
            ReceivedOrderContent = content;
            if (content.Contains("ir-context/0.1.0-draft.1/document.schema.json", StringComparison.Ordinal))
                DeliveredContextualEnvelope = true;
            var output = Encoding.UTF8.GetBytes("73\n");
            return ModelAccessStep.NewComplete(_sessionId!,
                [new ModelArtifactChunk("order.txt", 0, output.Length, Sha256Digest.Compute(output), Convert.ToBase64String(output), true)], []);
        }

        public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
            _guest.Dispatcher.DispatchAsync(message, cancellation);

        public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
            _guest.Dispatcher.DispatchAsync(message, cancellation);

        public void Dispose() => Disposed = true;
    }

    private sealed class Draft1OnlyChannelGuest(int maxDecodedBytes = 1024 * 1024, bool failGenerate = false)
    {
        public string? ReceivedPayloadLayout { get; private set; }
        public string? ReceivedPayloadCodec { get; private set; }
        public string? ReceivedPayloadSha256 { get; private set; }
        public string? ReceivedModelId { get; private set; }
        public string? ReceivedRevision { get; private set; }
        public string? ReceivedIrVersion { get; private set; }
        public int GenerateCalls { get; private set; }

        public ISingleMessageChannel OpenChannel()
        {
            var limits = new ModelTransportLimits(maxDecodedBytes * 2, maxDecodedBytes, 128, 100_000);
            var support = new ModelTransportSupport(
                [MepGenerationTransport.V4Tuple, MepGenerationTransport.Classic31Tuple, MepGenerationTransport.ClassicTuple],
                limits, []);
            const string claimsJson = """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"org.example.draft1-backend","name":"Draft1 backend","version":"1.0.0","types":["backend"]},"capabilities":{}}""";
            var claims = JsonSerializer.Deserialize(claimsJson, MepJson.Default.CapabilityClaimSet)!;
            var supportJson = JsonSerializer.Serialize(support, MepJson.Default.ModelTransportSupport);
            var capabilities = JsonSerializer.Deserialize(
                $$"""{"backend":{"targets":["text"],"irVersions":["3.0.0","3.1.0","4.0.0"],"generate":false},"modelTransport":{{supportJson}}}""",
                MepJson.Default.ExtensionCapabilities)!;

            var handlers = new Dictionary<string, MethodHandler>
            {
                [Method.GenerateModel.WireValue] = MethodHandler.Request(MepJson.Default.GenerateModelRequest, MepJson.Default.GenerateResult,
                    (request, _) =>
                    {
                        GenerateCalls++;
                        ReceivedPayloadLayout = request.Model.Payload.Layout.WireValue;
                        ReceivedPayloadCodec = request.Model.Payload.Codec.ToString();
                        ReceivedPayloadSha256 = request.Model.Payload.Sha256.Value;
                        ReceivedModelId = request.Model.ModelId;
                        ReceivedRevision = request.Model.Revision;
                        ReceivedIrVersion = request.Model.IrVersion.Value;
                        var decoded = Convert.FromBase64String(request.Model.Payload.Content);
                        using var doc = JsonDocument.Parse(decoded);
                        doc.RootElement.TryGetProperty("formatVersion", out JsonElement _).Should().BeTrue();
                        if (failGenerate)
                            return Task.FromResult(new GenerateResult(false, [],
                                [new Morphir.Mep.Diagnostic(Morphir.Mep.DiagnosticSeverity.Error, "guest.failed", "Simulated draft.1 guest failure.", null, [], null)]));
                        return Task.FromResult(new GenerateResult(true, [new Morphir.Mep.Artifact("converted.txt", "ok\n", false)], []));
                    }),
            };
            var guest = new Guest(new GuestDefinition(claims, capabilities, handlers));
            return new DispatchChannel(guest.Dispatcher);
        }

        private sealed class DispatchChannel(Dispatcher dispatcher) : ISingleMessageChannel, IDisposable
        {
            public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
                dispatcher.DispatchAsync(message, cancellation);

            public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
                dispatcher.DispatchAsync(message, cancellation);

            public void Dispose() { }
        }
    }
}
