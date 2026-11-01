module Morphir.IR.Pipeline.Tests.PipelineBuilderTests

open Expecto
open Morphir.IR.Pipeline

let private key1 = Property.create<string> "test.key1"
let private key2 = Property.create<int> "test.key2"
let private key3 = Property.create<bool> "test.key3"
let private configuredKey = Property.create<bool> "test.configured"

[<Tests>]
let pipelineBuilderBasicTests =
    testList "PipelineBuilder Basic" [
        test "empty pipeline should create empty frozen processor" {
            let proc: MorphirProcessor<string> = pipeline { () }

            Expect.isEmpty proc.Parsers "should have no parsers"
            Expect.isEmpty proc.Plugins "should have no plugins"
            Expect.isEmpty proc.Compilers "should have no compilers"
            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen by default"
        }

        test "parse should add parser" {
            let testParser: Parser<string> = fun _ -> Ok "parsed"

            let proc = pipeline { parse testParser }

            Expect.hasLength proc.Parsers 1 "should have 1 parser"
        }

        test "uses should add plugin" {
            let testPlugin: Plugin<string> =
                {
                    Name = "test"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let proc = pipeline { uses testPlugin }

            Expect.hasLength proc.Plugins 1 "should have 1 plugin"
        }

        test "stringify should add compiler" {
            let testCompiler: Compiler<string> = fun _ _ -> VFileTree.empty

            let proc = pipeline { stringify testCompiler }

            Expect.hasLength proc.Compilers 1 "should have 1 compiler"
        }

        test "freeze should freeze processor" {
            let proc: MorphirProcessor<string> = pipeline { freeze }

            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen"
        }

        test "unfrozen should leave the processor unfrozen" {
            let proc: MorphirProcessor<string> = pipeline { unfrozen }

            Expect.isFalse (MorphirProcessor.isFrozen proc) "should stay unfrozen"
        }

        test "unfrozen should not linger once the pipeline is built" {
            let proc: MorphirProcessor<string> = pipeline { unfrozen }
            let frozenAgain = proc |> MorphirProcessor.freeze

            Expect.isTrue (MorphirProcessor.isFrozen frozenAgain) "freezing afterwards should still work"
        }

        test "data should set processor data" {
            let proc: MorphirProcessor<string> = pipeline { data key1 "value" }

            let value = MorphirProcessor.tryGet key1 proc

             
            Expect.equal value (Some "value") "value should match"
        }
    ]

[<Tests>]
let pipelineBuilderCompositionTests =
    testList "PipelineBuilder Composition" [
        test "should support multiple parsers" {
            let parser1: Parser<string> = fun _ -> Result.Error (ParseError.UnsupportedInput "fail")
            let parser2: Parser<string> = fun _ -> Ok "parsed"

            let proc = pipeline {
                parse parser1
                parse parser2
            }

            Expect.hasLength proc.Parsers 2 "should have 2 parsers"
        }

        test "should support multiple plugins" {
            let plugin1: Plugin<string> =
                {
                    Name = "plugin1"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let plugin2: Plugin<string> =
                {
                    Name = "plugin2"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let proc = pipeline {
                uses plugin1
                uses plugin2
            }

            Expect.hasLength proc.Plugins 2 "should have 2 plugins"
            Expect.equal proc.Plugins.[0].Name "plugin1" "first should be plugin1"
            Expect.equal proc.Plugins.[1].Name "plugin2" "second should be plugin2"
        }

        test "should support multiple compilers" {
            let compiler1: Compiler<string> = fun _ _ -> VFileTree.empty
            let compiler2: Compiler<string> = fun _ _ -> VFileTree.empty

            let proc = pipeline {
                stringify compiler1
                stringify compiler2
            }

            Expect.hasLength proc.Compilers 2 "should have 2 compilers"
        }

        test "should support full pipeline" {
            let parser: Parser<string> = fun _ -> Ok "IR"

            let plugin1: Plugin<string> =
                {
                    Name = "validate"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let plugin2: Plugin<string> =
                {
                    Name = "optimize"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let compiler: Compiler<string> = fun _ _ -> VFileTree.empty

            let proc = pipeline {
                parse parser
                uses plugin1
                uses plugin2
                stringify compiler
                freeze
            }

            Expect.hasLength proc.Parsers 1 "should have 1 parser"
            Expect.hasLength proc.Plugins 2 "should have 2 plugins"
            Expect.hasLength proc.Compilers 1 "should have 1 compiler"
            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen"
        }

        test "should support multiple data values" {
            let proc: MorphirProcessor<string> = pipeline {
                data key1 "value1"
                data key2 42
                data key3 true
            }

            let val1 = MorphirProcessor.tryGet key1 proc
            let val2 = MorphirProcessor.tryGet key2 proc
            let val3 = MorphirProcessor.tryGet key3 proc

            Expect.equal val1 (Some "value1") "key1 should match"
            Expect.equal val2 (Some 42) "key2 should match"
            Expect.equal val3 (Some true) "key3 should match"
        }
    ]

[<Tests>]
let pipelineBuilderExecutionTests =
    let parser: Parser<string> = fun _ -> Ok "parsed IR"
    let basePlugin: Plugin<string> = Plugin.create "base" (fun model file -> (Some model, file))
    let variantPlugin: Plugin<string> = Plugin.create "variant" (fun model file -> (Some model, file))

    testList "PipelineBuilder Execution" [
        test "pipeline should infer the model type and run every stage" {
            let validate = Plugin.create "validate" (fun (model: string) file -> (Some model, file |> VFile.info "Validated"))

            let compiler: Compiler<string> =
                fun model _ -> VFileTree.empty |> VFileTree.addFile (VFile.create "out.txt" model)

            let proc = pipeline {
                parse parser
                uses validate
                stringify compiler
            }

            let result = MorphirProcessor.processFile (VFile.fromPath "/test/input.json") proc

            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen by default"
            Expect.hasLength result.Messages 1 "should move the plugin's message to the tree"
            Expect.hasLength (VFileTree.allFiles result) 1 "should produce the compiler's file"
        }

        test "frozen pipeline should create variants" {
            let basePipeline = pipeline {
                parse parser
                uses basePlugin
                freeze
            }

            let variant = basePipeline |> MorphirProcessor.plugin variantPlugin

            Expect.isTrue (MorphirProcessor.isFrozen basePipeline) "base should remain frozen"
            Expect.hasLength basePipeline.Plugins 1 "base should have 1 plugin"
            Expect.isFalse (MorphirProcessor.isFrozen variant) "variant should be unfrozen"
            Expect.hasLength variant.Plugins 2 "variant should have 2 plugins"
        }

        test "pipeline with plugin configuration" {
            let configuring: Plugin<string> =
                { basePlugin with Configure = PropertyBag.set configuredKey true }

            let proc = pipeline { uses configuring }

            Expect.equal (MorphirProcessor.tryGet configuredKey proc) (Some true) "plugin should have configured processor"
        }

        test "Pipeline.immutable should freeze" {
            let proc = Pipeline.immutable { parse parser }

            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen"
        }

        test "Pipeline.mut should stay unfrozen" {
            let proc = Pipeline.mut { parse parser }

            Expect.isFalse (MorphirProcessor.isFrozen proc) "should be unfrozen"
        }

        test "a pipeline with an error should report it and produce no files" {
            let strict =
                Plugin.create "strict" (fun (model: string) file ->
                    (Some model, file |> VFile.error "Type mismatch" None |> VFile.error "Undefined variable" None))

            let warning = Plugin.create "warn" (fun (model: string) file -> (Some model, file |> VFile.warn "Deprecated" None))

            let compiler: Compiler<string> =
                fun model _ -> VFileTree.empty |> VFileTree.addFile (VFile.create "out.txt" model)

            let proc = pipeline {
                parse parser
                uses strict
                uses warning
                stringify compiler
            }

            let result = MorphirProcessor.processFile (VFile.fromPath "/test/input.json") proc
            let stats = VFileTree.statistics result

            Expect.equal stats.ErrorCount 2 "should have 2 errors"
            Expect.equal stats.WarningCount 1 "later plugins still run after an error"
            Expect.equal stats.TotalFiles 0 "compilers should not run"
        }
    ]