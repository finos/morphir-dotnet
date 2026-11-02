using Eleven19.IonDotnet.Builders;

namespace Morphir.Tooling.Tests.Features.Generate;

internal static class GenerationInputFixtures
{
    public static string DailyRate(string directory, string version, string format)
    {
        var fixtures = Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirIon");
        if (format == "tree")
        {
            var path = Path.Combine(directory, "tree");
            var source = Path.Combine(fixtures, "trees", "v3-daily-rate");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(path, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, File.ReadAllText(file).Replace("3.0.0", version));
            }
            return path;
        }
        var text = File.ReadAllText(Path.Combine(fixtures, "pairs", "v3-daily-rate.ion")).Replace("3.0.0", version);
        var input = Path.Combine(directory, "model.unusual");
        Directory.CreateDirectory(directory);
        if (format == "text") File.WriteAllText(input, text);
        else
        {
            using var reader = IonReaderBuilder.Build(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
            using var output = new MemoryStream();
            using var writer = IonBinaryWriterBuilder.Build(output);
            writer.WriteValues(reader); writer.Finish();
            File.WriteAllBytes(input, output.ToArray());
        }
        return input;
    }
}
