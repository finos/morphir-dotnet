module Morphir.IR.Pipeline.Plugins.Tests.OptimizerTests

open Expecto
open Morphir.IR.Classic
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins

[<Tests>]
let optimizerCreationTests =
    testList "Optimizer Creation" [
        test "create should return a plugin" {
            let plugin = Optimizer.create()
            Expect.equal plugin.Name "optimizer" "plugin name should be 'optimizer'"
        }

        test "createWithPasses should return a plugin with pass count" {
            let plugin = Optimizer.createWithPasses 3
            Expect.equal plugin.Name "optimizer-3-passes" "plugin name should include pass count"
        }
    ]

[<Tests>]
let optimizerExecutionTests =
    testList "Optimizer Execution" [
        test "plugin should execute without error" {
            let plugin = Optimizer.create()
            let file = VFile.empty
            let node = box "test-node"

            let (resultNode, resultFile) = plugin.Transform node file

            Expect.isSome resultNode "result node should be Some"
            Expect.hasLength resultFile.Messages 1 "should have one info message"
            Expect.equal resultFile.Messages.[0].Severity Info "message should be Info"
        }
    ]

[<Tests>]
let constantFoldingTests =
    testList "Constant Folding" [
        test "add two integers should fold" {
            let result = Optimizer.tryFoldBinary "add" (Literal.WholeNumberLiteral 2L) (Literal.WholeNumberLiteral 3L)
            match result with
            | Some (Literal.WholeNumberLiteral n) -> Expect.equal n 5L "2 + 3 should equal 5"
            | _ -> failtest "expected folded WholeNumberLiteral"
        }

        test "subtract two integers should fold" {
            let result = Optimizer.tryFoldBinary "subtract" (Literal.WholeNumberLiteral 5L) (Literal.WholeNumberLiteral 3L)
            match result with
            | Some (Literal.WholeNumberLiteral n) -> Expect.equal n 2L "5 - 3 should equal 2"
            | _ -> failtest "expected folded WholeNumberLiteral"
        }

        test "multiply two integers should fold" {
            let result = Optimizer.tryFoldBinary "multiply" (Literal.WholeNumberLiteral 4L) (Literal.WholeNumberLiteral 5L)
            match result with
            | Some (Literal.WholeNumberLiteral n) -> Expect.equal n 20L "4 * 5 should equal 20"
            | _ -> failtest "expected folded WholeNumberLiteral"
        }

        test "divide two integers should fold" {
            let result = Optimizer.tryFoldBinary "divide" (Literal.WholeNumberLiteral 10L) (Literal.WholeNumberLiteral 2L)
            match result with
            | Some (Literal.WholeNumberLiteral n) -> Expect.equal n 5L "10 / 2 should equal 5"
            | _ -> failtest "expected folded WholeNumberLiteral"
        }

        test "divide by zero should not fold" {
            let result = Optimizer.tryFoldBinary "divide" (Literal.WholeNumberLiteral 10L) (Literal.WholeNumberLiteral 0L)
            Expect.isNone result "divide by zero should not fold"
        }

        test "and two booleans should fold" {
            let result = Optimizer.tryFoldBinary "and" (Literal.BoolLiteral true) (Literal.BoolLiteral false)
            match result with
            | Some (Literal.BoolLiteral b) -> Expect.isFalse b "true && false should be false"
            | _ -> failtest "expected folded BoolLiteral"
        }

        test "or two booleans should fold" {
            let result = Optimizer.tryFoldBinary "or" (Literal.BoolLiteral true) (Literal.BoolLiteral false)
            match result with
            | Some (Literal.BoolLiteral b) -> Expect.isTrue b "true || false should be true"
            | _ -> failtest "expected folded BoolLiteral"
        }

        test "append two strings should fold" {
            let result = Optimizer.tryFoldBinary "append" (Literal.StringLiteral "hello") (Literal.StringLiteral " world")
            match result with
            | Some (Literal.StringLiteral s) -> Expect.equal s "hello world" "strings should concatenate"
            | _ -> failtest "expected folded StringLiteral"
        }

        test "add two floats should fold" {
            let result = Optimizer.tryFoldBinary "add" (Literal.FloatLiteral 1.5) (Literal.FloatLiteral 2.5)
            match result with
            | Some (Literal.FloatLiteral f) -> Expect.floatClose Accuracy.medium f 4.0 "1.5 + 2.5 should equal 4.0"
            | _ -> failtest "expected folded FloatLiteral"
        }

        test "equal literals should fold to true" {
            let result = Optimizer.tryFoldBinary "equal" (Literal.WholeNumberLiteral 5L) (Literal.WholeNumberLiteral 5L)
            match result with
            | Some (Literal.BoolLiteral b) -> Expect.isTrue b "5 == 5 should be true"
            | _ -> failtest "expected folded BoolLiteral"
        }

        test "unequal literals should fold to false" {
            let result = Optimizer.tryFoldBinary "equal" (Literal.WholeNumberLiteral 5L) (Literal.WholeNumberLiteral 3L)
            match result with
            | Some (Literal.BoolLiteral b) -> Expect.isFalse b "5 == 3 should be false"
            | _ -> failtest "expected folded BoolLiteral"
        }

        test "lessThan should fold correctly" {
            let result = Optimizer.tryFoldBinary "lessThan" (Literal.WholeNumberLiteral 3L) (Literal.WholeNumberLiteral 5L)
            match result with
            | Some (Literal.BoolLiteral b) -> Expect.isTrue b "3 < 5 should be true"
            | _ -> failtest "expected folded BoolLiteral"
        }

        test "greaterThan should fold correctly" {
            let result = Optimizer.tryFoldBinary "greaterThan" (Literal.WholeNumberLiteral 5L) (Literal.WholeNumberLiteral 3L)
            match result with
            | Some (Literal.BoolLiteral b) -> Expect.isTrue b "5 > 3 should be true"
            | _ -> failtest "expected folded BoolLiteral"
        }
    ]

[<Tests>]
let optimizationStatsTests =
    testList "Optimization Statistics" [
        test "empty stats should have zero counts" {
            let stats = Optimizer.emptyStats
            Expect.equal stats.ConstantFolds 0 "should have 0 constant folds"
            Expect.equal stats.DeadCodeEliminations 0 "should have 0 dead code eliminations"
            Expect.equal stats.IdentityEliminations 0 "should have 0 identity eliminations"
        }
    ]
