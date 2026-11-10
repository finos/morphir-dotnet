namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The library reads its import table from the pinned contract, embedded as a resource (design PIN-1), so the
/// embedded copy must be the pinned file.
/// </summary>
public class ContractTests
{
    [Test]
    public async Task The_embedded_contract_is_the_pinned_file()
    {
        var pinned = MepWasmFixtures.Sha256(MepWasmFixtures.Contract);

        await Assert.That(WasmProfileContract.Pinned.Sha256).IsEqualTo(pinned);
    }

    [Test]
    public async Task The_embedded_contract_has_the_pinned_identity()
    {
        var contract = WasmProfileContract.Pinned;

        await Assert.That(contract.Id).IsEqualTo("morphir/mep-wasm-execution-profiles");
        await Assert.That(contract.Version).IsEqualTo("0.1.0-draft.1");
    }
}
