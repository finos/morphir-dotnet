module Morphir.IR.Pipeline.Tests.FileTests

open Expecto
open Morphir.IR.Pipeline

[<Tests>]
let messageTypeTests =
    testList "MessageSeverity" [
        test "should have four severity levels" {
            let severities = [ Info; Warning; Error; Fatal ]
            Expect.equal (List.length severities) 4 "should have exactly 4 severity levels"
        }

        test "should distinguish between severity levels" {
            Expect.notEqual Info Warning "Info should not equal Warning"
            Expect.notEqual Warning Error "Warning should not equal Error"
            Expect.notEqual Error Fatal "Error should not equal Fatal"
        }
    ]

[<Tests>]
let sourcePositionTests =
    testList "SourcePosition" [
        test "should create position with line and column" {
            let pos = { Line = 10; Column = 5; Offset = None }
            Expect.equal pos.Line 10 "line should be 10"
            Expect.equal pos.Column 5 "column should be 5"
            Expect.isNone pos.Offset "offset should be None"
        }

        test "should create position with offset" {
            let pos = { Line = 10; Column = 5; Offset = Some 123 }
            Expect.equal pos.Line 10 "line should be 10"
            Expect.equal pos.Column 5 "column should be 5"
            Expect.isSome pos.Offset "offset should be Some"
            Expect.equal pos.Offset (Some 123) "offset should be 123"
        }

        test "should support 1-based line numbering" {
            let pos = { Line = 1; Column = 1; Offset = Some 0 }
            Expect.equal pos.Line 1 "line should be 1 (1-based)"
            Expect.equal pos.Column 1 "column should be 1 (1-based)"
        }
    ]

