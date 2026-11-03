using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

internal static class ReaderValues
{
    internal static FSharpList<T> List<T>(IEnumerable<T> values) => ListModule.OfSeq(values);
    internal static ConfigValue Object(IEnumerable<KeyValuePair<string, ConfigValue>> values) =>
        ConfigValue.NewObject(MapModule.OfSeq(values.Select(item => Tuple.Create(item.Key, item.Value))));

    internal static ConfigDiagnostic Diagnostic(SourceIdentity source, string code, string message,
        IEnumerable<string>? path = null, SourcePosition? position = null) =>
        new(code, message, DiagnosticSeverity.Error, source, List(path ?? []),
            position is null ? FSharpOption<SourcePosition>.None : FSharpOption<SourcePosition>.Some(position));
}

internal sealed class ReaderException(string code, string message, string[] path, SourcePosition? position = null)
    : Exception(message)
{
    internal string Code { get; } = code;
    internal string[] KeyPath { get; } = path;
    internal SourcePosition? Position { get; } = position;
}

internal sealed class ParsedTree(ConfigValue value,
    List<Tuple<FSharpList<string>, SourcePosition>> positions)
{
    internal ConfigValue Value { get; } = value;
    internal FSharpMap<FSharpList<string>, SourcePosition> Positions { get; } = MapModule.OfSeq(positions);
}
