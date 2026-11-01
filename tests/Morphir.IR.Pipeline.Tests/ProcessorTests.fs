module Morphir.IR.Pipeline.Tests.ProcessorTests

open Expecto
open Morphir.IR.Pipeline

let private configuredKey = Property.create<bool> "test.configured"
let private step1Key = Property.create<bool> "test.step1"
let private step2Key = Property.create<bool> "test.step2"

let private input = VFile.fromPath "/test/input.json"

let private constantParser (value: 'T) : Parser<'T> = fun _ -> Ok value

let private passthrough name : Plugin<'T> = Plugin.create name (fun model file -> (Some model, file))

/// A plugin that adds an informational message and keeps the model.
let private noting name message : Plugin<'T> =
    Plugin.create name (fun model file -> (Some model, file |> VFile.info message))

/// A compiler that writes the model's text to one file.
let private writing path : Compiler<string> =
    fun model _ -> VFileTree.empty |> VFileTree.addFile (VFile.create path model)

let private locations (tree: VFileTree) =
    tree |> VFileTree.toFileMap |> Map.toList |> List.map fst

let private textAt path (tree: VFileTree) =
    match VFileTree.findFile path tree with
    | Some file ->
        match VFile.readText file with
        | Ok text -> text
        | Result.Error error -> failtestf "could not read %s: %A" path error
    | None -> failtestf "no file at %s in %A" path (locations tree)

let private severities (tree: VFileTree) =
    tree.Messages |> List.map (fun message -> message.Severity)

[<Tests>]
let processorCreationTests =
    testList "MorphirProcessor Creation" [
        test "empty should create processor with no components" {
            let proc: MorphirProcessor<string> = MorphirProcessor.empty

            Expect.isEmpty proc.Parsers "parsers should be empty"
            Expect.isEmpty proc.Plugins "plugins should be empty"
            Expect.isEmpty proc.Compilers "compilers should be empty"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "processor should not be frozen"
            Expect.equal proc.Data.Count 0 "data should be empty"
        }
    ]

[<Tests>]
let processorParserTests =
    testList "MorphirProcessor Parsers" [
        test "parse should add multiple parsers in order" {
            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "first")
                |> MorphirProcessor.parse (constantParser "second")

            Expect.hasLength proc.Parsers 2 "should have 2 parsers"
            Expect.isOk (proc.Parsers.[0] (File input)) "parser should be callable"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "should remain unfrozen"
        }

        test "parse should create unfrozen copy when adding to frozen processor" {
            let frozen: MorphirProcessor<string> = MorphirProcessor.empty |> MorphirProcessor.freeze
            let withParser = frozen |> MorphirProcessor.parse (constantParser "parsed")

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen withParser) "new processor should be unfrozen"
            Expect.hasLength withParser.Parsers 1 "new processor should have parser"
            Expect.isEmpty frozen.Parsers "original should have no parsers"
        }

        test "forFile should parse a file" {
            let parser = Parser.forFile (fun file -> Ok file.Path)

            match parser (File input) with
            | Ok path -> Expect.equal path input.Path "should receive the file"
            | Result.Error error -> failtestf "expected success, got %A" error
        }

        test "forFile should report unsupported input for a directory" {
            let parser = Parser.forFile (fun _ -> Ok "parsed")

            match parser (Directory VFileTree.empty) with
            | Result.Error (ParseError.UnsupportedInput _) -> ()
            | other -> failtestf "expected unsupported input, got %A" other
        }

        test "ofParseable should parse any type with a static Parse member" {
            let parser: Parser<System.Version> = Parser.ofParseable ()

            match parser (File (VFile.create "v.txt" "1.2.3")) with
            | Ok version -> Expect.equal version (System.Version "1.2.3") "should parse"
            | Result.Error error -> failtestf "expected success, got %A" error
        }

        test "ofParseable should report an exception from Parse as invalid input" {
            let parser: Parser<System.Version> = Parser.ofParseable ()

            match parser (File (VFile.create "v.txt" "not a version")) with
            | Result.Error (ParseError.InvalidInput failure) -> Expect.isNotEmpty failure.Message "should carry the reason"
            | other -> failtestf "expected invalid input, got %A" other
        }

        test "ofParseable should report a read failure as a failed parser" {
            let parser: Parser<System.Version> = Parser.ofParseable ()
            let unreadable = VFile.create "v.txt" (StreamSource(fun () -> raise (System.IO.IOException "gone")))

            match parser (File unreadable) with
            | Result.Error (ParseError.Failed (path, cause)) ->
                Expect.equal path (Some "v.txt") "should name the file"
                Expect.equal cause.Message "gone" "should keep the cause"
            | other -> failtestf "expected a failed parser, got %A" other
        }
    ]

