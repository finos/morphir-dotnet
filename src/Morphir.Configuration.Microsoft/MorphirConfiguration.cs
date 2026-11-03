// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.FSharp.Collections;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.Microsoft;

/// <summary>A projection failure with the original, case-sensitive Morphir paths.</summary>
public sealed record MorphirProjectionDiagnostic(string Code, string Message, IReadOnlyList<string> Paths);

/// <summary>One resolved document projected for .NET consumers. Canonical metadata is retained even on failure.</summary>
public sealed class MorphirConfigurationProjection
{
    public DocumentResolution Canonical { get; }
    public IReadOnlyDictionary<string, string?> Values { get; }
    public IReadOnlyList<MorphirProjectionDiagnostic> Diagnostics { get; }
    public bool IsSuccess => Diagnostics.Count == 0;
    internal IReadOnlySet<string> ReplacementPaths { get; }

    private MorphirConfigurationProjection(DocumentResolution canonical, SortedDictionary<string, string?> values,
        List<MorphirProjectionDiagnostic> diagnostics, HashSet<string> replacementPaths)
    {
        Canonical = canonical;
        Values = new ReadOnlyDictionary<string, string?>(values);
        Diagnostics = diagnostics.AsReadOnly();
        ReplacementPaths = replacementPaths;
    }

    /// <summary>Validate keys before flattening. No files, environment variables or secret references are read.</summary>
    public static MorphirConfigurationProjection Create(DocumentResolution canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var values = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<MorphirProjectionDiagnostic>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var replacementPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Display(string[] path) => KeyPath.format(ListModule.OfSeq(path));

        void Inspect(string[] path, ConfigValue value)
        {
            if (path.Length > 0)
            {
                var key = string.Join(':', path);
                var display = Display(path);
                if (paths.TryGetValue(key, out var prior) && prior != display)
                    diagnostics.Add(new("config.microsoft.collision", "Paths collide under .NET key comparison: " + prior + ", " + display,
                        Array.AsReadOnly(new[] { prior, display })));
                else
                    paths[key] = display;
                if (path[^1].Length == 0)
                    diagnostics.Add(new("config.microsoft.collision", "An empty segment cannot be represented as a .NET section key: " + display,
                        Array.AsReadOnly(new[] { display })));
                if (path[^1].Contains(':', StringComparison.Ordinal))
                {
                    var parent = string.Join(':', path[..^1]);
                    var competingPrefix = paths.Where(p => p.Key != parent && key.StartsWith(p.Key + ":", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(p => p.Key.Length).Select(p => p.Value).FirstOrDefault();
                    var affected = competingPrefix is null ? new[] { display } : new[] { competingPrefix, display };
                    diagnostics.Add(new("config.microsoft.collision", "A literal ':' cannot be represented as a .NET key segment: " + display,
                        Array.AsReadOnly(affected)));
                }
            }
            switch (value)
            {
                case ConfigValue.Object fields:
                    foreach (var field in fields.Item) Inspect([.. path, field.Key], field.Value);
                    break;
                case ConfigValue.Array array:
                    var index = 0;
                    foreach (var item in array.Item) Inspect([.. path, (index++).ToString(CultureInfo.InvariantCulture)], item);
                    break;
            }
        }

        void Flatten(string[] path, ConfigValue value)
        {
            var key = string.Join(':', path);
            if (path.Length > 0 && (value is not ConfigValue.Object fieldsValue || fieldsValue.Item.Count == 0))
                replacementPaths.Add(key);
            switch (value)
            {
                case ConfigValue.Object fields when fields.Item.Count > 0:
                    foreach (var field in fields.Item) Flatten([.. path, field.Key], field.Value);
                    break;
                case ConfigValue.Array array when !array.Item.IsEmpty:
                    var index = 0;
                    foreach (var item in array.Item) Flatten([.. path, (index++).ToString(CultureInfo.InvariantCulture)], item);
                    break;
                case ConfigValue.Array:
                    // .NET 10's generated binder treats an empty string marker as a present empty array.
                    values[key] = string.Empty;
                    break;
                case ConfigValue.String text: values[key] = text.Item; break;
                case ConfigValue.Boolean boolean: values[key] = boolean.Item ? "true" : "false"; break;
                case ConfigValue.Integer integer: values[key] = integer.Item.ToString(CultureInfo.InvariantCulture); break;
                case ConfigValue.BigInteger integer: values[key] = integer.Item.ToString(CultureInfo.InvariantCulture); break;
                case ConfigValue.Number number: values[key] = number.Item.ToString("R", CultureInfo.InvariantCulture); break;
                case ConfigValue.Temporal temporal: values[key] = temporal.text; break;
                default:
                    if (path.Length > 0) values[key] = null;
                    break;
            }
        }

        var errors = canonical.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            diagnostics.Add(new("config.microsoft.invalid_resolution", "Resolve configuration errors before registering the .NET projection.",
                Array.AsReadOnly(errors.Select(d => KeyPath.format(d.KeyPath)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())));
        if (canonical.Effective is not ConfigValue.Object)
            diagnostics.Add(new("config.microsoft.invalid_root", "A configuration document must be an object.", Array.Empty<string>()));
        Inspect([], canonical.Effective);
        if (diagnostics.Count == 0)
            Flatten([], Inspection.redact(FSharpList<string>.Empty, canonical.Effective));
        return new(canonical, values, diagnostics, replacementPaths);
    }
}

/// <summary>Registration rejected a projection. The canonical result is still available to the caller.</summary>
public sealed class MorphirConfigurationException(DocumentResolution canonical, IReadOnlyList<MorphirProjectionDiagnostic> diagnostics)
    : InvalidOperationException(string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)))
{
    public DocumentResolution Canonical { get; } = canonical;
    public IReadOnlyList<MorphirProjectionDiagnostic> Diagnostics { get; } = diagnostics;
}

/// <summary>Explicit, additive configuration registration. Does not change logging or discover ambient sources.</summary>
public static class MorphirConfigurationExtensions
{
    public static IConfigurationBuilder AddMorphirConfiguration(this IConfigurationBuilder builder, DocumentResolution canonical)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var projection = MorphirConfigurationProjection.Create(canonical);
        if (!projection.IsSuccess) throw new MorphirConfigurationException(canonical, projection.Diagnostics);
        builder.Add(new MorphirConfigurationSource(projection));
        return builder;
    }

