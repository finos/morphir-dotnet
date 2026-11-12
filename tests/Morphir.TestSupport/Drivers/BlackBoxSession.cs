// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The workspace and the drivers of one test, for one host of the selected target. A hook makes one before a test
/// and ends it after the test; a step or a test gets the drivers from it and needs no call to the machine.
/// </summary>
public sealed class DriverSet : IAsyncDisposable
{
    private readonly bool _keepFailed;
    private bool _ended;

    internal DriverSet(
        TestTarget target, MorphirHost host, string artifact, IScenarioWorkspace workspace, bool keepFailed, TestPrograms programs,
        PackageSources? packageSources = null, PackedPackages? packedPackages = null)
    {
        _keepFailed = keepFailed;
        Target = target;
        Host = host;
        Workspace = workspace;
        Cli = new CliDriver(target, host, artifact, workspace);
        Verification = new ModelVerificationDriver(Cli);
        Generation = new GenerationDriver(Cli);
        LibraryHosts = new LibraryHostDriver(programs, workspace);
        BuiltFiles = new ArtifactInspectionDriver(programs);
        Packages = new PackageDriver(workspace, packageSources, packedPackages);
        Capabilities = DriverCapabilities.Of(target, host);
    }

    public TestTarget Target { get; }

    public MorphirHost Host { get; }

    public IScenarioWorkspace Workspace { get; }

    public ICliDriver Cli { get; }

    public IModelVerificationDriver Verification { get; }

    public IGenerationDriver Generation { get; }

    /// <summary>The programs of the repository that are not the CLI: run, publish, compare.</summary>
    public ILibraryHostDriver LibraryHosts { get; }

    /// <summary>The built files of those programs, and their metadata.</summary>
    public IArtifactInspectionDriver BuiltFiles { get; }

    public IPackageDriver Packages { get; }

    /// <summary>The capabilities that these drivers support.</summary>
    public IReadOnlySet<DriverCapability> Capabilities { get; }

    /// <summary>
    /// Ends the test: removes the workspace, or keeps it when the test failed and the run keeps evidence
    /// (<c>MORPHIR_TEST_KEEP=failed</c>). Returns the path of a kept workspace, or null.
    /// </summary>
    public async ValueTask<string?> EndAsync(bool failed)
    {
        if (_ended) return null;
        _ended = true;
        var keep = failed && _keepFailed;
        if (keep) Workspace.Keep();
        await Workspace.DisposeAsync().ConfigureAwait(false);
        return keep ? Workspace.Root : null;
    }

    public async ValueTask DisposeAsync() => await EndAsync(failed: false).ConfigureAwait(false);
}

/// <summary>
/// The session of one black-box test process. It reads the environment once, resolves the target to its own
/// artifact before the first test, decides if each test runs, and makes the workspace and the drivers of a test.
/// </summary>
public sealed class BlackBoxSession
{
    private readonly string _fixtures;
    private readonly MachineTripwire? _tripwire;

    private BlackBoxSession(
        TestRunSettings settings, CliArtifacts artifacts, TestPrograms programs, string fixtures, MachineTripwire? tripwire = null,
        PackageSources? packageSources = null, Func<string, string?>? variable = null)
    {
        Settings = settings;
        Artifacts = artifacts;
        Programs = programs;
        PackageSources = packageSources;
        // The packages lane reads the packed packages before the first test: a run with no package stops here.
        PackedPackages = settings.Lane == TestLane.Packages
            ? PackedPackages.Of(variable?.Invoke(PackedPackages.Variable), packageSources?.Configuration ?? artifacts.Configuration)
            : null;
        Profile = RunProfile.Of(settings);
        _fixtures = fixtures;
        _tripwire = tripwire;
    }

    public TestRunSettings Settings { get; }

    /// <summary>The files of the hosts of the target.</summary>
    public CliArtifacts Artifacts { get; }

    /// <summary>The other programs of the repository that the test assembly names. None for a target other than the development build.</summary>
    public TestPrograms Programs { get; }

    public RunProfile Profile { get; }

    /// <summary>The project files of the packages that a test packs, or null when the test project does not import Morphir.PackageSources.targets.</summary>
    public PackageSources? PackageSources { get; }

