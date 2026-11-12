// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps that pack a package into the feed of the workspace, that take a package that the build packed (the
/// packages lane), and that check the manifest, the entries and the assemblies of a package.
/// </summary>
[Binding]
public sealed class PackageSteps(IPackageDriver packages, IScenarioWorkspace workspace, PackageRun state)
{
    private static IEnumerable<string> Ids(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // ---- Packages of the scenario -------------------------------------------------------------------------------

    [Given("the package {string} packed from the development build")]
    public Task ThePackagePacked(string id) => ThePackagesPacked(id);

    /// <summary>Packs each package at one version, so that the packages of one release can refer to each other.</summary>
    [Given("the packages {string} packed from the development build")]
    public async Task ThePackagesPacked(string ids)
    {
        var version = "0.1.0-test." + Guid.NewGuid().ToString("N");
        foreach (var id in Ids(ids))
        {
            var package = await packages.PackAsync(PackageProjects.Parse(id), new PackOptions { PackageVersion = version });
            state.InFeed[package.Id] = package;
            state.Package = package;
        }
    }

    [Given("the packed package {string}")]
    public void ThePackedPackage(string id) => state.Package = packages.Packed.Find(id);

    [Given("the packed package {string} in the feed of the workspace")]
    public void ThePackedPackageInTheFeed(string id)
    {
        var package = packages.AddToFeed(packages.Packed.Find(id));
        state.InFeed[package.Id] = package;
        state.Package = package;
    }

    // ---- The manifest -------------------------------------------------------------------------------------------

    [Then("the manifest of the package has the id {string}")]
    public void TheManifestHasTheId(string id) => Expect(state.Package.Manifest.Id == id, $"the id is '{state.Package.Manifest.Id}', not '{id}'");

    [Then("the authors of the package include {string}")]
    public void TheAuthorsInclude(string author) =>
        Expect(state.Package.Manifest.Authors.Contains(author, StringComparison.Ordinal), $"the authors '{state.Package.Manifest.Authors}' do not include '{author}'");

    [Then("the package has a license")]
    public void ThePackageHasALicense() => Expect(state.Package.Manifest.License.Length > 0, "the manifest has no license and no license URL");

    [Then("the project URL of the package contains {string}")]
    public void TheProjectUrlContains(string text) =>
        Expect(state.Package.Manifest.ProjectUrl.Contains(text, StringComparison.Ordinal), $"the project URL '{state.Package.Manifest.ProjectUrl}' does not contain '{text}'");

    [Then("the package type of the package is {string}")]
    public void ThePackageTypeIs(string type) =>
        Expect(state.Package.Manifest.PackageTypes.FirstOrDefault() == type, $"the package types are [{string.Join(", ", state.Package.Manifest.PackageTypes)}], and the first is not '{type}'");

    [Then("the version of the package is the version of the changelog")]
    public void TheVersionIsTheVersionOfTheChangelog() =>
        Expect(state.Package.Version == ChangelogVersion(), $"the version is {state.Package.Version}, and the changelog has {ChangelogVersion()}");

    [Then("the packed packages {string} have one version")]
    public void ThePackedPackagesHaveOneVersion(string ids)
    {
        var versions = Ids(ids).Select(id => packages.Packed.Find(id)).Select(package => $"{package.Id} {package.Version}").ToArray();
        if (versions.Select(text => text.Split(' ')[1]).Distinct().Count() != 1)
            throw new InvalidOperationException($"The packages have more than one version: {string.Join(", ", versions)}.");
    }

    [Then("each of the packed packages {string} has release notes that contain {string}")]
    public void EachHasReleaseNotesThatContain(string ids, string text)
    {
        foreach (var package in Ids(ids).Select(id => packages.Packed.Find(id)))
        {
            if (!package.Manifest.ReleaseNotes.Contains(text, StringComparison.Ordinal))
                throw new InvalidOperationException($"The release notes of {package.FileName} do not contain '{text}': '{package.Manifest.ReleaseNotes}'.");
        }
    }

    [Then("each Morphir dependency of each packed package is packed at its version")]
    public void EachMorphirDependencyIsPacked()
    {
        var packed = packages.Packed.All.ToDictionary(package => package.Id, package => package.Version, StringComparer.OrdinalIgnoreCase);
        var missing = packages.Packed.All
            .SelectMany(package => package.Manifest.Dependencies
                .Where(dependency => dependency.Id.StartsWith("Morphir", StringComparison.OrdinalIgnoreCase))
                .Select(dependency => (From: package.Id, Dependency: dependency)))
            .Where(item => !packed.TryGetValue(item.Dependency.Id, out var version) || !item.Dependency.IsExactly(version))
            .Select(item => $"{item.From} -> {item.Dependency.Id} {item.Dependency.VersionRange}")
            .Distinct()
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"These Morphir dependencies are not packed at their version: {string.Join("; ", missing)}.");
    }

    // ---- The entries --------------------------------------------------------------------------------------------

    [Then("the package has the entries:")]
    public void ThePackageHasTheEntries(Table table)
    {
        var missing = table.Rows.Select(row => row["entry"]).Where(entry => !state.Package.Entries.Contains(entry)).ToArray();
        Expect(missing.Length == 0, $"the package has no entry {string.Join(", ", missing)}");
    }

    [Then("the package has an entry below {string}")]
    public void ThePackageHasAnEntryBelow(string folder) =>
        Expect(state.Package.Entries.Any(entry => entry.StartsWith(folder, StringComparison.Ordinal)), $"the package has no entry below {folder}");

