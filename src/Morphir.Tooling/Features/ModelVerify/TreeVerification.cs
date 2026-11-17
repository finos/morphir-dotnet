using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.FSharp.Core;
using Morphir.Files;
using Morphir.IR.Trees;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Features.ModelVerify;

/// <summary>
/// Verifies a document tree (design VER-1): each file of a v4 tree against the parent's schema of its kind, then the
/// whole tree through <see cref="DocumentTreeStore"/>, which checks the tree's rules and decodes every document. A v3
/// tree is checked by the store alone, as the parent publishes no v3 tree-file schema. Every error is collected, each
/// with the file it is in and its JSON location in that file. The version comes from the manifest; a
/// <c>--schema-version</c> that contradicts it is refused with <see cref="SchemaVersionConflict"/> and exit code 2,
/// and nothing is checked.
/// </summary>
public static class TreeVerification
{
    /// <summary>The code of a <c>--schema-version</c> that contradicts the manifest of a tree.</summary>
    public const string SchemaVersionConflict = "verify.schema_version_conflict";

    /// <summary>The code of a tree whose directory could not be read.</summary>
    public const string UnreadableDirectory = "verify.unreadable_directory";

    /// <summary>The exit code of a <c>--schema-version</c> that contradicts the manifest, as of a usage error.</summary>
    private const int ConflictExitCode = 2;

    /// <summary>The file of the distribution manifest, at the root of a tree.</summary>
    private const string ManifestFile = "manifest.json";

    /// <summary>Verifies the tree in the directory <paramref name="directory"/>.</summary>
    public static Task<VerifyModelResult> VerifyDirectoryAsync(
        string directory, int? schemaVersion, SchemaValidator validator, CancellationToken ct)
    {
        var loaded = VFileTreeModule.loadFromDisk(directory);
        if (loaded.IsError)
        {
            return Task.FromResult(Result(directory, schemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                schemaVersion.HasValue ? "manual" : "manifest", 0,
                [new ValidationError("$", TreeLoadErrorModule.describe(loaded.ErrorValue)) { Code = UnreadableDirectory }]));
        }

        return VerifyAsync(loaded.ResultValue, directory, schemaVersion, validator, ct);
    }

    /// <summary>
    /// Verifies the files of a tree. <paramref name="filePath"/> is the path that the report names. The tree's files
    /// are read with the default limits of the store (<see cref="ReadOptionsModule.defaults"/>).
    /// </summary>
    public static async Task<VerifyModelResult> VerifyAsync(
        VFileTree tree, string filePath, int? schemaVersion, SchemaValidator validator, CancellationToken ct)
    {
        var limits = ReadOptionsModule.defaults;
        var files = TreeFiles(tree);
        var manifestVersion = ManifestVersion(files, limits.MaxFileBytes);

        if (schemaVersion is { } given && Contradicts(given, manifestVersion))
            return Conflict(filePath, given, manifestVersion);

        var version = manifestVersion ?? schemaVersion?.ToString(CultureInfo.InvariantCulture);
        var detection = schemaVersion.HasValue ? "manual" : "manifest";
        var errors = new List<ValidationError>();

        if (version == "4" && files.Count <= limits.MaxFiles)
        {
            foreach (var (location, file) in files)
            {
                if (TreeFileKinds.Of(location) is not { } kind || Text(file, limits.MaxFileBytes) is not { } json) continue;
                SchemaValidationResult checkedFile;
                try
                {
                    checkedFile = await validator.ValidateTreeFileAsync(json, kind, ct);
                }
                catch (JsonException)
                {
                    // Text that is not JSON: the store names it, with invalid_json.
                    continue;
                }

                errors.AddRange(checkedFile.Errors.Select(error => error with { File = location }));
            }
        }

        var read = DocumentTreeStore.Read(limits, tree);
        if (read.IsError)
            errors.AddRange(read.ErrorValue.Select(ErrorOf));

        return Result(filePath, version ?? "unknown", detection, files.Count, errors);
    }

