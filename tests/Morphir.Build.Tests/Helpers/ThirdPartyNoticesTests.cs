// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The third-party notices of the morphirup executable come from what the Native AOT compiler linked: the files of its
/// response file, each traced to the restored package that holds it, with that package's license and notice files, or a
/// committed fallback text where the package ships none.
/// </summary>
[Category("PlatformSpecific")]
public class ThirdPartyNoticesTests
{
    private const string Mit = "Permission is hereby granted, free of charge, to any person obtaining a copy";

    [Test]
    public async Task TheLinkedFilesAreTheInputsReferencesAndSatellitesOfTheResponseFile()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var project = Directory.CreateDirectory(sandbox.PathOf("repo", "src", "Tool")).FullName;
        var rsp = $"""
            obj/Release/net10.0/linux-x64/tool.dll
            -o:obj/Release/net10.0/linux-x64/native/tool.o
            -r:/packages/cstoml/1.8.6/lib/net10.0/CsToml.dll
            --satellite:/packages/fsharp.core/10.1.403/lib/netstandard2.1/de/FSharp.Core.resources.dll
            --targetos:linux
            -m:/packages/runtime/10.0.14/mibc/StandardOptimizationData.mibc
            --feature:System.Globalization.Invariant=true
            """;

        ThirdPartyNotices.LinkedFiles(rsp, project).Select(path => path.Replace('\\', '/')).Should().Equal(
            Path.Combine(project, "obj/Release/net10.0/linux-x64/tool.dll").Replace('\\', '/'),
            "/packages/cstoml/1.8.6/lib/net10.0/CsToml.dll",
            "/packages/fsharp.core/10.1.403/lib/netstandard2.1/de/FSharp.Core.resources.dll");
    }

    [Test]
    public async Task EachLinkedFileIsTracedToItsPackageOrToThisRepository()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var repository = Directory.CreateDirectory(sandbox.PathOf("repo")).FullName;
        var one = Package(sandbox, "cstoml", "1.8.6", "CsToml", "MIT");
        var own = Path.Combine(repository, "src", "Tool", "bin", "Tool.dll");
        var stray = sandbox.PathOf("elsewhere", "Stray.dll");

        var (packages, unowned) = ThirdPartyNotices.Packages(
            [Path.Combine(one, "lib", "net10.0", "CsToml.dll"), Path.Combine(one, "lib", "net10.0", "de", "CsToml.resources.dll"), own, stray], repository);

        packages.Should().ContainSingle().Which.Should().Be(new LinkedPackage("CsToml", "1.8.6", one));
        unowned.Should().Equal(stray);
    }

    [Test]
    public async Task TheNoticesNameEveryPackageWithItsLicenseAndNotices()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var fallbacks = Directory.CreateDirectory(sandbox.PathOf("fallbacks")).FullName;
        await File.WriteAllTextAsync(Path.Combine(fallbacks, "CsToml.txt"), $"License: MIT\nSource: https://example.org/LICENSE\n\nMIT License\n\nCopyright (c) 2024 prozolic\n\n{Mit}\n");
        var cstoml = Package(sandbox, "cstoml", "1.8.6", "CsToml", "MIT");
        var runtime = Package(sandbox, "microsoft.netcore.app.runtime.nativeaot.linux-x64", "10.0.14", "Microsoft.NETCore.App.Runtime.NativeAOT.linux-x64", "MIT");
        await File.WriteAllTextAsync(Path.Combine(runtime, "LICENSE.TXT"), $"The MIT License (MIT)\r\n\r\nCopyright (c) .NET Foundation and Contributors\r\n\r\n{Mit}\r\n");
        await File.WriteAllTextAsync(Path.Combine(runtime, "THIRD-PARTY-NOTICES.TXT"), "License notice for zlib-ng\n");
        var hashing = Package(sandbox, "system.io.hashing", "10.0.11", "System.IO.Hashing", "MIT");
        await File.WriteAllTextAsync(Path.Combine(hashing, "THIRD-PARTY-NOTICES.TXT"), "License notice for zlib-ng\r\n");
        await File.WriteAllTextAsync(Path.Combine(fallbacks, "System.IO.Hashing.txt"), $"License: MIT\nSource: https://example.org/LICENSE\n\n{Mit}\n");
        LinkedPackage[] packages = [new("Microsoft.NETCore.App.Runtime.NativeAOT.linux-x64", "10.0.14", runtime), new("CsToml", "1.8.6", cstoml), new("System.IO.Hashing", "10.0.11", hashing)];

        var notices = ThirdPartyNotices.Write("1.2.3", "linux-x64", packages, fallbacks, unicodeLicense: "UNICODE LICENSE V3");

        notices.Should().Contain("- CsToml 1.8.6, MIT").And.Contain("- Microsoft.NETCore.App.Runtime.NativeAOT.<rid> 10.0.14, MIT");
        notices.Should().Contain("Copyright (c) 2024 prozolic", "the fallback text is used when the package ships none");
        notices.Should().Contain("Copyright (c) .NET Foundation and Contributors").And.Contain("License notice for zlib-ng");
        notices.Should().Contain("UNICODE LICENSE V3").And.NotContain("\r");
        Regex.Matches(notices, "License notice for zlib-ng").Should().ContainSingle("one repository's notices file is written once");
        notices.Should().Contain("THIRD-PARTY-NOTICES.TXT of System.IO.Hashing 10.0.11 is the same text as THIRD-PARTY-NOTICES.TXT of Microsoft.NETCore.App.Runtime.NativeAOT.<rid> 10.0.14, above.");
        notices.IndexOf("- CsToml", StringComparison.Ordinal).Should().BeLessThan(notices.IndexOf("- Microsoft.NETCore", StringComparison.Ordinal), "the list is sorted");
        notices.Should().NotContain("linux-x64", "the text is the same for every platform, so one release file serves them all");
        ThirdPartyNotices.Missing(notices, packages, "linux-x64").Should().BeEmpty();
        ThirdPartyNotices.Missing(notices, [.. packages, new LinkedPackage("Semver", "3.0.0", cstoml)], "linux-x64").Should().Equal("Semver 3.0.0");
    }

    [Test]
    public async Task APackageWithNoLicenseTextOrAFallbackForAnotherLicenseFailsTheBuild()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var fallbacks = Directory.CreateDirectory(sandbox.PathOf("fallbacks")).FullName;
        var semver = Package(sandbox, "semver", "3.0.0", "Semver", "MIT");
        var act = () => ThirdPartyNotices.Write("1.2.3", "linux-x64", [new LinkedPackage("Semver", "3.0.0", semver)], fallbacks, "U");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Semver 3.0.0*no license text*");

        await File.WriteAllTextAsync(Path.Combine(fallbacks, "Semver.txt"), "License: Apache-2.0\n\ntext\n");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Semver*Apache-2.0*MIT*");
    }

    [Test]
    public void TheCommittedFallbacksAreForTheLicenseTheirPackagesDeclare()
    {
        var fallbacks = Path.Combine(PathHelper.GetRepositoryRoot(), "build", "notices");
        foreach (var file in Directory.GetFiles(fallbacks, "*.txt"))
        {
            var lines = File.ReadAllLines(file);
            var name = Path.GetFileNameWithoutExtension(file);
            // The NuGet client packages declare Apache-2.0; their fallback is the license of NuGet/NuGet.Client.
            var apache = name.StartsWith("NuGet.", StringComparison.Ordinal);
            lines[0].Should().Be(apache ? "License: Apache-2.0" : "License: MIT", $"{Path.GetFileName(file)} is the license text its package declares");
            lines[1].Should().StartWith("Source: https://github.com/");
            string.Join('\n', lines).Should().Contain(apache ? "Apache License, Version 2.0" : Mit).And.Contain("Copyright");
        }

        Directory.GetFiles(fallbacks, "*.txt").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)
            .Should().Equal("CsToml", "FSharp.Core", "Microsoft.Extensions.Primitives", "NuGet.Common", "NuGet.Configuration",
                "NuGet.Credentials", "NuGet.Frameworks", "NuGet.Packaging", "NuGet.Protocol", "NuGet.Versioning", "Semver",
                "System.IO.Hashing", "System.Security.Cryptography.Pkcs");
    }

    [Test]
    public async Task ALinkedNativeLibraryOutsideThePackagesNeedsADeclaredEntryWithItsDigest()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var repository = Directory.CreateDirectory(sandbox.PathOf("repo")).FullName;
        var runtime = Package(sandbox, "microsoft.netcore.app.runtime.nativeaot.linux-x64", "10.0.14", "Microsoft.NETCore.App.Runtime.NativeAOT.linux-x64", "MIT");
        var fromPackage = Path.Combine(runtime, "runtimes", "linux-x64", "native", "libRuntime.WorkstationGC.a");
        var wasmtime = sandbox.PathOf("native", "wasmtime-v30.0.0-x86_64-linux-c-api", "lib", "libwasmtime.a");
        Directory.CreateDirectory(Path.GetDirectoryName(wasmtime)!);
        await File.WriteAllBytesAsync(wasmtime, [1, 2, 3]);
        var declarations = Directory.CreateDirectory(sandbox.PathOf("repo", "build", "notices", "native")).FullName;
        await File.WriteAllTextAsync(Path.Combine(declarations, "wasmtime.LICENSE.txt"), "Apache License\nVersion 2.0, January 2004\n");
        DeclaredNativeLibrary wasmtimeEntry = new("libwasmtime.a", "30.0.0", "Apache-2.0 WITH LLVM-exception",
            "wasmtime.LICENSE.txt", "https://github.com/bytecodealliance/wasmtime/releases/download/v30.0.0/wasmtime-v30.0.0-x86_64-linux-c-api.tar.xz",
            ReleaseArchive.Sha256(wasmtime));

        // A native library from a restored package is that package's; any other needs a declared entry.
        ThirdPartyNotices.NativeLibraryProblems([fromPackage, wasmtime], repository, []).Should().ContainSingle()
            .Which.Should().Contain("libwasmtime.a").And.Contain("build/notices/native");
        ThirdPartyNotices.NativeLibraryProblems([fromPackage, wasmtime], repository, [wasmtimeEntry]).Should().BeEmpty();
        ThirdPartyNotices.NativeLibraryProblems([wasmtime], repository, [wasmtimeEntry with { Sha256 = new string('0', 64) }])
            .Should().ContainSingle().Which.Should().Contain("SHA-256");

        // The declaration is read from a JSON file, and its license text is written into the notices.
        await File.WriteAllTextAsync(Path.Combine(declarations, "wasmtime.json"), $$"""
            { "name": "libwasmtime.a", "version": "30.0.0", "license": "Apache-2.0 WITH LLVM-exception",
              "licenseFile": "wasmtime.LICENSE.txt", "source": "{{wasmtimeEntry.Source}}", "sha256": "{{wasmtimeEntry.Sha256}}" }
            """);
        ThirdPartyNotices.ReadDeclaredNativeLibraries(declarations).Should().Equal(wasmtimeEntry);
        var notices = ThirdPartyNotices.Write("1.2.3", "linux-x64", [], Path.Combine(repository, "build", "notices"), "U",
            [wasmtimeEntry], declarations);
        notices.Should().Contain("- libwasmtime.a 30.0.0, Apache-2.0 WITH LLVM-exception, " + wasmtimeEntry.Source);
        notices.Should().Contain("Apache License\nVersion 2.0, January 2004").And.Contain(wasmtimeEntry.Sha256);
    }

    [Test]
    public async Task ALinkedLibraryMatchesTheDeclarationWithItsNameAndDigestAndOnlyThatOneIsListed()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var repository = Directory.CreateDirectory(sandbox.PathOf("repo")).FullName;
        var linux = sandbox.PathOf("linux", "libwasmtime.a");
        var mac = sandbox.PathOf("mac", "libwasmtime.a");
        Directory.CreateDirectory(Path.GetDirectoryName(linux)!);
        Directory.CreateDirectory(Path.GetDirectoryName(mac)!);
        await File.WriteAllBytesAsync(linux, [1, 1]);
        await File.WriteAllBytesAsync(mac, [2, 2]);
        DeclaredNativeLibrary Declared(string file) =>
            new("libwasmtime.a", "30.0.0", "Apache-2.0 WITH LLVM-exception", "wasmtime.LICENSE.txt", "https://example.org/wasmtime.tar.xz", ReleaseArchive.Sha256(file));
        var linuxEntry = Declared(linux);
        var macEntry = Declared(mac);

        // One declaration per platform, all named libwasmtime.a: the linked file takes the one with its digest.
        var (matched, problems) = ThirdPartyNotices.MatchNativeLibraries([mac], repository, [linuxEntry, macEntry]);
        problems.Should().BeEmpty();
        matched.Should().Equal(macEntry);

        await File.WriteAllBytesAsync(mac, [3, 3]);
        (matched, problems) = ThirdPartyNotices.MatchNativeLibraries([mac], repository, [linuxEntry, macEntry]);
        matched.Should().BeEmpty();
        problems.Should().ContainSingle().Which.Should().Contain(ReleaseArchive.Sha256(mac))
            .And.Contain(linuxEntry.Sha256).And.Contain(macEntry.Sha256);
    }

    [Test]
    public async Task ADeclarationsNoticeFileFollowsItsLicenseAndAMissingFileFailsTheBuild()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        var declarations = Directory.CreateDirectory(sandbox.PathOf("native")).FullName;
        await File.WriteAllTextAsync(Path.Combine(declarations, "kernel.LICENSE.txt"), "Apache License\r\nVersion 2.0");
        await File.WriteAllTextAsync(Path.Combine(declarations, "kernel.NOTICE.txt"), "Extism kernel\r\nCopyright Dylibso\r\n");
        await File.WriteAllTextAsync(Path.Combine(declarations, "kernel.json"), """
            { "kind": "embedded-resource", "name": "extism-kernel.wasm", "version": "0.3.0", "license": "Apache-2.0",
              "licenseFile": "kernel.LICENSE.txt", "noticeFile": "kernel.NOTICE.txt", "source": "https://example.org/extism.zip",
              "sha256": "3d23b39dc4cb508c32d5f8911ceeb625070dbe1335288d871e17feb03a7fba17" }
            """);

        var kernel = ThirdPartyNotices.ReadDeclaredNativeLibraries(declarations).Should().ContainSingle().Subject;
        kernel.Kind.Should().Be(DeclaredKind.EmbeddedResource);
        kernel.NoticeFile.Should().Be("kernel.NOTICE.txt");

        var notices = ThirdPartyNotices.Write("1.2.3", "linux-x64", [], declarations, "U", [kernel], declarations);
        var license = notices.IndexOf("Apache License\r\nVersion 2.0", StringComparison.Ordinal);
        var notice = notices.IndexOf("Extism kernel\r\nCopyright Dylibso\r\n", StringComparison.Ordinal);
        license.Should().BePositive("the license text is written as the file holds it");
        notice.Should().BeGreaterThan(license, "the notice follows the license, as a separate text");

        File.Delete(Path.Combine(declarations, "kernel.NOTICE.txt"));
        FluentActions.Invoking(() => ThirdPartyNotices.ReadDeclaredNativeLibraries(declarations))
            .Should().Throw<InvalidOperationException>().WithMessage("*kernel.NOTICE.txt*");
        await File.WriteAllTextAsync(Path.Combine(declarations, "kernel.json"), """
            { "kind": "static-blob", "name": "x", "version": "1", "license": "MIT", "licenseFile": "kernel.LICENSE.txt", "source": "s", "sha256": "0" }
            """);
        FluentActions.Invoking(() => ThirdPartyNotices.ReadDeclaredNativeLibraries(declarations))
            .Should().Throw<InvalidOperationException>().WithMessage("*static-blob*");
    }

    [Test]
    public async Task AnEmbeddedResourceDeclarationIsListedOnlyWhenALinkedAssemblyEmbedsThatFile()
    {
        await using var sandbox = TestSandbox.Create("notices-");
        byte[] kernelBytes = [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];
        var wasm = sandbox.PathOf("Morphir.Extensions.Wasm.dll");
        StandInAssembly.Write(wasm, "Morphir.Extensions.Wasm", ("Morphir.Extensions.Wasm.Kernel.extism-kernel.wasm", kernelBytes),
            ("Morphir.Extensions.Wasm.Contracts.manager-result.md", "first-party"u8.ToArray()));
        var other = sandbox.PathOf("Other.dll");
        StandInAssembly.Write(other, "Other");

        var resources = ThirdPartyNotices.EmbeddedResources([wasm, other]);
        resources.Select(resource => resource.LogicalName).Should().Equal(
            "Morphir.Extensions.Wasm.Kernel.extism-kernel.wasm", "Morphir.Extensions.Wasm.Contracts.manager-result.md");

        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(kernelBytes));
        var kernel = new DeclaredNativeLibrary("extism-kernel.wasm", "0.3.0", "Apache-2.0", "kernel.LICENSE.txt", "https://example.org/extism.zip", digest)
        {
            Kind = DeclaredKind.EmbeddedResource,
        };

        // One-directional: a declared resource is listed when a linked assembly embeds that file, by name and digest; an
        // undeclared resource (the first-party contract file) needs nothing, and a declaration that matches nothing is not listed.
        var (matched, problems) = ThirdPartyNotices.MatchEmbeddedResources(resources, [kernel]);
        matched.Should().Equal(kernel);
        problems.Should().BeEmpty();
        ThirdPartyNotices.MatchEmbeddedResources(ThirdPartyNotices.EmbeddedResources([other]), [kernel]).Matched.Should().BeEmpty();

        // The same file name with another digest is an embedded file whose declaration is stale: that fails the build.
        (matched, problems) = ThirdPartyNotices.MatchEmbeddedResources(resources, [kernel with { Sha256 = new string('0', 64) }]);
        matched.Should().BeEmpty();
        problems.Should().ContainSingle().Which.Should().Contain("extism-kernel.wasm").And.Contain("SHA-256");
    }

    private static string Package(TestSandbox sandbox, string folder, string version, string id, string license)
    {
        var directory = Directory.CreateDirectory(sandbox.PathOf("packages", folder, version)).FullName;
        File.WriteAllText(Path.Combine(directory, $"{folder}.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>someone</authors>
                <license type="expression">{license}</license>
                <projectUrl>https://example.org/</projectUrl>
                <copyright>Copyright someone</copyright>
              </metadata>
            </package>
            """);
        return directory;
    }
}
