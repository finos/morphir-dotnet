using CsToml.Error;
using Microsoft.FSharp.Core;
using YamlDotNet.Core;

namespace Morphir.Configuration.IO;

/// <summary>Reads only caller-supplied documents. No discovery, secret evaluation, or CLI construction.</summary>
public static class ConfigurationReader
{
    public static SourceInput ReadText(string path, string text,
        SourceRole role = SourceRole.RootPrimary, int order = 0, string? profile = null, bool required = true) =>
        ReadText(new SourceIdentity(path, Format(path) ?? DocumentFormat.Memory, role, order,
            profile is null ? FSharpOption<string>.None : FSharpOption<string>.Some(profile)), text, required);

    public static SourceInput ReadText(SourceIdentity source, string text, bool required = true)
    {
        var format = Format(source.Path);
        if (format is null)
            return Invalid(source, required, ReaderValues.Diagnostic(source, "config.unsupported_format",
                $"Unsupported configuration format: {source.Path}"));

        source = new SourceIdentity(source.Path, format.Value, source.Role, source.Order, source.Profile);
        try
        {
            var tree = format.Value switch
            {
                DocumentFormat.Json => Json5Reader.Parse(text),
                DocumentFormat.Yaml => YamlReader.Parse(text),
                DocumentFormat.Toml => TomlReader.Parse(text),
                _ => throw new InvalidOperationException("Unsupported parser dispatch")
            };
            if (tree.Value is not ConfigValue.Object)
                throw new ReaderException("config.document_shape", "Configuration root must be an object", []);
            var document = Normalization.normalize(source, tree.Value, tree.Positions);
            if (document.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
                return new SourceInput(source, SourceContent.NewInvalid(document.Diagnostics), required);
            return new SourceInput(source, SourceContent.NewAvailable(document), required);
        }
        catch (ReaderException error)
        {
            return Invalid(source, required, ReaderValues.Diagnostic(source, error.Code, error.Message,
                error.KeyPath, error.Position));
        }
        catch (YamlException error)
        {
            return Invalid(source, required, ReaderValues.Diagnostic(source, "config.parse_error", error.Message,
                position: new SourcePosition((int)error.Start.Line, (int)error.Start.Column)));
        }
        catch (CsTomlException error)
        {
            var parse = error is CsTomlSerializeException aggregate ? aggregate.ParseExceptions?.FirstOrDefault()
                : error as CsTomlParseException;
            return Invalid(source, required, ReaderValues.Diagnostic(source, "config.parse_error", error.Message,
                position: parse is null ? null : new SourcePosition((int)parse.LineNumber, 1)));
        }
    }

    /// <summary>Reads one explicit path, retaining missing/required state for resolution reports.</summary>
    public static SourceInput ReadFile(SourceIdentity source, bool required = true)
    {
        try { return ReadText(source, File.ReadAllText(source.Path), required); }
        catch (FileNotFoundException) { return new SourceInput(source, SourceContent.Missing, required); }
        catch (DirectoryNotFoundException) { return new SourceInput(source, SourceContent.Missing, required); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Invalid(source, required, ReaderValues.Diagnostic(source, "config.read_error", error.Message));
        }
    }

    private static SourceInput Invalid(SourceIdentity source, bool required, ConfigDiagnostic diagnostic) =>
        new(source, SourceContent.NewInvalid(ReaderValues.List([diagnostic])), required);

    private static DocumentFormat? Format(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" or ".json5" => DocumentFormat.Json,
        ".yaml" or ".yml" => DocumentFormat.Yaml,
        ".toml" => DocumentFormat.Toml,
        _ => null
    };
}
