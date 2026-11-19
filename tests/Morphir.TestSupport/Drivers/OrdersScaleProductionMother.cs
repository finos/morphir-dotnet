// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace Morphir.TestSupport.Drivers;

/// <summary>One original-size module of the frozen Orders scale formula at umbrella 3652032a.</summary>
public static class OrdersScaleProductionMother
{
    public const string GuestSha256 = "9c67d3e6130726d62abf662e1d7a656ab6e92c2059b634bea2e1d444fac7a8ef";
    public const string ArtifactSha256 = "29db9e2199ef6c85f21c25b2a6375d7a5cce82b238ec1e0fbdc450e0a6217fa5";
    public const int ArtifactBytes = 1_065_025;
    private const string Prefix = "pkg/acme/orders/m000000/";

    public static IEnumerable<KeyValuePair<string, byte[]>> ProductionDocuments()
    {
        static KeyValuePair<string, byte[]> Document(string path, string text) => new(path, Encoding.UTF8.GetBytes(text + "\n"));
        yield return Document("manifest.json", """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":4000,"dependencies":["morphir/SDK"]}""");
        yield return Document("deps/morphir/_sdk/@/string/module.json", """{"formatVersion":4,"path":"string","types":["string"],"values":[]}""");
        yield return Document("deps/morphir/_sdk/@/string/string.type.json", """{"formatVersion":4,"name":"string","spec":{"OpaqueTypeSpecification":{}}}""");
        var names = Enumerable.Range(0, 64).Select(index => "order" + index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var inventory = "{\"formatVersion\":4,\"path\":\"m000000\",\"types\":[\"order000000\"],\"values\":["
            + string.Join(",", names.Append("total-in-USD").Select(name => "\"" + name + "\"")) + "]}";
        yield return Document(Prefix + "module.json", inventory);
        yield return Document(Prefix + "order000000.type.json", """{"formatVersion":4,"name":"order000000","def":{"Public":{"TypeAliasDefinition":{"typeParams":[],"typeExp":"morphir/SDK:string#string"}}}}""");
        foreach (var name in names)
        {
            var payload = ("m000000/" + name + ":").PadRight(16_384, 'x');
            const string template = """{"formatVersion":4,"name":"NAME","def":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":"morphir/SDK:string#string","body":{"Literal":{"StringLiteral":"PAYLOAD"}}}}}}""";
            var document = Document(Prefix + name + ".value.json", template.Replace("NAME", name, StringComparison.Ordinal).Replace("PAYLOAD", payload, StringComparison.Ordinal));
            if (document.Value.Length != 16_560) throw new InvalidOperationException("The frozen production document changed.");
            yield return document;
        }
        yield return Document(Prefix + "total-in-_usd.value.json", """{"formatVersion":4,"name":"total-in-USD","def":{"Public":{"ExpressionBody":{"inputTypes":{},"outputType":"morphir/SDK:string#string","body":{"Reference":"acme/orders:m000000#order000063"}}}}}""");
    }

    public static IEnumerable<KeyValuePair<string, byte[]>> DocumentOneOver() => ProductionDocuments().Select(document =>
        document.Key == Prefix + "order000000.value.json"
            ? new(document.Key, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(document.Value).PadRight(32_769, ' ')))
            : document);
}
