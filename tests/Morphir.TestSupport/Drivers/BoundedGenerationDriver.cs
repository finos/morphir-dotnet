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
    WorkspaceDirectory RecordTree(int order, int? reserve = null);
    Task<CommandResult> GenerateAsync(string output, params string[] extra);
    Task<CommandResult> GenerateAsync(string output, bool configured);
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

    private Task<CommandResult> Generate(string output, bool configured, string[] extra) => generation.GenerateAsync(new GenerationRequest
    {
        Target = "text",
        Output = workspace.Directory(output),
        Options = new GenerationOptions
        {
            Json = JsonOutputArgument.Document,
            Extension = configured ? null : extensions.BuiltExtension(BuiltExtension.BoundedOrders),
            Arguments = ["--out-dir", OutDir, .. extra],
        },
    });

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

    public const string WasmConfiguration = ProjectConfiguration
        + "[extensions.text]\npath = \"orders.wasm\"\nabi = \"extism-kernel-v1\"\n";

    public const string CompletedTree = """
        {"schema":1,"task":"compile","module":"","language":"fsharp","inputs":[],"value":["recorded/orders-model"],"ir":{"path":"recorded/orders-model","layout":"document-tree","format":"json","version":"v4"},"installed":{},"completedAt":"2026-11-18T00:00:00Z"}
        """;
}
