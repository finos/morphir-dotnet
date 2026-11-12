// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The notice declarations in <c>build/notices/native/</c> of the code that no NuGet package provides (tasks 5.2 and
/// 5.3 of add-wasm-extension-host): the Wasmtime static library linked into the Native AOT executable, one declaration
/// per platform archive, and the embedded Extism kernel module. Each must agree with the pin it describes, so that a
/// pin moved without its declaration fails here.
/// </summary>
public class NativeNoticeDeclarationsTests
{
    private static readonly string Root = PathHelper.GetRepositoryRoot();
    private static readonly string Notices = Path.Combine(Root, "build", "notices", "native");
    private static readonly XDocument CApiProps = XDocument.Load(Path.Combine(Root, "build", "wasmtime", "WasmtimeCApi.props"));

    private sealed record Declaration(string Name, string Version, string License, string LicenseFile, string Source, string Sha256);

    [Test]
    public void EveryPinnedCApiArchiveHasOneDeclarationAndNoOtherWasmtimeDeclarationExists()
    {
        var pinned = Archives().Select(archive => archive.Rid);
        var declared = Directory.GetFiles(Notices, "wasmtime.*.json").Select(path => Path.GetFileNameWithoutExtension(path)["wasmtime.".Length..]);

        declared.Should().BeEquivalentTo(pinned);
    }

    [Test]
    public void EachWasmtimeDeclarationAgreesWithItsPinnedArchive()
    {
        var version = CApiProps.Descendants("MorphirWasmtimeCApiVersion").Single().Value.Trim();
        foreach (var archive in Archives())
        {
            var declaration = Read($"wasmtime.{archive.Rid}.json");
            declaration.Name.Should().Be(Path.GetFileName(archive.StaticLibrary), archive.Rid);
            declaration.Version.Should().Be(version, archive.Rid);
            declaration.Source.Should().Be(archive.Url, archive.Rid);
            declaration.License.Should().Be("Apache-2.0 WITH LLVM-exception");
            declaration.LicenseFile.Should().Be("wasmtime.LICENSE.txt");
            declaration.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");

            // The pin holds the archive's digest, not the digest of the library inside it. When the build has fetched and
            // verified this archive (the static-link targets leave a marker named after the archive's pinned SHA-256), the
            // declared digest is checked against the extracted file; otherwise only the version and source are checked here.
            var cache = Path.Combine(Root, "artifacts", "wasmtime-c-api");
            var library = Path.Combine(cache, archive.Root, archive.StaticLibrary);
            if (File.Exists(Path.Combine(cache, $"{archive.Root}.{archive.Sha256}.extracted")) && File.Exists(library))
                Sha(File.ReadAllBytes(library)).Should().Be(declaration.Sha256, $"{archive.Rid}: the declaration describes the linked {archive.StaticLibrary}");
        }

        File.ReadAllText(Path.Combine(Notices, "wasmtime.LICENSE.txt")).Should().Contain("Apache License").And.Contain("LLVM Exceptions to the Apache 2.0 License");
    }

    [Test]
    public void TheKernelDeclarationAgreesWithTheEmbeddedModuleAndItsProvenance()
    {
        var kernel = Path.Combine(Root, "src", "Morphir.Extensions.Wasm", "Kernel");
        var source = File.ReadAllText(Path.Combine(Root, "src", "Morphir.Extensions.Wasm", "ExtismKernel.cs"));
        string Constant(string name) => Regex.Match(source, $@"const string {name} = ""([^""]+)""").Groups[1].Value;

        var declaration = Read("extism-kernel.json");

        declaration.Name.Should().Be("extism-kernel.wasm");
        declaration.Version.Should().Be(Constant("PackageVersion")).And.NotBeEmpty();
        declaration.Sha256.Should().Be(Constant("Sha256")).And.Be(Sha(File.ReadAllBytes(Path.Combine(kernel, "extism-kernel.wasm"))));
        declaration.License.Should().Be("Apache-2.0");
        File.ReadAllText(Path.Combine(kernel, "PROVENANCE.md")).Should().Contain(declaration.Source);
        File.ReadAllBytes(Path.Combine(Notices, declaration.LicenseFile)).Should().Equal(File.ReadAllBytes(Path.Combine(kernel, "LICENSE")));
        // The kernel is an embedded resource of Morphir.Extensions.Wasm, not a NativeLibrary item, and it has a
        // notice as well as a licence: the declaration says both, so the notices step can find and check it.
        using var raw = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Notices, "extism-kernel.json")));
        raw.RootElement.GetProperty("kind").GetString().Should().Be("embedded-resource");
        var noticeFile = raw.RootElement.GetProperty("noticeFile").GetString();
        noticeFile.Should().Be("extism-kernel.NOTICE.txt");
        File.ReadAllBytes(Path.Combine(Notices, noticeFile!)).Should().Equal(File.ReadAllBytes(Path.Combine(kernel, "NOTICE")));
        File.ReadAllText(Path.Combine(Root, "src", "Morphir.Extensions.Wasm", "Morphir.Extensions.Wasm.csproj"))
            .Should().Contain("<EmbeddedResource Include=\"Kernel/extism-kernel.wasm\" LogicalName=\"Morphir.Extensions.Wasm.Kernel.extism-kernel.wasm\" />");
    }

    [Test]
    public void EveryDeclaredLicenceFileExists()
    {
        foreach (var path in Directory.GetFiles(Notices, "*.json"))
            File.Exists(Path.Combine(Notices, Read(Path.GetFileName(path)).LicenseFile)).Should().BeTrue(path);
    }

    private static Declaration Read(string file) =>
        JsonSerializer.Deserialize<Declaration>(File.ReadAllText(Path.Combine(Notices, file)), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException(file);

    private static IEnumerable<(string Rid, string Url, string Sha256, string Root, string StaticLibrary)> Archives() =>
        CApiProps.Descendants("MorphirWasmtimeCApiArchive").Select(element => (
            (string)element.Attribute("Include")!,
            element.Element("Url")!.Value.Trim(),
            element.Element("Sha256")!.Value.Trim(),
            element.Element("Root")!.Value.Trim(),
            element.Element("StaticLibrary")!.Value.Trim()));

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
