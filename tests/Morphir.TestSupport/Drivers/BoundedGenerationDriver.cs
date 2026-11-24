// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Morphir.TestSupport.Fixtures;

namespace Morphir.TestSupport.Drivers;

/// <summary>Recorded Orders inputs and configured guests for bounded generation.</summary>
public interface IBoundedGenerationDriver
{
    void Prepare(string mode);
    /// <summary>
    /// A project with <c>ir.layout = "document-tree"</c> and the Orders domain source, for a real compile, and the orders
    /// guest in its mode for compiled trees. No record is written: the compile writes it.
    /// </summary>
    void PrepareCompiledOrders();
    void ConfigureOrdersScaleGuest();
    WorkspaceDirectory RecordScaleProductionTree();
    WorkspaceDirectory RecordScaleDocumentOverTree();
    Task<CommandResult> GenerateOrdersScaleAsync(string output);
    WorkspaceDirectory RecordTree(int order, int? reserve = null);
    Task<CommandResult> GenerateAsync(string output, params string[] extra);
    Task<CommandResult> GenerateAsync(string output, bool configured);
    /// <summary>
    /// Bounded generation with the orders guest from the record of the last compile, in the out root that the compile used
    /// by default: no <c>--out-dir</c> and no <c>--input</c>.
    /// </summary>
    Task<CommandResult> GenerateFromLastCompileAsync(string output);
    void ConfigureWasmLimits(int stateBytes, int frameBytes);
    void ConfigureMemoryLimitGuest();
    void ConfigureSharedOrdersGuest(string module);
    string? SharedOrdersModule { get; }
    bool IsWindows { get; }
    void ReplaceWithFifo(WorkspaceFile file);
}

public sealed class BoundedGenerationDriver(IScenarioWorkspace workspace, IGenerationDriver generation, IExtensionDriver extensions) : IBoundedGenerationDriver
{
    public const string SharedOrdersVariable = "MORPHIR_SHARED_ORDERS_WASM";
    private const string OutDir = "build-artifacts";
    public bool IsWindows => OperatingSystem.IsWindows();
    public string? SharedOrdersModule => Environment.GetEnvironmentVariable(SharedOrdersVariable);

    public void Prepare(string mode)
    {
        workspace.WriteText(workspace.File("morphir.toml"), BoundedOrdersRecordMother.ProjectConfiguration);
        workspace.WriteText(workspace.File("bounded-orders.mode"), mode);
    }

    public void PrepareCompiledOrders()
    {
        workspace.WriteText(workspace.File("morphir.toml"), BoundedOrdersRecordMother.DocumentTreeConfiguration);
        workspace.WriteText(workspace.File("src/Domain.fs"), BoundedOrdersRecordMother.DomainSource);
        workspace.WriteText(workspace.File("bounded-orders.mode"), "compiled-orders");
    }

    public WorkspaceDirectory RecordTree(int order, int? reserve = null)
    {
        var root = workspace.MakeDirectory(OutDir, "compile.dest", "recorded", "orders-model");
        var prefix = FixtureCatalog.BoundedOrders.Directory + "v4/plain-json/";
        foreach (var fixture in FixtureCatalog.BoundedOrders.Files.Where(item => item.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = fixture.RelativePath[prefix.Length..];
            var path = root.File(relative).Path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (order != 73 && relative == "pkg/acme/orders/domain/order.type.json")
                File.WriteAllText(path, "{");
            else if (order != 73 && relative == "pkg/acme/orders/domain/order.value.json")
            {
                var contents = FixtureCatalog.ReadText(fixture).Replace("IntegerLiteral\": 73",
                    "IntegerLiteral\": " + order.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                File.WriteAllText(path, contents);
            }
            else
                File.WriteAllBytes(path, FixtureCatalog.ReadBytes(fixture));
        }
        if (reserve is { } amount)
        {
            var domain = root.Directory("pkg/acme/orders/domain").Path;
            var modulePath = Path.Combine(domain, "module.json");
            var module = JsonNode.Parse(File.ReadAllText(modulePath))!;
            module["values"]!.AsArray().Insert(1, JsonValue.Create("reserve"));
            File.WriteAllText(modulePath, module.ToJsonString());

            var referencePath = Path.Combine(domain, "total-in-_usd.value.json");
            var reference = JsonNode.Parse(File.ReadAllText(referencePath))!;
            reference["def"]!["Public"]!["ExpressionBody"]!["body"]!["Reference"] = "acme/orders:domain#reserve";
            File.WriteAllText(referencePath, reference.ToJsonString());

            var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(domain, "order.value.json")))!;
            definition["name"] = "reserve";
            definition["def"]!["Public"]!["ExpressionBody"]!["body"]!["Literal"]!["IntegerLiteral"] = amount;
            File.WriteAllText(Path.Combine(domain, "reserve.value.json"), definition.ToJsonString());
        }

        workspace.WriteText(workspace.File(OutDir, "compile.json"), BoundedOrdersRecordMother.CompletedTree);
        return root;
    }

    public Task<CommandResult> GenerateAsync(string output, params string[] extra) => Generate(output, false, extra);
    public Task<CommandResult> GenerateAsync(string output, bool configured) => Generate(output, configured, []);
    public Task<CommandResult> GenerateFromLastCompileAsync(string output) => Generate(output, false, [], outDir: false);

