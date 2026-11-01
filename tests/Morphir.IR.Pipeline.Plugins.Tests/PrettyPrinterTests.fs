module Morphir.IR.Pipeline.Plugins.Tests.PrettyPrinterTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins

let private package : Package.PackageDefinition<unit, unit> =
    let definition: ValueDefinition<unit, unit> = {
        InputTypes = []
        OutputType = Type.Reference((), FQName.fromString "Morphir.SDK:Basics:Int" ":", [])
        Body = Value.Literal((), Literal.WholeNumberLiteral 5000L)
    }

    let moduleDefinition: Module.ModuleDefinition<unit, unit> = {
        Types = Map.empty
        Values = Map.ofList [ Name.fromString "dailyRate", { Access = Public; Value = WithoutDocumentation definition } ]
        Doc = None
    }

    { Modules = Map.ofList [ ModulePath.modulePath (Path [ Name.fromString "Inventory" ]), { Access = Public; Value = moduleDefinition } ] }

[<Tests>]
let prettyPrinterPluginTests =
    testList "PrettyPrinter plugin" [
        test "should keep the model and store the formatted package" {
            let plugin = PrettyPrinter.create ()

            let result, file = plugin.Transform package VFile.empty
            let text = VFile.tryGet PrettyPrinter.output file |> Option.defaultValue ""

            Expect.equal result (Some package) "the model should be unchanged"
            Expect.stringContains text "Inventory.dailyRate" "should name the value"
            Expect.stringContains text "5000" "should format the real body"
        }

        test "should use custom configuration" {
            let plugin = PrettyPrinter.createWithConfig (PrettyPrinter.defaultConfig |> PrettyPrinter.withoutTypes)

            let _, file = plugin.Transform package VFile.empty
            let text = VFile.tryGet PrettyPrinter.output file |> Option.defaultValue ""

            Expect.isFalse (text.Contains "Morphir.SDK") "should hide type annotations"
        }

        test "output should reach the run's tree" {
            let parser: Parser<Package.PackageDefinition<unit, unit>> = fun _ -> Ok package

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin (PrettyPrinter.create ())
                |> MorphirProcessor.processFile VFile.empty

            Expect.isSome (VFileTree.tryGet PrettyPrinter.output tree) "should be readable from the tree"
        }
    ]

[<Tests>]
let configurationTests =
    testList "Configuration" [
        test "defaultConfig should have expected values" {
            let config = PrettyPrinter.defaultConfig
            Expect.equal config.IndentWidth 2 "default indent should be 2"
            Expect.isTrue config.ShowTypes "default should show types"
            Expect.isFalse config.UseColors "default should not use colors"
            Expect.equal config.MaxLineLength 80 "default max line length should be 80"
        }

        test "withColors should enable colors" {
            let config = PrettyPrinter.defaultConfig |> PrettyPrinter.withColors
            Expect.isTrue config.UseColors "colors should be enabled"
        }

        test "withIndent should set custom indent width" {
            let config = PrettyPrinter.defaultConfig |> PrettyPrinter.withIndent 4
            Expect.equal config.IndentWidth 4 "indent width should be 4"
        }

        test "withoutTypes should hide type annotations" {
            let config = PrettyPrinter.defaultConfig |> PrettyPrinter.withoutTypes
            Expect.isFalse config.ShowTypes "should not show types"
        }
    ]

[<Tests>]
let literalFormattingTests =
    testList "Literal Formatting" [
        test "bool literal should format correctly" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.formatLiteral config (Literal.BoolLiteral true)
            Expect.equal result "true" "should format as 'true'"
        }

        test "string literal should format with quotes" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.formatLiteral config (Literal.StringLiteral "hello")
            Expect.equal result "\"hello\"" "should format with quotes"
        }

        test "int literal should format correctly" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.formatLiteral config (Literal.WholeNumberLiteral 42L)
            Expect.equal result "42" "should format as '42'"
        }

        test "float literal should format correctly" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.formatLiteral config (Literal.FloatLiteral 3.14)
            Expect.stringContains result "3.14" "should contain '3.14'"
        }

        test "char literal should format with single quotes" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.formatLiteral config (Literal.CharLiteral 'x')
            Expect.equal result "'x'" "should format with single quotes"
        }
    ]

[<Tests>]
let colorTests =
    testList "Color Support" [
        test "colorize should add ANSI codes when enabled" {
            let config = PrettyPrinter.defaultConfig |> PrettyPrinter.withColors
            let result = PrettyPrinter.colorize config PrettyPrinter.Colors.keyword "let"
            Expect.isTrue (result.Contains "\x1b[") "should contain ANSI escape codes"
            Expect.isTrue (result.Contains "let") "should contain original text"
        }

        test "colorize should not add codes when disabled" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.colorize config PrettyPrinter.Colors.keyword "let"
            Expect.equal result "let" "should be plain text"
        }
    ]

[<Tests>]
let indentTests =
    testList "Indentation" [
        test "indent at level 0 should be empty" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.indent 0 config
            Expect.equal result "" "should be empty string"
        }

        test "indent at level 1 should be 2 spaces by default" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.indent 1 config
            Expect.equal result "  " "should be 2 spaces"
        }

        test "indent should respect custom width" {
            let config = PrettyPrinter.defaultConfig |> PrettyPrinter.withIndent 4
            let result = PrettyPrinter.indent 1 config
            Expect.equal result "    " "should be 4 spaces"
        }

        test "indent at level 2 should multiply width" {
            let config = PrettyPrinter.defaultConfig
            let result = PrettyPrinter.indent 2 config
            Expect.equal result "    " "should be 4 spaces (2 levels * 2 width)"
        }
    ]

[<Tests>]
let patternFormattingTests =
    testList "Pattern Formatting" [
        test "wildcard pattern should format as underscore" {
            let config = PrettyPrinter.defaultConfig
            let pattern = Pattern.WildcardPattern()
            let result = PrettyPrinter.formatPattern config 0 pattern
            Expect.equal result "_" "should format as '_'"
        }

        test "unit pattern should format as ()" {
            let config = PrettyPrinter.defaultConfig
            let pattern = Pattern.UnitPattern()
            let result = PrettyPrinter.formatPattern config 0 pattern
            Expect.equal result "()" "should format as '()'"
        }

        test "literal pattern should format like literal" {
            let config = PrettyPrinter.defaultConfig
            let pattern = Pattern.LiteralPattern((), Literal.BoolLiteral true)
            let result = PrettyPrinter.formatPattern config 0 pattern
            Expect.equal result "true" "should format as 'true'"
        }

        test "empty list pattern should format as []" {
            let config = PrettyPrinter.defaultConfig
            let pattern = Pattern.EmptyListPattern()
            let result = PrettyPrinter.formatPattern config 0 pattern
            Expect.equal result "[]" "should format as '[]'"
        }
    ]
