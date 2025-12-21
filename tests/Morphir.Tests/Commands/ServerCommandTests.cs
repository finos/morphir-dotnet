using TUnit.Core;
using FluentAssertions;
using System.CommandLine;
using Morphir;

namespace Morphir.Tests.Commands;

public class ServerCommandTests
{
    [Test]
    public void ServerCommand_Should_Have_Correct_Name()
    {
        var command = Program.CreateServerCommand();
        command.Name.Should().Be("server");
    }

    [Test]
    public void ServerCommand_Should_Have_Port_Option()
    {
        var command = Program.CreateServerCommand();
        var portOption = command.Options.OfType<Option<int?>>()
            .FirstOrDefault(o => o.Name == "--port");

        portOption.Should().NotBeNull();
    }

    [Test]
    public void ServerCommand_Should_Have_Urls_Option()
    {
        var command = Program.CreateServerCommand();
        var urlsOption = command.Options.OfType<Option<string?>>()
            .FirstOrDefault(o => o.Name == "--urls");

        urlsOption.Should().NotBeNull();
    }

    [Test]
    public void ServerCommand_Should_Have_Environment_Option()
    {
        var command = Program.CreateServerCommand();
        var environmentOption = command.Options.OfType<Option<string?>>()
            .FirstOrDefault(o => o.Name == "--environment");

        environmentOption.Should().NotBeNull();
    }
}

