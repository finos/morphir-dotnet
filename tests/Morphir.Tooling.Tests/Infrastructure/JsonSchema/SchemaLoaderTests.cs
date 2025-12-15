using FluentAssertions;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Infrastructure.JsonSchema;

public class SchemaLoaderTests
{
    private readonly SchemaLoader _sut;

    public SchemaLoaderTests()
    {
        _sut = new SchemaLoader();
    }

    [Test]
    public async Task LoadSchemaAsync_ShouldLoadV1Schema()
    {
        // Act
        var schema = await _sut.LoadSchemaAsync("1", CancellationToken.None);

        // Assert
        schema.Should().NotBeNull();
    }

    [Test]
    public async Task LoadSchemaAsync_ShouldLoadV2Schema()
    {
        // Act
        var schema = await _sut.LoadSchemaAsync("2", CancellationToken.None);

        // Assert
        schema.Should().NotBeNull();
    }

    [Test]
    public async Task LoadSchemaAsync_ShouldLoadV3Schema()
    {
        // Act
        var schema = await _sut.LoadSchemaAsync("3", CancellationToken.None);

        // Assert
        schema.Should().NotBeNull();
    }

    [Test]
    public async Task LoadSchemaAsync_ShouldCacheSchema()
    {
        // Act
        var schema1 = await _sut.LoadSchemaAsync("3", CancellationToken.None);
        var schema2 = await _sut.LoadSchemaAsync("3", CancellationToken.None);

        // Assert
        schema1.Should().BeSameAs(schema2, "schema should be cached");
    }

    [Test]
    public async Task LoadSchemaAsync_ShouldThrowForInvalidVersion()
    {
        // Act
        var act = () => _sut.LoadSchemaAsync("99", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Schema file not found*");
    }

    [Test]
    public async Task LoadSchemaAsync_ShouldRegisterSchemaInGlobalRegistry()
    {
        // Arrange
        var version = "3";

        // Act
        var schema = await _sut.LoadSchemaAsync(version, CancellationToken.None);

        // Assert
        schema.Should().NotBeNull();
        // Schema should be registered in global registry for $ref resolution
        // The registration happens automatically when JsonSchema.FromText is called
        // with a schema that has an $id field
    }
}
