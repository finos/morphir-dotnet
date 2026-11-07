/// Every IIrBuilder member in each family (task 2.5): classic nodes carry the inferred type as their
/// attribute, v4 nodes carry their inferred type and their source.
module Morphir.Compilation.Tests.BuilderMemberTests

open System.Numerics
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.IR.Model

let private here =
    Location.Create("Main.fs", Range.Create(Position.Create(2, 4), Position.Create(2, 7)))

let private n (text: string) = Name.fromString text

let private fq local = {
    PackagePath = PackageName.packageName (Path.fromList [ n "acme" ])
    ModulePath = ModulePath.modulePath (Path.fromList [ n "main" ])
    LocalName = n local
}

/// One member test: the built node equals the expected node.
let private item name (actual: 'T) (expected: 'T) =
    test name { Expect.equal actual expected name }

let private ok (result: Checked<'T>) =
    match result with
    | Passed(value, []) -> value
    | other -> failtest $"Expected a passed result without diagnostics, got {other}"

let private plan = {
    Path = ModulePath.modulePath (Path.fromList [ n "main" ])
    Access = Access.Public
    Documentation = Some "Main."
    Location = here
}

let private package = {
    Package = PackageName.packageName (Path.fromList [ n "acme" ])
    Documentation = None
}

module private C =
    let b = ClassicBuilder() :> IIrBuilder<_, _, _, _, _, _, _>
    let t = Classic.Type.Reference((), fq "int", [])
    let v = Classic.Value.Unit t
    let p = Classic.WildcardPattern t

    let d: Classic.ValueDefinition<unit, Classic.Type<unit>> = {
        InputTypes = [ n "x", t, t ]
        OutputType = t
        Body = v
    }

module private V =
    let b = V4Builder() :> IIrBuilder<_, _, _, _, _, _, _>

    let source: V4.SourceLocation = {
        StartLine = 3
        StartColumn = 5
        EndLine = 3
        EndColumn = 8
    }

    let ta: V4.TypeAttributes =
        { V4.TypeAttributes.empty with
            Source = Some source
        }

    let t = V4.Type.Reference(V4.TypeAttributes.empty, fq "int", [])

    let a: V4.ValueAttributes = {
        Source = Some source
        InferredType = Some t
        Extensions = None
    }

    let v = V4.Value.Unit a
    let p = V4.WildcardPattern a

    let d: V4.ValueDefinition = {
        InputTypes = [ n "x", t ]
        Body = V4.ExpressionBody(t, v)
    }

[<Tests>]
let tests =
    testList "Every builder member" [
        testList "ClassicBuilder" [
            let b, t, v, p, d = C.b, C.t, C.v, C.p, C.d
            item "Family" b.Family ModelFamily.Classic

            item
                "TypeVariable"
                (ok (b.TypeVariable(here, n "a")))
                (Classic.Type.Variable((), n "a"))

            item
                "TypeReference"
                (ok (b.TypeReference(here, fq "list", [ t ])))
                (Classic.Type.Reference((), fq "list", [ t ]))

            item "TypeTuple" (ok (b.TypeTuple(here, [ t; t ]))) (Classic.Type.Tuple((), [ t; t ]))

            item
                "TypeRecord"
                (ok (b.TypeRecord(here, [ n "a", t ])))
                (Classic.Type.Record((), [ { Name = n "a"; Type = t } ]))

            item "TypeFunction" (ok (b.TypeFunction(here, t, t))) (Classic.Type.Function((), t, t))
            item "TypeUnit" (ok (b.TypeUnit here)) (Classic.Type.Unit())
            item "PatternWildcard" (ok (b.PatternWildcard(here, t))) (Classic.WildcardPattern t)
            item "PatternAs" (ok (b.PatternAs(here, t, p, n "x"))) (Classic.AsPattern(t, p, n "x"))

            item
                "PatternTuple"
                (ok (b.PatternTuple(here, t, [ p ])))
                (Classic.TuplePattern(t, [ p ]))

            item
                "PatternConstructor"
                (ok (b.PatternConstructor(here, t, fq "just", [ p ])))
                (Classic.ConstructorPattern(t, fq "just", [ p ]))

            item "PatternEmptyList" (ok (b.PatternEmptyList(here, t))) (Classic.EmptyListPattern t)

            item
                "PatternHeadTail"
                (ok (b.PatternHeadTail(here, t, p, p)))
                (Classic.HeadTailPattern(t, p, p))

            item
                "PatternLiteral"
                (ok (b.PatternLiteral(here, t, LiteralValue.Bool true)))
                (Classic.LiteralPattern(t, Classic.BoolLiteral true))

            item "PatternUnit" (ok (b.PatternUnit(here, t))) (Classic.UnitPattern t)

            item
                "Literal"
                (ok (b.Literal(here, t, LiteralValue.String "s")))
                (Classic.Value.Literal(t, Classic.StringLiteral "s"))

            item
                "Constructor"
                (ok (b.Constructor(here, t, fq "just")))
                (Classic.Value.Constructor(t, fq "just"))

            item "Tuple" (ok (b.Tuple(here, t, [ v; v ]))) (Classic.Value.Tuple(t, [ v; v ]))
            item "List" (ok (b.List(here, t, [ v ]))) (Classic.Value.List(t, [ v ]))

            item
                "Record"
                (ok (b.Record(here, t, [ n "a", v ])))
                (Classic.Value.Record(t, Map.ofList [ n "a", v ]))

            item "Variable" (ok (b.Variable(here, t, n "x"))) (Classic.Value.Variable(t, n "x"))

            item
                "Reference"
                (ok (b.Reference(here, t, fq "rate")))
                (Classic.Value.Reference(t, fq "rate"))

            item "Field" (ok (b.Field(here, t, v, n "a"))) (Classic.Value.Field(t, v, n "a"))

            item
                "FieldFunction"
                (ok (b.FieldFunction(here, t, n "a")))
                (Classic.Value.FieldFunction(t, n "a"))

            item "Apply" (ok (b.Apply(here, t, v, v))) (Classic.Value.Apply(t, v, v))
            item "Lambda" (ok (b.Lambda(here, t, p, v))) (Classic.Value.Lambda(t, p, v))

            item
                "LetDefinition"
                (ok (b.LetDefinition(here, t, n "x", d, v)))
                (Classic.Value.LetDefinition(t, n "x", d, v))

            item
                "LetRecursion"
                (ok (b.LetRecursion(here, t, [ n "f", d ], v)))
                (Classic.Value.LetRecursion(t, Map.ofList [ n "f", d ], v))

            item
                "Destructure"
                (ok (b.Destructure(here, t, p, v, v)))
                (Classic.Value.Destructure(t, p, v, v))

            item
                "IfThenElse"
                (ok (b.IfThenElse(here, t, v, v, v)))
                (Classic.Value.IfThenElse(t, v, v, v))

            item
                "PatternMatch"
                (ok (b.PatternMatch(here, t, v, [ p, v ])))
                (Classic.Value.PatternMatch(t, v, [ p, v ]))

            item
                "UpdateRecord"
                (ok (b.UpdateRecord(here, t, v, [ n "a", v ])))
                (Classic.Value.UpdateRecord(t, v, Map.ofList [ n "a", v ]))

            item "Unit" (ok (b.Unit(here, t))) (Classic.Value.Unit t)
            item "ValueDefinition" (ok (b.ValueDefinition(here, [ n "x", t ], t, v))) d

            item
                "TypeAlias"
                (ok (b.TypeAlias(here, [ n "a" ], t)))
                (Classic.Type.TypeAliasDefinition([ n "a" ], t))

            item
                "CustomType"
                (ok (
                    b.CustomType(
                        here,
                        [],
                        Access.Private,
                        [
                            {
                                Name = n "just"
                                Arguments = [ n "value", t ]
                            }
                        ]
                    )
                ))
                (Classic.Type.CustomTypeDefinition(
                    [],
                    ({
                        Access = Classic.AccessControlled.Private
                        Value = Map.ofList [ n "just", [ n "value", t ] ]
                    }
                    : Classic.AccessControlled.AccessControlled<_>)
                ))

            test "Module and Package" {
                let m =
                    ok (
                        b.Module(
                            plan,
                            [],
                            [
                                {
                                    Name = n "rate"
                                    Access = Access.Private
                                    Documentation = Some "Rate."
                                    Definition = d
                                    Location = here
                                }
                            ]
                        )
                    )

                let expectedModule: Classic.Module.ModuleDefinition<unit, Classic.Type<unit>> = {
                    Types = Map.empty
                    Values =
                        Map.ofList [
                            n "rate",
                            ({
                                Access = Classic.AccessControlled.Private
                                Value = Classic.Documented.WithDocumentation("Rate.", d)
                            }
                            : Classic.AccessControlled.AccessControlled<_>)
                        ]
                    Doc = Some "Main."
                }

                Expect.equal
                    (snd m)
                    ({
                        Access = Classic.AccessControlled.Public
                        Value = expectedModule
                    }
                    : Classic.AccessControlled.AccessControlled<_>)
                    "module"

                match ok (b.Package(package, [ m ])) with
                | Classic.Distribution.Library(name, dependencies, definition) ->
                    Expect.equal name package.Package "package"
                    Expect.isEmpty dependencies "no dependencies"

                    Expect.equal
                        (Map.keys definition.Modules
                         |> List.ofSeq)
                        [ plan.Path ]
                        "modules"
                | other -> failtest $"{other}"
            }
        ]

        testList "V4Builder" [
            let b, t, ta, a, v, p, d = V.b, V.t, V.ta, V.a, V.v, V.p, V.d
            item "Family" b.Family ModelFamily.V4
            item "TypeVariable" (ok (b.TypeVariable(here, n "a"))) (V4.Type.Variable(ta, n "a"))

            item
                "TypeReference"
                (ok (b.TypeReference(here, fq "list", [ t ])))
                (V4.Type.Reference(ta, fq "list", [ t ]))

            item "TypeTuple" (ok (b.TypeTuple(here, [ t; t ]))) (V4.Type.Tuple(ta, [ t; t ]))

            item
                "TypeRecord"
                (ok (b.TypeRecord(here, [ n "a", t ])))
                (V4.Type.Record(ta, [ n "a", t ]))

            item "TypeFunction" (ok (b.TypeFunction(here, t, t))) (V4.Type.Function(ta, t, t))
            item "TypeUnit" (ok (b.TypeUnit here)) (V4.Type.Unit ta)
            item "PatternWildcard" (ok (b.PatternWildcard(here, t))) (V4.WildcardPattern a)
            item "PatternAs" (ok (b.PatternAs(here, t, p, n "x"))) (V4.AsPattern(a, p, n "x"))
            item "PatternTuple" (ok (b.PatternTuple(here, t, [ p ]))) (V4.TuplePattern(a, [ p ]))

            item
                "PatternConstructor"
                (ok (b.PatternConstructor(here, t, fq "just", [ p ])))
                (V4.ConstructorPattern(a, fq "just", [ p ]))

            item "PatternEmptyList" (ok (b.PatternEmptyList(here, t))) (V4.EmptyListPattern a)

            item
                "PatternHeadTail"
                (ok (b.PatternHeadTail(here, t, p, p)))
                (V4.HeadTailPattern(a, p, p))

            item
                "PatternLiteral"
                (ok (b.PatternLiteral(here, t, LiteralValue.Bool true)))
                (V4.LiteralPattern(a, V4.BoolLiteral true))

            item "PatternUnit" (ok (b.PatternUnit(here, t))) (V4.UnitPattern a)

            item
                "Literal"
                (ok (b.Literal(here, t, LiteralValue.String "s")))
                (V4.Value.Literal(a, V4.StringLiteral "s"))

            item
                "Constructor"
                (ok (b.Constructor(here, t, fq "just")))
                (V4.Value.Constructor(a, fq "just"))

            item "Tuple" (ok (b.Tuple(here, t, [ v; v ]))) (V4.Value.Tuple(a, [ v; v ]))
            item "List" (ok (b.List(here, t, [ v ]))) (V4.Value.List(a, [ v ]))
            item "Record" (ok (b.Record(here, t, [ n "a", v ]))) (V4.Value.Record(a, [ n "a", v ]))
            item "Variable" (ok (b.Variable(here, t, n "x"))) (V4.Value.Variable(a, n "x"))

            item
                "Reference"
                (ok (b.Reference(here, t, fq "rate")))
                (V4.Value.Reference(a, fq "rate"))

            item "Field" (ok (b.Field(here, t, v, n "a"))) (V4.Value.Field(a, v, n "a"))

            item
                "FieldFunction"
                (ok (b.FieldFunction(here, t, n "a")))
                (V4.Value.FieldFunction(a, n "a"))

            item "Apply" (ok (b.Apply(here, t, v, v))) (V4.Value.Apply(a, v, v))
            item "Lambda" (ok (b.Lambda(here, t, p, v))) (V4.Value.Lambda(a, p, v))

            item
                "LetDefinition"
                (ok (b.LetDefinition(here, t, n "x", d, v)))
                (V4.Value.LetDefinition(a, n "x", { Doc = None; Value = d }, v))

            item
                "Destructure"
                (ok (b.Destructure(here, t, p, v, v)))
                (V4.Value.Destructure(a, p, v, v))

            item
                "IfThenElse"
                (ok (b.IfThenElse(here, t, v, v, v)))
                (V4.Value.IfThenElse(a, v, v, v))

            item
                "PatternMatch"
                (ok (b.PatternMatch(here, t, v, [ p, v ])))
                (V4.Value.PatternMatch(a, v, [ p, v ]))

            item
                "UpdateRecord"
                (ok (b.UpdateRecord(here, t, v, [ n "a", v ])))
                (V4.Value.UpdateRecord(a, v, [ n "a", v ]))

            item "Unit" (ok (b.Unit(here, t))) v
            item "ValueDefinition" (ok (b.ValueDefinition(here, [ n "x", t ], t, v))) d

            item
                "TypeAlias"
                (ok (b.TypeAlias(here, [ n "a" ], t)))
                (V4.TypeAliasDefinition([ n "a" ], t))

            test "LetRecursion and CustomType keep order in ordered maps" {
                match ok (b.LetRecursion(here, t, [ n "g", d; n "f", d ], v)) with
                | V4.Value.LetRecursion(attributes, definitions, inValue) ->
                    Expect.equal attributes a "attributes"
                    Expect.equal (V4.OrderedMap.keys definitions) [ n "g"; n "f" ] "order"
                    Expect.equal inValue v "in"
                | other -> failtest $"{other}"

                match
                    ok (
                        b.CustomType(
                            here,
                            [],
                            Access.Public,
                            [
                                { Name = n "nothing"; Arguments = [] }
                                {
                                    Name = n "just"
                                    Arguments = [ n "value", t ]
                                }
                            ]
                        )
                    )
                with
                | V4.CustomTypeDefinition([], V4.Public, constructors) ->
                    Expect.equal (V4.OrderedMap.keys constructors) [ n "nothing"; n "just" ] "order"
                | other -> failtest $"{other}"
            }

            test "Module and Package" {
                let m =
                    ok (
                        b.Module(
                            plan,
                            [],
                            [
                                {
                                    Name = n "rate"
                                    Access = Access.Private
                                    Documentation = Some "Rate."
                                    Definition = d
                                    Location = here
                                }
                            ]
                        )
                    )

                match snd m with
                | {
                      Access = V4.Public
                      Value = definition
                  } ->
                    Expect.equal definition.Doc (Some "Main.") "module doc"

                    match V4.OrderedMap.toList definition.Values with
                    | [ name,
                        ({
                             Access = V4.Private
                             Value = { Doc = Some "Rate."; Value = value }
                         }: V4.AccessControlled<_>) ] ->
                        Expect.equal name (n "rate") "value name"
                        Expect.equal value d "definition"
                    | other -> failtest $"{other}"
                | other -> failtest $"{other}"

                match ok (b.Package(package, [ m ])) with
                | V4.Library(name, _, definition) ->
                    Expect.equal name package.Package "package"
                    Expect.equal (V4.OrderedMap.keys definition.Modules) [ plan.Path ] "modules"
                | other -> failtest $"{other}"
            }
        ]

        testList "Duplicate names are refused in both families" [
            let member' name = {
                Name = n name
                Access = Access.Public
                Documentation = None
                Definition = C.d
                Location = here
            }

            test "classic module" {
                let result = C.b.Module(plan, [], [ member' "rate"; member' "rate" ])

                Expect.equal
                    (result.Diagnostics
                     |> Seq.map (fun d ->
                         (d.CodeOrNull
                          |> Option.ofObj
                          |> Option.map string)
                     )
                     |> List.ofSeq)
                    [ Some "compile.duplicate_name" ]
                    "code"
            }

            test "v4 module" {
                let m name = {
                    Name = n name
                    Access = Access.Public
                    Documentation = None
                    Definition = V.d
                    Location = here
                }

                let result = V.b.Module(plan, [], [ m "rate"; m "rate" ])

                Expect.equal
                    (result.Diagnostics
                     |> Seq.map (fun d ->
                         (d.CodeOrNull
                          |> Option.ofObj
                          |> Option.map string)
                     )
                     |> List.ofSeq)
                    [ Some "compile.duplicate_name" ]
                    "code"
            }

            test "packages with a repeated module path" {
                let m = ok (C.b.Module(plan, [], []))
                Expect.isFalse (C.b.Package(package, [ m; m ])).IsPassed "classic"
                let v = ok (V.b.Module(plan, [], []))
                Expect.isFalse (V.b.Package(package, [ v; v ])).IsPassed "v4"
            }
        ]
    ]
