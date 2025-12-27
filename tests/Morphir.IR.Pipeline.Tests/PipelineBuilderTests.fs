module Morphir.IR.Pipeline.Tests.PipelineBuilderTests

open Expecto
open Morphir.IR.Pipeline

[<Tests>]
let pipelineBuilderBasicTests =
    testList "PipelineBuilder Basic" [
        test "empty pipeline should create empty processor" {
            let proc = pipeline { () }

            Expect.isEmpty proc.Parsers "should have no parsers"
            Expect.isEmpty proc.Plugins "should have no plugins"
            Expect.isEmpty proc.Compilers "should have no compilers"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "should not be frozen"
        }

        test "parse should add parser" {
            let testParser: Parser = fun file -> Result.Ok(box "parsed")

            let proc = pipeline { parse testParser }

            Expect.hasLength proc.Parsers 1 "should have 1 parser"
        }

        test "plugin should add plugin" {
            let testPlugin: Plugin =
                {
                    Name = "test"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let proc = pipeline { plugin testPlugin }

            Expect.hasLength proc.Plugins 1 "should have 1 plugin"
        }

        test "stringify should add compiler" {
            let testCompiler: Compiler = fun node file -> file

            let proc = pipeline { stringify testCompiler }

            Expect.hasLength proc.Compilers 1 "should have 1 compiler"
        }

        test "freeze should freeze processor" {
            let proc = pipeline { freeze }

            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen"
        }

        test "data should set processor data" {
            let proc = pipeline { data "key" (box "value") }

            let value = MorphirProcessor.getData "key" proc

            Expect.isSome value "should have data"
            Expect.equal value (Some(box "value")) "value should match"
        }
    ]

[<Tests>]
let pipelineBuilderCompositionTests =
    testList "PipelineBuilder Composition" [
        test "should support multiple parsers" {
            let parser1: Parser = fun file -> Result.Error "fail"
            let parser2: Parser = fun file -> Result.Ok(box "parsed")

            let proc = pipeline {
                parse parser1
                parse parser2
            }

            Expect.hasLength proc.Parsers 2 "should have 2 parsers"
        }

        test "should support multiple plugins" {
            let plugin1: Plugin =
                {
                    Name = "plugin1"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let plugin2: Plugin =
                {
                    Name = "plugin2"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let proc = pipeline {
                plugin plugin1
                plugin plugin2
            }

            Expect.hasLength proc.Plugins 2 "should have 2 plugins"
            Expect.equal proc.Plugins.[0].Name "plugin1" "first should be plugin1"
            Expect.equal proc.Plugins.[1].Name "plugin2" "second should be plugin2"
        }

        test "should support multiple compilers" {
            let compiler1: Compiler = fun node file -> file
            let compiler2: Compiler = fun node file -> file

            let proc = pipeline {
                stringify compiler1
                stringify compiler2
            }

            Expect.hasLength proc.Compilers 2 "should have 2 compilers"
        }

        test "should support full pipeline" {
            let parser: Parser = fun file -> Result.Ok(box "IR")

            let plugin1: Plugin =
                {
                    Name = "validate"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let plugin2: Plugin =
                {
                    Name = "optimize"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let compiler: Compiler = fun node file -> file

            let proc = pipeline {
                parse parser
                plugin plugin1
                plugin plugin2
                stringify compiler
                freeze
            }

            Expect.hasLength proc.Parsers 1 "should have 1 parser"
            Expect.hasLength proc.Plugins 2 "should have 2 plugins"
            Expect.hasLength proc.Compilers 1 "should have 1 compiler"
            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen"
        }

        test "should support multiple data values" {
            let proc = pipeline {
                data "key1" (box "value1")
                data "key2" (box 42)
                data "key3" (box true)
            }

            let val1 = MorphirProcessor.getDataAs<string> "key1" proc
            let val2 = MorphirProcessor.getDataAs<int> "key2" proc
            let val3 = MorphirProcessor.getDataAs<bool> "key3" proc

            Expect.equal val1 (Some "value1") "key1 should match"
            Expect.equal val2 (Some 42) "key2 should match"
            Expect.equal val3 (Some true) "key3 should match"
        }
    ]

[<Tests>]
let pipelineBuilderExecutionTests =
    testList "PipelineBuilder Execution" [
        test "pipeline should execute successfully" {
            let parser: Parser = fun file -> Result.Ok(box "parsed IR")

            let validatePlugin: Plugin =
                {
                    Name = "validate"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Validated")
                }

            let compiler: Compiler = fun node file -> file |> MorphirFile.info "Compiled"

            let proc = pipeline {
                parse parser
                plugin validatePlugin
                stringify compiler
            }

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isSome result.Content "should have content"
            Expect.hasLength result.Messages 2 "should have 2 messages"
        }

        test "frozen pipeline should create variants" {
            let parser: Parser = fun file -> Result.Ok(box "IR")

            let basePlugin: Plugin =
                {
                    Name = "base"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let variantPlugin: Plugin =
                {
                    Name = "variant"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let basePipeline = pipeline {
                parse parser
                plugin basePlugin
                freeze
            }

            let variantPipeline = pipeline {
                parse parser
                plugin basePlugin
                plugin variantPlugin
                freeze
            }

            Expect.isTrue (MorphirProcessor.isFrozen basePipeline) "base should be frozen"
            Expect.hasLength basePipeline.Plugins 1 "base should have 1 plugin"

            Expect.isTrue (MorphirProcessor.isFrozen variantPipeline) "variant should be frozen"
            Expect.hasLength variantPipeline.Plugins 2 "variant should have 2 plugins"
        }

        test "pipeline with plugin configuration" {
            let configuringPlugin: Plugin =
                {
                    Name = "configuring"
                    Configure = fun proc -> MorphirProcessor.setData "configured" (box true) proc
                    Transform = fun node file -> (Some node, file)
                }

            let proc = pipeline { plugin configuringPlugin }

            let configured = MorphirProcessor.getDataAs<bool> "configured" proc

            Expect.equal configured (Some true) "plugin should have configured processor"
        }
    ]

[<Tests>]
let pipelineBuilderIntegrationTests =
    testList "PipelineBuilder Integration" [
        test "realistic IR validation pipeline" {
            let irParser: Parser = fun file -> Result.Ok(box "{ module: 'Test' }")

            let syntaxPlugin: Plugin =
                {
                    Name = "syntax-validator"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Syntax valid")
                }

            let semanticPlugin: Plugin =
                {
                    Name = "semantic-validator"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Semantics valid")
                }

            let normalizePlugin: Plugin =
                {
                    Name = "normalizer"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Normalized")
                }

            let jsonCompiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiled to JSON"

            let validationPipeline = pipeline {
                parse irParser
                plugin syntaxPlugin
                plugin semanticPlugin
                plugin normalizePlugin
                stringify jsonCompiler
                freeze
            }

            let inputFile = MorphirFile.fromPath "/test/module.json"
            let result = MorphirProcessor.processFile inputFile validationPipeline

            Expect.isFalse (MorphirFile.hasErrors result) "should have no errors"
            Expect.hasLength result.Messages 4 "should have 4 info messages"
        }

        test "optimization pipeline variant" {
            let parser: Parser = fun file -> Result.Ok(box 100)

            let validatePlugin: Plugin =
                {
                    Name = "validate"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Validated")
                }

            let basePipeline = pipeline {
                parse parser
                plugin validatePlugin
                freeze
            }

            let optimizePlugin: Plugin =
                {
                    Name = "optimize"
                    Configure = id
                    Transform =
                        fun node file ->
                            let value = unbox<int> node
                            (Some(box (value * 2)), file |> MorphirFile.info "Optimized")
                }

            // Create variant by adding optimize plugin to frozen base
            let optimizedPipeline =
                basePipeline |> MorphirProcessor.plugin optimizePlugin

            Expect.isTrue (MorphirProcessor.isFrozen basePipeline) "base should remain frozen"

            Expect.isFalse (MorphirProcessor.isFrozen optimizedPipeline) "variant should be unfrozen"

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let baseResult = MorphirProcessor.processFile inputFile basePipeline
            let optResult = MorphirProcessor.processFile inputFile optimizedPipeline

            let baseValue = baseResult.Content |> Option.map unbox<int>
            let optValue = optResult.Content |> Option.map unbox<int>

            Expect.equal baseValue (Some 100) "base should return 100"
            Expect.equal optValue (Some 200) "optimized should return 200"
        }

        test "error handling pipeline" {
            let parser: Parser = fun file -> Result.Ok(box "IR with errors")

            let strictValidatorPlugin: Plugin =
                {
                    Name = "strict-validator"
                    Configure = id
                    Transform =
                        fun node file ->
                            let updatedFile =
                                file
                                |> MorphirFile.error "Type mismatch in function A" None
                                |> MorphirFile.error "Undefined variable in function B" None

                            (Some node, updatedFile)
                }

            let warningPlugin: Plugin =
                {
                    Name = "deprecation-checker"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.warn "Deprecated API usage" None)
                }

            let errorPipeline = pipeline {
                parse parser
                plugin strictValidatorPlugin
                plugin warningPlugin
            }

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile errorPipeline

            Expect.isTrue (MorphirFile.hasErrors result) "should have errors"
            let errors = MorphirFile.errors result
            let warnings = MorphirFile.warnings result

            Expect.hasLength errors 2 "should have 2 errors"
            Expect.hasLength warnings 1 "should have 1 warning"
        }
    ]
