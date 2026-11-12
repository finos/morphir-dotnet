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

    private readonly Func<MorphirHost, string>? _artifactOf;

    internal DriverSet(
        TestTarget target, MorphirHost host, string artifact, IScenarioWorkspace workspace, bool keepFailed, TestPrograms programs,
        ExtensionArtifacts? guests = null, Func<MorphirHost, string>? artifactOf = null, PackagedTarget? packaged = null,
        HostCopies? hostCopies = null)
    {
        _artifactOf = artifactOf;
        _keepFailed = keepFailed;
        Target = target;
        Host = host;
        Workspace = workspace;
        Cli = new CliDriver(target, host, artifact, workspace);
        Verification = new ModelVerificationDriver(Cli);
        Generation = new GenerationDriver(Cli);
        Compilation = new CompilationDriver(Cli);
        Configuration = new ConfigurationDriver(Cli);
        Migration = new MigrationDriver(Cli);
        Extensions = new ExtensionDriver(Cli, guests);
        ToolRouting = new ToolRoutingDriver(Cli, workspace, guests);
        Validation = new ModelValidationDriver(Cli);
        LibraryHosts = new LibraryHostDriver(programs, workspace);
        BuiltFiles = new ArtifactInspectionDriver(programs);
        Capabilities = DriverCapabilities.Of(target, host);
        _hostCopies = hostCopies;
        Sessions = new ProcessSessionDriver(target, host, artifact, workspace);
        if (packaged is not null) _packaged = new PackagedArtifactDriver(packaged, target, host, workspace);
    }

    private readonly HostCopies? _hostCopies;
    private readonly PackagedArtifactDriver? _packaged;
    private readonly List<IProcessSession> _sessions = [];

    /// <summary>The session driver of the host: a product process that stays alive.</summary>
    public IProcessSessionDriver Sessions { get; }

    /// <summary>The installed offering of a packaged target. Only a test with the capability PackagedInstallation uses it.</summary>
    public IPackagedArtifactDriver Packaged =>
        _packaged ?? throw new TestSessionException($"The target {Target.Name} is not a packaged offering, so it has no packaged artifact driver.");

    /// <summary>The host of a test that compares two hosts: the tool host, or the native host when the test runs on the tool host.</summary>
    public MorphirHost OtherHost => Host == MorphirHost.DotnetTool ? MorphirHost.Native : MorphirHost.DotnetTool;

    /// <summary>
    /// A copy of the folder of the host with each WASM engine library deleted, and the drivers of the copy. The real
    /// build output is not changed.
    /// </summary>
    public (HostCopy Copy, ICliDriver Cli, IProcessSessionDriver Sessions) WithoutEngineLibraries()
    {
        var copies = _hostCopies ?? throw new TestSessionException("This driver set has no host copies.");
        var copy = copies.WithoutEngineLibraries(ArtifactOf(Host));
        return (copy, new CliDriver(Target, Host, copy.Artifact, Workspace), new ProcessSessionDriver(Target, Host, copy.Artifact, Workspace));
    }

    /// <summary>The names of the files in the folder of the file of the host, in ordinal order.</summary>
    public IReadOnlyList<string> FilesBesideTheHost() =>
        [.. Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(ArtifactOf(Host)))!).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    /// <summary>Keeps <paramref name="session"/> so that the end of the test stops it when it is still alive.</summary>
    public IProcessSession Own(IProcessSession session)
    {
        lock (_sessions) _sessions.Add(session);
        return session;
    }

    private string ArtifactOf(MorphirHost host) =>
        (_artifactOf ?? throw new TestSessionException("This driver set knows only the artifact of its own host."))(host);

    public TestTarget Target { get; }

    public MorphirHost Host { get; }

    public IScenarioWorkspace Workspace { get; }

    public ICliDriver Cli { get; }

    public IModelVerificationDriver Verification { get; }

    public IGenerationDriver Generation { get; }

    public ICompilationDriver Compilation { get; }

    public IConfigurationDriver Configuration { get; }

    public IMigrationDriver Migration { get; }

    public IExtensionDriver Extensions { get; }

    public IModelValidationDriver Validation { get; }

    public IToolRoutingDriver ToolRouting { get; }

    /// <summary>
    /// The CLI driver of another host of the target, in the same workspace, for a test that compares the hosts
    /// (<see cref="DriverCapability.AllHosts"/>). A host that the target does not have is an error.
    /// </summary>
    public ICliDriver CliOf(MorphirHost host) =>
        host == Host ? Cli : new CliDriver(Target, host, (_artifactOf ?? throw new TestSessionException(Target.MissingHostReason(host)))(host), Workspace);

    /// <summary>The validation driver of another host of the target, on <see cref="CliOf"/>.</summary>
    public IModelValidationDriver ValidationOf(MorphirHost host) => host == Host ? Validation : new ModelValidationDriver(CliOf(host));

    /// <summary>The programs of the repository that are not the CLI: run, publish, compare.</summary>
    public ILibraryHostDriver LibraryHosts { get; }

    /// <summary>The built files of those programs, and their metadata.</summary>
    public IArtifactInspectionDriver BuiltFiles { get; }

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
        IProcessSession[] sessions;
        lock (_sessions) sessions = [.. _sessions];
        foreach (var session in sessions) await session.DisposeAsync().ConfigureAwait(false);
        if (_packaged is not null) await _packaged.DisposeAsync().ConfigureAwait(false);
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
    private readonly ExtensionArtifacts _guests;

    private BlackBoxSession(
        TestRunSettings settings, CliArtifacts artifacts, TestPrograms programs, string fixtures, MachineTripwire? tripwire = null,
        ExtensionArtifacts? guests = null, PackagedTarget? packaged = null)
    {
        _guests = settings.Target is TestTarget.DevelopmentBuild ? guests ?? ExtensionArtifacts.None : ExtensionArtifacts.None;
        Settings = settings;
        Artifacts = artifacts;
        Programs = programs;
        Profile = RunProfile.Of(settings);
        _fixtures = fixtures;
        _tripwire = tripwire;
        Packaged = packaged;
    }

    /// <summary>The installed offering of a packaged target, or null for another target.</summary>
    public PackagedTarget? Packaged { get; }

    public TestRunSettings Settings { get; }

    /// <summary>The files of the hosts of the target.</summary>
    public CliArtifacts Artifacts { get; }

    /// <summary>The other programs of the repository that the test assembly names. None for a target other than the development build.</summary>
    public TestPrograms Programs { get; }

    public RunProfile Profile { get; }

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
        var (artifacts, packaged) = Resolve(
            settings, () => CliArtifacts.OfDevelopmentBuild(testAssembly),
            () => ArtifactLocations.Of(ArtifactLocations.MetadataOf(testAssembly), testAssembly.GetName().Name ?? "the test assembly", variable), fileExists);
        return new BlackBoxSession(
            settings,
            artifacts,
            settings.Target is TestTarget.DevelopmentBuild ? TestPrograms.OfTestAssembly(testAssembly) : TestPrograms.None,
            fixtures,
            tripwire,
            ExtensionArtifacts.OfTestAssembly(testAssembly),
            packaged);
    }

    /// <summary>Starts a session from the assembly metadata that Morphir.CliArtifacts.targets writes.</summary>
    public static BlackBoxSession Start(
        IReadOnlyDictionary<string, string> metadata, string testAssembly, Func<string, string?> variable, Func<string, bool> fileExists, string fixtures)
    {
        var settings = TestRunSettings.Parse(variable, fileExists);
        var (artifacts, packaged) = Resolve(
            settings, () => CliArtifacts.OfDevelopmentBuild(metadata, testAssembly, fileExists),
            () => ArtifactLocations.Of(metadata, testAssembly, variable), fileExists);
        return new BlackBoxSession(
            settings,
            artifacts,
            settings.Target is TestTarget.DevelopmentBuild ? TestPrograms.Of(metadata, testAssembly, fileExists) : TestPrograms.None,
            fixtures,
            guests: ExtensionArtifacts.Of(metadata, fileExists),
            packaged: packaged);
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
        Packaged?.Dispose();
        _hostCopies.Dispose();
        _tripwire?.AssertUnchanged();
    }

    /// <summary>Makes the workspace and the drivers of one test on <paramref name="host"/>, which the target must have.</summary>
    public DriverSet BeginTest(MorphirHost host)
    {
        var artifact = Artifacts.PathOf(host);
        return new DriverSet(
            Settings.Target, host, artifact, ScenarioWorkspace.Create(_fixtures), Settings.KeepFailed, Programs, _guests, Artifacts.PathOf, Packaged, _hostCopies);
    }

    private readonly HostCopies _hostCopies = new();

    // Each target resolves to the artifact of its own form. No target uses the artifact of another form.
    private static (CliArtifacts Artifacts, PackagedTarget? Packaged) Resolve(
        TestRunSettings settings, Func<CliArtifacts> developmentBuild, Func<ArtifactLocations> locations, Func<string, bool> fileExists)
    {
        switch (settings.Target)
        {
            case TestTarget.DevelopmentBuild:
                return (developmentBuild(), null);
            case TestTarget.Executable executable:
                return (CliArtifacts.Of(executable), null);
            case TestTarget.Trimmed or TestTarget.Untrimmed or TestTarget.NativeAot:
                return (CliArtifacts.OfPublished(settings.Target, PublishedExecutables.PathOf(settings.Target, locations(), fileExists)), null);
            case TestTarget.Packaged packaged:
                var installed = PackagedTarget.Install(packaged, locations());
                return (CliArtifacts.OfPublished(packaged, installed.Installation.LaunchPath), installed);
            default:
                throw new TestSessionException($"The target '{settings.Target.Name}' has no artifact resolution.");
        }
    }
}
