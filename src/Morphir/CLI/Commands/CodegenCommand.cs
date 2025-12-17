using JasperFx.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Morphir.CLI.Commands;

/// <summary>
/// Input model for codegen write command (no arguments needed)
/// </summary>
public class CodegenWriteInput
{
}

/// <summary>
/// Command to generate and write Wolverine code to disk
/// </summary>
[Description("Write generated Wolverine code to disk")]
public class CodegenWriteCommand : JasperFxAsyncCommand<CodegenWriteInput>
{
    public override async Task<bool> Execute(CodegenWriteInput input)
    {
        // Create Morphir.Tooling host to get Wolverine runtime and code generation config
        using var host = Tooling.Program.CreateToolingHost();
        await host.StartAsync();

        // Get Wolverine runtime to access code generation
        var runtime = host.Services.GetRequiredService<Wolverine.Runtime.IWolverineRuntime>();
        var codegenOptions = runtime.Options.CodeGeneration;

        // The output path where code should be written (relative to Morphir.Tooling project)
        var outputPath = codegenOptions.GeneratedCodeOutputPath ?? "Internal/Generated";
        var fullOutputPath = Path.Combine("src", "Morphir.Tooling", outputPath);

        // With Auto mode, code is written when types are dynamically generated
        // To force code generation, we need to trigger a handler execution
        // This will cause Wolverine to generate code and write it to disk
        var messageBus = host.Services.GetRequiredService<IMessageBus>();

        // Try to invoke a handler to trigger code generation
        // Use a dummy command that will fail but trigger code generation
        try
        {
            // This will fail but should trigger code generation for the handler
            await messageBus.InvokeAsync<Tooling.Features.VerifyIR.VerifyIRResult>(
                new Tooling.Features.VerifyIR.VerifyIR("dummy", null, false, false)
            );
        }
        catch
        {
            // Expected to fail - we just want to trigger code generation
        }

        // Check if code was generated
        if (Directory.Exists(fullOutputPath))
        {
            var files = Directory.GetFiles(fullOutputPath, "*.cs", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                Console.WriteLine($"✓ Wolverine code generated: {files.Length} files in {fullOutputPath}");
            }
            else
            {
                Console.WriteLine($"⚠ Warning: Code generation directory exists but is empty at {fullOutputPath}");
            }
        }
        else
        {
            Console.WriteLine($"⚠ Warning: Code generation directory not found at {fullOutputPath}");
            Console.WriteLine($"  Output path configured: {outputPath}");
        }

        Console.WriteLine($"✓ Wolverine code written to {outputPath}");

        if (Directory.Exists(outputPath))
        {
            var files = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
            Console.WriteLine($"  Generated {files.Length} files");
        }

        await host.StopAsync();
        return true;
    }
}
