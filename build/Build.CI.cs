using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Morphir.Build.Helpers;
using Fallout.Common;
using Fallout.Common.Tools.DotNet;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for CI/CD workflows and local simulation of GitHub Actions pipeline
/// </summary>
partial class Build
{
    /// <summary>
    /// Simulates the lint job from development.yml workflow
    /// Runs: Restore -> Lint (format check)
    /// </summary>
    Target CILint => _ => _
        .DependsOn(Restore, Lint)
        .Description("Run lint checks (simulates GitHub Actions lint job)")
        .Executes(() =>
        {
            Serilog.Log.Information("✓ Lint checks completed successfully");
        });

    /// <summary>
    /// Simulates the test job from development.yml workflow
    /// Runs: Restore -> Compile -> Test
    /// This is the core build and test sequence that runs on all platforms
    /// </summary>
    Target CITest => _ => _
        .DependsOn(Restore, Compile, Test)
        .Description("Run build and tests (simulates GitHub Actions test job)")
        .Executes(() =>
        {
            Serilog.Log.Information("✓ Build and test completed successfully");
        });

    /// <summary>
    /// Simulates the complete GitHub Actions development.yml workflow locally
    /// Runs: CILint -> CITest -> aggregation
    /// This allows developers to verify their changes will pass CI before pushing
    /// </summary>
    Target DevWorkflow => _ => _
        .DependsOn(CILint, CITest)
        .Description("Run complete development workflow (simulates entire GitHub Actions PR build)")
        .Executes(() =>
        {
            Serilog.Log.Information("═══════════════════════════════════════");
            Serilog.Log.Information("✓ All CI checks passed!");
            Serilog.Log.Information("═══════════════════════════════════════");
            Serilog.Log.Information("Your changes are ready for PR submission");
            Serilog.Log.Information("");
            Serilog.Log.Information("What just ran:");
            Serilog.Log.Information("  ✓ Lint (code formatting)");
            Serilog.Log.Information("  ✓ Build (compile all projects)");
            Serilog.Log.Information("  ✓ Test (unit tests)");
            Serilog.Log.Information("");
            Serilog.Log.Information("This simulates the GitHub Actions workflow:");
            Serilog.Log.Information("  .github/workflows/development.yml");
        });

    [Parameter("CIPlan: the os input of a manual or label-dispatched run (auto, linux, full, or a comma-separated list of windows and macos)")]
    readonly string CiOs = "";

    [Parameter("CIPlan: the pull request number of a manual or label-dispatched run")]
    readonly string CiPullRequest = "";

    /// <summary>
    /// The plan job of development.yml. Selects the platforms of this run from the event (pull request draft state,
    /// the ci:windows, ci:macos, ci:full and ci:linux-only labels, the os input of a dispatched run), and skips the
    /// checks that passed in an earlier development.yml run for the same head commit. Writes the plan to
    /// GITHUB_OUTPUT, the selection to the job summary and a notice annotation to the run page.
    /// </summary>
    Target CIPlan => _ => _
        .Description("Select the CI checks of this GitHub Actions run and write them to GITHUB_OUTPUT")
        .Executes(() =>
        {
            var (trigger, headSha) = ReadCiTrigger();
            var request = CiPolicy.Resolve(trigger);
            var passed = request.ReuseEvidence && headSha is { Length: > 0 }
                ? PassedChecksFor(headSha)
                : [];
            var plan = CiPlan.Create(request, passed);

            var notice = CiReport.PlanNotice(plan);
            Serilog.Log.Information("CI plan for {Sha}: {Notice}", headSha ?? "-", notice);
            // GitHub Actions reads workflow commands from stdout. The notice shows on the run page.
            Console.WriteLine($"::notice title=CI plan::{notice}");
            AppendGitHubFile("GITHUB_OUTPUT", string.Concat(plan.Outputs().Select(output => $"{output.Key}={output.Value}\n")));
            AppendGitHubFile("GITHUB_STEP_SUMMARY", CiReport.PlanSummary(plan));
        });