    /// <summary>
    /// The files of the tree that a read takes as tree files: each JSON or YAML file, by its location, in ordinal
    /// order. A YAML file counts, though the store refuses it, as it is a file of the tree that was checked.
    /// </summary>
    private static List<(string Location, VFile File)> TreeFiles(VFileTree tree) =>
        [.. VFileTreeModule.toFileMap(tree)
            .Where(entry => entry.Key.EndsWith(".json", StringComparison.Ordinal)
                || entry.Key.EndsWith(".yaml", StringComparison.Ordinal)
                || entry.Key.EndsWith(".yml", StringComparison.Ordinal))
            .Select(entry => (entry.Key, entry.Value))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)];

    /// <summary>
    /// The IR version that the JSON manifest states, as the store reads it: <c>"3"</c> for a <c>formatVersion</c> of
    /// major 3, <c>"4"</c> for any other, or null when the tree has no JSON manifest or it cannot be read.
    /// </summary>
    private static string? ManifestVersion(List<(string Location, VFile File)> files, long limit)
    {
        var manifest = files.FirstOrDefault(file => file.Location == ManifestFile);
        if (manifest.File is null || Text(manifest.File, limit) is not { } json) return null;
        try
        {
            using var document = JsonDocument.Parse(json, SchemaValidator.ModelDocumentOptions);
            return IsMajor3(document.RootElement) ? "3" : "4";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a manifest states a format version of major 3: the string <c>"3"</c> or one that starts with
    /// <c>"3."</c>, or a number from 3 up to 4. The store reads such a manifest as a v3 tree.
    /// </summary>
    private static bool IsMajor3(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("formatVersion", out var version)
        && version.ValueKind switch
        {
            JsonValueKind.String => version.GetString() is { } text && (text == "3" || text.StartsWith("3.", StringComparison.Ordinal)),
            JsonValueKind.Number => version.TryGetDouble(out var number) && number is >= 3.0 and < 4.0,
            _ => false,
        };

    /// <summary>
    /// A given version contradicts the tree when the manifest states another one, or when it is no version of a
    /// document tree (3 or 4).
    /// </summary>
    private static bool Contradicts(int given, string? manifestVersion) =>
        manifestVersion is not null
            ? given.ToString(CultureInfo.InvariantCulture) != manifestVersion
            : given is not (3 or 4);

    private static VerifyModelResult Conflict(string filePath, int given, string? manifestVersion)
    {
        var message = manifestVersion is not null
            ? $"--schema-version={given} contradicts the manifest of the tree, which states the IR version {manifestVersion}."
            : $"--schema-version={given} is no version of a document tree, which is 3 or 4.";
        return Result(filePath, manifestVersion ?? "unknown", manifestVersion is null ? "manual" : "manifest", 0,
            [new ValidationError("/formatVersion", message, Expected: manifestVersion ?? "3 or 4", Found: given.ToString(CultureInfo.InvariantCulture))
            {
                File = ManifestFile,
                Code = SchemaVersionConflict,
            }]) with
        {
            ExitCode = ConflictExitCode,
        };
    }

    private static VerifyModelResult Result(string filePath, string version, string detection, int filesChecked, List<ValidationError> errors) =>
        new(
            IsValid: errors.Count == 0,
            SchemaVersion: version,
            DetectionMethod: detection,
            FilePath: filePath,
            Errors: errors,
            Timestamp: DateTime.UtcNow)
        {
            Layout = ModelLayouts.DocumentTree,
            FilesChecked = filesChecked,
        };

    /// <summary>
    /// The text of a file, without a UTF-8 byte order mark, or null when it cannot be read or has more than
    /// <paramref name="limit"/> bytes. The store reports such a file.
    /// </summary>
    private static string? Text(VFile file, long limit)
    {
        if (OptionModule.ToObj(file.Content) is not { } content) return "";
        var opened = VFileContentModule.openRead(content);
        if (opened.IsError) return null;
        try
        {
            using var stream = opened.ResultValue;
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + count > limit) return null;
                buffer.Write(chunk, 0, count);
            }

            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];
            return Encoding.UTF8.GetString(bytes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The error of a store diagnostic: its code, its file, and the JSON location that starts its message as a JSON
    /// Pointer in <c>Path</c>, with the rest of the message. A diagnostic of a whole file or of the tree has the
    /// location of the whole file, <c>""</c>.
    /// </summary>
    private static ValidationError ErrorOf(StoreError error)
    {
        var (pointer, message) = StoreLocation.Split(error.Message);
        return new ValidationError(pointer, message)
        {
            File = OptionModule.ToObj(error.File),
            Code = error.Code,
        };
    }
}

/// <summary>
/// The JSON location at the start of a message of the document-tree store, such as
/// <c>$.def.Public.TypeAliasDefinition.typeExp: Unknown …</c>, turned into a JSON Pointer (RFC 6901). The store spells a
/// member as <c>.name</c> when it has only letters, digits, <c>-</c> and <c>_</c>, and as <c>["name"]</c> otherwise,
/// and an item as <c>[index]</c>.
/// </summary>
public static class StoreLocation
{
    /// <summary>
    /// The JSON Pointer of the location that starts <paramref name="message"/>, and the rest of the message; or the
    /// pointer of the whole file, <c>""</c>, and the whole message when it starts with no location.
    /// </summary>
    public static (string Pointer, string Message) Split(string message)
    {
        if (!message.StartsWith('$')) return ("", message);
        var pointer = new StringBuilder();
        var index = 1;
        while (index < message.Length)
        {
            if (message[index] == '.')
            {
                var start = ++index;
                while (index < message.Length && (char.IsAsciiLetterOrDigit(message[index]) || message[index] is '-' or '_')) index++;
                if (index == start) return ("", message);
                Append(pointer, message[start..index]);
            }
            else if (message[index] == '[' && index + 1 < message.Length && message[index + 1] == '"')
            {
                var end = message.IndexOf("\"]", index + 2, StringComparison.Ordinal);
                if (end < 0) return ("", message);
                Append(pointer, message[(index + 2)..end]);
                index = end + 2;
            }
            else if (message[index] == '[')
            {
                var end = message.IndexOf(']', index + 1);
                if (end < 0 || !int.TryParse(message.AsSpan(index + 1, end - index - 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return ("", message);
                Append(pointer, message[(index + 1)..end]);
                index = end + 1;
            }
            else break;
        }

        return message.AsSpan(index).StartsWith(": ")
            ? (pointer.ToString(), message[(index + 2)..])
            : ("", message);
    }

    private static void Append(StringBuilder pointer, string token) =>
        pointer.Append('/').Append(token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
}