[<Tests>]
let processorPluginTests =
    testList "MorphirProcessor Plugins" [
        test "plugin should add multiple plugins in order" {
            let proc: MorphirProcessor<string> =
                MorphirProcessor.empty
                |> MorphirProcessor.plugin (passthrough "plugin1")
                |> MorphirProcessor.plugin (passthrough "plugin2")

            Expect.equal (proc.Plugins |> List.map (fun p -> p.Name)) [ "plugin1"; "plugin2" ] "order"
        }

        test "plugin should apply configuration to the processor's data" {
            let configuring: Plugin<string> =
                { passthrough "configuring" with Configure = PropertyBag.set configuredKey true }

            let proc = MorphirProcessor.empty |> MorphirProcessor.plugin configuring

            Expect.equal (MorphirProcessor.tryGet configuredKey proc) (Some true) "should have configured"
        }

        test "plugins should accumulate configuration" {
            let plugin1: Plugin<string> = { passthrough "one" with Configure = PropertyBag.set step1Key true }
            let plugin2: Plugin<string> = { passthrough "two" with Configure = PropertyBag.set step2Key true }

            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.plugin plugin1
                |> MorphirProcessor.plugin plugin2

            Expect.equal (MorphirProcessor.tryGet step1Key proc) (Some true) "step1"
            Expect.equal (MorphirProcessor.tryGet step2Key proc) (Some true) "step2"
        }

        test "plugin should create unfrozen copy when adding to frozen processor" {
            let frozen: MorphirProcessor<string> = MorphirProcessor.empty |> MorphirProcessor.freeze
            let withPlugin = frozen |> MorphirProcessor.plugin (passthrough "test")

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen withPlugin) "new processor should be unfrozen"
            Expect.isEmpty frozen.Plugins "original should have no plugins"
        }

        test "focus should run a plugin on part of a larger model" {
            let inner = Plugin.create "double" (fun (value: int) file -> (Some (value * 2), file))
            let outer = Plugin.focus fst (fun value (_, text: string) -> (value, text)) inner

            let result, _ = outer.Transform (21, "kept") VFile.empty

            Expect.equal result (Some (42, "kept")) "should update only the focused part"
            Expect.equal outer.Name "double" "should keep the name"
        }

        test "focus should keep a rejection and file changes" {
            let inner = Plugin.create "reject" (fun (_: int) file -> (None, file |> VFile.error "bad" None))
            let outer = Plugin.focus fst (fun value (_, text: string) -> (value, text)) inner

            let result, file = outer.Transform (21, "kept") VFile.empty

            Expect.isNone result "should reject the whole model"
            Expect.isTrue (VFile.hasErrors file) "should keep the message"
        }

        test "focus should keep configuration" {
            let inner: Plugin<int> = { passthrough "inner" with Configure = PropertyBag.set configuredKey true }
            let outer: Plugin<int * string> = Plugin.focus fst (fun value (_, text) -> (value, text)) inner

            let proc = MorphirProcessor.empty |> MorphirProcessor.plugin outer

            Expect.equal (MorphirProcessor.tryGet configuredKey proc) (Some true) "should configure the processor"
        }
    ]

[<Tests>]
let processorCompilerTests =
    testList "MorphirProcessor Compilers" [
        test "stringify should add compilers" {
            let proc =
                MorphirProcessor.empty
                |> MorphirProcessor.stringify (writing "a.txt")
                |> MorphirProcessor.stringify (writing "b.txt")

            Expect.hasLength proc.Compilers 2 "should have 2 compilers"
            Expect.isFalse (MorphirProcessor.isFrozen proc) "should remain unfrozen"
        }

        test "stringify should create unfrozen copy when adding to frozen processor" {
            let frozen: MorphirProcessor<string> = MorphirProcessor.empty |> MorphirProcessor.freeze
            let withCompiler = frozen |> MorphirProcessor.stringify (writing "a.txt")

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen withCompiler) "new processor should be unfrozen"
            Expect.isEmpty frozen.Compilers "original should have no compilers"
        }
    ]

