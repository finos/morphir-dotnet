// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Compile;
using FormatVersion = Morphir.IR.Versioning.FormatVersion;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>A frontend with a fixed capability. By default it delegates compilation to the real F# frontend.</summary>
public sealed class FakeFrontend : IFrontend
{
    private readonly Func<CompileRequest, CancellationToken, Task<FrontendOutcome>> _compile;

    public FakeFrontend(string identity, FrontendCapability capability,
        Func<CompileRequest, CancellationToken, Task<FrontendOutcome>>? compile = null, FrontendTransport? transport = null)
    {
        Identity = ComponentId.Create(identity);
        Capability = capability;
        Transport = transport ?? FrontendTransport.InProcess;
        _compile = compile ?? ((request, ct) => FSharpFrontend.Registration.CompileAsync(request, ct));
    }

    /// <summary>The requests the frontend received, in order.</summary>
    public List<CompileRequest> Requests { get; } = [];

    public ComponentId Identity { get; }
    public FrontendTransport Transport { get; }
    public FrontendCapability Capability { get; }

    public Task<FrontendOutcome> CompileAsync(CompileRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _compile(request, cancellationToken);
    }

    /// <summary>The capability of the real F# frontend, or of F# with only the given targets.</summary>
    public static FrontendCapability FSharp(params IrTarget[] outputs) => new(
        [new LanguageCapability(LanguageId.Create("fsharp"), [".fs", ".fsi"])],
        outputs.Length == 0 ? FSharpFrontend.Registration.Capability.Outputs : outputs,
        false, true);

    /// <summary>An IR target.</summary>
    public static IrTarget Target(ModelFamily family, string version) =>
        new(family, Morphir.IR.Versioning.parse(version).ResultValue);

    /// <summary>A classic target of version <paramref name="version"/>.</summary>
    public static IrTarget Classic(string version) => Target(ModelFamily.Classic, version);

    /// <summary>The v4 target of version 4.</summary>
    public static IrTarget V4 => new(ModelFamily.V4, FormatVersion.NewClassic(4));
}

/// <summary>Builds compile runners over fakes and the real services.</summary>
public static class CompileRunners
{
    /// <summary>The writers of both hosts.</summary>
    public static WriterRegistry Writers { get; } = WriterRegistry.Create([new ClassicIrWriter(), new V4IrWriter()]);

    /// <summary>A runner over the native file system, with the given frontends, hints and providers.</summary>
    public static CompileRunner Create(IEnumerable<IFrontend> frontends, IEnumerable<FrontendHint>? hints = null,
        IEnumerable<IProjectFileProvider>? providers = null, ICompilePublisher? publisher = null,
        IFrontendExtensionConnector? extensions = null) =>
        new(new ProjectCompileSettingsResolver(), Writers, new FrontendResolver(frontends, hints ?? []),
            new SourceSelector(ProjectFileProviderRegistry.Create(providers ?? [])), new Morphir.Tooling.Features.Compile.Sources.SourceCapture(),
            publisher ?? new TaskRecordCompilePublisher(new Morphir.Tooling.Features.TaskRecords.TaskStoreOptions { Diagnostics = TextWriter.Null }), extensions ?? new UnavailableFrontendExtensionConnector());

    /// <summary>Every event of one run.</summary>
    public static async Task<List<CompileEvent>> Events(ICompileRunner runner, CompileOverrides overrides, CompileContext context,
        CancellationToken ct = default)
    {
        var events = new List<CompileEvent>();
        await foreach (var compileEvent in runner.RunAsync(overrides, context, ct)) events.Add(compileEvent);
        return events;
    }

    /// <summary>The codes of the error diagnostics of a result.</summary>
    public static string[] ErrorCodes(CompileResult result) =>
        [.. result.Diagnostics.Where(d => d.Diagnostic.IsError).Select(d => d.Diagnostic.CodeOrNull?.Value ?? "")];
}

/// <summary>A connector that gives a fixed frontend, records its requests and records disposal.</summary>
public sealed class FakeExtensionConnector(IFrontend frontend) : IFrontendExtensionConnector
{
    public List<FrontendExtensionRequest> Requests { get; } = [];

    public Task<Checked<IFrontend>> ConnectAsync(FrontendExtensionRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Checked<IFrontend>.Pass(frontend));
    }
}

/// <summary>A frontend that records that the run disposed it.</summary>
public sealed class DisposableFrontend(IFrontend inner) : IFrontend, IAsyncDisposable
{
    public bool Disposed { get; private set; }
    public ComponentId Identity => inner.Identity;
    public FrontendTransport Transport => inner.Transport;
    public FrontendCapability Capability => inner.Capability;
    public Task<FrontendOutcome> CompileAsync(CompileRequest request, CancellationToken cancellationToken) => inner.CompileAsync(request, cancellationToken);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
