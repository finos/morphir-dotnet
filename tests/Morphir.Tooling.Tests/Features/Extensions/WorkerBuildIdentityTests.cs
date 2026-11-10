// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// The worker's build identity (task 4.1): 64 hexadecimal digits that <c>--help --json</c> reports and that a worker
/// started with another identity refuses. The pinned contract fixes its form only; the parts it covers are the MoonBit
/// worker's, in this host's terms. Also the engine identity, which task 4.2's runtime report reuses.
/// </summary>
public sealed class WorkerBuildIdentityTests
{
    private static readonly HostIdentity Native = new(MorphirHost.Native, ReleaseVersion.Create("0.4.0-alpha.3+9af11f835eac40e338ee0d7232f8959ffbef1be4"));

    [Test]
    public void The_identity_is_64_lower_case_hexadecimal_digits()
    {
        WorkerBuildIdentity.Of(Native).Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Test]
    public void The_identity_is_the_SHA_256_of_its_stated_text()
    {
        var text = WorkerBuildIdentity.Text(Native);

        WorkerBuildIdentity.Of(Native).Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    [Test]
    public void The_text_names_the_contract_the_host_the_build_the_kernel_and_the_engine()
    {
        var contract = WasmProfileContract.Pinned;
        WorkerBuildIdentity.Text(Native).Should().Be(
            $"worker {contract.Id} {contract.Version} sha256 {contract.Sha256}\n" +
            "host morphir\n" +
            "version 0.4.0-alpha.3+9af11f835eac40e338ee0d7232f8959ffbef1be4\n" +
            $"kernel moonrockz/extism {ExtismKernel.PackageVersion} sha256 {ExtismKernel.Sha256}\n" +
            "engine wasmtime 50.0.1\n");
    }

    [Test]
    public void The_same_build_has_the_same_identity_and_another_host_or_build_another()
    {
        var same = new HostIdentity(MorphirHost.Native, ReleaseVersion.Create("0.4.0-alpha.3+9af11f835eac40e338ee0d7232f8959ffbef1be4"));
        WorkerBuildIdentity.Of(same).Should().Be(WorkerBuildIdentity.Of(Native));
        WorkerBuildIdentity.Of(new HostIdentity(MorphirHost.DotnetTool, Native.Version)).Should().NotBe(WorkerBuildIdentity.Of(Native));
        WorkerBuildIdentity.Of(new HostIdentity(MorphirHost.Native, ReleaseVersion.Create("0.4.0-alpha.3+0000000000000000000000000000000000000000")))
            .Should().NotBe(WorkerBuildIdentity.Of(Native));
        WorkerBuildIdentity.Of(new HostIdentity(MorphirHost.Native, ReleaseVersion.Create("0.4.0-alpha.4"))).Should().NotBe(WorkerBuildIdentity.Of(Native));
    }

    [Test]
    public void The_engine_identity_is_the_Wasmtime_package_this_build_references()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Packages.props"));
        var pinned = props.Descendants("PackageVersion").Single(element => (string?)element.Attribute("Include") == "Wasmtime");

        WasmRuntimeIdentity.EngineName.Should().Be("wasmtime");
        WasmRuntimeIdentity.EngineVersion.Should().Be((string?)pinned.Attribute("Version"));
        WasmRuntimeIdentity.Engine.Should().Be($"wasmtime {WasmRuntimeIdentity.EngineVersion}");
    }

    [Test]
    public void The_engine_identity_is_the_Wasmtime_assembly_that_is_loaded()
    {
        var informational = typeof(Wasmtime.Engine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        informational.Should().NotBeNull();
        informational!.Split('+')[0].Should().Be(WasmRuntimeIdentity.EngineVersion);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) return directory.FullName;
        }

        throw new InvalidOperationException("No Directory.Packages.props above the test output.");
    }
}

/// <summary>The crafted guests of the worker tests are the conversion of their WebAssembly text, so a reader can
/// trust the text beside each module.</summary>
public sealed class WorkerTestGuestTests
{
    [Test]
    [Arguments("flood-output")]
    [Arguments("workspace-then-spin")]
    public void A_worker_test_guest_is_the_conversion_of_its_text(string name)
    {
        var wat = File.ReadAllText(Path.ChangeExtension(WorkerHarness.TestGuest(name), ".wat"));

        File.ReadAllBytes(WorkerHarness.TestGuest(name)).Should().Equal(Wasmtime.Module.ConvertText(wat));
    }
}
