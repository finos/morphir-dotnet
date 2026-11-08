namespace Morphir.TestSupport.Tests;

// TEMPORARY probe for the CI hang experiment. Not for merge.
public class HangProbeTests
{
    [Test]
    public async Task HangProbe()
    {
        switch (Environment.GetEnvironmentVariable("MORPHIR_PROBE"))
        {
            case "hang":
                await Task.Delay(Timeout.Infinite);
                break;
            case "hang-once":
                var marker = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath(), "morphir-hang-probe-done");
                if (!File.Exists(marker))
                {
                    File.WriteAllText(marker, "first attempt hung");
                    await Task.Delay(Timeout.Infinite);
                }
                break;
            case "fail":
                throw new InvalidOperationException("Probe: a normal test failure.");
        }
    }
}
