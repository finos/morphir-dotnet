// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport.Drivers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The rules of the black-box test project that a program can check (design GUARD-1 of the change
/// consolidate-black-box-tests). Each rule has a fixture that it must find, and the last tests apply each rule to
/// the files of the repository.
/// </summary>
public class BlackBoxRulesTests
{
    private const string Project = "tests/Morphir.BlackBox.Tests";

    private static ImmutableArray<BlackBoxTest> Feature(string file, string text) => BlackBoxRules.ParseFeature(file, text);

    private static BlackBoxTest Test(string name, params string[] tags) => new("Smoke/A.feature", 3, name, [.. tags]);

    private static string Only(IEnumerable<RuleViolation> violations) => violations.Should().ContainSingle().Subject.ToString();

    // One group tag, and the folder agrees.

    [Test]
    public void AScenarioGetsTheTagsOfItsFeatureAndItsOwnTags()
    {
        var tests = Feature("Integration/Verify.feature", """
            @integration @needs-model-verification
            Feature: Verify a model

              Background:
                Given the model fixture "a.json"

              # A comment with @smoke in it is not a tag.
              @host-managed
              Scenario: A valid model
                When I verify the model

              Scenario Outline: A model of version <version>
                When I verify the model

                @lane @published
                Examples:
                  | version |
                  | 1       |
            """);

        tests.Select(test => test.Name).Should().Equal("A valid model", "A model of version <version>");
        tests[0].Tags.Should().Equal("integration", "needs-model-verification", "host-managed");
        tests[0].Line.Should().Be(9);
        tests[0].File.Should().Be("Integration/Verify.feature");
        tests[1].Tags.Should().Equal("integration", "needs-model-verification", "lane", "published");
    }

    [Test]
    public void AScenarioInARuleGetsTheTagsOfTheRule()
    {
        var tests = Feature("Smoke/A.feature", """
            @smoke
            Feature: A
              @host-tool
              Rule: The tool
                Scenario: One
              Rule: The default
                Scenario: Two
            """);

        tests[0].Tags.Should().Equal("smoke", "host-tool");
        tests[1].Tags.Should().Equal("smoke");
    }

    [Test]
    public void AScenarioWithOneGroupInTheFolderOfItsGroupPasses() =>
        BlackBoxRules.GroupViolations(Feature("Journeys/A.feature", "@journey\nFeature: A\n  Scenario: One\n")).Should().BeEmpty();

    [Test]
    public void AScenarioWithNoGroupIsFoundWithItsFeatureFileAndItsName() =>
        Only(BlackBoxRules.GroupViolations(Feature("Smoke/A.feature", "@needs-generation\nFeature: A\n  Scenario: No group here\n")))
            .Should().Be("Smoke/A.feature:3: one-group: 'No group here' has no group. Give it one of: @integration, @e2e, @smoke, @journey.");

    [Test]
    public void AScenarioWithTwoGroupsIsFoundWithBothTags() =>
        Only(BlackBoxRules.GroupViolations(Feature("Journeys/A.feature", "@journey\nFeature: A\n  @integration\n  Scenario: Two groups\n")))
            .Should().Be("Journeys/A.feature:4: one-group: 'Two groups' has two groups: @journey, @integration. A test has one group.");

    [Test]
    [Arguments("Smoke/A.feature", "e2e", "EndToEnd")]
    [Arguments("Integration/Sub/A.feature", "smoke", "Smoke")]
    [Arguments("A.feature", "journey", "Journeys")]
    public void AFeatureInAFolderThatIsNotTheFolderOfItsGroupIsFound(string file, string group, string folder) =>
        Only(BlackBoxRules.GroupViolations(Feature(file, $"@{group}\nFeature: A\n  Scenario: One\n")))
            .Should().Be($"{file}:3: one-group: 'One' has the group @{group}, so its file must be in the folder {folder}.");

    // Framework tests have one group.

