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
        TestTarget target, MorphirHost host, string artifact, IScenarioWorkspace workspace, bool keepFailed, ExtensionArtifacts? guests = null,
        Func<MorphirHost, string>? artifactOf = null)
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
        Capabilities = DriverCapabilities.Of(target, host);
    }

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
    private readonly ExtensionArtifacts _guests;

    private BlackBoxSession(TestRunSettings settings, CliArtifacts artifacts, string fixtures, MachineTripwire? tripwire = null, ExtensionArtifacts? guests = null)
    {
        _guests = settings.Target is TestTarget.DevelopmentBuild ? guests ?? ExtensionArtifacts.None : ExtensionArtifacts.None;
        Settings = settings;
        Artifacts = artifacts;
        Profile = RunProfile.Of(settings);
        _fixtures = fixtures;
        _tripwire = tripwire;
    }

    public TestRunSettings Settings { get; }

    /// <summary>The files of the hosts of the target.</summary>
    public CliArtifacts Artifacts { get; }

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
        return new BlackBoxSession(
            settings, Resolve(settings, () => CliArtifacts.OfDevelopmentBuild(testAssembly)), fixtures, tripwire, ExtensionArtifacts.OfTestAssembly(testAssembly));
    }

    /// <summary>Starts a session from the assembly metadata that Morphir.CliArtifacts.targets writes.</summary>
    public static BlackBoxSession Start(
        IReadOnlyDictionary<string, string> metadata, string testAssembly, Func<string, string?> variable, Func<string, bool> fileExists, string fixtures)
    {
        var settings = TestRunSettings.Parse(variable, fileExists);
        return new BlackBoxSession(
            settings, Resolve(settings, () => CliArtifacts.OfDevelopmentBuild(metadata, testAssembly, fileExists)), fixtures, guests: ExtensionArtifacts.Of(metadata, fileExists));
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
        return new DriverSet(Settings.Target, host, artifact, ScenarioWorkspace.Create(_fixtures), Settings.KeepFailed, _guests, Artifacts.PathOf);
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
