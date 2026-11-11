// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// The loader of a configured WASM module (task 2.6 of <c>add-wasm-extension-host</c>, design CFG-1), at the service
/// level: no product command reaches it in this change. The modules are the pinned crafted corpus under
/// <c>tests/Fixtures/MepWasm/</c>; each test copies the bytes it needs into a <see cref="TestSandbox"/>.
/// </summary>
public sealed class WasmModuleLoaderTests
{
    private static readonly HostIdentity Host = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    private static readonly WasmModuleLoader Loader = new(WasmRuntime.Process, Host);

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "MepWasm");

    private static string Module(string name) => Path.Combine(Fixtures, "fixtures", "wasm", $"{name}.wasm");

    /// <summary>The SHA-256 the pinned corpus records for a module.</summary>
    private static string RecordedDigest(string name)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "wasm-host.cases.json")));
        return corpus.RootElement.GetProperty("modules").GetProperty(name).GetProperty("sha256").GetString()!;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static ModuleDigest Digest(string hex) =>
        ModuleDigest.TryParse(hex, out var digest) ? digest : throw new ArgumentException($"not a digest: {hex}", nameof(hex));

    /// <summary>Copies a corpus module into the sandbox under <paramref name="fileName"/>.</summary>
    private static string Place(TestSandbox sandbox, string module, string fileName = "extension.wasm")
    {
        var path = Path.Combine(sandbox.Work, fileName);
        File.Copy(Module(module), path, overwrite: true);
        return path;
    }

    private static LoadedWasmModule Loaded(WasmModuleLoad load) => load.Match(
        module => module,
        failure => throw new InvalidOperationException($"expected a loaded module, got {failure.GetType().Name}: {failure.Message}"));

    private static WasmModuleFailure Failed(WasmModuleLoad load) => load.Match(
        module => throw new InvalidOperationException($"expected a failure, got a {module.Profile} module {module.Digest}"),
        failure => failure);

    // --- By content ---------------------------------------------------------------------------------------------

    [Test]
    public void ATextFileAtThePathIsNotAWasmModuleAndTheDiagnosticNamesThePath()
    {
        using var sandbox = TestSandbox.Create();
        var path = Path.Combine(sandbox.Work, "python.wasm");
        var text = Encoding.UTF8.GetBytes("This is a text file, not a module.\n");
        File.WriteAllBytes(path, text);

        var failure = Failed(Loader.Load(new WasmModuleSource("python", path)));

        failure.Should().BeOfType<WasmModuleFailure.NotAModule>();
        failure.Extension.Should().Be("python");
        failure.Path.Should().Be(path);
        failure.Code.Should().BeNull("the profile contract has no code for bytes that are not a module");
        failure.Message.Should().Contain($"'{path}'").And.Contain("is not a WASM module");
        ((WasmModuleFailure.NotAModule)failure).Digest.Hex.Should().Be(Sha256(text));
    }

    [Test]
    public void AnEmptyFileIsNotAWasmModule()
    {
        using var sandbox = TestSandbox.Create();
        var path = Path.Combine(sandbox.Work, "empty.wasm");
        File.WriteAllBytes(path, []);

        Failed(Loader.Load(new WasmModuleSource("python", path))).Should().BeOfType<WasmModuleFailure.NotAModule>();
    }

    [Test]
    public void AModuleIsRecognizedByItsBytesAndNotByItsFileName()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo", "README.txt");

        var module = Loaded(Loader.Load(new WasmModuleSource("echo", path)));

        module.Profile.Should().Be(WasmAbi.ExtismKernel);
        module.Path.Should().Be(path);
    }

    [Test]
    public void AMissingFileFailsAndNamesThePath()
    {
        using var sandbox = TestSandbox.Create();
        var path = Path.Combine(sandbox.Work, "missing", "python.wasm");

        var failure = Failed(Loader.Load(new WasmModuleSource("python", path)));

        failure.Should().BeOfType<WasmModuleFailure.NotFound>();
        failure.Code.Should().BeNull();
        failure.Message.Should().Contain($"'{path}'").And.Contain("python").And.Contain("no file");
    }

    [Test]
    public void ADirectoryAtThePathCannotBeReadAndTheDiagnosticNamesThePath()
    {
        using var sandbox = TestSandbox.Create();
        var path = Directory.CreateDirectory(Path.Combine(sandbox.Work, "python.wasm")).FullName;

        var failure = Failed(Loader.Load(new WasmModuleSource("python", path)));

        failure.Should().BeOfType<WasmModuleFailure.Unreadable>();
        failure.Code.Should().BeNull();
        failure.Message.Should().Contain($"'{path}'").And.Contain("cannot be read").And.Contain("is a directory");
    }

    [Test]
    public void APathTheFileSystemRejectsCannotBeRead()
    {
        using var sandbox = TestSandbox.Create();
        var path = Path.Combine(sandbox.Work, "python\0.wasm");

        var failure = Failed(Loader.Load(new WasmModuleSource("python", path)));

        failure.Should().BeOfType<WasmModuleFailure.Unreadable>();
        failure.Message.Should().Contain("cannot be read");
    }

    // --- Classification -----------------------------------------------------------------------------------------

    [Test]
    public void AnExtismModuleFromThePinnedCorpusIsAdmittedWithItsDigest()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");

        var module = Loaded(Loader.Load(new WasmModuleSource("echo", path)));

        module.Extension.Should().Be("echo");
        module.Profile.Should().Be(WasmAbi.ExtismKernel);
        module.Profile.Id.Should().Be("extism-kernel-v1");
        module.Digest.Hex.Should().Be(RecordedDigest("extism-echo"));
        module.ByteLength.Should().Be(new FileInfo(path).Length);
        module.Module.Profile.Should().Be(WasmAbi.ExtismKernel);
        module.Module.Compiled.Digest.Should().Be(module.Digest);
    }

    [Test]
    public void AByteProfileModuleIsRefusedWithTheProfileNamed()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "bytes-echo");

        var failure = Failed(Loader.Load(new WasmModuleSource("gleam", path)));

        var refused = failure.Should().BeOfType<WasmModuleFailure.Refused>().Subject;
        refused.Code.Should().Be("extension.abi_unsupported");
        refused.Refusal.Should().BeOfType<WasmRefusal.ProfileNotRun>();
        refused.Digest.Hex.Should().Be(RecordedDigest("bytes-echo"));
        refused.Message.Should().Contain("morphir-bytes-v1").And.Contain("this host runs Extism modules")
            .And.Contain($"'{path}'").And.Contain(RecordedDigest("bytes-echo"));
    }

    [Test]
    public void AnImportOutsideTheContractIsRefusedWithTheImportNamed()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "env-open-socket");

        var refused = Failed(Loader.Load(new WasmModuleSource("socket", path))).Should().BeOfType<WasmModuleFailure.Refused>().Subject;

        refused.Code.Should().Be("extension.abi_unsupported");
        refused.Message.Should().Contain("env.open_socket");
    }

    [Test]
    public void AModuleThatNeedsAFeatureTheEngineLacksIsRefusedAndIsStillAModule()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "shared-memory");

        var refused = Failed(Loader.Load(new WasmModuleSource("shared", path))).Should().BeOfType<WasmModuleFailure.Refused>().Subject;

        refused.Code.Should().Be("extension.wasm_feature_unsupported");
        refused.Message.Should().NotContain("is not a WASM module");
    }

    // --- Declared ABI -------------------------------------------------------------------------------------------

    [Test]
    public void ADeclaredAbiEqualToTheClassifiedProfileIsAdmitted()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");

        var module = Loaded(Loader.Load(new WasmModuleSource("echo", path, DeclaredAbi: WasmAbi.ExtismKernel)));

        module.Profile.Should().Be(WasmAbi.ExtismKernel);
    }

    [Test]
    [Arguments("extism-echo", "morphir-bytes-v1", "extism-kernel-v1")]
    [Arguments("bytes-echo", "extism-kernel-v1", "morphir-bytes-v1")]
    public void ADeclaredAbiThatDiffersFromTheClassifiedProfileIsRefusedNamingBoth(string module, string declared, string classified)
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, module);
        WasmAbi.TryParse(declared, out var abi).Should().BeTrue();

        var refused = Failed(Loader.Load(new WasmModuleSource("python", path, DeclaredAbi: abi))).Should().BeOfType<WasmModuleFailure.Refused>().Subject;

        refused.Code.Should().Be("extension.abi_mismatch");
        refused.Refusal.Should().Be(new WasmRefusal.DeclaredAbiMismatch(abi!, classified == "extism-kernel-v1" ? WasmAbi.ExtismKernel : WasmAbi.MorphirBytes));
        refused.Message.Should().Contain(declared).And.Contain(classified);
    }

    // --- Declared digest ----------------------------------------------------------------------------------------

    [Test]
    public void ADeclaredDigestEqualToTheBytesIsAdmitted()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");

        var module = Loaded(Loader.Load(new WasmModuleSource("echo", path, DeclaredSha256: Digest(RecordedDigest("extism-echo")))));

        module.Digest.Hex.Should().Be(RecordedDigest("extism-echo"));
    }

    [Test]
    public void ADeclaredDigestThatDiffersIsRefusedWithBothDigests()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");
        var declared = Digest(RecordedDigest("extism-trap"));

        var failure = Failed(Loader.Load(new WasmModuleSource("echo", path, DeclaredSha256: declared)));

        var mismatch = failure.Should().BeOfType<WasmModuleFailure.DigestMismatch>().Subject;
        mismatch.Declared.Should().Be(declared);
        mismatch.Actual.Hex.Should().Be(RecordedDigest("extism-echo"));
        mismatch.Code.Should().BeNull("the profile contract has no code for a digest mismatch");
        mismatch.Message.Should().Contain(RecordedDigest("extism-trap")).And.Contain(RecordedDigest("extism-echo")).And.Contain($"'{path}'");
    }

    [Test]
    public void ADeclaredDigestIsCheckedBeforeTheBytesAreClassified()
    {
        // A text file is not a module, so classification would fail. The digest failure is the one reported.
        using var sandbox = TestSandbox.Create();
        var path = Path.Combine(sandbox.Work, "python.wasm");
        var text = Encoding.UTF8.GetBytes("not a module");
        File.WriteAllBytes(path, text);
        var declared = Digest(RecordedDigest("extism-echo"));

        var mismatch = Failed(Loader.Load(new WasmModuleSource("python", path, DeclaredSha256: declared)))
            .Should().BeOfType<WasmModuleFailure.DigestMismatch>().Subject;

        mismatch.Actual.Hex.Should().Be(Sha256(text));
        mismatch.Message.Should().NotContain("is not a WASM module");
    }

    [Test]
    public void ADeclaredDigestIsCheckedBeforeAByteProfileModuleIsRefused()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "bytes-echo");

        var failure = Failed(Loader.Load(new WasmModuleSource("gleam", path, DeclaredAbi: WasmAbi.ExtismKernel,
            DeclaredSha256: Digest(RecordedDigest("extism-echo")))));

        failure.Should().BeOfType<WasmModuleFailure.DigestMismatch>();
    }

    // --- A replaced module --------------------------------------------------------------------------------------

    [Test]
    public void AReplacedModuleIsLoadedFromItsNewBytes()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");
        var first = Loaded(Loader.Load(new WasmModuleSource("python", path)));

        File.Copy(Module("extism-trap"), path, overwrite: true);
        var second = Loaded(Loader.Load(new WasmModuleSource("python", path)));

        first.Digest.Hex.Should().Be(RecordedDigest("extism-echo"));
        second.Digest.Hex.Should().Be(RecordedDigest("extism-trap"));
        second.ByteLength.Should().Be(new FileInfo(Module("extism-trap")).Length);
        second.Module.Compiled.Should().NotBeSameAs(first.Module.Compiled, "the engine reuses a compiled module by digest, never by path");
        second.Module.Compiled.Digest.Should().Be(second.Digest);
    }

    [Test]
    public void AModuleReplacedByAByteProfileModuleIsClassifiedAgain()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");
        Loaded(Loader.Load(new WasmModuleSource("python", path)));

        File.Copy(Module("bytes-echo"), path, overwrite: true);
        var refused = Failed(Loader.Load(new WasmModuleSource("python", path))).Should().BeOfType<WasmModuleFailure.Refused>().Subject;

        refused.Digest.Hex.Should().Be(RecordedDigest("bytes-echo"));
        refused.Message.Should().Contain("morphir-bytes-v1");
    }

    [Test]
    public void AReplacedModuleIsRefusedWhenItsDigestIsDeclared()
    {
        using var sandbox = TestSandbox.Create();
        var path = Place(sandbox, "extism-echo");
        var source = new WasmModuleSource("python", path, DeclaredSha256: Digest(RecordedDigest("extism-echo")));
        Loaded(Loader.Load(source));

        File.Copy(Module("extism-trap"), path, overwrite: true);
        var mismatch = Failed(Loader.Load(source)).Should().BeOfType<WasmModuleFailure.DigestMismatch>().Subject;

        mismatch.Declared.Hex.Should().Be(RecordedDigest("extism-echo"));
        mismatch.Actual.Hex.Should().Be(RecordedDigest("extism-trap"));
    }

    [Test]
    public void TheSameBytesAtTwoPathsShareOneCompiledModule()
    {
        using var sandbox = TestSandbox.Create();
        var first = Loaded(Loader.Load(new WasmModuleSource("a", Place(sandbox, "extism-echo", "a.wasm"))));
        var second = Loaded(Loader.Load(new WasmModuleSource("b", Place(sandbox, "extism-echo", "b.wasm"))));

        second.Module.Compiled.Should().BeSameAs(first.Module.Compiled);
        second.Path.Should().NotBe(first.Path);
    }

    // --- The path -----------------------------------------------------------------------------------------------

    [Test]
    [Arguments("python.wasm")]
    [Arguments("extensions/python.wasm")]
    [Arguments("./python.wasm")]
    public void TheLoaderTakesOnlyAFullyQualifiedPathAndResolvesNoRelativePathItself(string path)
    {
        var create = () => new WasmModuleSource("python", path);

        create.Should().Throw<ArgumentException>().WithParameterName("Path");
    }

    [Test]
    public void TheSourceNeedsAnExtensionName()
    {
        using var sandbox = TestSandbox.Create();
        var create = () => new WasmModuleSource(" ", Path.Combine(sandbox.Work, "python.wasm"));

        create.Should().Throw<ArgumentException>().WithParameterName("Extension");
    }

    // --- Registration -------------------------------------------------------------------------------------------

    [Test]
    public void TheManagementScenarioRegistersTheLoaderWithTheOtherExtensionServices()
    {
        // The loader names the entry point's host when the runtime is unavailable, as the registry does.
        using var app = MorphirCli.CreateBuilder([]).ConfigureServices(services => services.AddSingleton(Host)).AddScenario<ManagementScenario>().Build();

        app.Services.GetRequiredService<IWasmModuleLoader>().Should().BeOfType<WasmModuleLoader>();
        app.Services.GetRequiredService<IWasmModuleLoader>().Should().BeSameAs(app.Services.GetRequiredService<IWasmModuleLoader>());
    }
}