    private const string FrameworkSource = """
        namespace Morphir.BlackBox.Tests.EndToEnd;

        // [Category("smoke")] in a comment is not a category.
        [Category("e2e")]
        [Category("needs-generation")]
        public sealed class CorpusTests : BlackBoxTest
        {
            private static string Name => "[Test] in a string is not a test";

            [Test]
            public async Task EachCaseOfTheCorpus() => await Task.CompletedTask;

            [Test, Category("lane"), Category("published")]
            [Arguments(1)]
            public async Task TheWorkerOnAPublishedExecutable(int value) => await Task.CompletedTask;

            private void Helper() { }
        }

        public class NoGroupTests : BlackBoxTest
        {
            [Test]
            public void AFrameworkTestWithNoGroup() { }
        }
        """;

    [Test]
    public void AFrameworkTestGetsTheCategoriesOfItsClassAndItsOwnCategories()
    {
        var tests = BlackBoxRules.ParseFrameworkTests("EndToEnd/CorpusTests.cs", FrameworkSource);

        tests.Select(test => test.Name).Should().Equal(
            "CorpusTests.EachCaseOfTheCorpus", "CorpusTests.TheWorkerOnAPublishedExecutable", "NoGroupTests.AFrameworkTestWithNoGroup");
        tests[0].Tags.Should().Equal("e2e", "needs-generation");
        tests[0].Line.Should().Be(11);
        tests[1].Tags.Should().Equal("e2e", "needs-generation", "lane", "published");
        tests[2].Tags.Should().BeEmpty();
    }

    [Test]
    public void AFrameworkTestWithNoGroupIsFoundWithItsName() =>
        Only(BlackBoxRules.GroupViolations(BlackBoxRules.ParseFrameworkTests("EndToEnd/CorpusTests.cs", FrameworkSource)))
            .Should().StartWith("EndToEnd/CorpusTests.cs:23: one-group: 'NoGroupTests.AFrameworkTestWithNoGroup' has no group.");

    // At most one lane.

    [Test]
    public void ATestWithNoLaneAndATestWithOneLanePass() =>
        BlackBoxRules.LaneViolations([Test("No lane", "smoke"), Test("One lane", "e2e", "lane", "packages")]).Should().BeEmpty();

    [Test]
    public void ATestWithTwoLaneTagsIsFound() =>
        Only(BlackBoxRules.LaneViolations([Test("Two lanes", "e2e", "lane", "packages", "published")]))
            .Should().Be("Smoke/A.feature:3: one-lane: 'Two lanes' has two lanes: @packages, @published. A test has at most one lane.");

    [Test]
    public void ATestWithALaneTagAndNoLaneMarkerIsFound() =>
        Only(BlackBoxRules.LaneViolations([Test("No marker", "e2e", "native-aot")]))
            .Should().Be("Smoke/A.feature:3: one-lane: 'No marker' has the lane @native-aot and not @lane. A test with a lane has both, so that a group run does not select it.");

    [Test]
    public void ATestWithTheLaneMarkerAndNoLaneIsFound() =>
        Only(BlackBoxRules.LaneViolations([Test("No lane", "e2e", "lane")]))
            .Should().Be("Smoke/A.feature:3: one-lane: 'No lane' has @lane and no lane. Give it one of: @packages, @native-aot, @published.");

    // No assembly from src/ is visible.

