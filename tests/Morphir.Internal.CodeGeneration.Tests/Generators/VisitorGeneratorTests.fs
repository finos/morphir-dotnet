namespace Morphir.Internal.CodeGeneration.Tests.Generators

open Expecto
open Myriad.Core
open Morphir.Internal.CodeGeneration.Generators
open Morphir.Internal.CodeGeneration.Plugin
open Morphir.Internal.CodeGeneration.Tests.TestHelpers

/// Tests for VisitorGenerator
module VisitorGeneratorTests =

    [<Tests>]
    let tests = testList "VisitorGenerator" [
        
        test "Generator can be instantiated" {
            let generator = VisitorGenerator()
            // Generator is a value type, so we just verify it was created
            Expect.isTrue true "Generator should be instantiated"
        }

        test "Generator accepts .fs file extension" {
            let generator = VisitorGenerator() :> IMyriadGenerator
            assertGeneratorAcceptsExtension generator ".fs"
        }

        test "Generator returns output for valid input" {
            let generator = VisitorGenerator() :> IMyriadGenerator
            let testFile = getTestDataPath "VisitorTestTypes.fs"
            assertGeneratorReturnsOutput generator testFile
        }

        test "Generator returns empty AST for stub implementation" {
            let generator = VisitorGenerator() :> IMyriadGenerator
            let testFile = getTestDataPath "VisitorTestTypes.fs"
            let output = runGenerator generator testFile
            
            match output with
            | Output.Ast ast ->
                // Stub implementation returns empty AST
                Expect.equal (List.length ast) 0 "Stub generator should return empty AST"
            | _ -> failtest "Expected Output.Ast"
        }

        test "Generator is registered in plugin" {
            let generator = VisitorGenerator() :> IMyriadGenerator
            let registered = 
                Plugin.generators 
                |> List.exists (fun g -> g.GetType() = generator.GetType())
            Expect.isTrue registered "Generator should be registered in plugin"
        }
    ]

