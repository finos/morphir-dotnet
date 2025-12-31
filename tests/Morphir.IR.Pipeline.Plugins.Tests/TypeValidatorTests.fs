module Morphir.IR.Pipeline.Plugins.Tests.TypeValidatorTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins

[<Tests>]
let typeValidatorCreationTests =
    testList "TypeValidator Creation" [
        test "create should return a plugin" {
            let plugin = TypeValidator.create()
            Expect.equal plugin.Name "type-validator" "plugin name should be 'type-validator'"
        }

        test "createWithEnv should return a plugin with environment" {
            let env = TypeValidator.emptyEnv
            let plugin = TypeValidator.createWithEnv env
            Expect.equal plugin.Name "type-validator-with-env" "plugin name should be 'type-validator-with-env'"
        }
    ]

[<Tests>]
let typeValidatorExecutionTests =
    testList "TypeValidator Execution" [
        test "plugin should execute without error" {
            let plugin = TypeValidator.create()
            let file = MorphirFile.empty
            let node = box "test-node"

            let (resultNode, resultFile) = plugin.Transform node file

            Expect.isSome resultNode "result node should be Some"
            Expect.hasLength resultFile.Messages 1 "should have one info message"
            Expect.equal resultFile.Messages.[0].Severity Info "message should be Info"
        }

        test "plugin should preserve node" {
            let plugin = TypeValidator.create()
            let file = MorphirFile.empty
            let testNode = box "test-node"

            let (resultNode, _) = plugin.Transform testNode file

            match resultNode with
            | Some node -> Expect.equal node testNode "node should be preserved"
            | None -> failtest "node should not be None"
        }
    ]

[<Tests>]
let literalTypeInferenceTests =
    testList "Literal Type Inference" [
        test "bool literal should infer Bool type" {
            let lit = Literal.BoolLiteral true
            let typ = TypeValidator.inferLiteralType lit

            match typ with
            | Type.Reference(_, fqName, []) ->
                let fqStr = FQName.toString fqName
                Expect.stringContains fqStr "Bool" "type should be Bool"
            | _ -> failtest "expected Reference type"
        }

        test "string literal should infer String type" {
            let lit = Literal.StringLiteral "test"
            let typ = TypeValidator.inferLiteralType lit

            match typ with
            | Type.Reference(_, fqName, []) ->
                let fqStr = FQName.toString fqName
                Expect.stringContains fqStr "String" "type should be String"
            | _ -> failtest "expected Reference type"
        }

        test "int literal should infer Int type" {
            let lit = Literal.WholeNumberLiteral 42L
            let typ = TypeValidator.inferLiteralType lit

            match typ with
            | Type.Reference(_, fqName, []) ->
                let fqStr = FQName.toString fqName
                Expect.stringContains fqStr "Int" "type should be Int"
            | _ -> failtest "expected Reference type"
        }

        test "float literal should infer Float type" {
            let lit = Literal.FloatLiteral 3.14
            let typ = TypeValidator.inferLiteralType lit

            match typ with
            | Type.Reference(_, fqName, []) ->
                let fqStr = FQName.toString fqName
                Expect.stringContains fqStr "Float" "type should be Float"
            | _ -> failtest "expected Reference type"
        }
    ]

[<Tests>]
let typeEqualityTests =
    testList "Type Equality" [
        test "same type variables should be equal" {
            let t1 = Type.Variable((), Name.fromString "a")
            let t2 = Type.Variable((), Name.fromString "a")
            Expect.isTrue (TypeValidator.typesEqual t1 t2) "same type variables should be equal"
        }

        test "different type variables should not be equal" {
            let t1 = Type.Variable((), Name.fromString "a")
            let t2 = Type.Variable((), Name.fromString "b")
            Expect.isFalse (TypeValidator.typesEqual t1 t2) "different type variables should not be equal"
        }

        test "Unit types should be equal" {
            let t1 = Type.Unit()
            let t2 = Type.Unit()
            Expect.isTrue (TypeValidator.typesEqual t1 t2) "Unit types should be equal"
        }

        test "same function types should be equal" {
            let intType = Type.Variable((), Name.fromString "Int")
            let f1 = Type.Function((), intType, intType)
            let f2 = Type.Function((), intType, intType)
            Expect.isTrue (TypeValidator.typesEqual f1 f2) "same function types should be equal"
        }
    ]