    private Task<CommandResult> Generate(string output, bool configured, string[] extra, bool outDir = true) => generation.GenerateAsync(new GenerationRequest
    {
        Target = "text",
        Output = workspace.Directory(output),
        Options = new GenerationOptions
        {
            Json = JsonOutputArgument.Document,
            Extension = configured ? null : extensions.BuiltExtension(BuiltExtension.BoundedOrders),
            Arguments = outDir ? ["--out-dir", OutDir, .. extra] : extra,
        },
    });

    public void ConfigureOrdersScaleGuest()
    {
        var guest = FixtureCatalog.OrdersScale.Guest;
        workspace.WriteText(workspace.File("morphir.toml"), BoundedOrdersRecordMother.WasmConfiguration
            + "sha256 = \"" + guest.Sha256 + "\"\n");
        workspace.WriteBytes(workspace.File("orders.wasm"), FixtureCatalog.ReadBytes(guest));
    }

    public WorkspaceDirectory RecordScaleProductionTree() => RecordScale(OrdersScaleProductionMother.ProductionDocuments());
    public WorkspaceDirectory RecordScaleDocumentOverTree() => RecordScale(OrdersScaleProductionMother.DocumentOneOver());
    private WorkspaceDirectory RecordScale(IEnumerable<KeyValuePair<string, byte[]>> documents)
    {
        var root = workspace.MakeDirectory(OutDir, "compile.dest", "recorded", "orders-model");
        foreach (var document in documents) workspace.WriteBytes(root.File(document.Key), document.Value);
        workspace.WriteText(workspace.File(OutDir, "compile.json"), BoundedOrdersRecordMother.CompletedTree);
        return root;
    }
    public Task<CommandResult> GenerateOrdersScaleAsync(string output) => Generate(output, true, ["--bounded-profile", "orders-scale"]);
    public void ConfigureMemoryLimitGuest()
    {
        ConfigureWasmProject();
        workspace.WriteBytes(workspace.File("orders.wasm"),
            workspace.ReadBytes(workspace.CopyFixture("MepWasm/fixtures/wasm/initial-memory-4097.wasm")));
    }

    public void ConfigureSharedOrdersGuest(string module)
    {
        ConfigureWasmProject();
        workspace.WriteText(workspace.File("morphir.toml"), BoundedOrdersRecordMother.WasmConfiguration
            + "sha256 = \"d12319c81f125c41d4e073e10d11c61bde9e64ebe14671dcf7ffffc8cb2320e0\"\n");
        workspace.WriteBytes(workspace.File("orders.wasm"), File.ReadAllBytes(module));
    }

    private void ConfigureWasmProject() =>
        workspace.WriteText(workspace.File("morphir.toml"), BoundedOrdersRecordMother.WasmConfiguration);

    public void ReplaceWithFifo(WorkspaceFile file)
    {
        if (MkFifo(file.Path, 0x180) != 0) throw new IOException("Could not create the recorded FIFO.");
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    public void ConfigureWasmLimits(int stateBytes, int frameBytes)
    {
        ConfigureWasmProject();
        var fixture = (stateBytes, frameBytes) switch
        {
            (1, 32768) => FixtureCatalog.BoundedWasmLimits.StateBelowPage,
            (1048576, 4119) => FixtureCatalog.BoundedWasmLimits.FrameOneUnder,
            (1048576, 4120) => FixtureCatalog.BoundedWasmLimits.FrameExact,
            _ => throw new ArgumentException("No fixture selects those WASM limits."),
        };
        workspace.WriteBytes(workspace.File("orders.wasm"), FixtureCatalog.ReadBytes(fixture));
    }
}

/// <summary>Schema-1 producer record written independently of the product's record codec.</summary>
public static class BoundedOrdersRecordMother
{
    public const string ProjectConfiguration = "[project]\nname = \"acme/orders\"\n\n[frontend]\nlanguage = \"fsharp\"\n";

    /// <summary>The Orders project that compiles to a JSON document tree.</summary>
    public const string DocumentTreeConfiguration = ProjectConfiguration + "\n[ir]\nlayout = \"document-tree\"\n";

    /// <summary>
    /// The F# source of the Orders domain that the orders guest reads: the type and the value <c>order</c> (73), and the
    /// value <c>total-in-USD</c> that references it, as in the pinned Orders tree.
    /// </summary>
    public const string DomainSource =
        "namespace Acme.Orders\n\nmodule Domain =\n    type Order = int\n    let order : int = 73\n    let totalInUSD : int = order\n";

    public const string WasmConfiguration = ProjectConfiguration
        + "[extensions.text]\npath = \"orders.wasm\"\nabi = \"extism-kernel-v1\"\n";

    public const string CompletedTree = """
        {"schema":1,"task":"compile","module":"","language":"fsharp","inputs":[],"value":["recorded/orders-model"],"ir":{"path":"recorded/orders-model","layout":"document-tree","format":"json","version":"v4"},"installed":{},"completedAt":"2026-11-18T00:00:00Z"}
        """;
}
