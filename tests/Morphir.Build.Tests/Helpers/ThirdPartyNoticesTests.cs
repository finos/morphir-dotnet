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
            lines[0].Should().Be("License: MIT", $"{Path.GetFileName(file)} is the MIT text of its package");
            lines[1].Should().StartWith("Source: https://github.com/");
            string.Join('\n', lines).Should().Contain(Mit).And.Contain("Copyright");
        }

        Directory.GetFiles(fallbacks, "*.txt").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)
            .Should().Equal("CsToml", "FSharp.Core", "Microsoft.Extensions.Primitives", "Semver", "System.IO.Hashing");
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
