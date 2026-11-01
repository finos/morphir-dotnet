module Morphir.IR.Pipeline.Plugins.Tests.OptimizerTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins

let private sdkFn name = FQName.fromString ("Morphir.SDK:Basics:" + name) ":"
let private userFn name = FQName.fromString ("My.App:Rules:" + name) ":"

let private whole (value: int64): Value<unit, unit> = Value.Literal((), Literal.WholeNumberLiteral value)
let private boolean (value: bool): Value<unit, unit> = Value.Literal((), Literal.BoolLiteral value)

let private apply2 (fn: FQName) left right : Value<unit, unit> =
    Value.Apply((), Value.Apply((), Value.Reference((), fn), left), right)

let private intType: Type<unit> = Type.Reference((), FQName.fromString "Morphir.SDK:Basics:Int" ":", [])

let private definition (body: Value<unit, unit>) : ValueDefinition<unit, unit> =
    { InputTypes = []; OutputType = intType; Body = body }

let private package (body: Value<unit, unit>) : Package.PackageDefinition<unit, unit> =
    let moduleDefinition: Module.ModuleDefinition<unit, unit> = {
        Types = Map.empty
        Values = Map.ofList [ Name.fromString "value", { Access = Public; Value = WithoutDocumentation(definition body) } ]
        Doc = None
    }

    { Modules = Map.ofList [ ModulePath.modulePath (Path [ Name.fromString "Rules" ]), { Access = Public; Value = moduleDefinition } ] }

let private bodyOf (result: Package.PackageDefinition<unit, unit>) =
    let moduleDefinition = result.Modules |> Map.toList |> List.head |> snd

    match moduleDefinition.Value.Values |> Map.toList |> List.head |> snd with
    | { Value = WithoutDocumentation value } -> value.Body
    | { Value = WithDocumentation(_, value) } -> value.Body

let private run options body =
    let plugin = Optimizer.create options
    let result, file = plugin.Transform (package body) VFile.empty
    bodyOf (Option.get result), file

let private folded = fst << run Optimizer.foldingOnly

