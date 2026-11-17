// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Morphir.IR.Trees;
using Morphir.TestSupport.Drivers;
using Morphir.Tooling.Features.ModelConvert;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.ModelConvert;

/// <summary>
/// Runs <c>morphir model convert</c> in the test process, over the scenario's workspace. It is an
/// <see cref="ICliDriver"/>, so <see cref="Conversion"/> is the shared <see cref="ModelConversionDriver"/>: the
/// command line that the in-process scenarios give is the one that the black-box scenarios give. The command's store
/// is the disk store behind a <see cref="ControllableModelStore"/>, which records each call and can hold a write.
/// </summary>
public sealed class ModelConvertCommandDriver : ICliDriver, IAsyncDisposable
{
    private CapturedOutput? _output;

    /// <summary>The scenario's files.</summary>
    public ScenarioWorkspace Workspace { get; } = ScenarioWorkspace.Create(AppContext.BaseDirectory);

    /// <summary>The store of the command.</summary>
    public ControllableModelStore Store { get; } = new(new DiskModelConversionStore());

    /// <summary>The <c>model convert</c> driver over this in-process CLI.</summary>
    public IModelConversionDriver Conversion => new ModelConversionDriver(this);

    /// <summary>The stdout of the run in progress, as far as the command has flushed it, one line per record.</summary>
    public IReadOnlyList<string> FlushedLines => _output?.FlushedLines ?? [];

    /// <inheritdoc />
    public TestTarget Target { get; } = new TestTarget.DevelopmentBuild();

    /// <inheritdoc />
    public MorphirHost Host => MorphirHost.Native;

    /// <inheritdoc />
    public async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
    {
        var output = _output = new CapturedOutput();
        await using var app = MorphirCli.CreateBuilder([.. arguments])
            .AddScenario<ModelToolingScenario>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IModelConvertOutput>(output);
                services.AddSingleton<IModelConversionStore>(Store);
            })
            .Build();
        var exit = await app.RunAsync(cancellation);
        return new CommandResult("morphir " + string.Join(' ', arguments), exit, output.Text, output.Error, TimeSpan.Zero, []);
    }

    /// <inheritdoc />
    public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default) =>
        throw new NotSupportedException("The conversion driver gives each argument apart.");

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Workspace.DisposeAsync();

    /// <summary>Captured stdout and stderr. Stdout records what was flushed.</summary>
    private sealed class CapturedOutput : IModelConvertOutput
    {
        private readonly FlushRecordingStream _stdout = new();
        private readonly StringWriter _stderr = new();

        public Stream StandardOutput => _stdout;

        public TextWriter StandardError => _stderr;

        public string Text => Encoding.UTF8.GetString(_stdout.ToArray());

        public string Error => _stderr.ToString();

        public IReadOnlyList<string> FlushedLines => Encoding.UTF8.GetString(_stdout.Flushed).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class FlushRecordingStream : MemoryStream
    {
        private volatile byte[] _flushed = [];

        public byte[] Flushed => _flushed;

        public override void Flush()
        {
            base.Flush();
            _flushed = ToArray();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flush();
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The store of an in-process scenario: the disk store, with each read and write recorded, and a write that can be
/// held until the test releases it, so a test can read the stream while the command waits in its write stage.
/// </summary>
public sealed class ControllableModelStore(IModelConversionStore inner) : IModelConversionStore
{
    private readonly TaskCompletionSource _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _release;

    /// <summary>The reads that the command asked for.</summary>
    public int Reads { get; private set; }

    /// <summary>The writes that the command asked for.</summary>
    public int Writes { get; private set; }

    /// <summary>Completes when the command asks for a write.</summary>
    public Task WriteEntered => _writeEntered.Task;

    /// <summary>From now on, each write waits until <see cref="ReleaseWrites"/>.</summary>
    public void HoldWrites() => _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets the held writes go on.</summary>
    public void ReleaseWrites() => _release?.TrySetResult();

    /// <inheritdoc />
    public ValueTask<ModelRead> ReadAsync(ModelSource source, CancellationToken ct)
    {
        Reads++;
        return inner.ReadAsync(source, ct);
    }

    /// <inheritdoc />
    public async ValueTask<ModelWrite> WriteAsync(StoredModel model, ModelTarget target, CancellationToken ct)
    {
        Writes++;
        _writeEntered.TrySetResult();
        if (_release is { } release) await release.Task.WaitAsync(ct);
        return await inner.WriteAsync(model, target, ct);
    }
}
