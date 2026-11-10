namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The bound on guest output (task 3.4, and the library side of task 2.6): the newest outputs whose reported text
/// is at most 64 KiB, each cut to 4 KiB of JSON text ending with <c>…</c>, and a count of the UTF-8 bytes dropped.
/// The sizes and the rule are the MoonBit worker's, which task 4.1's <c>output</c> events follow.
/// </summary>
public class GuestOutputTailTests
{
    private static GuestOutput Stdout(string text) => new(GuestOutputSource.Stdout, text);

    [Test]
    public async Task The_bounds_are_the_worker_tail_and_message_cut()
    {
        int[] bounds = [GuestOutputTail.TailBytes, GuestOutputTail.MessageBytes];

        await Assert.That(bounds).IsEquivalentTo(new[] { 65536, 4096 });
    }

    [Test]
    public async Task A_short_output_is_kept_whole()
    {
        var tail = new GuestOutputTail();

        tail.Add(Stdout("stdout: hello"));

        await Assert.That(tail.Outputs.Single().Text).IsEqualTo("stdout: hello");
        await Assert.That(tail.Outputs.Single().Cut).IsFalse();
        await Assert.That(tail.KeptBytes).IsEqualTo(13);
        await Assert.That(tail.DroppedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task A_long_output_is_cut_to_the_message_bound_with_a_marker_and_the_rest_counted()
    {
        var tail = new GuestOutputTail();
        var text = new string('a', 10000);

        tail.Add(Stdout(text));

        var kept = tail.Outputs.Single();
        await Assert.That(kept.Cut).IsTrue();
        await Assert.That(kept.Text).EndsWith("…");
        await Assert.That(GuestOutputTail.ReportedBytes(kept.Text)).IsLessThanOrEqualTo(GuestOutputTail.MessageBytes);
        // 4,093 characters and the three bytes of the marker.
        await Assert.That(kept.Text.Length).IsEqualTo(GuestOutputTail.MessageBytes - 3 + 1);
        await Assert.That(tail.DroppedBytes).IsEqualTo(10000 - (GuestOutputTail.MessageBytes - 3));
    }

    [Test]
    public async Task Control_characters_count_as_JSON_escapes()
    {
        await Assert.That(GuestOutputTail.ReportedBytes("a\nb")).IsEqualTo(1 + 6 + 1);
        await Assert.That(GuestOutputTail.ReportedBytes("\"\\")).IsEqualTo(4);
        await Assert.That(GuestOutputTail.ReportedBytes("é€😀")).IsEqualTo(2 + 3 + 4);
    }

    [Test]
    public async Task The_oldest_outputs_are_dropped_above_the_tail_bound()
    {
        var tail = new GuestOutputTail();
        var item = new string('b', 1000);

        for (var index = 0; index < 100; index++)
        {
            tail.Add(Stdout(item));
        }

        await Assert.That(tail.KeptBytes).IsLessThanOrEqualTo(GuestOutputTail.TailBytes);
        await Assert.That(tail.Outputs.Count).IsEqualTo(GuestOutputTail.TailBytes / 1000);
        await Assert.That(tail.DroppedBytes).IsEqualTo((100 - tail.Outputs.Count) * 1000L);
    }

    [Test]
    public async Task Drain_returns_the_kept_outputs_and_the_dropped_count_and_empties_the_tail()
    {
        var tail = new GuestOutputTail();
        tail.Add(Stdout("first"));
        tail.Add(Stdout(new string('c', 5000)));

        var drained = tail.Drain();

        await Assert.That(drained.Outputs.Select(output => output.Text).First()).IsEqualTo("first");
        await Assert.That(drained.DroppedBytes).IsEqualTo(5000 - (GuestOutputTail.MessageBytes - 3));
        await Assert.That(tail.Outputs.Count).IsEqualTo(0);
        await Assert.That(tail.KeptBytes).IsEqualTo(0);
        await Assert.That(tail.DroppedBytes).IsEqualTo(0);
    }
}