[<Tests>]
let processorFreezingTests =
    testList "MorphirProcessor Freezing" [
        test "freeze should set frozen flag" {
            let proc: MorphirProcessor<string> = MorphirProcessor.empty |> MorphirProcessor.freeze

            Expect.isTrue (MorphirProcessor.isFrozen proc) "processor should be frozen"
        }

        test "unfreeze should clear frozen flag" {
            let proc: MorphirProcessor<string> =
                MorphirProcessor.empty
                |> MorphirProcessor.freeze
                |> MorphirProcessor.unfreeze

            Expect.isFalse (MorphirProcessor.isFrozen proc) "processor should be unfrozen"
        }

        test "should support base + variant pattern" {
            let base' =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "parsed")
                |> MorphirProcessor.plugin (passthrough "base")
                |> MorphirProcessor.freeze

            let variant = base' |> MorphirProcessor.plugin (passthrough "variant")

            Expect.isTrue (MorphirProcessor.isFrozen base') "base should be frozen"
            Expect.hasLength base'.Plugins 1 "base should keep 1 plugin"
            Expect.isFalse (MorphirProcessor.isFrozen variant) "variant should be unfrozen"
            Expect.equal (variant.Plugins |> List.map (fun p -> p.Name)) [ "base"; "variant" ] "variant plugins"
            Expect.hasLength variant.Parsers 1 "variant should keep the parser"
        }
    ]

[<Tests>]
let processorDataTests =
    let count = Property.create<int> "test.count"

    testList "MorphirProcessor Data" [
        test "set and tryGet should round trip a typed value" {
            let proc: MorphirProcessor<string> = MorphirProcessor.empty |> MorphirProcessor.set count 42

            Expect.equal (MorphirProcessor.tryGet count proc) (Some 42) "value should be 42"
        }

        test "tryGet should return None when no value is stored" {
            let proc: MorphirProcessor<string> = MorphirProcessor.empty

            Expect.isNone (MorphirProcessor.tryGet count proc) "value should be None"
        }

        test "remove should remove the value" {
            let proc: MorphirProcessor<string> =
                MorphirProcessor.empty
                |> MorphirProcessor.set count 42
                |> MorphirProcessor.remove count

            Expect.isNone (MorphirProcessor.tryGet count proc) "value should be None"
        }

        test "set should create unfrozen copy when modifying frozen processor" {
            let frozen: MorphirProcessor<string> = MorphirProcessor.empty |> MorphirProcessor.freeze
            let withData = frozen |> MorphirProcessor.set count 1

            Expect.isTrue (MorphirProcessor.isFrozen frozen) "original should remain frozen"
            Expect.isFalse (MorphirProcessor.isFrozen withData) "new processor should be unfrozen"
            Expect.isNone (MorphirProcessor.tryGet count frozen) "original should not have data"
            Expect.equal (MorphirProcessor.tryGet count withData) (Some 1) "new processor should have data"
        }
    ]

[<Tests>]
let parsePhaseTests =
    testList "MorphirProcessor parse phase" [
        test "should stop at the first parser that succeeds" {
            let failing: Parser<string> = fun _ -> Result.Error (ParseError.UnsupportedInput "not mine")

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse failing
                |> MorphirProcessor.parse (constantParser "second")
                |> MorphirProcessor.parse (constantParser "third")
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.processFile input

            Expect.equal (textAt "out.txt" tree) "second" "second parser's model should be compiled"
        }

        test "failed parsers should leave warnings when a later parser succeeds" {
            let unsupported: Parser<string> = fun _ -> Result.Error (ParseError.UnsupportedInput "not mine")
            let invalid: Parser<string> = fun _ -> Result.Error (ParseError.invalidInput "bad content")

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse unsupported
                |> MorphirProcessor.parse invalid
                |> MorphirProcessor.parse (constantParser "parsed")
                |> MorphirProcessor.processFile input

            Expect.equal (severities tree) [ Warning; Warning ] "both failures are warnings"
            Expect.isFalse (VFileTree.hasErrors tree) "the run succeeded"
        }

        test "malformed input should become an error keeping its details when every parser fails" {
            let range: SourceRange = {
                Start = { Line = 3; Column = 7; Offset = None }
                End = { Line = 3; Column = 12; Offset = None }
            }

            let failure: ParseFailure = {
                Path = None
                RuleId = Some "unsupported-version"
                Location = Some "$.formatVersion"
                Message = "version 4"
                Position = Some range
            }

            let parser: Parser<string> = fun _ -> Result.Error (ParseError.InvalidInput failure)

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.processFile input

            match tree.Messages with
            | [ message ] ->
                Expect.equal message.Severity Error "should be an error"
                Expect.equal message.RuleId (Some "unsupported-version") "should keep the rule"
                Expect.equal message.Location (Some "$.formatVersion") "should keep the location"
                Expect.equal message.Position (Some range) "should keep the position"
                Expect.equal message.Path input.Path "should name the input"
                Expect.equal message.Message "version 4" "should keep the message"
            | other -> failtestf "expected one error, got %A" other

            Expect.isEmpty (VFileTree.allFiles tree) "should produce no files"
        }

        test "a failure's own path should win over the input path" {
            let failure: ParseFailure = {
                Path = Some "other.json"
                RuleId = None
                Location = None
                Message = "bad"
                Position = None
            }

            let parser: Parser<string> = fun _ -> Result.Error (ParseError.InvalidInput failure)

            let tree = MorphirProcessor.empty |> MorphirProcessor.parse parser |> MorphirProcessor.processFile input

            Expect.equal tree.Messages.[0].Path (Some "other.json") "should keep the failure's path"
        }

        test "a failed parser should become an error keeping its cause" {
            let parser: Parser<string> =
                fun _ -> Result.Error (ParseError.Failed (Some "a.json", System.IO.IOException "disk gone"))

            let tree = MorphirProcessor.empty |> MorphirProcessor.parse parser |> MorphirProcessor.processFile input

            match tree.Messages with
            | [ message ] ->
                Expect.equal message.Severity Error "should be an error"
                Expect.equal message.Path (Some "a.json") "should name the file"
                Expect.stringContains message.Message "disk gone" "should include the cause"
            | other -> failtestf "expected one error, got %A" other
        }

        test "unsupported input from every parser should warn and add a generic error" {
            let parser: Parser<string> = fun _ -> Result.Error (ParseError.UnsupportedInput "not JSON")

            let tree = MorphirProcessor.empty |> MorphirProcessor.parse parser |> MorphirProcessor.processFile input

            Expect.equal (severities tree) [ Warning; Error ] "warning then error"
            Expect.stringContains tree.Messages.[0].Message "not JSON" "should include the reason"
            Expect.stringContains tree.Messages.[1].Message "No parser succeeded" "should be the generic error"
        }

        test "a mix of failures should not add the generic error" {
            let unsupported: Parser<string> = fun _ -> Result.Error (ParseError.UnsupportedInput "not mine")
            let invalid: Parser<string> = fun _ -> Result.Error (ParseError.invalidInput "bad content")

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse unsupported
                |> MorphirProcessor.parse invalid
                |> MorphirProcessor.processFile input

            Expect.equal (severities tree) [ Warning; Error ] "the real cause is the only error"
            Expect.equal tree.Messages.[1].Message "bad content" "should lead with the real cause"
        }

        test "no parsers should report an error" {
            let tree: VFileTree =
                (MorphirProcessor.empty: MorphirProcessor<string>)
                |> MorphirProcessor.processFile input

            Expect.isTrue (VFileTree.hasErrors tree) "a run needs a parser"
        }

        test "plugins and compilers should not run when parsing fails" {
            let parser: Parser<string> = fun _ -> Result.Error (ParseError.invalidInput "bad")
            let mutable pluginRan = false
            let mutable compilerRan = false

            let plugin =
                Plugin.create "spy" (fun model file ->
                    pluginRan <- true
                    (Some model, file))

            let compiler: Compiler<string> =
                fun _ _ ->
                    compilerRan <- true
                    VFileTree.empty

            MorphirProcessor.empty
            |> MorphirProcessor.parse parser
            |> MorphirProcessor.plugin plugin
            |> MorphirProcessor.stringify compiler
            |> MorphirProcessor.processFile input
            |> ignore

            Expect.isFalse pluginRan "plugin should not run"
            Expect.isFalse compilerRan "compiler should not run"
        }

        test "a failed run's tree should use the input path" {
            let parser: Parser<string> = fun _ -> Result.Error (ParseError.invalidInput "bad")

            let tree = MorphirProcessor.empty |> MorphirProcessor.parse parser |> MorphirProcessor.processFile input

            Expect.equal tree.Path "/test/input.json" "should identify the input"
            Expect.isEmpty tree.Content "should have no files"
        }

        test "a parser should be able to read a stream source after an earlier parser read it" {
            let readText: Parser<string> =
                Parser.forFile (fun file ->
                    match VFile.readText file with
                    | Ok text -> Ok text
                    | Result.Error (ContentError.ReadFailed (path, cause)) -> Result.Error (ParseError.Failed (path, cause)))

            let readThenFail: Parser<string> =
                fun node ->
                    readText node |> ignore
                    Result.Error (ParseError.UnsupportedInput "not mine")

            let source =
                StreamSource(fun () ->
                    new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes "content") :> System.IO.Stream)

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse readThenFail
                |> MorphirProcessor.parse readText
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.processFile (VFile.create "in.txt" source)

            Expect.equal (textAt "out.txt" tree) "content" "second parser should read from the start"
        }
    ]

