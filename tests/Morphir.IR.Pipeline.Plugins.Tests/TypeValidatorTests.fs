module Morphir.IR.Pipeline.Plugins.Tests.TypeValidatorTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins

let private fq text = FQName.fromString text ":"
let private sdkType name : Type<unit> = Type.Reference((), fq ("Morphir.SDK:" + name), [])
let private intType = sdkType "Basics:Int"
let private floatType = sdkType "Basics:Float"
let private boolType = sdkType "Basics:Bool"
let private stringType = sdkType "String:String"
let private charType = sdkType "Char:Char"

let private literal (value: Literal) : Value<unit, unit> = Value.Literal((), value)
let private whole (value: int64) = literal (Literal.WholeNumberLiteral value)
let private text (value: string) = literal (Literal.StringLiteral value)
let private flag (value: bool) = literal (Literal.BoolLiteral value)
let private variable (name: string) : Value<unit, unit> = Value.Variable((), Name.fromString name)

let private definition (inputs: (string * Type<unit>) list) output body : ValueDefinition<unit, unit> =
    {
        InputTypes = inputs |> List.map (fun (name, typ) -> Name.fromString name, (), typ)
        OutputType = output
        Body = body
    }

let private package (value: ValueDefinition<unit, unit>) : Package.PackageDefinition<unit, unit> =
    let moduleDefinition: Module.ModuleDefinition<unit, unit> = {
        Types = Map.empty
        Values = Map.ofList [ Name.fromString "value", { Access = Public; Value = WithoutDocumentation value } ]
        Doc = None
    }

    { Modules = Map.ofList [ ModulePath.modulePath (Path [ Name.fromString "Rules" ]), { Access = Public; Value = moduleDefinition } ] }

/// The messages from validating one definition. The model should always be kept.
let private validate (value: ValueDefinition<unit, unit>) =
    let plugin = TypeValidator.create ()
    let input = package value
    let result, file = plugin.Transform input VFile.empty

    Expect.equal result (Some input) "validation should keep the model so later plugins can report more"
    file.Messages

let private expectClean value = Expect.isEmpty (validate value) "should report nothing"

let private expectMismatch value =
    match validate value with
    | [ message ] ->
        Expect.equal message.Severity Error "should be an error"
        Expect.equal message.RuleId (Some "type-mismatch") "should have a rule"
        Expect.equal message.Location (Some "Rules.value") "should name the value"
        Expect.equal message.Source (Some "type-validator") "should name the plugin"
    | other -> failtestf "expected one error, got %A" other

[<Tests>]
let definiteMismatchTests =
    testList "TypeValidator definite mismatches" [
        test "a string body under a declared Int should be an error" {
            expectMismatch (definition [] intType (text "five"))
        }

        test "an input used at the wrong type should be an error" {
            expectMismatch (definition [ "x", stringType ] intType (variable "x"))
        }

        test "a let binding used at the wrong type should be an error" {
            let body =
                Value.LetDefinition((), Name.fromString "x", definition [] stringType (text "s"), variable "x")

            expectMismatch (definition [] intType body)
        }

        test "an if condition that is not Bool should be an error" {
            expectMismatch (definition [] intType (Value.IfThenElse((), whole 1L, whole 2L, whole 3L)))
        }

        test "if branches of different types should be an error" {
            expectMismatch (definition [] (Type.Variable((), Name.fromString "a")) (Value.IfThenElse((), flag true, whole 2L, text "3")))
        }

        test "list elements of different types should be an error" {
            expectMismatch (definition [] (Type.Variable((), Name.fromString "a")) (Value.List((), [ whole 1L; text "2" ])))
        }

        test "a function argument of the wrong type should be an error" {
            let fn = Type.Function((), intType, stringType)
            let body = Value.Apply((), variable "f", text "wrong")

            expectMismatch (definition [ "f", fn ] stringType body)
        }

        test "a function result used at the wrong type should be an error" {
            let fn = Type.Function((), intType, stringType)
            let body = Value.Apply((), variable "f", whole 1L)

            expectMismatch (definition [ "f", fn ] intType body)
        }

        test "a mismatch nested inside a tuple should be found" {
            let inner = Value.IfThenElse((), whole 1L, whole 2L, whole 3L)

            expectMismatch (definition [] (Type.Variable((), Name.fromString "a")) (Value.Tuple((), [ inner ])))
        }
    ]