    [Test]
    public void AReferenceThatExposesAProductAssemblyIsFound()
    {
        const string xml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../Morphir.TestSupport/Morphir.TestSupport.csproj" />
                <ProjectReference Include="..\..\src\Morphir.Tooling\Morphir.Tooling.csproj" />
              </ItemGroup>
            </Project>
            """;

        Only(BlackBoxRules.ReferenceViolations($"{Project}/Morphir.BlackBox.Tests.csproj", xml))
            .Should().Be($"{Project}/Morphir.BlackBox.Tests.csproj:4: no-product-assembly: the reference to src/Morphir.Tooling/Morphir.Tooling.csproj makes a product assembly visible. A reference below src/ has ReferenceOutputAssembly=\"false\".");
    }

    [Test]
    public void ABuildOrderReferencePasses()
    {
        const string xml = """
            <Project>
              <ItemGroup>
                <ProjectReference Include="$(MSBuildThisFileDirectory)../../../src/Morphir/Morphir.csproj"
                                  ReferenceOutputAssembly="false" Private="false" />
                <ProjectReference Include="../../../src/Morphir.Tool/Morphir.Tool.csproj" ReferenceOutputAssembly="False" />
              </ItemGroup>
            </Project>
            """;

        BlackBoxRules.ReferenceViolations("tests/Morphir.TestSupport/build/Morphir.CliArtifacts.targets", xml).Should().BeEmpty();
    }

    [Test]
    public void AProductProjectThatComesThroughAnotherReferenceIsFoundInTheAssetsFile()
    {
        const string assets = """
            {
              "targets": {
                "net11.0": {
                  "Morphir.Conformance/1.0.0": {
                    "type": "project",
                    "dependencies": { "Morphir.Compilation": "1.0.0" },
                    "compile": { "bin/placeholder/Morphir.Conformance.dll": {} }
                  },
                  "Morphir.Compilation/1.0.0": {
                    "type": "project",
                    "compile": { "bin/placeholder/Morphir.Compilation.dll": {} }
                  },
                  "Morphir.TestSupport/1.0.0": {
                    "type": "project",
                    "compile": { "bin/placeholder/Morphir.TestSupport.dll": {} }
                  },
                  "Morphir.Excluded/1.0.0": { "type": "project", "compile": { "_._": {} } },
                  "TUnit/1.0.0": { "type": "package", "compile": { "lib/net10.0/TUnit.dll": {} } }
                }
              },
              "libraries": {
                "Morphir.Conformance/1.0.0": { "type": "project", "path": "../../src/Morphir.Conformance/Morphir.Conformance.fsproj" },
                "Morphir.Compilation/1.0.0": { "type": "project", "path": "../../src/Morphir.Compilation/Morphir.Compilation.fsproj" },
                "Morphir.TestSupport/1.0.0": { "type": "project", "path": "../Morphir.TestSupport/Morphir.TestSupport.csproj" },
                "Morphir.Excluded/1.0.0": { "type": "project", "path": "../../src/Morphir.Excluded/Morphir.Excluded.csproj" },
                "TUnit/1.0.0": { "type": "package", "path": "tunit/1.0.0" }
              },
              "project": {
                "frameworks": { "net11.0": { "projectReferences": {} } },
                "restore": {
                  "frameworks": {
                    "net11.0": {
                      "projectReferences": {
                        "/repo/src/Morphir.Conformance/Morphir.Conformance.fsproj": { "projectPath": "/repo/src/Morphir.Conformance/Morphir.Conformance.fsproj" },
                        "/repo/tests/Morphir.TestSupport/Morphir.TestSupport.csproj": { "projectPath": "/repo/tests/Morphir.TestSupport/Morphir.TestSupport.csproj" }
                      }
                    }
                  }
                }
              }
            }
            """;

        var violations = BlackBoxRules.AssetsViolations($"{Project}/obj/project.assets.json", assets, Project).Select(violation => violation.ToString());

        violations.Should().Equal(
            $"{Project}/obj/project.assets.json:1: no-product-assembly: the assembly of src/Morphir.Conformance/Morphir.Conformance.fsproj is visible. It is a direct reference.",
            $"{Project}/obj/project.assets.json:1: no-product-assembly: the assembly of src/Morphir.Compilation/Morphir.Compilation.fsproj is visible. It comes through Morphir.Conformance.");
    }

    // Steps and tests use drivers. This check is partial: it finds the listed names only.

    [Test]
    [Arguments("var info = ChildProcess.StartInfo(\"dotnet\");", "ChildProcess")]
    [Arguments("using System.Diagnostics;", "System.Diagnostics")]
    [Arguments("using var process = new Process();", "Process")]
    [Arguments("using var sandbox = TestSandbox.Create();", "TestSandbox")]
    [Arguments("var text = File.ReadAllText(path);", "File.")]
    [Arguments("Directory.CreateDirectory(path);", "Directory.")]
    [Arguments("var root = Path.GetTempPath();", "Path.GetTempPath")]
    [Arguments("var home = Environment.GetEnvironmentVariable(\"HOME\");", "Environment.")]
    public void AStepThatGoesPastADriverIsFound(string line, string name)
    {
        var source = $"namespace Steps;\npublic class GenerationSteps\n{{\n    public void Run()\n    {{\n        {line}\n    }}\n}}\n";

        Only(BlackBoxRules.DriverUseViolations("Steps/GenerationSteps.cs", source))
            .Should().Be($"Steps/GenerationSteps.cs:6: use-drivers: '{name}' goes past the drivers. A step or a test reaches the product, the file system and other processes only through a driver interface.");
    }

    [Test]
    public void AForbiddenNameInACommentOrInAStringIsIgnored()
    {
        const string source = """"
            namespace Steps;
            /// <summary>Does not use ChildProcess or File.ReadAllText.</summary>
            public class GenerationSteps
            {
                // TestSandbox is the type that a workspace wraps.
                /* Process and
                   Directory.Delete are not for a step. */
                private const string Message = "Use Environment.NewLine and File.Exists in the driver";
                private const string Verbatim = @"C:\File.txt ""Process""";
                private const string Raw = """
                    Path.GetTempPath() "and" System.Diagnostics
                    """;
                private const char Quote = '"';
                private string Interpolated(int n) => $"ChildProcess {n} File.";
                public WorkspaceFile Model(IScenarioWorkspace workspace) => workspace.Directory("a").File("b.json");
                public string Name(WorkspaceFile file) => file.Path + ProcessGate.LimitVariable;
            }
            """";

        BlackBoxRules.DriverUseViolations("Steps/GenerationSteps.cs", source).Should().BeEmpty();
    }

    [Test]
    public void ABypassAfterACommentAndAStringOnTheSameLineIsFound() =>
        Only(BlackBoxRules.DriverUseViolations("Steps/A.cs", "var a = \"File.\"; /* File. */ File.Delete(a); // File.\n"))
            .Should().StartWith("Steps/A.cs:1: use-drivers: 'File.'");

    // The step table is current.

    private const string Readme = """
        # Black-box tests

        ## Steps

        | Capability | Step |
        | --- | --- |
        | CLI | `When I run the command {string}` |
        | CLI | `Then the exit code is {int}` |

        ## Other

        | Tag | Meaning |
        | --- | --- |
        | `@smoke` | A smoke test. |
        """;

    private const string StepSource = """
        [Binding]
        public sealed class CliSteps
        {
            [When("I run the command {string}")]
            public void Run(string command) { }

            [Then("the exit code is {int}")]
            [Then(@"the exit code should be {int}")]
            public void ExitCode(int code) { }
        }
        """;

    [Test]
    public void TheStepsOfTheSourcesAreRead() =>
        BlackBoxRules.StepPatterns(StepSource).Should().Equal(
            "When I run the command {string}", "Then the exit code is {int}", "Then the exit code should be {int}");

    [Test]
    public void AStepTableThatHasEachStepOfTheSourcesPasses() =>
        BlackBoxRules.StepTableViolations("README.md", Readme, [("Steps/CliSteps.cs", StepSource.Replace("    [Then(@\"the exit code should be {int}\")]\n", ""))])
            .Should().BeEmpty();

    [Test]
    public void AStepThatIsNotInTheTableAndARowWithNoStepAreFound()
    {
        var source = StepSource.Replace("I run the command {string}", "I run {string}");

        var violations = BlackBoxRules.StepTableViolations("README.md", Readme, [("Steps/CliSteps.cs", source)]).Select(violation => violation.ToString());

        violations.Should().BeEquivalentTo(
            "README.md:1: step-table: the step table has 'When I run the command {string}', and no step class has that step.",
            "Steps/CliSteps.cs:1: step-table: the step 'When I run {string}' is not in the step table of README.md.",
            "Steps/CliSteps.cs:1: step-table: the step 'Then the exit code should be {int}' is not in the step table of README.md.");
    }

    [Test]
    public void TwoStepsWithTheSamePatternAreFound()
    {
        var other = "[Binding]\npublic sealed class OtherSteps\n{\n    [Then(\"the exit code is {int}\")]\n    public void Same(int code) { }\n}\n";

        var violations = BlackBoxRules.StepTableViolations(
            "README.md", Readme, [("Steps/CliSteps.cs", StepSource.Replace("    [Then(@\"the exit code should be {int}\")]\n", "")), ("Steps/OtherSteps.cs", other)]);

        Only(violations).Should().Be("Steps/OtherSteps.cs:1: step-table: the step 'Then the exit code is {int}' is also in Steps/CliSteps.cs. Reuse the step.");
    }

    // Each pull request test has one run that supports it.

    private static PullRequestRun Run(string lane, string name, params (string Host, string[] Capabilities)[] hosts) =>
        new(lane, name, hosts[0].Host, hosts.ToImmutableDictionary(host => host.Host, host => host.Capabilities.ToImmutableHashSet()));

    private static readonly PullRequestRun Linux = Run("none", "the development build on Linux",
        ("native", ["needs-generation", "needs-native-engine"]), ("managed", ["needs-generation", "needs-in-process-fsharp-frontend"]));

    private static readonly PullRequestRun Windows = Run("none", "the development build on Windows",
        ("native", ["needs-generation", "needs-job-object"]), ("managed", ["needs-generation"]));

    private static readonly PullRequestRun Packages = Run("packages", "the packages run", ("native", ["needs-generation"]));

    [Test]
    public void ATestThatOneRunOfItsLaneSupportsPasses() =>
        BlackBoxRules.RunSupportViolations(
            [
                Test("Default host", "smoke", "needs-generation"),
                Test("Only on Windows", "smoke", "needs-job-object"),
                Test("Managed", "smoke", "host-managed", "needs-in-process-fsharp-frontend"),
                Test("Packages", "e2e", "lane", "packages", "needs-generation"),
            ],
            [Linux, Windows, Packages]).Should().BeEmpty();

    [Test]
    public void ATestWithTwoCapabilitiesThatNoOneRunSupportsTogetherIsFoundThoughEachCapabilityHasARun() =>
        Only(BlackBoxRules.RunSupportViolations([Test("Two capabilities", "smoke", "needs-native-engine", "needs-job-object")], [Linux, Windows]))
            .Should().Be("Smoke/A.feature:3: pull-request-run: no pull request run supports 'Two capabilities' (host native; @needs-native-engine, @needs-job-object). " +
                "the development build on Linux does not support @needs-job-object; the development build on Windows does not support @needs-native-engine.");

    [Test]
    public void ATestThatNamesAHostThatItsPullRequestRunDoesNotHaveIsFound() =>
        Only(BlackBoxRules.RunSupportViolations([Test("Tool host", "smoke", "host-tool")], [Linux]))
            .Should().Be("Smoke/A.feature:3: pull-request-run: no pull request run supports 'Tool host' (host tool). the development build on Linux does not have the host tool.");

    [Test]
    public void ATestOfThePackagesLaneThatNeedsACapabilityThatThePackagesRunDoesNotSupportIsFound() =>
        Only(BlackBoxRules.RunSupportViolations([Test("Package test", "e2e", "lane", "packages", "needs-in-process-fsharp-frontend")], [Linux, Packages]))
            .Should().Be("Smoke/A.feature:3: pull-request-run: no pull request run supports 'Package test' (host native; @needs-in-process-fsharp-frontend). " +
                "the packages run does not support @needs-in-process-fsharp-frontend.");

    [Test]
    public void ATestOfALaneWithNoPullRequestRunIsFound() =>
        Only(BlackBoxRules.RunSupportViolations([Test("Native AOT test", "smoke", "lane", "native-aot")], [Linux, Packages]))
            .Should().Be("Smoke/A.feature:3: pull-request-run: no pull request run executes the lane native-aot, so no pull request runs 'Native AOT test'.");

    [Test]
    public void ATestOfThePublishedLaneIsATestOfTheReleaseAndManualWorkflows() =>
        BlackBoxRules.RunSupportViolations([Test("Published", "e2e", "lane", "published", "host-tool", "needs-job-object")], [Linux]).Should().BeEmpty();

    // Each group with a test is a suite of the plan, and a suite has a test.

    private static readonly TestSuite SmokeSuite = TestSuite.TUnit("Morphir.BlackBox.Tests").InGroup("smoke");
    private static readonly TestSuite JourneySuite = TestSuite.TUnit("Morphir.BlackBox.Tests").InGroup("journey");

    [Test]
    public void AGroupWithATestAndASuitePasses() =>
        BlackBoxRules.SuiteViolations([Test("One", "smoke")], [TestSuite.TUnit("A.Tests"), SmokeSuite], "Morphir.BlackBox.Tests").Should().BeEmpty();

    [Test]
    public void ARegisteredSuiteWithNoTestIsFound() =>
        Only(BlackBoxRules.SuiteViolations([Test("One", "smoke"), Test("Lane only", "journey", "lane", "packages")], [SmokeSuite, JourneySuite], "Morphir.BlackBox.Tests"))
            .Should().Be("build/Helpers/TestPlan.cs:1: group-suite: the suite of the group journey selects no test: no test with no lane has @journey. A run of a group with no test fails.");

    [Test]
    public void AGroupWithATestAndNoSuiteIsFound() =>
        Only(BlackBoxRules.SuiteViolations([Test("One", "smoke"), Test("Two", "integration")], [SmokeSuite], "Morphir.BlackBox.Tests"))
            .Should().Be("Smoke/A.feature:3: group-suite: 'Two' has the group @integration, and the test plan has no suite for that group. Add TestSuite.TUnit(\"Morphir.BlackBox.Tests\").InGroup(\"integration\") to TestPlan.Suites.");

    // The repository.

    private static string Root => TestFixture.GetRepositoryRoot();

    private static string ProjectDirectory => Path.Combine(Root, "tests", "Morphir.BlackBox.Tests");

    private static IEnumerable<(string File, string Text)> ProjectFiles(string extension) =>
        Directory.EnumerateFiles(ProjectDirectory, "*" + extension, SearchOption.AllDirectories)
            .Select(path => (File: Path.GetRelativePath(ProjectDirectory, path).Replace(Path.DirectorySeparatorChar, '/'), Path: path))
            .Where(file => !file.File.StartsWith("bin/", StringComparison.Ordinal) && !file.File.StartsWith("obj/", StringComparison.Ordinal))
            .OrderBy(file => file.File, StringComparer.Ordinal)
            .Select(file => (file.File, File.ReadAllText(file.Path)));

    private static ImmutableArray<BlackBoxTest> RepositoryTests() =>
    [
        .. ProjectFiles(".feature").SelectMany(file => BlackBoxRules.ParseFeature(file.File, file.Text)),
        .. ProjectFiles(".cs").SelectMany(file => BlackBoxRules.ParseFrameworkTests(file.File, file.Text)),
    ];

    /// <summary>
    /// The pull request runs, from the capabilities of the drivers. The development build runs on each operating
    /// system with the same hosts and capabilities, so it is one row. Add the packages run and the Native AOT run
    /// in the change that adds the run to the build.
    /// </summary>
    private static ImmutableArray<PullRequestRun> PullRequestRuns()
    {
        var target = new TestTarget.DevelopmentBuild();
        var capabilities = target.Hosts.ToImmutableDictionary(
            host => host.Name(),
            host => DriverCapabilities.Of(target, host).Select(DriverCapabilities.TagOf).ToImmutableHashSet());
        return
        [
            new PullRequestRun(TestLane.None.Name(), "the development build (each operating system)", target.DefaultHost.Name(), capabilities),
            // The step of the Linux job that runs TestAot, on the development build.
            new PullRequestRun(TestLane.NativeAot.Name(), "the Native AOT step (Linux)", target.DefaultHost.Name(), capabilities),
        ];
    }

    [Test]
    public void TheTagsOfTheRulesAreTheTagsOfTheTestSupportLibrary()
    {
        BlackBoxRules.LaneTags.Should().Equal(TestLanes.All.Where(lane => lane != TestLane.None).Select(TestLanes.Name));
        BlackBoxRules.LaneMarker.Should().Be(TestNeeds.LaneTag).And.Be(TestPlan.LaneMarker);
        BlackBoxRules.HostTagPrefix.Should().Be(TestNeeds.HostTagPrefix);
        BlackBoxRules.CapabilityTagPrefix.Should().Be(DriverCapabilities.TagPrefix);
        BlackBoxRules.Folders.Keys.Should().BeEquivalentTo(TestPlan.Groups);
    }

    [Test]
    public void TheBlackBoxProjectHasTests() => RepositoryTests().Should().NotBeEmpty();

    [Test]
    public void EachTestOfTheBlackBoxProjectHasOneGroupInTheFolderOfItsGroup() =>
        BlackBoxRules.GroupViolations(RepositoryTests()).Should().BeEmpty();

    [Test]
    public void EachTestOfTheBlackBoxProjectHasAtMostOneLane() =>
        BlackBoxRules.LaneViolations(RepositoryTests()).Should().BeEmpty();

    [Test]
    public void TheBlackBoxProjectMakesNoProductAssemblyVisible()
    {
        var project = File.ReadAllText(Path.Combine(ProjectDirectory, "Morphir.BlackBox.Tests.csproj"));
        var targets = File.ReadAllText(Path.Combine(Root, "tests", "Morphir.TestSupport", "build", "Morphir.CliArtifacts.targets"));
        var programs = File.ReadAllText(Path.Combine(Root, "tests", "Morphir.TestSupport", "build", "Morphir.TestPrograms.targets"));

        project.Should().Contain("Morphir.CliArtifacts.targets", "the build of the project builds the CLI entry points first");
        BlackBoxRules.ReferenceViolations($"{Project}/Morphir.BlackBox.Tests.csproj", project).Should().BeEmpty();
        BlackBoxRules.ReferenceViolations("tests/Morphir.TestSupport/build/Morphir.CliArtifacts.targets", targets).Should().BeEmpty();
        // The programs of a test (hosts, probes, morphirup) are build-order references too.
        BlackBoxRules.ProjectReferences("tests/Morphir.TestSupport/build/Morphir.TestPrograms.targets", programs)
            .Should().ContainSingle().Which.ExposesAssembly.Should().BeFalse();
        BlackBoxRules.ProjectReferences($"{Project}/Morphir.BlackBox.Tests.csproj", project).Select(reference => reference.Path)
            .Should().Equal("tests/Morphir.TestSupport/Morphir.TestSupport.csproj");
    }

    [Test]
    public void TheRestoredDependenciesOfTheBlackBoxProjectHaveNoProductProject()
    {
        // The restore of the project writes this file. The build tests run after the solution build.
        var assets = Path.Combine(ProjectDirectory, "obj", "project.assets.json");

        File.Exists(assets).Should().BeTrue($"{assets} is there after a restore of the solution");
        BlackBoxRules.AssetsViolations($"{Project}/obj/project.assets.json", File.ReadAllText(assets), Project).Should().BeEmpty();
    }

    [Test]
    public void TheSourcesOfTheBlackBoxProjectUseTheDrivers() =>
        ProjectFiles(".cs").SelectMany(file => BlackBoxRules.DriverUseViolations(file.File, file.Text)).Should().BeEmpty();

    [Test]
    public void TheStepTableOfTheReadmeIsCurrent() =>
        BlackBoxRules.StepTableViolations(
            "README.md",
            File.ReadAllText(Path.Combine(ProjectDirectory, "README.md")),
            ProjectFiles(".cs").Where(file => file.File.StartsWith("Steps/", StringComparison.Ordinal))).Should().BeEmpty();

    [Test]
    public void EachPullRequestTestHasOneRunThatSupportsIt() =>
        BlackBoxRules.RunSupportViolations(RepositoryTests(), PullRequestRuns()).Should().BeEmpty();

    [Test]
    public void EachGroupWithATestIsASuiteOfThePlanAndEachSuiteHasATest() =>
        BlackBoxRules.SuiteViolations(RepositoryTests(), TestPlan.Suites, TestPlan.BlackBoxProject).Should().BeEmpty();
}
