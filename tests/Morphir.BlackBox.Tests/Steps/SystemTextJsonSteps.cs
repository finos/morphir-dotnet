// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of a consumer of the System.Text.Json plugin package (<c>Morphir.Myriad.SystemTextJson</c>) and its
/// annotations package: an F# project with one JSON context (<c>FSharpJsonContext</c>) on its file <c>Types.fs</c>.
/// </summary>
[Binding]
public sealed class SystemTextJsonSteps(IPackageDriver packages, IScenarioWorkspace workspace, PackageRun state)
{
    private const string Plugin = "Morphir.Myriad.SystemTextJson";
    private const string Annotations = "Morphir.Myriad.SystemTextJson.Annotations";

    private ConsumerProject Consumer => state.Consumer;

    /// <summary>
    /// The consumer: the context <c>AppJson</c> in the namespace <c>Consumer.Json</c>, a record and a union in
    /// <c>Types.fs</c>, and a program that writes one value of each as JSON.
    /// </summary>
    [Given("a System.Text.Json consumer")]
    public void ASystemTextJsonConsumer()
    {
        state.Consumer = new ConsumerProject(workspace.Directory("consumer")) { Strict = true };
        Consumer.Packages.Add((Plugin, state.VersionOf(Plugin), true));
        Consumer.Packages.Add((Annotations, state.VersionOf(Annotations), false));
        Consumer.Sources.Add("Types.fs");
        Consumer.Context = [("Name", "AppJson"), ("Namespace", "Consumer.Json")];
        packages.WriteNuGetConfig(Consumer.Folder, includeNuGetOrg: true);
        workspace.WriteText(Consumer.File("Types.fs"), """
            namespace Consumer

            open Morphir.Myriad.SystemTextJson

            type Person = { Name: string; Age: int }

            [<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), TagName = "kind")>]
            type Shape =
                | Circle of radius: float
                | Square of side: float

            """);
        workspace.WriteText(Consumer.File("Program.fs"), """
            open System.Text.Json
            open Consumer
            open Consumer.Json

            printfn "%s" (JsonSerializer.Serialize({ Name = "Ada"; Age = 36 }, AppJson.Default.Person))
            printfn "%s" (JsonSerializer.Serialize(Circle 1.5, AppJson.Default.Shape))

            """);
        Save();
    }

    [Given("the context of the consumer has the name {string}")]
    public void TheContextHasTheName(string name) => SetContext("Name", name);

    /// <summary>More metadata of the context item: the columns <c>metadata</c> and <c>value</c>.</summary>
    [Given("the context of the consumer has the metadata:")]
    public void TheContextHasTheMetadata(Table metadata)
    {
        foreach (var row in metadata.Rows) SetContext(row["metadata"], row["value"]);
    }

    private void SetContext(string name, string value)
    {
        var context = Consumer.Context ?? throw new InvalidOperationException("The consumer has no JSON context.");
        var index = context.FindIndex(item => item.Name == name);
        if (index >= 0) context[index] = (name, value);
        else context.Add((name, value));
        Save();
    }

    private void Save() => workspace.WriteText(Consumer.File(ConsumerProject.ProjectFile), Consumer.Render());
}