[<Tests>]
let foldingTests =
    let cases: (string * string * Literal * Literal * Literal) list = [
        "adds whole numbers", "add", Literal.WholeNumberLiteral 2L, Literal.WholeNumberLiteral 3L, Literal.WholeNumberLiteral 5L
        "subtracts whole numbers", "subtract", Literal.WholeNumberLiteral 5L, Literal.WholeNumberLiteral 3L, Literal.WholeNumberLiteral 2L
        "multiplies whole numbers", "multiply", Literal.WholeNumberLiteral 4L, Literal.WholeNumberLiteral 5L, Literal.WholeNumberLiteral 20L
        "divides whole numbers, truncating", "divide", Literal.WholeNumberLiteral -7L, Literal.WholeNumberLiteral 2L, Literal.WholeNumberLiteral -3L
        "adds floats", "add", Literal.FloatLiteral 1.5, Literal.FloatLiteral 2.5, Literal.FloatLiteral 4.0
        "compares equal", "equal", Literal.WholeNumberLiteral 5L, Literal.WholeNumberLiteral 5L, Literal.BoolLiteral true
        "compares unequal strings", "equal", Literal.StringLiteral "a", Literal.StringLiteral "b", Literal.BoolLiteral false
        "compares notEqual", "notEqual", Literal.BoolLiteral true, Literal.BoolLiteral false, Literal.BoolLiteral true
        "compares lessThan", "lessThan", Literal.WholeNumberLiteral 3L, Literal.WholeNumberLiteral 5L, Literal.BoolLiteral true
        "compares lessThanOrEqual", "lessThanOrEqual", Literal.WholeNumberLiteral 5L, Literal.WholeNumberLiteral 5L, Literal.BoolLiteral true
        "compares greaterThan floats", "greaterThan", Literal.FloatLiteral 1.0, Literal.FloatLiteral 2.0, Literal.BoolLiteral false
        "compares greaterThanOrEqual", "greaterThanOrEqual", Literal.WholeNumberLiteral 5L, Literal.WholeNumberLiteral 6L, Literal.BoolLiteral false
    ]

    let unfolded: (string * string * Literal * Literal) list = [
        "the sum overflows Int", "add", Literal.WholeNumberLiteral 2147483647L, Literal.WholeNumberLiteral 1L
        "the product overflows Int", "multiply", Literal.WholeNumberLiteral 100000L, Literal.WholeNumberLiteral 100000L
        "the minimum is divided by minus one", "divide", Literal.WholeNumberLiteral -2147483648L, Literal.WholeNumberLiteral -1L
        "an input is out of range, even if the result is not", "add", Literal.WholeNumberLiteral 3000000000L, Literal.WholeNumberLiteral -2000000000L
        "a comparison input is out of range", "lessThan", Literal.WholeNumberLiteral 3000000000L, Literal.WholeNumberLiteral 1L
        "dividing a whole number by zero", "divide", Literal.WholeNumberLiteral 10L, Literal.WholeNumberLiteral 0L
        "dividing a float by zero", "divide", Literal.FloatLiteral 1.0, Literal.FloatLiteral 0.0
        "a float result is infinite", "multiply", Literal.FloatLiteral 1e308, Literal.FloatLiteral 10.0
        "literal types differ", "equal", Literal.WholeNumberLiteral 1L, Literal.FloatLiteral 1.0
        "the SDK boolean and is unmapped", "and", Literal.BoolLiteral true, Literal.BoolLiteral false
        "the SDK boolean or is unmapped", "or", Literal.BoolLiteral true, Literal.BoolLiteral false
        "string append is unmapped", "append", Literal.StringLiteral "a", Literal.StringLiteral "b"
    ]

    testList "Optimizer folding" [
        testList "should fold when the result matches run time" [
            for name, fn, left, right, expected in cases do
                test name {
                    Expect.equal (Optimizer.tryFold (sdkFn fn) left right) (Some expected) "should fold"
                }
        ]

        testList "should not fold when" [
            for name, fn, left, right in unfolded do
                test name {
                    Expect.isNone (Optimizer.tryFold (sdkFn fn) left right) "should not fold"
                }
        ]

        test "should not fold a user function named like an SDK function" {
            Expect.isNone
                (Optimizer.tryFold (userFn "add") (Literal.WholeNumberLiteral 2L) (Literal.WholeNumberLiteral 3L))
                "the user's function must run"
        }

        test "should recognize an SDK module other than Basics as not foldable" {
            let listAdd = FQName.fromString "Morphir.SDK:List:add" ":"

            Expect.isNone (Optimizer.tryFold listAdd (Literal.WholeNumberLiteral 2L) (Literal.WholeNumberLiteral 3L)) "wrong module"
        }

        test "plugin should fold inside a package value" {
            Expect.equal (folded (apply2 (sdkFn "add") (whole 2L) (whole 3L))) (whole 5L) "should fold"
        }

        test "plugin should leave a user's add call alone" {
            let body = apply2 (userFn "add") (whole 2L) (whole 3L)

            Expect.equal (folded body) body "should not change"
        }

        test "plugin should fold bottom-up so nested expressions collapse" {
            let body = apply2 (sdkFn "add") (apply2 (sdkFn "add") (whole 1L) (whole 2L)) (whole 3L)

            Expect.equal (folded body) (whole 6L) "(1 + 2) + 3"
        }

        test "plugin should keep the attributes of the folded expression" {
            let plugin = Optimizer.create Optimizer.foldingOnly

            let attributed: Value<unit, string> =
                Value.Apply(
                    "outer",
                    Value.Apply("inner", Value.Reference("ref", sdkFn "add"), Value.Literal("left", Literal.WholeNumberLiteral 2L)),
                    Value.Literal("right", Literal.WholeNumberLiteral 3L)
                )

            let moduleDefinition: Module.ModuleDefinition<unit, string> = {
                Types = Map.empty
                Values =
                    Map.ofList [
                        Name.fromString "value",
                        { Access = Public
                          Value = WithoutDocumentation { InputTypes = []; OutputType = intType; Body = attributed } }
                    ]
                Doc = None
            }

            let input: Package.PackageDefinition<unit, string> =
                { Modules = Map.ofList [ ModulePath.modulePath (Path [ Name.fromString "Rules" ]), { Access = Public; Value = moduleDefinition } ] }

            let result, _ = plugin.Transform input VFile.empty
            let controlled = (Option.get result).Modules |> Map.toList |> List.head |> snd

            match controlled.Value.Values |> Map.toList |> List.head |> snd with
            | { Value = WithoutDocumentation value } ->
                Expect.equal value.Body (Value.Literal("outer", Literal.WholeNumberLiteral 5L)) "should keep the outer attributes"
            | other -> failtestf "unexpected value %A" other
        }

        test "plugin should keep documentation and access" {
            let plugin = Optimizer.create Optimizer.foldingOnly

            let moduleDefinition: Module.ModuleDefinition<unit, unit> = {
                Types = Map.empty
                Values =
                    Map.ofList [
                        Name.fromString "value",
                        { Access = Private
                          Value = WithDocumentation("docs", definition (apply2 (sdkFn "add") (whole 2L) (whole 3L))) }
                    ]
                Doc = None
            }

            let input: Package.PackageDefinition<unit, unit> =
                { Modules = Map.ofList [ ModulePath.modulePath (Path [ Name.fromString "Rules" ]), { Access = Private; Value = moduleDefinition } ] }

            let result, _ = plugin.Transform input VFile.empty
            let controlled = (Option.get result).Modules |> Map.toList |> List.head |> snd

            Expect.equal controlled.Access Private "module access"

            match controlled.Value.Values |> Map.toList |> List.head |> snd with
            | { Access = Private; Value = WithDocumentation("docs", value) } -> Expect.equal value.Body (whole 5L) "should fold"
            | other -> failtestf "unexpected value %A" other
        }

        test "plugin should record an informational message naming the value" {
            let _, file = run Optimizer.foldingOnly (apply2 (sdkFn "add") (whole 2L) (whole 3L))

            Expect.isNonEmpty file.Messages "should report the fold"
            Expect.all file.Messages (fun message -> message.Severity = Info) "should only be informational"
            Expect.equal file.Messages.Head.Location (Some "Rules.value") "should name the value"
            Expect.equal file.Messages.Head.Source (Some "optimizer") "should name the plugin"
        }

        test "plugin should report nothing when nothing changes" {
            let _, file = run Optimizer.foldingOnly (whole 1L)

            Expect.isEmpty file.Messages "no messages"
        }
    ]

