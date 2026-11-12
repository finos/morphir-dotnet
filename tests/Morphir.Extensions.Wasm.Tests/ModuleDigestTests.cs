using System.Text;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Reading a declared module digest (task 2.6 of add-wasm-extension-host, design CFG-1): the form the
/// configuration key <c>extensions.&lt;name&gt;.sha256</c> takes, 64 lowercase hexadecimal digits, which is also
/// how <see cref="ModuleDigest.Hex"/> writes a digest.
/// </summary>
public class ModuleDigestTests
{
    const string Hex = "c4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877acc";

    [Test]
    public async Task A_digest_reads_back_from_its_own_hex()
    {
        var digest = ModuleDigest.Of(Encoding.UTF8.GetBytes("module"));

        await Assert.That(ModuleDigest.TryParse(digest.Hex, out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(digest);
    }

    [Test]
    public async Task Sixty_four_lowercase_hexadecimal_digits_are_a_digest()
    {
        await Assert.That(ModuleDigest.TryParse(Hex, out var parsed)).IsTrue();
        await Assert.That(parsed.Hex).IsEqualTo(Hex);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("C4CC77B47D1D3251713A12BCECE9937AFAB58C73B29967C01F17D4102B877ACC")]
    [Arguments("c4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877ac")]
    [Arguments("c4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877acc0")]
    [Arguments("sha256:c4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877acc")]
    [Arguments(" c4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877acc")]
    [Arguments("g4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877acc")]
    public async Task Any_other_text_is_not_a_digest(string? text)
    {
        await Assert.That(ModuleDigest.TryParse(text, out var parsed)).IsFalse();
        await Assert.That(parsed).IsEqualTo(default(ModuleDigest));
    }
}