[<Tests>]
let pluginPhaseTests =
    testList "MorphirProcessor plugin phase" [
        test "plugins should run in sequence with typed models" {
            let increment = Plugin.create "increment" (fun (value: int) file -> (Some (value + 1), file))
            let double = Plugin.create "double" (fun (value: int) file -> (Some (value * 2), file))
            let compiler: Compiler<int> =
                fun value _ -> VFileTree.empty |> VFileTree.addFile (VFile.create "out.txt" (string value))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser 0)
                |> MorphirProcessor.plugin increment
                |> MorphirProcessor.plugin double
                |> MorphirProcessor.stringify compiler
                |> MorphirProcessor.processFile input

            Expect.equal (textAt "out.txt" tree) "2" "(0 + 1) * 2"
        }

        test "two plugins reporting errors should both run" {
            let error1 = Plugin.create "error1" (fun (model: string) file -> (Some model, file |> VFile.error "Error 1" None))
            let error2 = Plugin.create "error2" (fun (model: string) file -> (Some model, file |> VFile.error "Error 2" None))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "IR")
                |> MorphirProcessor.plugin error1
                |> MorphirProcessor.plugin error2
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.processFile input

            Expect.equal (tree.Messages |> List.map (fun m -> m.Message)) [ "Error 1"; "Error 2" ] "both errors"
            Expect.isEmpty (VFileTree.allFiles tree) "compilers should not run after an error"
        }

        test "a plugin returning None should stop later plugins and compilers" {
            let reject = Plugin.create "reject" (fun (_: string) file -> (None, file |> VFile.warn "rejected" None))
            let mutable laterRan = false

            let later =
                Plugin.create "later" (fun model file ->
                    laterRan <- true
                    (Some model, file))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "IR")
                |> MorphirProcessor.plugin reject
                |> MorphirProcessor.plugin later
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.processFile input

            Expect.isFalse laterRan "later plugin should not run"
            Expect.isEmpty (VFileTree.allFiles tree) "compiler should not run"
            Expect.equal tree.Messages.[0].Message "rejected" "message should reach the tree"
        }

        test "a fatal message should stop later plugins and compilers" {
            let fatal = Plugin.create "fatal" (fun (model: string) file -> (Some model, file |> VFile.fail "stop" None))
            let mutable laterRan = false

            let later =
                Plugin.create "later" (fun model file ->
                    laterRan <- true
                    (Some model, file))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "IR")
                |> MorphirProcessor.plugin fatal
                |> MorphirProcessor.plugin later
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.processFile input

            Expect.isFalse laterRan "later plugin should not run"
            Expect.isEmpty (VFileTree.allFiles tree) "compiler should not run"
        }

        test "warnings should not block compilers" {
            let warn = Plugin.create "warn" (fun (model: string) file -> (Some model, file |> VFile.warn "careful" None))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "IR")
                |> MorphirProcessor.plugin warn
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.processFile input

            Expect.equal (locations tree) [ "out.txt" ] "compiler should run"
            Expect.equal tree.Messages.[0].Message "careful" "warning should reach the tree"
        }
    ]

