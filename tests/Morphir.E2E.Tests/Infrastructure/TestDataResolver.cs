namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// Resolves test data file paths for E2E tests
/// </summary>
public class TestDataResolver
{
    private readonly string _testDataDirectory;

    public TestDataResolver()
    {
        var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        // Reference shared test data from Morphir.Tooling.Tests
        _testDataDirectory = Path.Combine(repositoryRoot, "tests", "Morphir.Tooling.Tests", "TestData");

        if (!Directory.Exists(_testDataDirectory))
        {
            throw new InvalidOperationException($"Test data directory not found at: {_testDataDirectory}");
        }
    }

    /// <summary>
    /// Resolve a test data file path, replacing &lt;test-data&gt; placeholder
    /// </summary>
    public string ResolvePath(string path)
    {
        if (path.Contains("<test-data>"))
        {
            return path.Replace("<test-data>", _testDataDirectory);
        }

        // If path is relative and starts with TestData, resolve it
        if (path.StartsWith("TestData/") || path.StartsWith("TestData\\"))
        {
            var fileName = Path.GetFileName(path);
            var resolvedPath = Path.Combine(_testDataDirectory, fileName);
            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }
        }

        // Return as-is if it's already an absolute path or doesn't match patterns
        return path;
    }

    /// <summary>
    /// Get the test data directory path
    /// </summary>
    public string GetTestDataDirectory() => _testDataDirectory;

    /// <summary>
    /// Find repository root by looking for .git directory
    /// </summary>
    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }
}

