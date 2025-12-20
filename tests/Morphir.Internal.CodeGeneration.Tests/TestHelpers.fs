namespace Morphir.Internal.CodeGeneration.Tests

open System
open System.IO
open Myriad.Core
open Morphir.Internal.CodeGeneration.Plugin
open Morphir.Internal.CodeGeneration.Generators
open Expecto

/// Helper functions for testing Myriad generators
module TestHelpers =

    /// Create a mock GeneratorContext for testing
    /// Note: This is a simplified version - in real usage, Myriad provides the context
    let createMockContext (inputFile: string) : GeneratorContext =
        // For testing purposes, we'll use Unchecked.defaultof to create a minimal context
        // In practice, Myriad creates the context automatically
        Unchecked.defaultof<GeneratorContext>

    /// Run a generator and return the output
    /// Note: For stub generators, we can test without a real context since they return empty output
    let runGenerator (generator: IMyriadGenerator) (_inputFile: string) : Output =
        // Use a default context for stub testing
        // Stub generators ignore the context anyway
        let context = Unchecked.defaultof<GeneratorContext>
        generator.Generate(context)

    /// Get a generator by name from the plugin
    let getGeneratorByName (name: string) : IMyriadGenerator option =
        Plugin.generators
        |> List.tryFind (fun gen ->
            // Check if generator matches the name
            // This is a simplified check - in real implementation,
            // we'd use reflection or a name property
            match gen with
            | :? JsonCodecGenerator when name = "json-codec" -> true
            | :? VisitorGenerator when name = "visitor" -> true
            | :? LensGenerator when name = "lenses" -> true
            | :? ActivePatternGenerator when name = "active-patterns" -> true
            | :? BuilderGenerator when name = "builder" -> true
            | _ -> false
        )

    /// Assert that a generator returns output (even if empty for stubs)
    let assertGeneratorReturnsOutput (generator: IMyriadGenerator) (inputFile: string) : unit =
        let output = runGenerator generator inputFile
        match output with
        | Output.Ast ast ->
            // For now, stubs return empty AST, which is valid
            // In future phases, we'll assert non-empty AST
            ()
        | _ -> failwith "Unexpected output type"

    /// Assert that a generator accepts a file extension
    let assertGeneratorAcceptsExtension (generator: IMyriadGenerator) (extension: string) : unit =
        let validExtensions = generator.ValidInputExtensions
        if not (validExtensions |> Seq.contains extension) then
            failwith $"Generator does not accept extension: {extension}"

    /// Get the path to a test data file
    let getTestDataPath (fileName: string) : string =
        let testDataDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",
            "tests", "Morphir.Internal.CodeGeneration.Tests", "TestData"
        )
        Path.Combine(testDataDir, fileName)
        |> Path.GetFullPath

    /// Test that all generators are registered
    let testAllGeneratorsRegistered () : unit =
        let expectedGenerators = [
            "json-codec"
            "visitor"
            "lenses"
            "active-patterns"
            "builder"
        ]
        
        for name in expectedGenerators do
            match getGeneratorByName name with
            | Some _ -> () // Generator found
            | None -> failwith $"Generator '{name}' not found in plugin"