[<Tests>]
let compilePhaseTests =
    testList "MorphirProcessor compile phase" [
        test "one compiler should be able to produce many files" {
            let compiler: Compiler<string> =
                fun model _ ->
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create "a.fs" model)
                    |> VFileTree.addFile (VFile.create "b.fs" model)
                    |> VFileTree.addDirectory (VFileTree.create "sub" TreeConfig.empty |> VFileTree.addFile (VFile.create "c.fs" model))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.stringify compiler
                |> MorphirProcessor.processFile input

            Expect.equal (locations tree) [ "a.fs"; "b.fs"; "sub/c.fs" ] "should produce every file"
        }

        test "compilers should merge their files in order" {
            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.stringify (writing "b.txt")
                |> MorphirProcessor.stringify (writing "a.txt")
                |> MorphirProcessor.processFile input

            let inOrder = VFileTree.allFiles tree |> List.map (fun file -> file.Path)

            Expect.equal inOrder [ Some "b.txt"; Some "a.txt" ] "first compiler's file comes first"
        }

        test "a compiler should see the file with earlier messages and data" {
            let seen = Property.create<string> "test.seen"
            let marking = Plugin.create "mark" (fun (model: string) file -> (Some model, file |> VFile.set seen "yes" |> VFile.info "noted"))
            let mutable observed = None

            let compiler: Compiler<string> =
                fun _ file ->
                    observed <- Some (VFile.tryGet seen file, file.Messages.Length)
                    VFileTree.empty

            MorphirProcessor.empty
            |> MorphirProcessor.parse (constantParser "code")
            |> MorphirProcessor.plugin marking
            |> MorphirProcessor.stringify compiler
            |> MorphirProcessor.processFile input
            |> ignore

            Expect.equal observed (Some (Some "yes", 1)) "should see the plugin's changes"
        }

        test "messages should move to the tree before a compiler's messages" {
            let compiler: Compiler<string> =
                fun _ _ -> VFileTree.empty |> VFileTree.report (VMessage.create Info "from compiler")

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.plugin (noting "note" "from plugin")
                |> MorphirProcessor.stringify compiler
                |> MorphirProcessor.processFile input

            Expect.equal (tree.Messages |> List.map (fun m -> m.Message)) [ "from plugin"; "from compiler" ] "order"
            Expect.equal tree.Messages.[0].Path input.Path "moved messages should name the input"
        }

        test "plugin data should reach the output tree" {
            let stored = Property.create<string> "test.stored"
            let storing = Plugin.create "store" (fun (model: string) file -> (Some model, file |> VFile.set stored "value"))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.plugin storing
                |> MorphirProcessor.processFile input

            Expect.equal (VFileTree.tryGet stored tree) (Some "value") "should be readable from the tree"
        }

        test "compiler metadata should win over plugin data" {
            let stored = Property.create<string> "test.stored"
            let storing = Plugin.create "store" (fun (model: string) file -> (Some model, file |> VFile.set stored "plugin"))
            let compiler: Compiler<string> = fun _ _ -> VFileTree.empty |> VFileTree.set stored "compiler"

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.plugin storing
                |> MorphirProcessor.stringify compiler
                |> MorphirProcessor.processFile input

            Expect.equal (VFileTree.tryGet stored tree) (Some "compiler") "compiler should win"
        }

        test "plugin data should reach the tree even when the run fails" {
            let stored = Property.create<string> "test.stored"
            let storing = Plugin.create "store" (fun (_: string) file -> (None, file |> VFile.set stored "value"))

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.plugin storing
                |> MorphirProcessor.processFile input

            Expect.equal (VFileTree.tryGet stored tree) (Some "value") "should be readable"
        }

        test "the input file should not be part of the output" {
            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (constantParser "code")
                |> MorphirProcessor.processFile input

            Expect.isEmpty (VFileTree.allFiles tree) "only compilers produce files"
        }

        test "processPath should read the file from disk when a parser asks" {
            let temp = System.IO.Path.GetTempFileName()

            try
                System.IO.File.WriteAllText(temp, "on disk")

                let parser: Parser<string> =
                    Parser.forFile (fun file ->
                        match VFile.readText file with
                        | Ok text -> Ok text
                        | Result.Error (ContentError.ReadFailed (path, cause)) -> Result.Error (ParseError.Failed (path, cause)))

                let tree =
                    MorphirProcessor.empty
                    |> MorphirProcessor.parse parser
                    |> MorphirProcessor.stringify (writing "out.txt")
                    |> MorphirProcessor.processPath temp

                Expect.equal (textAt "out.txt" tree) "on disk" "should read the file"
            finally
                System.IO.File.Delete temp
        }
    ]

