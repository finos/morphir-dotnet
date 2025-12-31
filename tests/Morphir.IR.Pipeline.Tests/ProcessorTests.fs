module Morphir.IR.Pipeline.Tests.ProcessorTests

open Expecto
open Morphir.IR.Pipeline

[<Tests>]
let processorCreationTests =
    testList "MorphirProcessor Creation" [
        test "empty should create processor with no components" {
            let proc = MorphirProcessor.empty

            Expect.isEmpty proc.Parsers "parsers should be empty"
            Expect.isEmpty proc.Plugins "plugins should be empty"
            Expect.isEmpty proc.Compilers "compilers should be empty"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "processor should not be frozen"
            Expect.equal proc.Data.Count 0 "data should be empty"
        }

        test "isFrozen should return false for unfrozen processor" {
            let proc = MorphirProcessor.empty

            Expect.isFalse (MorphirProcessor.isFrozen proc) "should not be frozen"
        }
    ]

[<Tests>]
let processorParserTests =
    testList "MorphirProcessor Parsers" [
        test "parse should add parser to unfrozen processor" {
            let testParser: Parser =
                fun file -> Ok(box "parsed content")

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse testParser

            Expect.hasLength proc.Parsers 1 "should have 1 parser"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "should remain unfrozen"
        }

        test "parse should add multiple parsers in order" {
            let parser1: Parser = fun file -> Ok(box "parser1")
            let parser2: Parser = fun file -> Ok(box "parser2")

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser1
                |> MorphirProcessor.parse parser2

            Expect.hasLength proc.Parsers 2 "should have 2 parsers"
        }

        test "parse should create unfrozen copy when adding to frozen processor" {
            let testParser: Parser = fun file -> Ok(box "parsed")

            let frozen =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            let withParser = frozen |> MorphirProcessor.parse testParser

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen withParser) "new processor should be unfrozen"
            Expect.hasLength withParser.Parsers 1 "new processor should have parser"
            Expect.isEmpty frozen.Parsers "original should have no parsers"
        }
    ]

