// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Cli;

namespace Morphir.Tooling.Tests.Cli;

/// <summary>
/// The host's shared parse-error path (adopt-streaming-json, cli-json-output scenario "A separated mode is refused with
/// a hint"): <c>MorphirCliApp</c> computes the messages once, with the <c>--json=&lt;mode&gt;</c> hint, for a command
/// that renders its own parse errors and for one that leaves them to System.CommandLine.
/// </summary>
public sealed class MorphirCliParseErrorTests
{
    /// <summary>A command with <c>--json</c> and no renderer of its own, as <c>extension list</c> is.</summary>
    private static Command WithoutRenderer()
    {
        var command = new Command("probe") { JsonOutputOption.Create() };
        command.SetAction(_ => 0);
        return command;
    }

    /// <summary>A command with <c>--json</c> that renders its own parse errors and keeps the messages it was given.</summary>
    private sealed class WithRenderer : Command, IMorphirParseErrorHandler
    {
        public WithRenderer() : base("probe")
        {
            Options.Add(JsonOutputOption.Create());
            SetAction(_ => 0);
        }

        public IReadOnlyList<string>? Messages { get; private set; }

        public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages)
        {
            Messages = messages;
            return 2;
        }
    }

    private static async Task<(int Exit, string Error)> RunAsync(Command command, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var app = MorphirCli.CreateBuilder(args)
            .AddCommand(command)
            .ConfigureServices(services => services.AddSingleton(new InvocationConfiguration { Output = output, Error = error }))
            .Build();
        var exit = await app.RunAsync();
        return (exit, error.ToString());
    }

    [Test]
    public async Task ASeparatedModeIsRefusedWithAHintOnACommandWithoutItsOwnRenderer()
    {
        var (exit, error) = await RunAsync(WithoutRenderer(), "probe", "--json", "document");

        exit.Should().NotBe(0);
        error.Should().Contain("'document'").And.Contain("--json=document");
    }

    [Test]
    public async Task AnArgumentThatNamesNoModeGetsNoHintOnACommandWithoutItsOwnRenderer()
    {
        var (exit, error) = await RunAsync(WithoutRenderer(), "probe", "--json", "model.json");

        exit.Should().NotBe(0);
        error.Should().Contain("'model.json'").And.NotContain("--json=");
    }

    [Test]
    public async Task ACommandThatRendersItsOwnParseErrorsIsGivenTheMessagesWithTheHint()
    {
        var command = new WithRenderer();

        var (exit, _) = await RunAsync(command, "probe", "--json", "document");

        exit.Should().Be(2);
        command.Messages.Should().ContainSingle().Which.Should().Contain("'document'").And.Contain("--json=document");
    }
}