[<Tests>]
let traversalTests =
    let foldable = apply2 (sdkFn "add") (whole 2L) (whole 3L)
    let five = whole 5L
    let x = Name.fromString "x"
    let wildcard: Pattern<unit> = Pattern.WildcardPattern()
    let nested (body: Value<unit, unit>) = definition body

    let forms: (string * Value<unit, unit> * Value<unit, unit>) list = [
        "tuple", Value.Tuple((), [ foldable ]), Value.Tuple((), [ five ])
        "list", Value.List((), [ foldable ]), Value.List((), [ five ])
        "record", Value.Record((), Map.ofList [ x, foldable ]), Value.Record((), Map.ofList [ x, five ])
        "field", Value.Field((), foldable, x), Value.Field((), five, x)
        "application argument",
        Value.Apply((), Value.Reference((), userFn "f"), foldable),
        Value.Apply((), Value.Reference((), userFn "f"), five)
        "lambda", Value.Lambda((), wildcard, foldable), Value.Lambda((), wildcard, five)
        "let definition",
        Value.LetDefinition((), x, nested foldable, foldable),
        Value.LetDefinition((), x, nested five, five)
        "let recursion",
        Value.LetRecursion((), Map.ofList [ x, nested foldable ], foldable),
        Value.LetRecursion((), Map.ofList [ x, nested five ], five)
        "destructure", Value.Destructure((), wildcard, foldable, foldable), Value.Destructure((), wildcard, five, five)
        "if", Value.IfThenElse((), foldable, foldable, foldable), Value.IfThenElse((), five, five, five)
        "pattern match", Value.PatternMatch((), foldable, [ wildcard, foldable ]), Value.PatternMatch((), five, [ wildcard, five ])
        "record update",
        Value.UpdateRecord((), foldable, Map.ofList [ x, foldable ]),
        Value.UpdateRecord((), five, Map.ofList [ x, five ])
    ]

    testList "Optimizer traversal" [
        for name, input, expected in forms do
            test $"should reach into a {name}" {
                Expect.equal (folded input) expected "should fold every nested expression"
            }
    ]

[<Tests>]
let simplificationTests =
    let x = Value.Variable((), Name.fromString "x")
    let y = Value.Variable((), Name.fromString "y")
    let yes = boolean true
    let no = boolean false
    let identity = Value.Lambda((), Pattern.AsPattern((), Pattern.WildcardPattern(), Name.fromString "v"), Value.Variable((), Name.fromString "v"))

    let rewrites: (string * Value<unit, unit> * Value<unit, unit>) list = [
        "true && x", apply2 (sdkFn "and") yes x, x
        "false && x", apply2 (sdkFn "and") no x, no
        "true || x", apply2 (sdkFn "or") yes x, yes
        "false || x", apply2 (sdkFn "or") no x, x
        "if true", Value.IfThenElse((), yes, x, y), x
        "if false", Value.IfThenElse((), no, x, y), y
        "identity lambda", Value.Apply((), identity, x), x
    ]

    testList "Optimizer simplification" [
        testList "should rewrite when enabled" [
            for name, input, expected in rewrites do
                test name {
                    let result, _ = run Optimizer.all input

                    Expect.equal result expected "should simplify"
                }
        ]

        testList "should leave alone in folding-only mode" [
            for name, input, _ in rewrites do
                test name {
                    Expect.equal (folded input) input "should not change which models are accepted"
                }
        ]

        test "should not treat a user function named and as boolean and" {
            let body = apply2 (userFn "and") yes x
            let result, _ = run Optimizer.all body

            Expect.equal result body "the user's function must run"
        }

        test "emptyStats should have zero counts" {
            let stats = Optimizer.emptyStats

            Expect.equal stats.ConstantFolds 0 "folds"
            Expect.equal stats.DeadCodeEliminations 0 "dead code"
            Expect.equal stats.IdentityEliminations 0 "identities"
        }
    ]

[<Tests>]
let pipelineTests =
    testList "Optimizer in a pipeline" [
        test "should work in a typed processor" {
            let input = package (apply2 (sdkFn "add") (whole 2L) (whole 3L))
            let parser: Parser<Package.PackageDefinition<unit, unit>> = fun _ -> Ok input
            let mutable compiled = None

            let compiler: Compiler<Package.PackageDefinition<unit, unit>> =
                fun model _ ->
                    compiled <- Some(bodyOf model)
                    VFileTree.empty

            let proc = pipeline {
                parse parser
                uses (Optimizer.create Optimizer.foldingOnly)
                stringify compiler
            }

            MorphirProcessor.processFile VFile.empty proc |> ignore

            Expect.equal compiled (Some (whole 5L)) "compiler should see the optimized model"
        }
    ]