[<Tests>]
let processorPluginTests =
    testList "MorphirProcessor Plugins" [
        test "plugin should add plugin to unfrozen processor" {
            let testPlugin: Plugin =
                {
                    Name = "test-plugin"
                    Configure = id // No configuration
                    Transform = fun node file -> (Some node, file)
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.plugin testPlugin

            Expect.hasLength proc.Plugins 1 "should have 1 plugin"
            Expect.equal proc.Plugins.[0].Name "test-plugin" "plugin name should match"
        }

        test "plugin should allow plugin to configure processor" {
            let configuringPlugin: Plugin =
                {
                    Name = "configuring-plugin"
                    Configure = fun proc -> MorphirProcessor.setData "configured" (box true) proc
                    Transform = fun node file -> (Some node, file)
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.plugin configuringPlugin

            let configData = MorphirProcessor.getDataAs<bool> "configured" proc

            Expect.equal configData (Some true) "plugin should have configured processor"
        }

        test "plugin should add multiple plugins in order" {
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

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.plugin plugin1
                |> MorphirProcessor.plugin plugin2

            Expect.hasLength proc.Plugins 2 "should have 2 plugins"
            Expect.equal proc.Plugins.[0].Name "plugin1" "first plugin should be plugin1"
            Expect.equal proc.Plugins.[1].Name "plugin2" "second plugin should be plugin2"
        }

        test "plugin should create unfrozen copy when adding to frozen processor" {
            let testPlugin: Plugin =
                {
                    Name = "test"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let frozen =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            let withPlugin = frozen |> MorphirProcessor.plugin testPlugin

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen withPlugin) "new processor should be unfrozen"
            Expect.hasLength withPlugin.Plugins 1 "new processor should have plugin"
            Expect.isEmpty frozen.Plugins "original should have no plugins"
        }
    ]

[<Tests>]
let processorCompilerTests =
    testList "MorphirProcessor Compilers" [
        test "stringify should add compiler to unfrozen processor" {
            let testCompiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiled"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.stringify testCompiler

            Expect.hasLength proc.Compilers 1 "should have 1 compiler"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "should remain unfrozen"
        }

        test "stringify should add multiple compilers in order" {
            let compiler1: Compiler = fun node file -> file |> MorphirFile.info "Compiler1"
            let compiler2: Compiler = fun node file -> file |> MorphirFile.info "Compiler2"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.stringify compiler1
                |> MorphirProcessor.stringify compiler2

            Expect.hasLength proc.Compilers 2 "should have 2 compilers"
        }

        test "stringify should create unfrozen copy when adding to frozen processor" {
            let testCompiler: Compiler = fun node file -> file

            let frozen =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            let withCompiler = frozen |> MorphirProcessor.stringify testCompiler

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"

            Expect.isFalse (MorphirProcessor.isFrozen withCompiler) "new processor should be unfrozen"

            Expect.hasLength withCompiler.Compilers 1 "new processor should have compiler"
            Expect.isEmpty frozen.Compilers "original should have no compilers"
        }
    ]

[<Tests>]
let processorFreezingTests =
    testList "MorphirProcessor Freezing" [
        test "freeze should set frozen flag" {
            let proc =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            Expect.isTrue (MorphirProcessor.isFrozen proc) "processor should be frozen"
        }

        test "frozen processor should remain frozen" {
            let frozen =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "should be frozen"
            Expect.isTrue (MorphirProcessor.isFrozen frozen) "should remain frozen on check"
        }

        test "frozen processor should create unfrozen variants" {
            let testParser: Parser = fun file -> Ok(box "parsed")
            let testPlugin: Plugin =
                {
                    Name = "test"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }
            let testCompiler: Compiler = fun node file -> file

            let frozen =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            let variant1 = frozen |> MorphirProcessor.parse testParser
            let variant2 = frozen |> MorphirProcessor.plugin testPlugin
            let variant3 = frozen |> MorphirProcessor.stringify testCompiler

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen variant1) "variant1 should be unfrozen"
            Expect.isFalse (MorphirProcessor.isFrozen variant2) "variant2 should be unfrozen"
            Expect.isFalse (MorphirProcessor.isFrozen variant3) "variant3 should be unfrozen"
        }

        test "should support base + variant pattern" {
            let parser: Parser = fun file -> Ok(box "parsed")
            let basePlugin: Plugin =
                {
                    Name = "base"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }
            let variantPlugin1: Plugin =
                {
                    Name = "variant1"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }
            let variantPlugin2: Plugin =
                {
                    Name = "variant2"
                    Configure = id
                    Transform = fun node file -> (Some node, file)
                }

            let base' =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin basePlugin
                |> MorphirProcessor.freeze

            let variant1 =
                base' |> MorphirProcessor.plugin variantPlugin1

            let variant2 =
                base' |> MorphirProcessor.plugin variantPlugin2

            // Base should be frozen and have 1 parser and 1 plugin
            Expect.isTrue (MorphirProcessor.isFrozen base') "base should be frozen"
            Expect.hasLength base'.Parsers 1 "base should have 1 parser"
            Expect.hasLength base'.Plugins 1 "base should have 1 plugin (base)"

            // Variant1 should be unfrozen and have 2 plugins
            Expect.isFalse (MorphirProcessor.isFrozen variant1) "variant1 should be unfrozen"
            Expect.hasLength variant1.Parsers 1 "variant1 should have 1 parser (from base)"
            Expect.hasLength variant1.Plugins 2 "variant1 should have 2 plugins (base + variant1)"
            Expect.equal variant1.Plugins.[0].Name "base" "variant1 first plugin should be base"
            Expect.equal variant1.Plugins.[1].Name "variant1" "variant1 second plugin should be variant1"

            // Variant2 should be unfrozen and have 2 plugins
            Expect.isFalse (MorphirProcessor.isFrozen variant2) "variant2 should be unfrozen"
            Expect.hasLength variant2.Parsers 1 "variant2 should have 1 parser (from base)"
            Expect.hasLength variant2.Plugins 2 "variant2 should have 2 plugins (base + variant2)"
            Expect.equal variant2.Plugins.[0].Name "base" "variant2 first plugin should be base"
            Expect.equal variant2.Plugins.[1].Name "variant2" "variant2 second plugin should be variant2"
        }
    ]

[<Tests>]
let processorDataTests =
    testList "MorphirProcessor Data" [
        test "setData should store data value" {
            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.setData "key1" (box "value1")

            let value = MorphirProcessor.getData "key1" proc

            Expect.isSome value "value should be Some"
            Expect.equal value (Some(box "value1")) "value should match"
        }

        test "getData should return None for missing key" {
            let proc = MorphirProcessor.empty

            let value = MorphirProcessor.getData "missing" proc

            Expect.isNone value "value should be None"
        }

        test "getDataAs should return typed value" {
            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.setData "count" (box 42)

            let value = MorphirProcessor.getDataAs<int> "count" proc

            Expect.equal value (Some 42) "value should be 42"
        }

        test "getDataAs should return None for wrong type" {
            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.setData "value" (box "string")

            let value = MorphirProcessor.getDataAs<int> "value" proc

            Expect.isNone value "value should be None (wrong type)"
        }

        test "setData should create unfrozen copy when modifying frozen processor" {
            let frozen =
                MorphirProcessor.empty |> MorphirProcessor.freeze

            let withData =
                frozen |> MorphirProcessor.setData "key" (box "value")

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"

            Expect.isFalse (MorphirProcessor.isFrozen withData) "new processor should be unfrozen"

            let value = MorphirProcessor.getData "key" frozen
            Expect.isNone value "original should not have data"

            let value2 = MorphirProcessor.getData "key" withData
            Expect.isSome value2 "new processor should have data"
        }
    ]

[<Tests>]
let processorIntegrationTests =
    testList "MorphirProcessor Integration" [
        test "should support full pipeline builder pattern" {
            let parser: Parser = fun file -> Ok(box "IR tree")

            let validatePlugin: Plugin =
                {
                    Name = "validate"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Validation passed")
                }

            let optimizePlugin: Plugin =
                {
                    Name = "optimize"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Optimization complete")
                }

            let compiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiled to JSON"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin validatePlugin
                |> MorphirProcessor.plugin optimizePlugin
                |> MorphirProcessor.stringify compiler
                |> MorphirProcessor.freeze

            Expect.hasLength proc.Parsers 1 "should have 1 parser"
            Expect.hasLength proc.Plugins 2 "should have 2 plugins"
            Expect.hasLength proc.Compilers 1 "should have 1 compiler"
            Expect.isTrue (MorphirProcessor.isFrozen proc) "should be frozen"
        }

        test "should support plugin configuration chain" {
            let plugin1: Plugin =
                {
                    Name = "plugin1"
                    Configure = fun proc -> MorphirProcessor.setData "step1" (box true) proc
                    Transform = fun node file -> (Some node, file)
                }

            let plugin2: Plugin =
                {
                    Name = "plugin2"
                    Configure = fun proc -> MorphirProcessor.setData "step2" (box true) proc
                    Transform = fun node file -> (Some node, file)
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.plugin plugin1
                |> MorphirProcessor.plugin plugin2

            let step1 = MorphirProcessor.getDataAs<bool> "step1" proc
            let step2 = MorphirProcessor.getDataAs<bool> "step2" proc

            Expect.equal step1 (Some true) "step1 should be configured"
            Expect.equal step2 (Some true) "step2 should be configured"
        }
    ]

[<Tests>]
let processorExecutionTests =
    testList "MorphirProcessor Execution" [
        test "processFile should execute parse phase" {
            let parser: Parser = fun file -> Ok(box "parsed IR")

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isSome result.Content "content should be parsed"
        }

        test "processFile should handle parse failures" {
            let failingParser: Parser = fun file -> Result.Error "Parse error"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse failingParser

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isTrue (MorphirFile.hasErrors result) "should have error from failed parser"
        }

        test "processFile should try multiple parsers until one succeeds" {
            let failParser1: Parser = fun file -> Result.Error "Parser 1 failed"
            let failParser2: Parser = fun file -> Result.Error "Parser 2 failed"
            let successParser: Parser = fun file -> Result.Ok(box "parsed by parser 3")

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse failParser1
                |> MorphirProcessor.parse failParser2
                |> MorphirProcessor.parse successParser

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isSome result.Content "content should be parsed by third parser"
            Expect.hasLength result.Messages 2 "should have 2 warnings from failed parsers"
        }

        test "processFile should execute transform phase" {
            let parser: Parser = fun file -> Ok(box "original IR")

            let transformPlugin: Plugin =
                {
                    Name = "transform"
                    Configure = id
                    Transform = fun node file -> (Some(box "transformed IR"), file |> MorphirFile.info "Transformed")
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin transformPlugin

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isSome result.Content "content should be transformed"
            Expect.hasLength result.Messages 1 "should have info message from plugin"
        }

        test "processFile should run multiple plugins in sequence" {
            let parser: Parser = fun file -> Ok(box 0)

            let incrementPlugin: Plugin =
                {
                    Name = "increment"
                    Configure = id
                    Transform =
                        fun node file ->
                            let value = unbox<int> node
                            (Some(box (value + 1)), file |> MorphirFile.info (sprintf "Incremented to %d" (value + 1)))
                }

            let doublePlugin: Plugin =
                {
                    Name = "double"
                    Configure = id
                    Transform =
                        fun node file ->
                            let value = unbox<int> node
                            (Some(box (value * 2)), file |> MorphirFile.info (sprintf "Doubled to %d" (value * 2)))
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin incrementPlugin
                |> MorphirProcessor.plugin doublePlugin

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            // (0 + 1) * 2 = 2
            let finalValue = result.Content |> Option.map unbox<int>
            Expect.equal finalValue (Some 2) "should apply both transformations"
            Expect.hasLength result.Messages 2 "should have 2 info messages"
        }

        test "processFile should handle plugin returning None" {
            let parser: Parser = fun file -> Ok(box "IR")

            let removingPlugin: Plugin =
                {
                    Name = "remove"
                    Configure = id
                    Transform = fun node file -> (None, file |> MorphirFile.error "Validation failed" None)
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin removingPlugin

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isNone result.Content "content should be removed by plugin"
            Expect.isTrue (MorphirFile.hasErrors result) "should have error"
        }

        test "processFile should skip transform phase if parse failed" {
            let failingParser: Parser = fun file -> Result.Error "Parse error"

            let shouldNotRunPlugin: Plugin =
                {
                    Name = "should-not-run"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Plugin ran")
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse failingParser
                |> MorphirProcessor.plugin shouldNotRunPlugin

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            let infoMessages = MorphirFile.messagesOfSeverity Info result
            Expect.isEmpty infoMessages "plugin should not run if parse failed"
        }

        test "processFile should execute stringify phase" {
            let parser: Parser = fun file -> Ok(box "IR tree")

            let compiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiled to JSON"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.stringify compiler

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            let messages = MorphirFile.messagesOfSeverity Info result
            Expect.hasLength messages 1 "should have info message from compiler"
        }

        test "processFile should run multiple compilers in sequence" {
            let parser: Parser = fun file -> Ok(box "IR")

            let jsonCompiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiled to JSON"

            let prettyPrintCompiler: Compiler =
                fun node file -> file |> MorphirFile.info "Pretty printed"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.stringify jsonCompiler
                |> MorphirProcessor.stringify prettyPrintCompiler

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            let messages = MorphirFile.messagesOfSeverity Info result
            Expect.hasLength messages 2 "should have 2 info messages from compilers"
        }

        test "processFile should skip stringify phase if transform removed content" {
            let parser: Parser = fun file -> Ok(box "IR")

            let removingPlugin: Plugin =
                {
                    Name = "remove"
                    Configure = id
                    Transform = fun node file -> (None, file |> MorphirFile.error "Removed" None)
                }

            let shouldNotRunCompiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiler ran"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin removingPlugin
                |> MorphirProcessor.stringify shouldNotRunCompiler

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            let infoMessages = MorphirFile.messagesOfSeverity Info result
            Expect.isEmpty infoMessages "compiler should not run if transform removed content"
        }

        test "processFile should execute full three-phase pipeline" {
            let parser: Parser = fun file -> Ok(box "parsed IR")

            let validatePlugin: Plugin =
                {
                    Name = "validate"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Validation passed")
                }

            let optimizePlugin: Plugin =
                {
                    Name = "optimize"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.info "Optimization complete")
                }

            let compiler: Compiler =
                fun node file -> file |> MorphirFile.info "Compiled to JSON"

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin validatePlugin
                |> MorphirProcessor.plugin optimizePlugin
                |> MorphirProcessor.stringify compiler

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isSome result.Content "content should be present"
            Expect.isFalse (MorphirFile.hasErrors result) "should have no errors"
            Expect.hasLength result.Messages 3 "should have 3 info messages (validate, optimize, compile)"
            Expect.equal result.Messages.[0].Message "Validation passed" "first message from validate"
            Expect.equal result.Messages.[1].Message "Optimization complete" "second message from optimize"
            Expect.equal result.Messages.[2].Message "Compiled to JSON" "third message from compiler"
        }

        test "processFile should accumulate errors while continuing" {
            let parser: Parser = fun file -> Ok(box "IR")

            let errorPlugin1: Plugin =
                {
                    Name = "error1"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.error "Error 1" None)
                }

            let errorPlugin2: Plugin =
                {
                    Name = "error2"
                    Configure = id
                    Transform = fun node file -> (Some node, file |> MorphirFile.error "Error 2" None)
                }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin errorPlugin1
                |> MorphirProcessor.plugin errorPlugin2

            let inputFile = MorphirFile.fromPath "/test/input.json"
            let result = MorphirProcessor.processFile inputFile proc

            Expect.isTrue (MorphirFile.hasErrors result) "should have errors"
            let errors = MorphirFile.errors result
            Expect.hasLength errors 2 "should have 2 errors"
        }

        test "processPath should create file from path and process it" {
            let parser: Parser = fun file -> Ok(box "parsed")

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser

            let result = MorphirProcessor.processPath "/test/input.json" proc

            Expect.equal result.Path (Some "/test/input.json") "should preserve path"
            Expect.isSome result.Content "should have parsed content"
        }
    ]
