using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The few documents of the generic guest kit's <c>0.2.0-draft.1</c> tier that a session on the Python guest needs,
/// pinned from finos/morphir at <c>48d10706f</c> (<see cref="MepWasmFixtures.PinnedCommit"/>; the kit is unchanged since
/// <c>7b049c223</c>). The inputs are those of
/// <c>spec/mep/mck/guest-generic-json.json</c> (the <c>python</c> language, the package and the context digest), and
/// each request is built as <c>crates/morphir-mck/src/mep/generic.rs</c> builds it: <c>offer</c> and <c>limits</c> for
/// the <c>model-initialize</c> offer, and <c>compile_params</c> with <c>model</c> set for <c>compile-model-vthree</c>
/// and <c>compile-model-vfour</c>. Only the kit's documents are copied; the expected IR stays with task 3.5.
/// </summary>
internal static class GenericGuestKit
{
    /// <summary>The model-transport offer of <c>model-initialize</c>: a compile and a generate tuple for each IR
    /// version both the kit and the guest have, the kit's limits, and no guarantees.</summary>
    public const string ModelTransportOffer =
        """
        {"tuples":[
          {"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"},
          {"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"},
          {"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"},
          {"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}],
         "limits":{"maxEncodedBytes":1000000,"maxDecodedBytes":750000,"maxDepth":128,"maxNodes":100000},
         "guarantees":[]}
        """;

    /// <summary>The kit's <c>python</c> source document.</summary>
    const string PythonSource = "def ready() -> bool:\\n    return True\\n\\n\\ndef blocked() -> bool:\\n    return False\\n";

    /// <summary>The kit's context digest.</summary>
    const string ContextDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    /// <summary>The IR versions of the model tier for this guest.</summary>
    public static IReadOnlyList<string> IrVersions { get; } = ["3.0.0", "4.0.0"];

    /// <summary>The params of <c>compile-model-vthree</c> (3.0.0) or <c>compile-model-vfour</c> (4.0.0).</summary>
    public static string CompileModelParams(string irVersion) =>
        $$$"""
        {"languageId":"python",
         "sources":{"documents":[{"uri":"main.py","languageId":"python","version":1,"text":"{{{PythonSource}}}"}]},
         "package":{"name":"mck/generic","exposedModules":["main"]},
         "options":{"typesOnly":false,"irVersion":"{{{irVersion}}}"},
         "contextDigest":"{{{ContextDigest}}}",
         "output":{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"{{{irVersion}}}"}}
        """;

    /// <summary>The offer as the session's model-transport support.</summary>
    public static ModelTransportSupport Offer() =>
        JsonSerializer.Deserialize(ModelTransportOffer, MepJson.Default.ModelTransportSupport)
        ?? throw new InvalidOperationException("The pinned offer is empty.");

    /// <summary>The host these sessions introduce. The kit sends <c>morphir-mck</c> with no kind; the guest does not
    /// read the host, and <c>HostSession</c> always writes a kind.</summary>
    public static PeerInfo Host { get; } = new(PeerKind.Cli, "morphir-dotnet-tests", "0.0.0");
}