    /// <summary>Resolve once through the supplied bounded host before adding a single provider.</summary>
    public static IConfigurationBuilder AddMorphirConfiguration(this IConfigurationBuilder builder, IProjectTree tree,
        ProjectDiscovery discovery, ProjectInfo? project = null, ProjectLoadOptions? options = null) =>
        builder.AddMorphirConfiguration(ProjectSystem.Load(tree, discovery, project, options));

    /// <summary>Return the canonical result from the most recently registered Morphir provider.</summary>
    public static DocumentResolution GetMorphirConfiguration(this IConfigurationRoot configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.Providers.OfType<MorphirConfigurationProvider>().LastOrDefault()?.Canonical
            ?? throw new InvalidOperationException("Morphir configuration has not been registered.");
    }
}

internal sealed class MorphirConfigurationSource(MorphirConfigurationProjection projection) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new MorphirConfigurationProvider(projection);
}

internal sealed class MorphirConfigurationProvider : ConfigurationProvider
{
    internal DocumentResolution Canonical { get; }
    private readonly IReadOnlySet<string> _replacementPaths;
    internal MorphirConfigurationProvider(MorphirConfigurationProjection projection)
    {
        Canonical = projection.Canonical;
        _replacementPaths = projection.ReplacementPaths;
        Data = new Dictionary<string, string?>(projection.Values, StringComparer.OrdinalIgnoreCase);
    }
    private bool Replaces(string key)
    {
        while (true)
        {
            if (_replacementPaths.Contains(key)) return true;
            var separator = key.LastIndexOf(':');
            if (separator < 0) return false;
            key = key[..separator];
        }
    }

    public override bool TryGet(string key, out string? value)
    {
        if (base.TryGet(key, out value)) return true;
        // A known replacement also owns absent descendants. Returning true with
        // null prevents an earlier provider from reviving discarded array items.
        return Replaces(key);
    }

    public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath) =>
        base.GetChildKeys(parentPath is not null && Replaces(parentPath) ? [] : earlierKeys, parentPath);

    public override void Set(string key, string? value) => throw new NotSupportedException("Resolved Morphir configuration is read-only.");
}