[<Tests>]
let directoryInputTests =
    /// A parser that accepts a directory and joins its file names into one model.
    let directoryParser: Parser<string> =
        function
        | Directory tree ->
            VFileTree.toFileMap tree
            |> Map.toList
            |> List.map fst
            |> String.concat ","
            |> Ok
        | File _ -> Result.Error (ParseError.UnsupportedInput "expected a directory")

    let project =
        VFileTree.create "project" TreeConfig.empty
        |> VFileTree.addFile (VFile.create "a.fs" "a")
        |> VFileTree.addDirectory (VFileTree.create "sub" TreeConfig.empty |> VFileTree.addFile (VFile.create "b.fs" "b"))

    testList "MorphirProcessor directory input" [
        test "run should give a directory parser the whole directory and produce one model" {
            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse directoryParser
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.run (Directory project)

            Expect.equal (textAt "out.txt" tree) "a.fs,sub/b.fs" "many files become one model"
        }

        test "run should try the next parser when a file-only parser is given a directory" {
            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse (Parser.forFile (fun _ -> Ok "file"))
                |> MorphirProcessor.parse directoryParser
                |> MorphirProcessor.stringify (writing "out.txt")
                |> MorphirProcessor.run (Directory project)

            Expect.equal (textAt "out.txt" tree) "a.fs,sub/b.fs" "directory parser should run"
            Expect.equal (severities tree) [ Warning ] "file-only parser's failure is a warning"
        }

        test "plugins should see the directory's metadata and path" {
            let tag = Property.create<string> "test.tag"
            let mutable seen = None

            let spy =
                Plugin.create "spy" (fun model file ->
                    seen <- Some (file.Path, VFile.tryGet tag file)
                    (Some model, file))

            MorphirProcessor.empty
            |> MorphirProcessor.parse directoryParser
            |> MorphirProcessor.plugin spy
            |> MorphirProcessor.run (Directory (project |> VFileTree.set tag "tagged"))
            |> ignore

            Expect.equal seen (Some (Some "project", Some "tagged")) "working file should describe the directory"
        }
    ]

