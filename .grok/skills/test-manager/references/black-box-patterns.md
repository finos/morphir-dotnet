# Black-box patterns

Patterns for `tests/Morphir.BlackBox.Tests`. The types are in `tests/Morphir.TestSupport/Drivers` (see `tests/Morphir.TestSupport/README.md`).

## A feature

```gherkin
@integration @needs-generation
Feature: Generate C# from a Morphir model

  Scenario: An input that is not there is an operational failure
    Given no model file "missing.json"
    When I generate "csharp" from the model
    Then the exit code is 2
    And the generation report says that the generation failed with the kind "operational"
    And the output directory is not there
```

- Put the group tag on the feature when each scenario has the same group.
- A step states an intent (`I generate "csharp" from the model`), not a command line. Use `When I run the command {string}` only when the command line is the subject of the test.
- A capability with no driver interface yet (the interface comes with its second scenario) has a step class of its own. Its step states the intent and gives the arguments to `ICliDriver.RunAsync`.
- Use a scenario outline for rows of data, and a table for a list.
- A scenario makes no assumption about another scenario. Each one has its own workspace.

## A step class

```csharp
[Binding]
public sealed class VerificationSteps(IModelVerificationDriver verification, ScenarioRun run)
{
    [When("I verify the model")]
    public async Task VerifyTheModel() => run.Result = await verification.VerifyAsync(run.Model);

    [Then("the verification report says that the model is valid")]
    public void TheModelIsValid() => VerificationReport.Of(run.Result).ShouldBeValid();
}
```

- One step class for each capability, in `Steps/`. The four groups share the steps.
- A step class gets its drivers, the `IScenarioWorkspace` and the `ScenarioRun` through its constructor. The hook `Hooks/ScenarioHooks.cs` registers them.
- `ScenarioRun` holds the state that the step classes share: the model and the result of the last command.
- A file comes from the workspace: `workspace.CopyFixture("Models/valid-ir-v3.json")`, `workspace.File("out", "a.json")`. Read and write it with the workspace (`ReadText`, `WriteText`, `FilesIn`). Do not use `File`, `Directory`, `Path.GetTempPath`, `Environment` or a process type: the guard test finds them.
- A failed check throws a `CommandAssertionException` with the result, so the message has the command line, the exit code and both streams.
- Add each new step to the step table of `tests/Morphir.BlackBox.Tests/README.md`.

## A framework test

```csharp
[Category("integration")]
public sealed class CorpusTests : BlackBoxTest
{
    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task EachCaseOfTheCorpusVerifies(string fixture)
    {
        var model = Workspace.CopyFixture(fixture);
        (await Drivers.Verification.VerifyAsync(model)).ShouldExitWith(0);
    }
}
```

- Only for a loop over a corpus, a property test or a test with a time budget.
- The class derives from `BlackBoxTest` and has the group as a category. `Drivers` and `Workspace` are there while the test runs.
- A lane, a host or a capability is a category with the same name as the tag, without `@`, on the class or on the test method: `[Category("host-managed")]`.

## A fixture

- Put a fixture file below `tests/Morphir.BlackBox.Tests/Fixtures/`. The build copies the folder to the output.
- When the file has one source in the repository (an example, a shared fixture below `tests/Fixtures/`), link it in the project file. Do not copy it.
- When a program made the file, add a `README.md` beside it that says how to make it again. Example: `Fixtures/WireForms/README.md`.

## How to add a capability

1. Add a case to `DriverCapability` (`tests/Morphir.TestSupport/Drivers/TestEligibility.cs`). Its tag is `needs-` and the name in lower case with hyphens.
2. Add the capability to `DriverCapabilities.Of` for each target and host whose drivers support it.
3. Add the driver interface, its implementation on `ICliDriver`, and its report type. Write the tests first, in `tests/Morphir.TestSupport.Tests/Drivers`: the command line with a path that has a space, and the report type against a real report of the command.
4. Add the driver to `DriverSet` and register it in `Hooks/ScenarioHooks.cs`.
5. Add the tag to `tests/Morphir.TestSupport/README.md` and to `tests/Morphir.BlackBox.Tests/README.md`.

## How to add a target

1. Add a case to `TestTarget` with its name, its hosts and its default host. Add tests for the row in `TestRunSettingsTests`.
2. Resolve its artifact in `BlackBoxSession`: the artifact of its own form, or an error with the command that makes it. Do not use the artifact of another form.
3. Add its capabilities to `DriverCapabilities.Of`.
4. When a pull request runs the target, add its run to `PullRequestRuns` in `tests/Morphir.Build.Tests/Helpers/BlackBoxRulesTests.cs`.

## How to add a group to the plan

A group is in `TestPlan.Suites` (`build/Helpers/TestPlan.cs`) from the change that adds its first test with no lane:

```csharp
TestSuite.TUnit(BlackBoxProject).InGroup("e2e"),
```

A group with no test fails its run, and the guard test `EachGroupWithATestIsASuiteOfThePlanAndEachSuiteHasATest` finds both errors.
