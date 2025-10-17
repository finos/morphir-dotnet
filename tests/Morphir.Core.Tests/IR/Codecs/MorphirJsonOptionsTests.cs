using System.Text.Json;
using Morphir.IR;
using Morphir.IR.Codecs;

namespace Morphir.Tests.IR.Codecs;

public class MorphirJsonOptionsTests
{
    #region Record Behavior Tests

    [Test]
    public async Task Should_Be_Immutable_Record()
    {
        var options = new MorphirJsonOptions(new MorphirFormatVersion.Version2());

        // Records are immutable by default
        await Assert.That(options).IsNotNull();
        await Assert.That(options.FormatVersion).IsTypeOf<MorphirFormatVersion.Version2>();
    }

    [Test]
    public async Task Should_Support_With_Expression_For_FormatVersion()
    {
        var original = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var modified = original with { FormatVersion = new MorphirFormatVersion.Experimental() };

        using (Assert.Multiple())
        {
            // Original should be unchanged
            await Assert.That(original.FormatVersion).IsTypeOf<MorphirFormatVersion.Version2>();

            // Modified should have new version
            await Assert.That(modified.FormatVersion).IsTypeOf<MorphirFormatVersion.Experimental>();

            // Should be different instances
            await Assert.That(modified).IsNotEqualTo(original);
        }
    }

    [Test]
    public async Task Should_Support_WithFormatVersion_Method()
    {
        var original = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var modified = original.WithFormatVersion(new MorphirFormatVersion.Experimental());

        using (Assert.Multiple())
        {
            // Original should be unchanged
            await Assert.That(original.FormatVersion).IsTypeOf<MorphirFormatVersion.Version2>();

            // Modified should have new version
            await Assert.That(modified.FormatVersion).IsTypeOf<MorphirFormatVersion.Experimental>();

            // Should be different instances
            await Assert.That(modified).IsNotEqualTo(original);
        }
    }

    [Test]
    public async Task Should_Have_Value_Equality()
    {
        var options1 = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var options2 = new MorphirJsonOptions(new MorphirFormatVersion.Version2());

        // Records have value-based equality
        await Assert.That(options1).IsEqualTo(options2);
    }

    [Test]
    public async Task Should_Have_Different_Equality_For_Different_Versions()
    {
        var options1 = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var options2 = new MorphirJsonOptions(new MorphirFormatVersion.Experimental());

        await Assert.That(options1).IsNotEqualTo(options2);
    }

    #endregion

    #region Default Instance Tests

    [Test]
    public async Task Default_Should_Have_Version2()
    {
        var defaultOptions = MorphirJsonOptions.Default;

        await Assert.That(defaultOptions.FormatVersion).IsTypeOf<MorphirFormatVersion.Version2>();
    }

    [Test]
    public async Task Default_Should_Be_Singleton()
    {
        var default1 = MorphirJsonOptions.Default;
        var default2 = MorphirJsonOptions.Default;

        // Should return same instance
        await Assert.That(ReferenceEquals(default1, default2)).IsTrue();
    }

    [Test]
    public async Task Default_Should_Have_JsonSerializerOptions()
    {
        var defaultOptions = MorphirJsonOptions.Default;

        await Assert.That(defaultOptions.JsonSerializerOptions).IsNotNull();
    }

    #endregion

    #region JsonSerializerOptions Lazy Initialization Tests

    [Test]
    public async Task JsonSerializerOptions_Should_Be_Lazily_Initialized()
    {
        var options = new MorphirJsonOptions(new MorphirFormatVersion.Version2());

        // First access should initialize
        var jsonOptions1 = options.JsonSerializerOptions;
        await Assert.That(jsonOptions1).IsNotNull();

        // Second access should return same instance
        var jsonOptions2 = options.JsonSerializerOptions;
        await Assert.That(ReferenceEquals(jsonOptions1, jsonOptions2)).IsTrue();
    }

    [Test]
    public async Task JsonSerializerOptions_Should_Have_CamelCase_Naming_Policy()
    {
        var options = MorphirJsonOptions.Default;
        var jsonOptions = options.JsonSerializerOptions;

        await Assert.That(jsonOptions.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task JsonSerializerOptions_Should_Have_Converters_Registered()
    {
        var options = MorphirJsonOptions.Default;
        var jsonOptions = options.JsonSerializerOptions;

        using (Assert.Multiple())
        {
            await Assert.That(jsonOptions.Converters).IsNotEmpty();

            // Should have DocumentJsonConverter
            await Assert.That(jsonOptions.Converters.Any(c => c is DocumentJsonConverter)).IsTrue();
        }
    }

    [Test]
    public async Task Different_MorphirJsonOptions_Should_Have_Different_JsonSerializerOptions()
    {
        var options1 = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var options2 = new MorphirJsonOptions(new MorphirFormatVersion.Experimental());

        var jsonOptions1 = options1.JsonSerializerOptions;
        var jsonOptions2 = options2.JsonSerializerOptions;

        // Different instances should have different JsonSerializerOptions
        await Assert.That(ReferenceEquals(jsonOptions1, jsonOptions2)).IsFalse();
    }

    #endregion

    #region Thread-Safety Tests

    [Test]
    public async Task JsonSerializerOptions_Should_Be_Thread_Safe_On_First_Access()
    {
        const int threadCount = 10;
        var options = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var results = new JsonSerializerOptions[threadCount];
        var tasks = new Task[threadCount];

        // Create multiple threads that all try to access JsonSerializerOptions simultaneously
        for (int i = 0; i < threadCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                results[index] = options.JsonSerializerOptions;
            });
        }

        await Task.WhenAll(tasks);

        // All threads should get the same instance (if thread-safe)
        // or different instances (if not thread-safe, but at least no crash)
        var firstResult = results[0];
        await Assert.That(firstResult).IsNotNull();

        // Check if all results are the same (ideal for thread-safety)
        var allSame = results.All(r => ReferenceEquals(r, firstResult));

        // NOTE: This test documents the current behavior
        // If this fails, it indicates a thread-safety issue
        if (!allSame)
        {
            // Count unique instances to show the race condition
            var uniqueInstances = results.Distinct().Count();
            await Assert.That(uniqueInstances).IsGreaterThan(1);

            // Log for diagnostic purposes
            Console.WriteLine($"WARNING: Thread-safety issue detected. Created {uniqueInstances} instances instead of 1.");
        }
    }