[<Tests>]
let runEachFileTests =
    let parseText: Parser<string> =
        Parser.forFile (fun file ->
            match VFile.readText file with
            | Ok "bad" -> Result.Error (ParseError.invalidInput "bad content")
            | Ok text -> Ok text
            | Result.Error (ContentError.ReadFailed (path, cause)) -> Result.Error (ParseError.Failed (path, cause)))

    /// Writes one output next to the input, named after it.
    let toOutput: Compiler<string> =
        fun model file ->
            VFileTree.empty
            |> VFileTree.addFile (VFile.create (Option.get file.Path + ".out") (model.ToUpperInvariant()))

    let processor =
        MorphirProcessor.empty
        |> MorphirProcessor.parse parseText
        |> MorphirProcessor.stringify toOutput

    let source =
        VFileTree.create "root" TreeConfig.empty
        |> VFileTree.addFile (VFile.create "a.txt" "aa")
        |> VFileTree.addDirectory (VFileTree.create "sub" TreeConfig.empty |> VFileTree.addFile (VFile.create "b.txt" "bb"))

    testList "MorphirProcessor.runEachFile" [
        test "each file should produce its own output at the same location" {
            let tree = MorphirProcessor.runEachFile source processor

            Expect.equal (locations tree) [ "a.txt.out"; "sub/b.txt.out" ] "outputs stay in their directories"
            Expect.equal (textAt "sub/b.txt.out" tree) "BB" "each file is processed by itself"
        }

        test "the output tree should keep the input tree's path" {
            Expect.equal (MorphirProcessor.runEachFile source processor).Path "root" "root path"
        }

        test "one failing file should not stop the others" {
            let withBadFile = source |> VFileTree.addFile (VFile.create "c.txt" "bad")

            let tree = MorphirProcessor.runEachFile withBadFile processor

            Expect.equal (locations tree) [ "a.txt.out"; "sub/b.txt.out" ] "other files still produce output"
            Expect.isTrue (VFileTree.hasErrors tree) "the tree should report the failure"
            Expect.equal tree.Messages.[0].Path (Some "c.txt") "the error should name the failed file"
        }

        test "a nested failing file should be named by its full location" {
            let nested =
                VFileTree.empty
                |> VFileTree.addDirectory (VFileTree.create "sub" TreeConfig.empty |> VFileTree.addFile (VFile.create "c.txt" "bad"))

            let tree = MorphirProcessor.runEachFile nested processor

            Expect.equal tree.Messages.[0].Path (Some "sub/c.txt") "should use the full location"
        }

        test "per-file data should stay on that file's output" {
            let stored = Property.create<string> "test.stored"
            let storing =
                Plugin.create "store" (fun (model: string) file -> (Some model, file |> VFile.set stored model))

            let tree = MorphirProcessor.runEachFile source (processor |> MorphirProcessor.plugin storing)

            let storedAt path = VFileTree.findFile path tree |> Option.bind (VFile.tryGet stored)

            Expect.equal (storedAt "a.txt.out") (Some "aa") "first file's value"
            Expect.equal (storedAt "sub/b.txt.out") (Some "bb") "second file's value"
            Expect.isNone (VFileTree.tryGet stored tree) "values should not collide in the root"
        }

        test "an output file's own data should win over per-file data" {
            let stored = Property.create<string> "test.stored"
            let storing =
                Plugin.create "store" (fun (model: string) file -> (Some model, file |> VFile.set stored "plugin"))

            let keepingOwn: Compiler<string> =
                fun model file ->
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create (Option.get file.Path + ".out") model |> VFile.set stored "own")

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parseText
                |> MorphirProcessor.plugin storing
                |> MorphirProcessor.stringify keepingOwn
                |> MorphirProcessor.runEachFile (VFileTree.empty |> VFileTree.addFile (VFile.create "a.txt" "aa"))

            let value = VFileTree.findFile "a.txt.out" tree |> Option.bind (VFile.tryGet stored)

            Expect.equal value (Some "own") "the file's own value should win"
        }

        test "empty directories should remain" {
            let withEmpty = VFileTree.empty |> VFileTree.addDirectory (VFileTree.create "empty" TreeConfig.empty)

            Expect.equal (VFileTree.directoryCount (MorphirProcessor.runEachFile withEmpty processor)) 1 "kept"
        }
    ]