    /// <summary>The packages that the build packed, in the packages lane; null in another lane.</summary>
    public PackedPackages? PackedPackages { get; }

    /// <summary>The count of the tests that <see cref="Decide"/> skipped, by the kind of reason.</summary>
    public SkipCounts Skips { get; } = new();

    /// <summary>
    /// Starts the session of <paramref name="testAssembly"/> from the environment of the test process, for the
    /// hook that runs before the first test. The fixture folder is <c>Fixtures</c> beside the test assembly. It
    /// first records the Morphir locations of the machine (<see cref="MachineTripwire"/>), which
    /// <see cref="EndRun"/> checks. A wrong setting or a missing artifact throws a
    /// <see cref="TestSessionException"/>, so the run stops before the first test.
    /// </summary>
    public static BlackBoxSession Start(Assembly testAssembly) =>
        Start(testAssembly, Environment.GetEnvironmentVariable, File.Exists, Path.Combine(AppContext.BaseDirectory, "Fixtures"), MachineTripwire.Capture());

    /// <param name="tripwire">The locations that no test must change, or null for a session with no such check.</param>
    public static BlackBoxSession Start(
        Assembly testAssembly, Func<string, string?> variable, Func<string, bool> fileExists, string fixtures, MachineTripwire? tripwire = null)
    {
        var settings = TestRunSettings.Parse(variable, fileExists);
        return new BlackBoxSession(
            settings,
            Resolve(settings, () => CliArtifacts.OfDevelopmentBuild(testAssembly)),
            settings.Target is TestTarget.DevelopmentBuild ? TestPrograms.OfTestAssembly(testAssembly) : TestPrograms.None,
            fixtures,
            tripwire,
            PackageSources.OfDevelopmentBuild(testAssembly),
            variable);
    }

    /// <summary>Starts a session from the assembly metadata that Morphir.CliArtifacts.targets writes.</summary>
    public static BlackBoxSession Start(
        IReadOnlyDictionary<string, string> metadata, string testAssembly, Func<string, string?> variable, Func<string, bool> fileExists, string fixtures)
    {
        var settings = TestRunSettings.Parse(variable, fileExists);
        return new BlackBoxSession(
            settings,
            Resolve(settings, () => CliArtifacts.OfDevelopmentBuild(metadata, testAssembly, fileExists)),
            settings.Target is TestTarget.DevelopmentBuild ? TestPrograms.Of(metadata, testAssembly, fileExists) : TestPrograms.None,
            fixtures,
            packageSources: PackageSources.Of(metadata),
            variable: variable);
    }

    /// <summary>The one decision of a test (<see cref="TestEligibility.Decide"/>). A skip is counted.</summary>
    public Eligibility Decide(TestNeeds needs)
    {
        var eligibility = TestEligibility.Decide(needs, Profile);
        if (eligibility is Eligibility.Skipped skipped) Skips.Add(skipped);
        return eligibility;
    }

    /// <summary>
    /// Ends the run, for the hook that runs after the last test. It writes the count of the skipped tests by
    /// reason to <paramref name="output"/> (the build reads that line for its report), and then fails if a test
    /// changed a Morphir location of the machine: such a test escaped its workspace.
    /// </summary>
    public void EndRun(TextWriter output)
    {
        output.WriteLine(Skips.ReportLine());
        _tripwire?.AssertUnchanged();
    }

    /// <summary>Makes the workspace and the drivers of one test on <paramref name="host"/>, which the target must have.</summary>
    public DriverSet BeginTest(MorphirHost host)
    {
        var artifact = Artifacts.PathOf(host);
        return new DriverSet(Settings.Target, host, artifact, ScenarioWorkspace.Create(_fixtures), Settings.KeepFailed, Programs, PackageSources, PackedPackages);
    }

    // Each target resolves to the artifact of its own form. No target uses the artifact of another form.
    private static CliArtifacts Resolve(TestRunSettings settings, Func<CliArtifacts> developmentBuild) => settings.Target switch
    {
        TestTarget.DevelopmentBuild => developmentBuild(),
        TestTarget.Executable executable => CliArtifacts.Of(executable),
        _ => throw new TestSessionException(
            $"The CLI driver has no artifact resolution for the target '{settings.Target.Name}' yet. The targets with a driver are: dev, executable."),
    };
}
