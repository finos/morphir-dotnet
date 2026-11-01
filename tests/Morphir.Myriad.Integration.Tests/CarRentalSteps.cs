using System.Diagnostics;
using Reqnroll;

namespace Morphir.Myriad.Integration.Tests;

[Binding]
public sealed class CarRentalSteps
{
    private static readonly string packageWorkspace = Path.Combine(Path.GetTempPath(), "morphir BDD package " + Guid.NewGuid().ToString("N"));
    private static readonly Lazy<Task<PackageArtifact>> package = new(PackPlugin);
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "morphir car rental " + Guid.NewGuid().ToString("N"));
    private string model = "";
    private string output = "";
    private bool fullJourney;
    private string buildDiagnostics = "";

    private sealed class DotnetFailureException(string message) : Exception(message);
    private sealed record PackageArtifact(string Feed, string Version);

    private static async Task<PackageArtifact> PackPlugin()
    {
        var root = FindRoot();
        var feed = Path.Combine(packageWorkspace, "feed");
        var version = "0.1.0-test." + Guid.NewGuid().ToString("N");
        await Run(root, "pack", Path.Combine(root, "src", "Morphir.Myriad", "Morphir.Myriad.fsproj"),
            "-c", "Release", "-o", feed, "-p:PackageVersion=" + version);
        return new PackageArtifact(feed, version);
    }

    [Given("a classic car rental model with a daily rate of 5000 cents")]
    public void GivenDailyRate()
    {
        model = ReadResource("daily-rate-v3.json");
    }

    [Given("the supplied car rental model in version {int}")]
    public void GivenFullModel(int version)
    {
        model = ReadResource($"car-rental-v{version}.json");
        fullJourney = true;
    }

    [Given("the model's daily rate changes to {int} cents")]
    [Then("the model's daily rate changes to {int} cents")]
    public void GivenRateChange(int rate)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(model)!;
        var modules = node["distribution"]![3]!["modules"]!.AsArray();
        var inventory = modules.Single(m => m![0]![0]![0]!.GetValue<string>() == "inventory")!;
        var value = inventory[1]!["value"]!["values"]!.AsArray()
            .Single(v => v![0]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(new[] { "daily", "rate" }))!;
        value[1]!["value"]!["value"]!["body"]![2]![1] = rate;
        model = node.ToJsonString();
    }

    [When("a clean F# consumer builds and runs through the packaged plugin")]
    public async Task WhenBuildAndRun()
    {
        Directory.CreateDirectory(workspace);
        var root = FindRoot();
        var artifact = await package.Value;
        var feed = artifact.Feed;
        var packageVersion = artifact.Version;
        var consumer = Path.Combine(workspace, "consumer");
        Directory.CreateDirectory(consumer);
        await File.WriteAllTextAsync(Path.Combine(consumer, "model.json"), model);
        await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.fsproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Morphir.Myriad" Version="{packageVersion}" /></ItemGroup>
              <ItemGroup><MorphirModel Include="model.json" /><Compile Include="Program.fs" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(consumer, "NuGet.config"), $"""
            <configuration><packageSources><clear /><add key="local" value="{System.Security.SecurityElement.Escape(feed)}" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
            """);
        await File.WriteAllTextAsync(Path.Combine(consumer, "Program.fs"), fullJourney
            ? File.ReadAllText(Path.Combine(root, "examples", "car-rental", "Program.fs"))
            : "printfn \"%d\" Car.Rental.Inventory.dailyRate\n");
        await Run(consumer, "build", "Consumer.fsproj", "-c", "Release", "--nologo");
        output = await Run(consumer, "run", "--project", "Consumer.fsproj", "-c", "Release", "--no-build");
    }

    [When("the existing consumer rebuilds and runs")]
    public async Task WhenRebuildAndRun()
    {
        var consumer = Path.Combine(workspace, "consumer");
        await File.WriteAllTextAsync(Path.Combine(consumer, "model.json"), model);
        await Run(consumer, "build", "Consumer.fsproj", "-c", "Release", "--no-restore", "--nologo");
        output = await Run(consumer, "run", "--project", "Consumer.fsproj", "-c", "Release", "--no-build");
    }

    [When("the model changes to invalid input {string}")]
    public async Task WhenInvalidInput(string input)
    {
        if (input == "malformed")
        {
            model = ReadResource("malformed-model.txt");
        }
        else
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(model)!.AsObject();
            switch (input)
            {
                case "missing": node.Remove("formatVersion"); break;
                case "string": node["formatVersion"] = "3"; break;
                case "fractional": node["formatVersion"] = 3.5; break;
                case "zero": node["formatVersion"] = 0; break;
                case "future": node["formatVersion"] = 4; break;
                case "non-library": node["distribution"]![0] = "DocumentTree"; break;
                default: throw new ArgumentException($"Unknown invalid-input fixture '{input}'.");
            }
            model = node.ToJsonString();
        }
        await File.WriteAllTextAsync(Path.Combine(workspace, "consumer", "model.json"), model);
    }

    [When("the existing consumer build fails")]
    public async Task WhenBuildFails()
    {
        try
        {
            await Run(Path.Combine(workspace, "consumer"), "build", "Consumer.fsproj", "-c", "Release", "--no-restore", "--nologo");
        }
        catch (DotnetFailureException error)
        {
            buildDiagnostics = error.Message;
            return;
        }
        throw new InvalidOperationException("Invalid model input unexpectedly compiled stale generated output.");
    }

    [Then("the build diagnostic includes {string} and the model path")]
    public void ThenBuildDiagnostic(string category)
    {
        if (!buildDiagnostics.Contains(category, StringComparison.Ordinal) || !buildDiagnostics.Contains("model.json", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{category}' and model path in:\n{buildDiagnostics}");
        }
    }

    [Then("the consumer reports a complete rental costing {int} cents")]
    public void ThenJourney(int cents)
    {
        foreach (var expected in new[] { "inventory=Available", "reservation=Reserved", "pickup=Rented", "return=Available", $"invoice={cents}" })
        {
            if (!output.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Missing '{expected}' in consumer output:\n{output}");
            }
        }
    }

    [Then("the consumer rejects invalid rental operations")]
    public void ThenRejects()
    {
        if (!output.Contains("rejections=7", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected seven verified business-rule rejections:\n{output}");
        }
    }

    [Then("the consumer prints {string}")]
    public void ThenPrints(string expected)
    {
        if (output.Trim() != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{output}'.");
        }
    }

    [AfterScenario]
    public void Cleanup()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, true);
        }
    }

    [AfterTestRun]
    public static void CleanupPackage()
    {
        if (Directory.Exists(packageWorkspace))
        {
            Directory.Delete(packageWorkspace, true);
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Morphir.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Cannot locate Morphir.slnx.");
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(CarRentalSteps).Assembly.GetManifestResourceStream("Morphir.Tests.Fixtures." + name)
            ?? throw new InvalidOperationException($"Missing model resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<string> Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("dotnet exceeded the consumer-build timeout.");
        }

        var result = await stdout;
        var diagnostics = await stderr;
        if (process.ExitCode != 0)
        {
            throw new DotnetFailureException($"dotnet {string.Join(' ', arguments)} failed with {process.ExitCode}:\n{result}\n{diagnostics}");
        }

        return result;
    }
}
