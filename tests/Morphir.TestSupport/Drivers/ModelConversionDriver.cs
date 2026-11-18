// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>How a model is stored: one file, or a document tree in a directory.</summary>
public enum ModelLayout
{
    SingleFile,
    DocumentTree,
}

/// <summary>The names of the layouts, as <c>--layout</c> and the conversion report give them.</summary>
public static class ModelLayouts
{
    /// <summary><c>single-file</c> or <c>document-tree</c>.</summary>
    public static string Name(this ModelLayout layout) => layout switch
    {
        ModelLayout.SingleFile => "single-file",
        ModelLayout.DocumentTree => "document-tree",
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "The layout has no name."),
    };
}

/// <summary>
/// A model in the workspace: a file (a single-file model) or a directory (a document tree). A
/// <see cref="WorkspaceFile"/> and a <see cref="WorkspaceDirectory"/> convert to it.
/// </summary>
public sealed record ModelLocation
{
    private ModelLocation(string path) => Path = path;

    /// <summary>The full path, for a command line and for a message.</summary>
    public string Path { get; }

    public static ModelLocation Of(WorkspaceFile file) => new(file.Path);

    public static ModelLocation Of(WorkspaceDirectory directory) => new(directory.Path);

    public static implicit operator ModelLocation(WorkspaceFile file) => Of(file);

    public static implicit operator ModelLocation(WorkspaceDirectory directory) => Of(directory);

    public override string ToString() => Path;
}

/// <summary>
/// The <c>--compact</c> argument of <c>model convert</c>: a bare <c>--compact</c> (the default mode), a mode with
/// <c>=</c>, or a mode after a space, which a test of the option convention gives on purpose.
/// </summary>
public sealed class CompactionArgument
{
    private CompactionArgument(IReadOnlyList<string> words) => Words = words;

    /// <summary>The words of the argument, in order.</summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>A bare <c>--compact</c>: the default mode of the command.</summary>
    public static CompactionArgument Default { get; } = new(["--compact"]);

    /// <summary><c>--compact=<paramref name="mode"/></c>, also for a mode that the command does not know.</summary>
    public static CompactionArgument WithMode(string mode) => new([$"--compact={mode}"]);

    /// <summary><c>--compact <paramref name="mode"/></c>: a mode after a space, which the command does not take as the mode.</summary>
    public static CompactionArgument Separated(string mode) => new(["--compact", mode]);

    public override string ToString() => string.Join(' ', Words);
}

/// <summary>The options of <c>model convert</c>.</summary>
public sealed record ConversionOptions
{
    /// <summary>The layout of the output (<c>--layout</c>), or null for the layout of the input.</summary>
    public ModelLayout? Layout { get; init; }

    /// <summary>
    /// Gives <see cref="Layout"/> after a space (<c>--layout document-tree</c>), the form that the command accepts beside
    /// <c>--layout=document-tree</c>, for a test of that form.
    /// </summary>
    public bool LayoutAfterASpace { get; init; }

    /// <summary>The compaction of the output (<c>--compact</c>), or null for plain output.</summary>
    public CompactionArgument? Compact { get; init; }

    /// <summary>The <c>--json</c> argument (default: the stream), or null for the text of a person.</summary>
    public JsonOutputArgument? Json { get; init; } = JsonOutputArgument.Stream;

    /// <summary>More arguments, after the others. For a scenario whose subject is an option that the command refuses.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

/// <summary>
/// The <c>model convert</c> command as an intent: store a model in another layout or encoding. A step names the
/// model, the layout and the compaction; the driver builds the command line.
/// </summary>
public interface IModelConversionDriver
{
    /// <summary>Converts the model at <paramref name="input"/> and writes it to <paramref name="output"/>.</summary>
    Task<CommandResult> ConvertAsync(ModelLocation input, ModelLocation output, ConversionOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>model convert</c> and gives it to the CLI driver.</summary>
public sealed class ModelConversionDriver(ICliDriver cli) : IModelConversionDriver
{
    public Task<CommandResult> ConvertAsync(ModelLocation input, ModelLocation output, ConversionOptions? options = null, CancellationToken cancellation = default)
    {
        options ??= new ConversionOptions();
        var arguments = new List<string> { "model", "convert", "--input", input.Path, "--output", output.Path };
        if (options.Layout is { } layout) arguments.AddRange(options.LayoutAfterASpace ? ["--layout", layout.Name()] : ["--layout=" + layout.Name()]);
        if (options.Compact is { } compact) arguments.AddRange(compact.Words);
        arguments.AddRange(options.Arguments);
        if (options.Json is { } json) arguments.AddRange([.. json.Words, .. json.Trailing]);
        return cli.RunAsync(arguments, cancellation: cancellation);
    }
}