    /// <summary>
    /// The ci aggregate job of development.yml. Reads the needs context from CI_NEEDS. Fails when a planned job did not
    /// succeed, or when a pull request that is ready for review does not have the full OS matrix.
    /// </summary>
    Target CIGate => _ => _
        .Description("Evaluate the aggregate ci check of a development.yml run from CI_NEEDS")
        .Executes(() =>
        {
            var needs = Environment.GetEnvironmentVariable("CI_NEEDS");
            if (string.IsNullOrWhiteSpace(needs)) throw new Exception("CI_NEEDS is not set. The ci job passes toJSON(needs).");

            var evaluation = CiGate.FromNeeds(needs);
            var verdict = evaluation.Verdict;
            AppendGitHubFile("GITHUB_STEP_SUMMARY", CiReport.GateSummary(evaluation.Plan, verdict, evaluation.Results));

            // GitHub Actions reads workflow commands from stdout.
            var command = verdict.Outcome == CiOutcome.Fail ? "error" : "warning";
            foreach (var message in verdict.Messages) Console.WriteLine($"::{command} title=ci::{message}");
            Serilog.Log.Information("ci: {Outcome}. Selection: {Selection}", verdict.Outcome, evaluation.Plan?.Selection ?? "unknown");

            if (verdict.Outcome == CiOutcome.Fail)
                throw new Exception($"The ci check failed: {string.Join(" ", verdict.Messages)}");
        });

    (CiTrigger Trigger, string? HeadSha) ReadCiTrigger()
    {
        var eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME");
        switch (eventName)
        {
            case "pull_request":
                {
                    var path = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH")
                        ?? throw new Exception("GITHUB_EVENT_PATH is not set for a pull_request event.");
                    var pr = GitHubCi.PullRequestFromEvent(File.ReadAllText(path));
                    return (pr.Trigger, pr.HeadSha);
                }
            case "workflow_dispatch":
                {
                    if (!CiPolicy.TryParseOsChoice(CiOs, out var choice))
                        throw new Exception($"The os input '{CiOs}' is not auto, linux, full, or a comma-separated list of windows and macos.");
                    var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
                    if (CiPullRequest.Length == 0) return (new CiTrigger.Dispatch(choice, null), sha);
                    if (!int.TryParse(CiPullRequest, out var number) || number <= 0)
                        throw new Exception($"The pr input '{CiPullRequest}' is not a pull request number.");

                    var pr = GitHubCi.PullRequestFromApi(GitHubGet($"pulls/{number}"));
                    if (pr.HeadSha != sha)
                    {
                        // The branch moved after the dispatch. Its push event starts its own run, so reuse nothing here.
                        Serilog.Log.Warning("Pull request #{Number} head is {Head}, but this run is for {Sha}. No earlier jobs are reused.",
                            number, pr.HeadSha, sha);
                        return (new CiTrigger.Dispatch(choice, pr.Trigger), null);
                    }

                    return (new CiTrigger.Dispatch(choice, pr.Trigger), pr.HeadSha);
                }
            default:
                return (new CiTrigger.Release(), null);
        }
    }

    /// <summary>The checks that succeeded in other development.yml runs for the head commit.</summary>
    IEnumerable<CiEvidence> PassedChecksFor(string headSha)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
        {
            Serilog.Log.Warning("GITHUB_TOKEN is not set, so no earlier jobs are reused.");
            return [];
        }

        var currentRun = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "";
        var runs = GitHubCi.RunIds([GitHubGet($"actions/workflows/development.yml/runs?head_sha={headSha}&per_page=100")])
            .Where(id => id.ToString() != currentRun);
        return GitHubCi.PassedChecks(runs.Select(id => GitHubGet($"actions/runs/{id}/jobs?filter=latest&per_page=100")));
    }

    static string GitHubGet(string relativePath)
    {
        var api = Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com";
        var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
            ?? throw new Exception("GITHUB_REPOSITORY is not set.");
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("morphir-dotnet-ci", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetStringAsync($"{api}/repos/{repository}/{relativePath}").GetAwaiter().GetResult();
    }

    static void AppendGitHubFile(string variable, string text)
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } path) File.AppendAllText(path, text);
    }
}