[<Tests>]
let treeMessageTests =
    testList "VFileTree tree-level messages" [
        test "hasErrors should include tree-level errors" {
            let tree = VFileTree.empty |> VFileTree.report (VMessage.create Error "bad")

            Expect.isTrue (VFileTree.hasErrors tree) "tree-level error"
        }

        test "hasErrors should include errors on a nested directory" {
            let nested = VFileTree.create "sub" TreeConfig.empty |> VFileTree.report (VMessage.create Fatal "bad")
            let tree = VFileTree.empty |> VFileTree.addDirectory nested

            Expect.isTrue (VFileTree.hasErrors tree) "nested tree-level error"
        }

        test "hasErrors should ignore warnings" {
            let tree = VFileTree.empty |> VFileTree.report (VMessage.create Warning "careful")

            Expect.isFalse (VFileTree.hasErrors tree) "warnings are not errors"
        }

        test "collectMessages should list tree messages before file messages" {
            let tree =
                VFileTree.empty
                |> VFileTree.addFile (VFile.empty |> VFile.info "file")
                |> VFileTree.report (VMessage.create Info "tree")

            Expect.equal (VFileTree.collectMessages tree |> List.map (fun m -> m.Message)) [ "tree"; "file" ] "order"
        }

        test "statistics should count tree-level messages" {
            let tree =
                VFileTree.empty
                |> VFileTree.report (VMessage.create Error "one")
                |> VFileTree.report (VMessage.create Warning "two")
                |> VFileTree.report (VMessage.create Info "three")

            let stats = VFileTree.statistics tree

            Expect.equal stats.ErrorCount 1 "errors"
            Expect.equal stats.WarningCount 1 "warnings"
            Expect.equal stats.InfoCount 1 "info"
        }

        test "merge should append and let overlay metadata win" {
            let key = Property.create<string> "test.key"

            let baseTree =
                VFileTree.create "base" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "a" "a")
                |> VFileTree.report (VMessage.create Info "first")
                |> VFileTree.set key "base"

            let overlay =
                VFileTree.empty
                |> VFileTree.addFile (VFile.create "b" "b")
                |> VFileTree.report (VMessage.create Info "second")
                |> VFileTree.set key "overlay"

            let merged = VFileTree.merge overlay baseTree

            Expect.equal merged.Path "base" "should keep the base path"
            Expect.equal (VFileTree.allFiles merged |> List.map (fun f -> f.Path)) [ Some "a"; Some "b" ] "files"
            Expect.equal (merged.Messages |> List.map (fun m -> m.Message)) [ "first"; "second" ] "messages"
            Expect.equal (VFileTree.tryGet key merged) (Some "overlay") "overlay wins"
        }
    ]