[<Tests>]
let noFalseErrorTests =
    let alias: Type<unit> = Type.Reference((), fq "My.App:Domain:cents", [])
    let x = Name.fromString "x"
    let any: Type<unit> = Type.Variable((), Name.fromString "a")
    let bindX: Pattern<unit> = Pattern.AsPattern((), Pattern.WildcardPattern(), x)

    testList "TypeValidator reports no false errors" [
        test "a declared input in scope should pass" {
            expectClean (definition [ "x", intType ] intType (variable "x"))
        }

        test "a literal under a model alias should pass" {
            expectClean (definition [] alias (whole 5L))
        }

        test "a whole number literal under Float should pass" {
            expectClean (definition [] floatType (whole 5L))
        }

        test "a string literal under String.String should pass" {
            expectClean (definition [] stringType (text "s"))
        }

        test "a char literal under Char.Char should pass" {
            expectClean (definition [] charType (literal (Literal.CharLiteral 'c')))
        }

        test "a bool literal under Bool.Bool should pass" {
            expectClean (definition [] (sdkType "Bool:Bool") (flag true))
        }

        test "a literal under a type variable should pass" {
            expectClean (definition [] any (whole 5L))
        }

        test "a lambda parameter should shadow an outer input" {
            let body = Value.Lambda((), bindX, variable "x")

            expectClean (definition [ "x", stringType ] any body)
        }

        test "a shadowed name used at its inner type should pass" {
            let body = Value.PatternMatch((), flag true, [ bindX, variable "x" ])

            expectClean (definition [ "x", stringType ] intType body)
        }

        test "a destructured name should shadow an outer input" {
            let body = Value.Destructure((), bindX, whole 1L, variable "x")

            expectClean (definition [ "x", stringType ] intType body)
        }

        test "a let with inputs should shadow an outer input without a known type" {
            let inner = definition [ "y", intType ] intType (variable "y")
            let body = Value.LetDefinition((), x, inner, variable "x")

            expectClean (definition [ "x", stringType ] intType body)
        }

        test "a recursive let should shadow an outer input" {
            let body =
                Value.LetRecursion((), Map.ofList [ x, definition [] intType (whole 1L) ], variable "x")

            expectClean (definition [ "x", stringType ] intType body)
        }

        testList "unsupported forms should be silent" [
            for name, body in
                [
                    "a reference", (Value.Reference((), fq "My.App:Rules:other"): Value<unit, unit>)
                    "a constructor", Value.Constructor((), fq "My.App:Rules:Other")
                    "a field function", Value.FieldFunction((), x)
                    "a record", Value.Record((), Map.ofList [ x, whole 1L ])
                    "a field", Value.Field((), variable "r", x)
                    "an unknown variable", variable "notInScope"
                    "a record update", Value.UpdateRecord((), variable "r", Map.ofList [ x, whole 1L ])
                    "an unknown function application", Value.Apply((), Value.Reference((), fq "My.App:Rules:f"), whole 1L)
                    "a unit", Value.Unit()
                ] do
                test name { expectClean (definition [] intType body) }
        ]
    ]

[<Tests>]
let packageTests =
    testList "TypeValidator on packages" [
        test "should validate every module and value" {
            let good = definition [] intType (whole 1L)
            let bad = definition [] intType (text "s")

            let moduleOf (values: (string * ValueDefinition<unit, unit>) list) : Module.ModuleDefinition<unit, unit> = {
                Types = Map.empty
                Values =
                    values
                    |> List.map (fun (name, value) -> Name.fromString name, { Access = Public; Value = WithDocumentation("doc", value) })
                    |> Map.ofList
                Doc = None
            }

            let input: Package.PackageDefinition<unit, unit> = {
                Modules =
                    Map.ofList [
                        ModulePath.modulePath (Path [ Name.fromString "Domain"; Name.fromString "Rules" ]),
                        { Access = Private; Value = moduleOf [ "bad", bad; "good", good ] }
                        ModulePath.modulePath (Path [ Name.fromString "Other" ]),
                        { Access = Public; Value = moduleOf [ "alsoBad", bad ] }
                    ]
            }

            let _, file = (TypeValidator.create ()).Transform input VFile.empty

            Expect.equal
                (file.Messages |> List.map (fun message -> message.Location) |> List.sort)
                [ Some "Domain.Rules.bad"; Some "Other.alsoBad" ]
                "each error should name its module and value"
        }

        test "createWithEnv should use the given bindings" {
            let env = TypeValidator.emptyEnv |> TypeValidator.addBinding (Name.fromString "x") stringType
            let _, file = (TypeValidator.createWithEnv env).Transform (package (definition [] intType (variable "x"))) VFile.empty

            Expect.hasLength file.Messages 1 "the binding's type should be known"
        }

        test "should work in a typed processor" {
            let input = package (definition [] intType (text "s"))
            let parser: Parser<Package.PackageDefinition<unit, unit>> = fun _ -> Ok input

            let tree =
                MorphirProcessor.empty
                |> MorphirProcessor.parse parser
                |> MorphirProcessor.plugin (TypeValidator.create ())
                |> MorphirProcessor.processFile VFile.empty

            Expect.isTrue (VFileTree.hasErrors tree) "the error should reach the tree"
        }
    ]

[<Tests>]
let literalTypeInferenceTests =
    let nameOf (lit: Literal) =
        match TypeValidator.inferLiteralType lit with
        | Type.Reference(_, fqName, []) -> FQName.toString fqName
        | other -> failtestf "expected Reference type, got %A" other

    testList "Literal Type Inference" [
        test "bool literal should infer Bool type" {
            Expect.stringContains (nameOf (Literal.BoolLiteral true)) "Bool" "type should be Bool"
        }

        test "string literal should infer String type" {
            Expect.stringContains (nameOf (Literal.StringLiteral "test")) "String" "type should be String"
        }

        test "int literal should infer Int type" {
            Expect.stringContains (nameOf (Literal.WholeNumberLiteral 42L)) "Int" "type should be Int"
        }

        test "float literal should infer Float type" {
            Expect.stringContains (nameOf (Literal.FloatLiteral 3.14)) "Float" "type should be Float"
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
