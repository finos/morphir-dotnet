namespace Morphir.Internal.CodeGeneration.Tests.Generators

open Expecto
open Myriad.Core
open Morphir.Internal.CodeGeneration.Generators
open Morphir.Internal.CodeGeneration.Plugin
open Morphir.Internal.CodeGeneration.Tests.TestHelpers

/// Tests for LensGenerator
module LensGeneratorTests =

    [<Tests>]
    let tests = testList "LensGenerator" [
        
        test "Generator can be instantiated" {
            let generator = LensGenerator()
            // Generator is a value type, so we just verify it was created
            Expect.isTrue true "Generator should be instantiated"
        }

        test "Generator accepts .fs file extension" {
            let generator = LensGenerator() :> IMyriadGenerator
            assertGeneratorAcceptsExtension generator ".fs"
        }

        test "Generator returns output for valid input" {
            let generator = LensGenerator() :> IMyriadGenerator
            let testFile = getTestDataPath "LensTestTypes.fs"
            assertGeneratorReturnsOutput generator testFile
        }

        test "Generator returns empty AST for stub implementation" {
            let generator = LensGenerator() :> IMyriadGenerator
            let testFile = getTestDataPath "LensTestTypes.fs"
            let output = runGenerator generator testFile
            
            match output with
            | Output.Ast ast ->
                // Stub implementation returns empty AST
                Expect.equal (List.length ast) 0 "Stub generator should return empty AST"
            | _ -> failtest "Expected Output.Ast"
        }

        test "Generator is registered in plugin" {
            let generator = LensGenerator() :> IMyriadGenerator
            let registered = 
                Plugin.generators 
                |> List.exists (fun g -> g.GetType() = generator.GetType())
            Expect.isTrue registered "Generator should be registered in plugin"
        }
    ]