[<Tests>]
let sourceRangeTests =
    testList "SourceRange" [
        test "should create range with start and end positions" {
            let start = { Line = 10; Column = 5; Offset = Some 100 }
            let end' = { Line = 12; Column = 20; Offset = Some 150 }
            let range = { Start = start; End = end' }

            Expect.equal range.Start.Line 10 "start line should be 10"
            Expect.equal range.End.Line 12 "end line should be 12"
        }

        test "should allow single-character range" {
            let pos = { Line = 10; Column = 5; Offset = Some 100 }
            let range = { Start = pos; End = pos }

            Expect.equal range.Start range.End "start and end should be equal for single character"
        }
    ]

[<Tests>]
let morphirMessageTests =
    testList "MorphirMessage" [
        test "should create message with severity and text" {
            let msg =
                {
                    Severity = Error
                    Message = "Type mismatch"
                    Position = None
                    Source = None
                    RuleId = None
                }

            Expect.equal msg.Severity Error "severity should be Error"
            Expect.equal msg.Message "Type mismatch" "message should match"
            Expect.isNone msg.Position "position should be None"
        }

        test "should create message with position" {
            let pos = { Line = 10; Column = 5; Offset = None }
            let range = { Start = pos; End = pos }

            let msg =
                {
                    Severity = Warning
                    Message = "Deprecated usage"
                    Position = Some range
                    Source = None
                    RuleId = None
                }

            Expect.isSome msg.Position "position should be Some"
        }

        test "should create message with source and rule ID" {
            let msg =
                {
                    Severity = Error
                    Message = "Invalid IR"
                    Position = None
                    Source = Some "validator"
                    RuleId = Some "IR-001"
                }

            Expect.equal msg.Source (Some "validator") "source should be 'validator'"
            Expect.equal msg.RuleId (Some "IR-001") "rule ID should be 'IR-001'"
        }
    ]

[<Tests>]
let morphirFileConstructorTests =
    testList "MorphirFile Constructors" [
        test "empty should create file with no content or messages" {
            let file = MorphirFile.empty

            Expect.isNone file.Content "content should be None"
            Expect.isNone file.Path "path should be None"
            Expect.isEmpty file.History "history should be empty"
            Expect.isEmpty file.Messages "messages should be empty"
            Expect.equal (file.Data.Count) 0 "data should be empty"
        }

        test "fromPath should create file with path and history" {
            let file = MorphirFile.fromPath "/test/file.json"

            Expect.isNone file.Content "content should be None"
            Expect.equal file.Path (Some "/test/file.json") "path should be set"
            Expect.equal file.History [ "/test/file.json" ] "history should contain path"
            Expect.isEmpty file.Messages "messages should be empty"
        }

        test "fromContent should create file with content" {
            let content = "test IR content"
            let file = MorphirFile.fromContent content

            Expect.isSome file.Content "content should be Some"
            Expect.isNone file.Path "path should be None"
            Expect.isEmpty file.History "history should be empty"
            Expect.isEmpty file.Messages "messages should be empty"
        }

        test "create should create file with both path and content" {
            let content = "test IR content"
            let file = MorphirFile.create "/test/file.json" content

            Expect.isSome file.Content "content should be Some"
            Expect.equal file.Path (Some "/test/file.json") "path should be set"
            Expect.equal file.History [ "/test/file.json" ] "history should contain path"
            Expect.isEmpty file.Messages "messages should be empty"
        }
    ]

[<Tests>]
let morphirFileDiagnosticTests =
    testList "MorphirFile Diagnostics" [
        test "info should add informational message" {
            let file =
                MorphirFile.empty
                |> MorphirFile.info "Processing started"

            Expect.hasLength file.Messages 1 "should have 1 message"
            Expect.equal file.Messages.[0].Severity Info "severity should be Info"
            Expect.equal file.Messages.[0].Message "Processing started" "message should match"
        }

        test "warn should add warning message" {
            let file =
                MorphirFile.empty
                |> MorphirFile.warn "Deprecated pattern" None

            Expect.hasLength file.Messages 1 "should have 1 message"
            Expect.equal file.Messages.[0].Severity Warning "severity should be Warning"
            Expect.equal file.Messages.[0].Message "Deprecated pattern" "message should match"
        }

        test "warn should add warning with position" {
            let pos = { Line = 10; Column = 5; Offset = None }
            let range = { Start = pos; End = pos }

            let file =
                MorphirFile.empty
                |> MorphirFile.warn "Deprecated pattern" (Some range)

            Expect.hasLength file.Messages 1 "should have 1 message"
            Expect.isSome file.Messages.[0].Position "should have position"
        }

        test "error should add error message" {
            let file =
                MorphirFile.empty
                |> MorphirFile.error "Type mismatch" None

            Expect.hasLength file.Messages 1 "should have 1 message"
            Expect.equal file.Messages.[0].Severity Error "severity should be Error"
            Expect.equal file.Messages.[0].Message "Type mismatch" "message should match"
        }

        test "fail should add fatal message" {
            let file =
                MorphirFile.empty
                |> MorphirFile.fail "Critical failure" None

            Expect.hasLength file.Messages 1 "should have 1 message"
            Expect.equal file.Messages.[0].Severity Fatal "severity should be Fatal"
            Expect.equal file.Messages.[0].Message "Critical failure" "message should match"
        }

        test "should accumulate multiple messages" {
            let file =
                MorphirFile.empty
                |> MorphirFile.info "Step 1"
                |> MorphirFile.warn "Warning 1" None
                |> MorphirFile.error "Error 1" None
                |> MorphirFile.info "Step 2"

            Expect.hasLength file.Messages 4 "should have 4 messages"
            Expect.equal file.Messages.[0].Message "Step 1" "first message should be 'Step 1'"
            Expect.equal file.Messages.[1].Message "Warning 1" "second message should be 'Warning 1'"
            Expect.equal file.Messages.[2].Message "Error 1" "third message should be 'Error 1'"
            Expect.equal file.Messages.[3].Message "Step 2" "fourth message should be 'Step 2'"
        }

        test "message should create custom message with source and rule ID" {
            let pos = { Line = 10; Column = 5; Offset = None }
            let range = { Start = pos; End = pos }

            let file =
                MorphirFile.empty
                |> MorphirFile.message Error "Invalid type" (Some range) (Some "validator") (Some "IR-001")

            Expect.hasLength file.Messages 1 "should have 1 message"
            Expect.equal file.Messages.[0].Severity Error "severity should be Error"
            Expect.equal file.Messages.[0].Source (Some "validator") "source should be 'validator'"
            Expect.equal file.Messages.[0].RuleId (Some "IR-001") "rule ID should be 'IR-001'"
        }
    ]

[<Tests>]
let morphirFileQueryTests =
    testList "MorphirFile Queries" [
        test "hasErrors should return false for file with no errors" {
            let file =
                MorphirFile.empty
                |> MorphirFile.info "Info message"
                |> MorphirFile.warn "Warning message" None

            Expect.isFalse (MorphirFile.hasErrors file) "should not have errors"
        }

        test "hasErrors should return true for file with errors" {
            let file =
                MorphirFile.empty
                |> MorphirFile.error "Error message" None

            Expect.isTrue (MorphirFile.hasErrors file) "should have errors"
        }

        test "hasErrors should return true for file with fatal errors" {
            let file =
                MorphirFile.empty
                |> MorphirFile.fail "Fatal error" None

            Expect.isTrue (MorphirFile.hasErrors file) "should have errors"
        }

        test "hasFatals should return false for file with no fatals" {
            let file =
                MorphirFile.empty
                |> MorphirFile.error "Error message" None

            Expect.isFalse (MorphirFile.hasFatals file) "should not have fatals"
        }

        test "hasFatals should return true for file with fatals" {
            let file =
                MorphirFile.empty
                |> MorphirFile.fail "Fatal error" None

            Expect.isTrue (MorphirFile.hasFatals file) "should have fatals"
        }

        test "messagesOfSeverity should filter by severity" {
            let file =
                MorphirFile.empty
                |> MorphirFile.info "Info 1"
                |> MorphirFile.warn "Warning 1" None
                |> MorphirFile.error "Error 1" None
                |> MorphirFile.info "Info 2"
                |> MorphirFile.error "Error 2" None

            let infos = MorphirFile.messagesOfSeverity Info file
            let warnings = MorphirFile.messagesOfSeverity Warning file
            let errors = MorphirFile.messagesOfSeverity Error file

            Expect.hasLength infos 2 "should have 2 info messages"
            Expect.hasLength warnings 1 "should have 1 warning message"
            Expect.hasLength errors 2 "should have 2 error messages"
        }

        test "errors should return all errors and fatals" {
            let file =
                MorphirFile.empty
                |> MorphirFile.error "Error 1" None
                |> MorphirFile.fail "Fatal 1" None
                |> MorphirFile.warn "Warning 1" None
                |> MorphirFile.error "Error 2" None

            let errorList = MorphirFile.errors file

            Expect.hasLength errorList 3 "should have 3 error/fatal messages"
        }

        test "warnings should return all warnings" {
            let file =
                MorphirFile.empty
                |> MorphirFile.warn "Warning 1" None
                |> MorphirFile.error "Error 1" None
                |> MorphirFile.warn "Warning 2" None

            let warningList = MorphirFile.warnings file

            Expect.hasLength warningList 2 "should have 2 warning messages"
        }
    ]

[<Tests>]
let morphirFileDataTests =
    testList "MorphirFile Data" [
        test "setData should store data value" {
            let file =
                MorphirFile.empty
                |> MorphirFile.setData "key1" (box "value1")

            let value = MorphirFile.getData "key1" file

            Expect.isSome value "value should be Some"
            Expect.equal value (Some (box "value1")) "value should match"
        }

        test "getData should return None for missing key" {
            let file = MorphirFile.empty

            let value = MorphirFile.getData "missing" file

            Expect.isNone value "value should be None"
        }

        test "getDataAs should return typed value" {
            let file =
                MorphirFile.empty
                |> MorphirFile.setData "count" (box 42)

            let value = MorphirFile.getDataAs<int> "count" file

            Expect.equal value (Some 42) "value should be 42"
        }

        test "getDataAs should return None for wrong type" {
            let file =
                MorphirFile.empty
                |> MorphirFile.setData "value" (box "string")

            let value = MorphirFile.getDataAs<int> "value" file

            Expect.isNone value "value should be None (wrong type)"
        }

        test "removeData should remove data value" {
            let file =
                MorphirFile.empty
                |> MorphirFile.setData "key1" (box "value1")
                |> MorphirFile.removeData "key1"

            let value = MorphirFile.getData "key1" file

            Expect.isNone value "value should be None after removal"
        }

        test "should support multiple data values" {
            let file =
                MorphirFile.empty
                |> MorphirFile.setData "string" (box "text")
                |> MorphirFile.setData "number" (box 123)
                |> MorphirFile.setData "flag" (box true)

            let str = MorphirFile.getDataAs<string> "string" file
            let num = MorphirFile.getDataAs<int> "number" file
            let flag = MorphirFile.getDataAs<bool> "flag" file

            Expect.equal str (Some "text") "string should match"
            Expect.equal num (Some 123) "number should match"
            Expect.equal flag (Some true) "flag should match"
        }

        test "should allow data updates" {
            let file =
                MorphirFile.empty
                |> MorphirFile.setData "key" (box "value1")
                |> MorphirFile.setData "key" (box "value2")

            let value = MorphirFile.getDataAs<string> "key" file

            Expect.equal value (Some "value2") "value should be updated to 'value2'"
        }
    ]

[<Tests>]
let morphirFileIntegrationTests =
    testList "MorphirFile Integration" [
        test "should support full pipeline flow" {
            let file =
                MorphirFile.fromPath "/test/input.json"
                |> MorphirFile.info "Parsing started"
                |> MorphirFile.setData "parser" (box "json-parser")
                |> MorphirFile.info "Parsing complete"
                |> MorphirFile.warn "Deprecated pattern found" None
                |> MorphirFile.info "Validation started"
                |> MorphirFile.info "Validation complete"

            Expect.equal file.Path (Some "/test/input.json") "path should be preserved"
            Expect.hasLength file.Messages 5 "should have 5 messages"
            Expect.isFalse (MorphirFile.hasErrors file) "should not have errors"

            let parserData = MorphirFile.getDataAs<string> "parser" file
            Expect.equal parserData (Some "json-parser") "parser data should be preserved"
        }

        test "should accumulate errors while continuing" {
            let file =
                MorphirFile.empty
                |> MorphirFile.info "Processing started"
                |> MorphirFile.error "Type error in function A" None
                |> MorphirFile.error "Type error in function B" None
                |> MorphirFile.warn "Unused import" None
                |> MorphirFile.info "Processing complete"

            Expect.hasLength file.Messages 5 "should have 5 messages"
            Expect.isTrue (MorphirFile.hasErrors file) "should have errors"

            let errorList = MorphirFile.errors file
            Expect.hasLength errorList 2 "should have 2 errors"
        }

        test "should track fatal errors" {
            let file =
                MorphirFile.empty
                |> MorphirFile.info "Processing started"
                |> MorphirFile.fail "Parse error: unexpected end of file" None
                |> MorphirFile.info "Attempting recovery"

            Expect.isTrue (MorphirFile.hasFatals file) "should have fatal errors"
            Expect.isTrue (MorphirFile.hasErrors file) "should have errors"
            Expect.hasLength file.Messages 3 "should have 3 messages (including after fatal)"
        }
    ]