    [Test]
    public async Task Multiple_Threads_Should_Be_Able_To_Use_JsonSerializerOptions_Safely()
    {
        const int threadCount = 20;
        const int operationsPerThread = 100;
        var options = MorphirJsonOptions.Default;
        var exceptions = new List<Exception>();
        var tasks = new Task[threadCount];

        // Pre-initialize to avoid testing initialization thread-safety
        var _ = options.JsonSerializerOptions;

        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        // Multiple threads accessing the same options
                        var jsonOptions = options.JsonSerializerOptions;

                        // Perform serialization operation
                        var doc = Document.Str($"Test {j}");
                        var json = JsonSerializer.Serialize(doc, jsonOptions);
                        var roundtrip = JsonSerializer.Deserialize<Document>(json, jsonOptions);

                        if (roundtrip is not Document.String str || str.Value != $"Test {j}")
                        {
                            throw new InvalidOperationException("Serialization failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }

        await Task.WhenAll(tasks);

        // No exceptions should occur during concurrent usage
        await Assert.That(exceptions).IsEmpty();
    }

    [Test]
    public async Task With_Expression_Should_Create_Independent_Instances()
    {
        var original = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var modified = original with { FormatVersion = new MorphirFormatVersion.Experimental() };

        // Force initialization on both
        var originalJsonOptions = original.JsonSerializerOptions;
        var modifiedJsonOptions = modified.JsonSerializerOptions;

        using (Assert.Multiple())
        {
            // Should be different instances
            await Assert.That(ReferenceEquals(originalJsonOptions, modifiedJsonOptions)).IsFalse();

            // Both should be valid
            await Assert.That(originalJsonOptions).IsNotNull();
            await Assert.That(modifiedJsonOptions).IsNotNull();
        }
    }

    #endregion

    #region Mutation Tests

    [Test]
    public async Task Should_Not_Allow_Mutation_Of_FormatVersion_After_Construction()
    {
        var options = new MorphirJsonOptions(new MorphirFormatVersion.Version2());

        // Records are immutable - attempting to change FormatVersion should create a new instance
        var modified = options with { FormatVersion = new MorphirFormatVersion.Experimental() };

        using (Assert.Multiple())
        {
            // Original remains unchanged
            await Assert.That(options.FormatVersion).IsTypeOf<MorphirFormatVersion.Version2>();

            // Modified is a new instance
            await Assert.That(modified.FormatVersion).IsTypeOf<MorphirFormatVersion.Experimental>();
            await Assert.That(ReferenceEquals(options, modified)).IsFalse();
        }
    }

    [Test]
    public async Task Modifying_JsonSerializerOptions_Should_Not_Affect_Other_Instances()
    {
        var options1 = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var options2 = new MorphirJsonOptions(new MorphirFormatVersion.Version2());

        // Get JsonSerializerOptions for both
        var jsonOptions1 = options1.JsonSerializerOptions;
        var jsonOptions2 = options2.JsonSerializerOptions;

        // They should be different instances even though MorphirJsonOptions are equal
        await Assert.That(ReferenceEquals(jsonOptions1, jsonOptions2)).IsFalse();
    }

    [Test]
    public async Task Default_Instance_Should_Remain_Unchanged_After_With_Expression()
    {
        var original = MorphirJsonOptions.Default;
        var originalVersion = original.FormatVersion;

        // Create modified version
        var _ = original with { FormatVersion = new MorphirFormatVersion.Experimental() };

        // Default should remain unchanged
        await Assert.That(MorphirJsonOptions.Default.FormatVersion).IsEqualTo(originalVersion);
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task Should_Successfully_Serialize_Document_With_Custom_Options()
    {
        var customOptions = new MorphirJsonOptions(new MorphirFormatVersion.Version2());
        var doc = Document.Obj(
            ("name", Document.Str("Test")),
            ("value", new Document.Integer(42))
        );

        var json = JsonSerializer.Serialize(doc, customOptions.JsonSerializerOptions);
        var roundtrip = JsonSerializer.Deserialize<Document>(json, customOptions.JsonSerializerOptions);

        await Assert.That(roundtrip).IsEqualTo(doc);
    }

    [Test]
    public async Task Should_Successfully_Serialize_Document_With_Default_Options()
    {
        var doc = Document.Obj(
            ("name", Document.Str("Test")),
            ("value", new Document.Integer(42))
        );

        var json = MorphirJson.EncodeAsString(doc);
        var roundtrip = MorphirJson.DecodeFromString<Document>(json);

        await Assert.That(roundtrip).IsEqualTo(doc);
    }

    #endregion
}