    [Then("the package has no entry below {string}")]
    public void ThePackageHasNoEntryBelow(string folder) =>
        Expect(!state.Package.Entries.Any(entry => entry.StartsWith(folder, StringComparison.Ordinal)), $"the package has an entry below {folder}");

    [Then("the package has an entry that contains {string}")]
    public void ThePackageHasAnEntryThatContains(string text) =>
        Expect(state.Package.Entries.Any(entry => entry.Contains(text, StringComparison.Ordinal)), $"the package has no entry that contains {text}");

    [Then("the package has no entry with the file name {string}")]
    public void ThePackageHasNoEntryWithTheFileName(string name) =>
        Expect(!state.Package.Entries.Any(entry => string.Equals(entry.Split('/')[^1], name, StringComparison.OrdinalIgnoreCase)), $"the package has an entry {name}");

    [Then("the entry {string} of the package contains the text:")]
    public void TheEntryContainsTheText(string entry, string text) =>
        Expect(state.Package.ReadEntry(entry).Contains(text, StringComparison.Ordinal), $"the entry {entry} does not contain '{text}'");

    [Then("the packed packages {string} have no entry that ends with {string}")]
    public void ThePackedPackagesHaveNoEntryThatEndsWith(string ids, string end)
    {
        foreach (var package in Ids(ids).Select(id => packages.Packed.Find(id)))
        {
            var found = package.Entries.Where(entry => entry.EndsWith(end, StringComparison.Ordinal)).ToArray();
            if (found.Length > 0) throw new InvalidOperationException($"{package.FileName} has {string.Join(", ", found)}.");
        }
    }

    [Then("the packed packages {string} have no entry that contains {string}")]
    public void ThePackedPackagesHaveNoEntryThatContains(string ids, string text)
    {
        foreach (var package in Ids(ids).Select(id => packages.Packed.Find(id)))
        {
            var found = package.Entries.Where(entry => entry.Contains(text, StringComparison.Ordinal)).ToArray();
            if (found.Length > 0) throw new InvalidOperationException($"{package.FileName} has {string.Join(", ", found)}.");
        }
    }

    // ---- The assemblies -----------------------------------------------------------------------------------------

    [Then("each Morphir assembly of the package below {string} has the assembly version of the changelog")]
    public void EachMorphirAssemblyHasTheVersionOfTheChangelog(string folder)
    {
        var expected = ChangelogAssemblyVersion();
        var assemblies = MorphirAssemblies(folder);
        var wrong = assemblies.Where(assembly => assembly.Identity.Version != expected).Select(assembly => $"{assembly.Identity.Name} {assembly.Identity.Version}").ToArray();
        Expect(wrong.Length == 0, $"each Morphir assembly has the version {expected}, and these do not: {string.Join(", ", wrong)}");
    }

    [Then("each reference between the Morphir assemblies of the package below {string} has the assembly version of the changelog")]
    public void EachReferenceHasTheVersionOfTheChangelog(string folder)
    {
        var expected = ChangelogAssemblyVersion();
        var wrong = MorphirAssemblies(folder)
            .SelectMany(assembly => assembly.Identity.References.Where(reference => IsMorphirName(reference.Name))
                .Select(reference => (From: assembly.Identity.Name, reference.Name, reference.Version)))
            .Where(reference => reference.Version != expected)
            .Select(reference => $"{reference.From} -> {reference.Name} {reference.Version}")
            .ToArray();
        Expect(wrong.Length == 0, $"each reference between Morphir assemblies has the version {expected}, and these do not: {string.Join(", ", wrong)}");
    }

    [Then("the package has no assembly")]
    public void ThePackageHasNoAssembly()
    {
        var found = state.Package.Entries.Where(entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        Expect(found.Length == 0, $"the package has the compiled files {string.Join(", ", found)}");
    }

    [Then("the package depends only on {string} at exactly its own version")]
    public void ThePackageDependsOnlyOn(string id)
    {
        var dependencies = state.Package.Manifest.Dependencies;
        Expect(dependencies.Length == 1 && dependencies[0].Id == id && dependencies[0].IsExactly(state.Package.Version),
            $"the package depends on [{string.Join(", ", dependencies.Select(dependency => $"{dependency.Id} {dependency.VersionRange}"))}], not only on {id} [{state.Package.Version}]");
    }

    // A Morphir assembly of a package is an assembly whose name starts with Morphir, or the tool entry point.
    private static bool IsMorphirName(string name) =>
        name.Equals("dotnet-morphir", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Morphir", StringComparison.OrdinalIgnoreCase);

    private PackedAssembly[] MorphirAssemblies(string folder)
    {
        var assemblies = state.Package
            .Assemblies(entry => entry.StartsWith(folder, StringComparison.Ordinal) && IsMorphirName(Path.GetFileNameWithoutExtension(entry.Split('/')[^1])))
            .ToArray();
        Expect(assemblies.Length > 0, $"the package has no Morphir assembly below {folder}");
        return assemblies;
    }

    /// <summary>The first released version of the changelog of the repository, for example <c>0.4.0-alpha.4</c>.</summary>
    private string ChangelogVersion()
    {
        var changelog = workspace.ReadText(workspace.CopyFixture("Repository/CHANGELOG.md"));
        var match = Regex.Match(changelog, @"^## \[(\d+\.\d+\.\d+[^\]]*)\]", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException("The changelog has no released version.");
    }

    /// <summary>The assembly version of the changelog version: major.minor.patch.0, with no prerelease label.</summary>
    private Version ChangelogAssemblyVersion() => new($"{ChangelogVersion().Split('-', '+')[0]}.0");

    private void Expect(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException($"{state.Package.FileName}: {failure}.");
    }
}
