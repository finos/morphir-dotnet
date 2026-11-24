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

        var originalCapture = await new BoundedModelCapture().CaptureAsync(new ModelInput(treeRoot), ModelReadLimits.Default, default);
        var originalTree = ((Outcome<RetainedInput>.Success)originalCapture).Value;
        var expectedRevision = "sha256:" + ModelUpgrade.SnapshotOf(originalTree).Hex;

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
        guest.ReceivedSnapshotRevision.Should().Be(expectedRevision);

        var mutatedCapture = await new BoundedModelCapture().CaptureAsync(new ModelInput(treeRoot), ModelReadLimits.Default, default);
        var mutatedRevision = "sha256:" + ModelUpgrade.SnapshotOf(((Outcome<RetainedInput>.Success)mutatedCapture).Value).Hex;
        guest.ReceivedSnapshotRevision.Should().NotBe(mutatedRevision);
    }

    [Test]
    public void SnapshotOfTreePreventsBoundaryShiftCollisions()
    {
        var single = new RetainedInput.Tree("/virtual", [new RetainedEntry("a", Encoding.UTF8.GetBytes("X\0bY"))]);
        var split = new RetainedInput.Tree("/virtual",
        [
            new RetainedEntry("a", Encoding.UTF8.GetBytes("X")),
            new RetainedEntry("b", Encoding.UTF8.GetBytes("Y")),
        ]);

        ModelUpgrade.SnapshotOf(single).Hex.Should().NotBe(ModelUpgrade.SnapshotOf(split).Hex);
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
        converted.Data!.Value.GetProperty("from").GetProperty("irVersion").GetString().Should().Be(version);
        converted.Data.Value.GetProperty("to").GetProperty("codec").GetString().Should().Be("morphir-json");
        converted.Data.Value.GetProperty("to").GetProperty("irVersion").GetString().Should().Be(version);
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
        converted.Data.Value.GetProperty("from").GetProperty("irVersion").GetString().Should().Be("3.1.0");
        converted.Data.Value.GetProperty("to").GetProperty("irVersion").GetString().Should().Be("3.1.0");
    }

    [Test]
    public async Task Draft1HostConversionRefusesLossyIonHeaderWithoutCallingGuest()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var lossyPath = GenerationInputFixtures.LossyDailyRateIonWithUnknownHeader(sandbox.PathOf("input"), "4.0.0");
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task Draft1HostConversionRefusesSingleDocumentIonWithPathBudget(bool annotated)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var lossyPath = GenerationInputFixtures.LossyDailyRateIonWithPathBudget(sandbox.PathOf("input"), "4.0.0", annotated);
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
    [Arguments(true)]
    [Arguments(false)]
    public async Task Draft1HostConversionRefusesIonTreeWithIgnoredFileOrAnnotatedPathBudget(bool ignoredSidecar)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = ignoredSidecar
            ? GenerationInputFixtures.LossyDailyRateIonTreeWithIgnoredFile(sandbox.PathOf("input"), "4.0.0", "README.md")
            : GenerationInputFixtures.LossyDailyRateIonTreeWithAnnotatedManifestPathBudget(sandbox.PathOf("input"), "4.0.0");
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
        var treeRoot = GenerationInputFixtures.OrdersTreeWithDroppedManifestVersion(sandbox.PathOf("input-tree"), "v4", "plain-json");
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
    public async Task Draft1FallbackRefusesTreeWithPackageJsonSidecarWithoutCallingGuest()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = GenerationInputFixtures.OrdersTreeWithPackageMetadata(sandbox.PathOf("input-tree"), "v4", "plain-json");
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
    public async Task RecordedFallbackHashesAndConvertsSingleCapturedSnapshotEvenIfDiskMutates()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
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
        var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
        var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
        string recordedTreeRoot;
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                IrStorageFormat.Json, Versioning.parse("4").ResultValue);
            recordedTreeRoot = descriptor.Path.Under(run.Dest);
            CopyOrdersFixture("v4", "plain-json", recordedTreeRoot);
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }

        // Mutate the recorded tree on disk right after the first (draft.2) probe connects, before fallback captures & joins.
        var mutated = false;
        var orderPath = Path.Combine(recordedTreeRoot, "pkg", "acme", "orders", "domain", "order.value.json");
        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) =>
        {
            if (!mutated)
            {
                mutated = true;
                File.WriteAllText(orderPath, File.ReadAllText(orderPath).Replace("73", "99", StringComparison.Ordinal));
            }
            return (guest.OpenChannel(), "", "");
        });
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Which.Value;
        var converted = success.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted").Which;
        var expectedCaptured = await new BoundedModelCapture().CaptureAsync(new ModelInput(recordedTreeRoot), ModelReadLimits.Default, default);
        var expectedSha = ModelUpgrade.SnapshotOf(((Outcome<RetainedInput>.Success)expectedCaptured).Value).Hex;
        converted.Data!.Value.GetProperty("from").GetProperty("payloadSha256").GetString().Should().Be(expectedSha);
        guest.ReceivedDecodedJson.Should().Contain("99", "fallback converted the exact captured tree that it hashed");
    }

    [Test]
    public async Task PlainTreeWithMemberOverDraft2CeilingFallsBackToDraft1Guest()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = GenerationInputFixtures.PlainV4OrdersTreeWithLargeMember(sandbox.PathOf("input-tree"), 3_300_000);
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

        var guest = new Draft1OnlyChannelGuest(maxDecodedBytes: 8 * 1024 * 1024);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
        guest.GenerateCalls.Should().Be(1);
    }

    [Test]
    public async Task PlainTreeWithDepthOver64SucceedsViaDraft1Fallback()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = GenerationInputFixtures.PlainV4OrdersTreeWithDeepNesting(sandbox.PathOf("input-tree"), 36);
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

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
        guest.GenerateCalls.Should().Be(1);
    }

    [Test]
    [Arguments(80, 128, true)]
    [Arguments(150, 128, false)]
    [Arguments(150, 200, true)]
    public async Task Draft1FallbackEnforcesModelReadLimitsMaxDepth(int targetDepth, int maxDepth, bool expectSuccess)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = GenerationInputFixtures.PlainV4OrdersTreeWithJsonDepth(sandbox.PathOf("input-tree"), targetDepth);
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

        var guest = new Draft1OnlyChannelGuest(maxDepth: 256);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var innerResolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });
        var resolver = new DepthOverrideSettingsResolver(innerResolver, maxDepth);

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        if (expectSuccess)
        {
            outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
            guest.GenerateCalls.Should().Be(1);
        }
        else
        {
            var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
            failure.Diagnostics.Should().Contain(d => d.Code == "depth_limit_exceeded");
            guest.GenerateCalls.Should().Be(0);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Draft1HostConversionRefusesIonWithAnnotatedDependencyFragments(bool tree)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = tree
            ? GenerationInputFixtures.LossyDailyRateIonTreeWithAnnotatedDependencyFragments(sandbox.PathOf("input"), "4.0.0")
            : GenerationInputFixtures.LossyDailyRateIonWithAnnotatedDependencyFragments(sandbox.PathOf("input"), "4.0.0");
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

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    [Arguments("contextual-json")]
    [Arguments("contextual-yaml")]
    public async Task Draft1FallbackRefusesContextualTreeWithContextPrefixesWithoutCallingGuest(string profile)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = GenerationInputFixtures.OrdersTreeWithContextPrefixes(sandbox.PathOf("input-tree"), "v4", profile);
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
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion" && d.Message.Contains("context.prefixes"));
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    [Arguments("v4", "plain-yaml", "compile.record_format_mismatch")]
    [Arguments("v3.1", "plain-json", "compile.record_version_mismatch")]
    public async Task RecordedFallbackRefusesFormatOrVersionMismatchWhenDiskTreeChangesDuringInit(
        string replacementVersionDir, string replacementProfile, string expectedCode)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
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
        var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
        var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
        string recordedTreeRoot;
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                IrStorageFormat.Json, Versioning.parse("4").ResultValue);
            recordedTreeRoot = descriptor.Path.Under(run.Dest);
            CopyOrdersFixture("v4", "plain-json", recordedTreeRoot);
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }

        // Replace the recorded JSON v4 tree on disk during draft.2 initialization so fallback captures the mismatched tree.
        var replaced = false;
        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) =>
        {
            if (!replaced)
            {
                replaced = true;
                Directory.Delete(recordedTreeRoot, recursive: true);
                CopyOrdersFixture(replacementVersionDir, replacementProfile, recordedTreeRoot);
            }
            return (guest.OpenChannel(), "", "");
        });
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Diagnostics.Should().Contain(d => d.Code == expectedCode);
        guest.GenerateCalls.Should().Be(0);
    }

    private sealed class DepthOverrideSettingsResolver(IGenerationSettingsResolver inner, int maxDepth) : IGenerationSettingsResolver
    {
        public async ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides overrides, InvocationContext invocation, CancellationToken ct)
        {
            var resolved = await inner.ResolveAsync(overrides, invocation, ct);
            return resolved is Outcome<GenerationSettings>.Success success
                ? new Outcome<GenerationSettings>.Success(success.Value with
                {
                    Limits = success.Value.Limits with { MaxDepth = maxDepth },
                })
                : resolved;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Draft1HostConversionRefusesIonWithAnnotatedInputTypesMap(bool tree)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.LossyIonWithAnnotatedInputTypesMap(sandbox.PathOf("input"), "4.0.0", tree);
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

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Draft1HostConversionAdmitsIonWithDocumentNumberUnderNameField(bool tree)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.IonWithDocumentNumberUnderNameField(sandbox.PathOf("input"), "4.0.0", tree);
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
        guest.GenerateCalls.Should().Be(1);
        var converted = success.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted").Which;
        converted.Data!.Value.GetProperty("dropped").GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task Draft1HostConversionHonorsRaisedModelReadLimitsForDeepIon()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        // 145 nested (list ...) layers exceed the default Ion depth of 128, but succeed with raised MaxDepth = 400.
        var inputPath = GenerationInputFixtures.DeepIonSingleDocument(sandbox.PathOf("input"), "4.0.0", 145);
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

        var guest = new Draft1OnlyChannelGuest(maxDepth: 512);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var innerResolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });
        var resolver = new DepthOverrideSettingsResolver(innerResolver, 400);

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: inputPath, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
        guest.GenerateCalls.Should().Be(1);
    }

    [Test]
    public async Task Draft1HostConversionReportsExceededIonVerificationLimitsSeparatelyFromLossyConversion()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.IonWithDocumentNumberUnderNameField(sandbox.PathOf("input"), "4.0.0", tree: false);
        var loadedOutcome = await new GenerationModelLoader(new BoundedModelCapture())
            .LoadAsync(new ModelInput(inputPath), ModelReadLimits.Default, default);
        var loaded = loadedOutcome.Should().BeOfType<Outcome<LoadedModel>.Success>().Which.Value;

        var wasmPath = sandbox.PathOf("draft1.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        var guest = new Draft1OnlyChannelGuest();
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var connected = await connector.ConnectAsync(
            new BackendExtensionRequest(new Morphir.Tooling.Features.Compile.Setting<string>(wasmPath,
                new Morphir.Tooling.Features.Compile.SettingOrigin.CommandLine("--extension")),
                TargetId.Create("text"), projectRoot)
            {
                WasmModule = new WasmModuleSource("draft1", wasmPath),
                Environment = SandboxEnvironment(sandbox),
            }, default);
        await using var backend = connected.Should().BeOfType<Outcome<IExternalGenerationBackend>.Success>().Which.Value;
        var options = backend.ValidateOptions(BackendOptions.Empty).Should().BeOfType<Outcome<ValidatedBackendOptions>.Success>().Which.Value;

        var result = await backend.GenerateAsync(
            new BackendGenerationRequest(loaded, TargetId.Create("text"), options)
            {
                ReadLimits = ModelReadLimits.Default with { MaxDepth = 2 },
            }, default);

        var failure = result.Should().BeOfType<Outcome<GenerationArtifacts>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Operational);
        failure.Diagnostics.Should().Contain(d => d.Code == "depth_limit_exceeded");
        failure.Diagnostics.Should().NotContain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Draft1HostConversionRefusesClassicIonWithLossyDecimalScale(bool inPattern)
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var inputPath = GenerationInputFixtures.ClassicIonWithLossyDecimalScale(sandbox.PathOf("input"), inPattern);
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

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    [Arguments("plain-json", "-0", false)]
    [Arguments("plain-json", "-0.00", false)]
    [Arguments("plain-json", "1.00000000000000000000000000000", false)]
    [Arguments("plain-json", "0.00000000000000000000000000001", false)]
    [Arguments("plain-json", "-0", true)]
    [Arguments("plain-json", "-0.00", true)]
    [Arguments("plain-json", "1.00000000000000000000000000000", true)]
    [Arguments("plain-json", "0.00000000000000000000000000001", true)]
    [Arguments("plain-yaml", "-0", false)]
    [Arguments("plain-yaml", "-0.00", false)]
    [Arguments("plain-yaml", "1.00000000000000000000000000000", false)]
    [Arguments("plain-yaml", "0.00000000000000000000000000001", false)]
    [Arguments("plain-yaml", "-0", true)]
    [Arguments("plain-yaml", "-0.00", true)]
    [Arguments("plain-yaml", "1.00000000000000000000000000000", true)]
    [Arguments("plain-yaml", "0.00000000000000000000000000001", true)]
    public async Task Draft1FallbackRefusesClassic31TreeWithLossyDecimalSignOrScale(string profile, string lexeme, bool inPattern)
    {
        using var sandbox = TestSandbox.Create();
        var (projectRoot, outputDir) = GenerationInputFixtures.WriteDraft1Project(
            sandbox.PathOf("project"), sandbox.PathOf("generated"), sandbox.PathOf("draft1.wasm"), "acme/orders");
        var inputPath = GenerationInputFixtures.ClassicOrdersTreeWithDecimal(sandbox.PathOf("input"), profile, lexeme, inPattern);

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

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Diagnostics.Should().Contain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    [Arguments("plain-json", "-1.50", false)]
    [Arguments("plain-json", "1.0000000000000000000000000000", true)]
    [Arguments("plain-yaml", "-1.50", false)]
    [Arguments("plain-yaml", "0.00", true)]
    public async Task Draft1FallbackAcceptsClassic31TreeWithOpaqueAttributeAndLosslessDecimal(string profile, string lexeme, bool inPattern)
    {
        using var sandbox = TestSandbox.Create();
        var (projectRoot, outputDir) = GenerationInputFixtures.WriteDraft1Project(
            sandbox.PathOf("project"), sandbox.PathOf("generated"), sandbox.PathOf("draft1.wasm"), "acme/orders");
        var inputPath = GenerationInputFixtures.ClassicOrdersTreeWithOpaqueAttributeAndDecimal(sandbox.PathOf("input"), profile, lexeme, inPattern);

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

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        guest.GenerateCalls.Should().Be(1);
        guest.ReceivedIrVersion.Should().Be("3.1.0");
        guest.ReceivedDecodedJson.Should().NotBeNull();
        guest.ReceivedDecodedJson.Should().Contain($"""["DecimalLiteral","{lexeme}"]""");
        guest.ReceivedDecodedJson.Should().Contain("""["Literal",{},["DecimalLiteral","-0.00"]]""");
    }

    [Test]
    public async Task Draft1HostConversionReportsJsonWriterDepthLimitSeparatelyFromLossyConversion()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        // 600 nested (list ...) layers pass Ion reading & verification with raised MaxDepth = 1024,
        // then hit V4JsonWriter's 512 write depth ceiling and must report depth_limit_exceeded, not data.lossy_conversion.
        var inputPath = GenerationInputFixtures.DeepIonSingleDocument(sandbox.PathOf("input"), "4.0.0", 600);
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

        var guest = new Draft1OnlyChannelGuest(maxDepth: 1024);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest.OpenChannel(), "", ""));
        var innerResolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });
        var resolver = new DepthOverrideSettingsResolver(innerResolver, 1024);

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: inputPath, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        var failure = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Operational);
        failure.Diagnostics.Should().Contain(d => d.Code == "depth_limit_exceeded");
        failure.Diagnostics.Should().NotContain(d => d.Code == "data.lossy_conversion");
        guest.GenerateCalls.Should().Be(0);
    }

    [Test]
    public async Task ExplicitTreeRouteEnforcesRequestIdReservationFromNegotiatedMaxHostCacheBytes()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = sandbox.PathOf("orders-tree");
        CopyOrdersFixture("v4", "plain-json", treeRoot);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("ext.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/orders"

            [codegen.text]
            extension = "ext"

            [extensions.ext]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        // 1. Negotiated maxHostCacheBytes = 4096 reserves 1024 bytes for discovery (enough for the orders tree)
        // and 2048 bytes for request IDs (256 fixed + ~256 per 64-char ID), so issuing distinct request IDs across
        // rounds must be refused by the bounded request-ID ledger well before 20 rounds complete.
        var overflowingGuest = new TightRequestCacheGuest(maxHostCacheBytes: 4096, roundsToRequest: 20);
        var overflowingConnector = new MepBackendExtensionConnector(null, (_, _) => (overflowingGuest, "", ""));
        var overflowOutcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), overflowingConnector, [], default);

        var overflowFailure = overflowOutcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        overflowFailure.Kind.Should().Be(GenerationFailureKind.Operational);
        overflowFailure.Diagnostics.Should().Contain(d =>
            d.Code == "extension.bounded_exchange"
            && d.Message.Contains("maxHostCacheBytes", StringComparison.Ordinal));
        overflowingGuest.RoundsCompleted.Should().BeLessThan(20, "request-ID budget must stop the session before all rounds complete");

        // 2. Negotiated maxHostCacheBytes = 256 yields a 128-byte request-ID share (< 256-byte fixed charge),
        // which must be refused immediately as extension.bounded_cache_limit before starting generation.
        var tooSmallGuest = new TightRequestCacheGuest(maxHostCacheBytes: 256, roundsToRequest: 1);
        var tooSmallConnector = new MepBackendExtensionConnector(null, (_, _) => (tooSmallGuest, "", ""));
        var tooSmallOutcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), tooSmallConnector, [], default);

        var tooSmallFailure = tooSmallOutcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error;
        tooSmallFailure.Kind.Should().Be(GenerationFailureKind.Operational);
        tooSmallFailure.Diagnostics.Should().Contain(d => d.Code == "extension.bounded_cache_limit");
    }

    [Test]
    public async Task NegotiatedExplicitTreeRouteServesFirstInventoryPageForLongPathTreeWhenDiscoveryCacheOverflows()
    {
        using var sandbox = TestSandbox.Create();
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var treeRoot = GenerationInputFixtures.PlainV4LongPathMultiModuleTree(sandbox.PathOf("long-path-tree"), 100);
        var outputDir = sandbox.PathOf("generated");
        var wasmPath = sandbox.PathOf("ext.wasm");
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "acme/flat"

            [codegen.text]
            extension = "ext"

            [extensions.ext]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);

        // Negotiated maxHostCacheBytes = 256 KiB reserves 64 KiB for discovery, which cannot hold 100 modules
        // under four 100-character segments (~184 KiB). The negotiated route must fall back to the metered uncached
        // scan and deliver the first guest inventory page cleanly instead of refusing with tree.discovery_cache_limit.
        var guest = new TightRequestCacheGuest(maxHostCacheBytes: 256 * 1024, roundsToRequest: 1, pageSize: 10);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot },
                new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
        guest.FirstInventoryPageCount.Should().Be(10);
    }

    [Test]
    public async Task BoundedGenerationSessionCancelsDuringFirstGuestRequestedInventoryPage()
    {
        using var sandbox = TestSandbox.Create();
        var treeRoot = GenerationInputFixtures.PlainV4MultiModuleTree(sandbox.PathOf("multi-module-tree"), 20);
        var stageDir = sandbox.PathOf("stage");
        Directory.CreateDirectory(stageDir);

        using var cts = new CancellationTokenSource();
        var measureDiscovery = false;
        var discoveryDirCalls = 0;
        var callsAfterCancel = 0;
        using var innerProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(treeRoot);
        var cancellingProvider = new InterceptingFileProvider(
            innerProvider,
            _ =>
            {
                if (!measureDiscovery) return;
                if (cts.IsCancellationRequested)
                {
                    callsAfterCancel++;
                    return;
                }
                discoveryDirCalls++;
                if (discoveryDirCalls == 2) cts.Cancel();
            },
            _ =>
            {
                if (measureDiscovery && cts.IsCancellationRequested) callsAfterCancel++;
            });

        var snapshotResult = DocumentTreeStore.OpenSnapshotWithDiscoveryCacheAndEncoding(
            new ReadOptions(ModelReadLimits.Default.MaxEntries, ModelReadLimits.Default.MaxBytes),
            BoundedTreeEncoding.Plain, 65536L, () => "r1", cancellingProvider);
        var snapshot = snapshotResult.Match(
            static ok => ok,
            static errs => throw new InvalidOperationException(string.Join("; ", errs.Select(e => e.Message))));

        var guest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain);
        var tuple = MepGenerationSnapshot.TupleFor(snapshot);
        var limits = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: true);
        var peer = new PeerInfo(PeerKind.Cli, "morphir-test", "0.1.0");
        var started = await MepChannelGuest.StartModelAccessAsync(
            guest, peer, new ModelAccessSupport([tuple], limits), TimeSpan.FromSeconds(5), default);
        await using var channelGuest = started.Match(
            static ok => ok,
            static err => throw new InvalidOperationException(err.Message));

        var optionsJson = JsonDocument.Parse("{}").RootElement.Clone();
        var start = new ModelAccessStart("sess-cancel",
            new ModelAccessSnapshot("snap-1", snapshot.PackageId, snapshot.RevisionId,
                Sha256Digest.Compute(Encoding.UTF8.GetBytes("{}")), "sess-cancel"),
            tuple, "text", optionsJson, [], limits);
        using var stage = new BoundedArtifactStage(stageDir, limits,
            new GenerationSessionDiskBudget(limits.MaxSessionDiskBytes), 65536L, 0L);

        measureDiscovery = true;
        var outcome = await BoundedGenerationSession.RunAsync(channelGuest.HostSession, snapshot, start, stage,
            () => Task.CompletedTask, cts.Token);

        var failure = outcome.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>().Which.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Cancelled);
        failure.Diagnostics.Should().Contain(d => d.Code == "cancelled");
        discoveryDirCalls.Should().Be(2, "discovery must stop immediately on the 2nd directory call instead of walking all 20 sibling modules");
        callsAfterCancel.Should().Be(0, "no further provider calls may execute after cancellation is triggered");
    }

    private sealed class InterceptingFileProvider(
        Microsoft.Extensions.FileProviders.IFileProvider inner,
        Action<string> onDirectory,
        Action<string>? onFile = null)
        : Microsoft.Extensions.FileProviders.IFileProvider
    {
        public Microsoft.Extensions.FileProviders.IDirectoryContents GetDirectoryContents(string subpath)
        {
            onDirectory(subpath);
            return inner.GetDirectoryContents(subpath);
        }

        public Microsoft.Extensions.FileProviders.IFileInfo GetFileInfo(string subpath)
        {
            onFile?.Invoke(subpath);
            return inner.GetFileInfo(subpath);
        }

        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) => inner.Watch(filter);
    }

    private sealed class TightRequestCacheGuest : ISingleMessageChannel, IDisposable
    {
        private readonly Guest _guest;
        private readonly int _roundsToRequest;
        private string? _sessionId;

        public int RoundsCompleted { get; private set; }
        public int FirstInventoryPageCount { get; private set; }

        public TightRequestCacheGuest(int maxHostCacheBytes, int roundsToRequest, int pageSize = 1)
        {
            _roundsToRequest = roundsToRequest;
            var tuple = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
                SemanticVersion.Create("4.0.0"), "morphir-ir", ModelAccessCodec.MorphirJson, "document-tree", ModelAccessEncoding.Plain);
            var baseLimits = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: true);
            var limits = new ModelAccessLimits(
                baseLimits.MaxFrameBytes, baseLimits.MaxEncodedBytesPerExchange, baseLimits.MaxDecodedBytesPerExchange,
                baseLimits.MaxDocumentsPerBatch, baseLimits.MaxDepth, baseLimits.MaxNodes,
                baseLimits.MaxDiagnosticsPerExchange, baseLimits.MaxOutstandingRequests, baseLimits.MaxGuestStateBytes,
                maxHostCacheBytes, baseLimits.MaxArtifactChunkBytes, baseLimits.MaxArtifactBytes,
                baseLimits.MaxTotalArtifactBytes, baseLimits.MaxSessionDiskBytes, baseLimits.MaxSessionWorkUnits,
                baseLimits.MaxRounds, baseLimits.MaxCallMilliseconds, baseLimits.MaxSessionMilliseconds);
            var support = new ModelAccessSupport([tuple], limits);
            const string claimsJson = """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.2"],"extension":{"id":"org.finos.morphir.tight-cache","name":"Tight cache guest","version":"0.1.0","types":["backend"]},"capabilities":{}}""";
            var claims = JsonSerializer.Deserialize(claimsJson, MepJson.Default.CapabilityClaimSet)!;
            var supportJson = JsonSerializer.Serialize(support, MepJson.Default.ModelAccessSupport);
            var capabilities = JsonSerializer.Deserialize(
                $$"""{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":{{supportJson}}}""",
                MepJson.Default.ExtensionCapabilities)!;

            var handlers = new Dictionary<string, MethodHandler>
            {
                [Method.GenerateModelStart.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessStart, MepJson.Default.ModelAccessStep,
                    (request, _) =>
                    {
                        _sessionId = request.SessionId;
                        return Task.FromResult(ModelAccessStep.NewContinue(request.SessionId, 1,
                            [ModelAccessRequest.NewInventory($"req-round-01-{new string('a', 52)}", "root", pageSize, null)], [], []));
                    }),
                [Method.GenerateModelAdvance.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessAdvance, MepJson.Default.ModelAccessStep,
                    (advance, _) =>
                    {
                        RoundsCompleted = advance.Round;
                        if (advance.Round == 1)
                        {
                            var delivery = JsonSerializer.SerializeToElement(advance.Deliveries.Single(), MepJson.Default.ModelAccessDelivery);
                            FirstInventoryPageCount = delivery.GetProperty("entries").GetArrayLength();
                        }
                        if (advance.Round >= _roundsToRequest)
                            return Task.FromResult(ModelAccessStep.NewComplete(_sessionId!, [], []));
                        var nextRound = advance.Round + 1;
                        return Task.FromResult(ModelAccessStep.NewContinue(_sessionId!, nextRound,
                            [ModelAccessRequest.NewInventory($"req-round-{nextRound:D2}-{new string('a', 52)}", "root", pageSize, null)], [], []));
                    }),
                [Method.GenerateModelCancel.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessCancel, MepJson.Default.ModelAccessCancel,
                    (cancel, _) => Task.FromResult(cancel)),
            };
            _guest = new Guest(new GuestDefinition(claims, capabilities, handlers));
        }

        public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
            _guest.Dispatcher.DispatchAsync(message, cancellation);

        public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
            _guest.Dispatcher.DispatchAsync(message, cancellation);

        public void Dispose() { }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task InstalledWasmExtensionClampsInitializeModelAccessOfferToWasmFrameCeiling(bool recorded)
    {
        using var sandbox = TestSandbox.Create();
        var home = sandbox.MorphirHome;
        Directory.CreateDirectory(home);
        var projectRoot = sandbox.PathOf("project");
        Directory.CreateDirectory(projectRoot);
        var outDir = sandbox.PathOf("out");
        var outputDir = sandbox.PathOf("generated");
        var treeRoot = sandbox.PathOf("input-tree");
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            """
            [project]
            name = "acme/orders"

            [frontend]
            language = "fsharp"
            """);

        var expectedTuple = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
            SemanticVersion.Create("4.0.0"), "morphir-ir", ModelAccessCodec.MorphirJson, "document-tree", ModelAccessEncoding.Plain);
        var claimedLimits = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: false);
        await InstallFakeWasmExtensionAsync(sandbox, home, "org.finos.morphir.orders", "Orders generator", "0.1.0",
            new ModelAccessSupport([expectedTuple], claimedLimits));

        var taskOptions = new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) };
        if (recorded)
        {
            var outRoot = OutRoot.Resolve(new OutRootRequest(projectRoot, projectRoot) { OutDirFlag = outDir }).ShouldSucceed();
            var store = new CompileTaskStore(outRoot, MemberPath.Root, taskOptions);
            using var run = (await store.BeginAsync(default)).ShouldSucceed();
            var descriptor = IrDescriptor.Create(RecordPath.Parse("recorded/orders-model"), IrLayout.DocumentTree,
                IrStorageFormat.Json, Versioning.parse("4").ResultValue);
            CopyOrdersFixture("v4", "plain-json", descriptor.Path.Under(run.Dest));
            run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        }
        else
        {
            CopyOrdersFixture("v4", "plain-json", treeRoot);
        }

        var guest = new OrdersChannelGuest("4.0.0", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain);
        var connector = new MepBackendExtensionConnector(null, (_, _) => (guest, "", ""));
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(),
            new FileSystemCompileConfigurationLoader(), taskOptions);
        var overrides = recorded
            ? new GenerateOverrides(Target: "text", Output: outputDir) { OutDir = outDir, ExtensionId = "org.finos.morphir.orders" }
            : new GenerateOverrides(Input: treeRoot, Target: "text", Output: outputDir) { Project = projectRoot, ExtensionId = "org.finos.morphir.orders" };

        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(overrides, new InvocationContext(projectRoot) { Environment = SandboxEnvironment(sandbox) }),
            resolver, new RejectingLoader(), new RegisteredBackendResolver([]), new GenerationPublisher(), connector, [], default);

        outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString());
        guest.ReceivedInitializeOfferMaxFrameBytes.Should().Be(RecordedGenerationAdmission.WasmFrameBodyBytes());
        guest.ReceivedInitializeOfferMaxFrameBytes.Should().BeLessThan(ContentLengthFraming.MaxBodyBytes);
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
        var converted = failure.Diagnostics.Should().ContainSingle(d => d.Code == "extension.model_converted" && d.Severity == "info").Which;
        converted.Data!.Value.GetProperty("from").GetProperty("irVersion").GetString().Should().Be("4.0.0");
        converted.Data.Value.GetProperty("to").GetProperty("irVersion").GetString().Should().Be("4.0.0");
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
              "$schema": "https://morphir.finos.org/schemas/extensions/0.1.0-preview.4/release.schema.json",
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
        public string? ReceivedSnapshotRevision { get; private set; }
        public int? ReceivedInitializeOfferMaxFrameBytes { get; private set; }
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
                        ReceivedSnapshotRevision = request.Snapshot.Revision;
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

        public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            if (method == Method.Initialize.WireValue)
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("params", out var p)
                    && p.TryGetProperty("modelAccess", out var access)
                    && access.TryGetProperty("limits", out var lim)
                    && lim.TryGetProperty("maxFrameBytes", out var frame))
                {
                    ReceivedInitializeOfferMaxFrameBytes = frame.GetInt32();
                }
            }
            return _guest.Dispatcher.DispatchAsync(message, cancellation);
        }

        public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
            _guest.Dispatcher.DispatchAsync(message, cancellation);

        public void Dispose() => Disposed = true;
    }

    private sealed class Draft1OnlyChannelGuest(int maxDecodedBytes = 1024 * 1024, bool failGenerate = false, int maxDepth = 128)
    {
        public string? ReceivedPayloadLayout { get; private set; }
        public string? ReceivedPayloadCodec { get; private set; }
        public string? ReceivedPayloadSha256 { get; private set; }
        public string? ReceivedModelId { get; private set; }
        public string? ReceivedRevision { get; private set; }
        public string? ReceivedIrVersion { get; private set; }
        public string? ReceivedDecodedJson { get; private set; }
        public int GenerateCalls { get; private set; }

        public ISingleMessageChannel OpenChannel()
        {
            var limits = new ModelTransportLimits(maxDecodedBytes * 2, maxDecodedBytes, maxDepth, 100_000);
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
                        ReceivedDecodedJson = Encoding.UTF8.GetString(decoded);
                        using var doc = JsonDocument.Parse(decoded, new JsonDocumentOptions { MaxDepth = 512 });
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
