using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Json.Classic;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Infrastructure.JsonSchema;

/// <summary>
/// Documents written by the classic writer conform to the bundled JSON schema of their version (task 2.2).
/// </summary>
public class ClassicWriterSchemaTests
{
    private readonly SchemaValidator _validator = new(new SchemaLoader());

    // The daily-rate model is small. Schema evaluation of the 200 KB car-rental model needs many gigabytes.
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task WrittenDocumentConformsToItsSchema(int version)
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "MorphirIR", "daily-rate-v3.json");
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        var read = ClassicJson.readDistribution(document.RootElement);
        read.IsOk.Should().BeTrue();

        var profile = version switch
        {
            1 => ClassicWireProfileModule.v1,
            2 => ClassicWireProfileModule.v2,
            _ => ClassicWireProfileModule.v3,
        };
        var written = ClassicJsonWriter.writeJsonAttributes(profile, false, read.ResultValue.Distribution);
        written.IsOk.Should().BeTrue();

        var result = await _validator.ValidateAsync(
            Encoding.UTF8.GetString(written.ResultValue), version.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Take(5).Select(e => e.ToString())));
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null && !File.Exists(Path.Combine(current.FullName, "Morphir.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
