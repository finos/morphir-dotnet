module Morphir.CodeGeneration.CSharp.Tests.FunctionCases

open System.Text.Json
open Morphir.IR
open Morphir.IR.Model

// One authored behavior corpus, built directly into each typed input family.
type Ty =
    | Bool
    | Text
    | UnitTy
    | TupleTy of Ty list
    | ListTy of Ty
    | RecordTy of (string * Ty) list
    | Own of string * string * Ty list
    | SdkTy of string * string * Ty list
    | Int
    | Generic of string
    | RawGeneric of Name
    | Fn of Ty * Ty

type Pattern =
    | PWild
    | PBind of string
    | PAs of Pattern * string
    | PTuple of Pattern list
    | PCtor of string * string * Pattern list
    | PEmptyList
    | PHeadTail of Pattern * Pattern
    | PInt of int
    | PBool of bool
    | PText of string
    | PUnit

type Expr =
    | Boolean of bool
    | StringValue of string
    | UnitValue
    | TupleValue of Expr list
    | ListValue of Expr list
    | RecordValue of (string * Expr) list
    | Ctor of string * string
    | Field of Expr * string
    | FieldFunction of string
    | Update of Expr * (string * Expr) list
    | If of Expr * Expr * Expr
    | Match of Expr * (Pattern * Expr) list
    | Destructure of Pattern * Expr * Expr
    | PatternLambda of Pattern * Expr
    | Number of int
    | Variable of string
    | Ref of string
    | SdkRef of string * string
    | Add
    | Apply of Expr * Expr
    | Lambda of string * Expr
    | Let of string * (string * Ty) list * Ty * Expr * Expr
    | LetRec of string * Ty * Expr * Expr

type Definition = {
    Name: string
    Inputs: (string * Ty) list
    Output: Ty
    Body: Expr
}

let app f args =
    List.fold (fun f arg -> Apply(f, arg)) f args

let add a b = app Add [ a; b ]

let definitions = [
    {
        Name = "identity"
        Inputs = [ "x", Generic "a" ]
        Output = Generic "a"
        Body = Variable "x"
    }
    {
        Name = "capture"
        Inputs = [ "x", Int ]
        Output = Int
        Body =
            Let(
                "f",
                [ "y", Int ],
                Int,
                add (Variable "x") (Variable "y"),
                Let("x", [], Int, Number 100, app (Variable "f") [ Number 3 ])
            )
    }
    {
        Name = "shadow"
        Inputs = [ "x", Int ]
        Output = Int
        Body = Let("x", [], Int, add (Variable "x") (Number 1), Variable "x")
    }
    {
        Name = "three"
        Inputs = [ "x", Int; "y", Int; "z", Int ]
        Output = Int
        Body = add (add (Variable "x") (Variable "y")) (Variable "z")
    }
    {
        Name = "partial"
        Inputs = [ "x", Int ]
        Output = Int
        Body =
            Let(
                "f",
                [],
                Fn(Int, Fn(Int, Int)),
                app (Ref "three") [ Variable "x" ],
                app (Variable "f") [ Number 2; Number 3 ]
            )
    }
    {
        Name = "make"
        Inputs = [ "x", Int ]
        Output = Fn(Int, Int)
        Body = Lambda("y", add (Variable "x") (Variable "y"))
    }
    {
        Name = "order"
        Inputs = [ "mark", Fn(Int, Int) ]
        Output = Int
        Body = add (app (Variable "mark") [ Number 8 ]) (app (Variable "mark") [ Number 3 ])
    }
    {
        Name = "genericUse"
        Inputs = []
        Output = Int
        Body = app (Ref "identity") [ Number 23 ]
    }
]

let n = Name.fromString
let pkg = PackageName.packageNameFromString "example"
let path = ModulePath.modulePathFromString "functions"
let own local = FQName.fqName pkg path (n local)
let a = Unchecked.defaultof<JsonElement>

let constructorName modul local =
    if List.contains modul [ "maybe"; "result"; "basics" ] then
        TypeCases.sdk modul local
    else
        FQName.fqName pkg (ModulePath.modulePathFromString modul) (n local)

let classicWith definitions =
    let rec typ =
        function
        | Int -> Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk "basics" "int", [])
        | Bool -> Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk "basics" "bool", [])
        | Text -> Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk "string" "string", [])
        | UnitTy -> Morphir.IR.Classic.Type.Unit a
        | TupleTy items -> Morphir.IR.Classic.Type.Tuple(a, List.map typ items)
        | ListTy item ->
            Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk "list" "list", [ typ item ])
        | RecordTy fields ->
            Morphir.IR.Classic.Type.Record(
                a,
                fields
                |> List.map (fun (name, t) -> {
                    Morphir.IR.Classic.Field.Name = n name
                    Type = typ t
                })
            )
        | Own(modul, local, args) ->
            Morphir.IR.Classic.Type.Reference(
                a,
                FQName.fqName pkg (ModulePath.modulePathFromString modul) (n local),
                List.map typ args
            )
        | SdkTy(modul, local, args) ->
            Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk modul local, List.map typ args)
        | Generic name -> Morphir.IR.Classic.Type.Variable(a, n name)
        | RawGeneric name -> Morphir.IR.Classic.Type.Variable(a, name)
        | Fn(input, output) -> Morphir.IR.Classic.Type.Function(a, typ input, typ output)

    let rec pattern =
        function
        | PWild -> Morphir.IR.Classic.WildcardPattern a
        | PBind name ->
            Morphir.IR.Classic.AsPattern(a, Morphir.IR.Classic.WildcardPattern a, n name)
        | PAs(inner, name) -> Morphir.IR.Classic.AsPattern(a, pattern inner, n name)
        | PTuple items -> Morphir.IR.Classic.TuplePattern(a, List.map pattern items)
        | PCtor(modul, local, args) ->
            Morphir.IR.Classic.ConstructorPattern(
                a,
                constructorName modul local,
                List.map pattern args
            )
        | PEmptyList -> Morphir.IR.Classic.EmptyListPattern a
        | PHeadTail(head, tail) -> Morphir.IR.Classic.HeadTailPattern(a, pattern head, pattern tail)
        | PInt x ->
            Morphir.IR.Classic.LiteralPattern(a, Morphir.IR.Classic.WholeNumberLiteral(int64 x))
        | PBool x -> Morphir.IR.Classic.LiteralPattern(a, Morphir.IR.Classic.BoolLiteral x)
        | PText x -> Morphir.IR.Classic.LiteralPattern(a, Morphir.IR.Classic.StringLiteral x)
        | PUnit -> Morphir.IR.Classic.UnitPattern a

    let rec value =
        function
        | LetRec(name, output, body, rest) ->
            Morphir.IR.Classic.Value.LetRecursion(
                a,
                Map.ofList [
                    n name,
                    {
                        InputTypes = []
                        OutputType = typ output
                        Body = value body
                    }
                ],
                value rest
            )
        | Boolean x -> Morphir.IR.Classic.Value.Literal(a, Morphir.IR.Classic.BoolLiteral x)
        | StringValue x -> Morphir.IR.Classic.Value.Literal(a, Morphir.IR.Classic.StringLiteral x)
        | UnitValue -> Morphir.IR.Classic.Value.Unit a
        | TupleValue items -> Morphir.IR.Classic.Value.Tuple(a, List.map value items)
        | ListValue items -> Morphir.IR.Classic.Value.List(a, List.map value items)
        | RecordValue fields ->
            Morphir.IR.Classic.Value.Record(
                a,
                fields
                |> List.map (fun (name, x) -> n name, value x)
                |> Map.ofList
            )
        | Ctor(modul, local) -> Morphir.IR.Classic.Value.Constructor(a, constructorName modul local)
        | Field(target, name) -> Morphir.IR.Classic.Value.Field(a, value target, n name)
        | FieldFunction name -> Morphir.IR.Classic.Value.FieldFunction(a, n name)
        | Update(target, fields) ->
            Morphir.IR.Classic.Value.UpdateRecord(
                a,
                value target,
                fields
                |> List.map (fun (name, x) -> n name, value x)
                |> Map.ofList
            )
        | If(condition, yes, no) ->
            Morphir.IR.Classic.Value.IfThenElse(a, value condition, value yes, value no)
        | Match(target, cases) ->
            Morphir.IR.Classic.Value.PatternMatch(
                a,
                value target,
                cases
                |> List.map (fun (p, body) -> pattern p, value body)
            )
        | Destructure(p, x, body) ->
            Morphir.IR.Classic.Value.Destructure(a, pattern p, value x, value body)
        | PatternLambda(p, body) -> Morphir.IR.Classic.Value.Lambda(a, pattern p, value body)
        | Number x ->
            Morphir.IR.Classic.Value.Literal(a, Morphir.IR.Classic.WholeNumberLiteral(int64 x))
        | Variable name -> Morphir.IR.Classic.Value.Variable(a, n name)
        | Ref name -> Morphir.IR.Classic.Value.Reference(a, own name)
        | Add -> Morphir.IR.Classic.Value.Reference(a, TypeCases.sdk "basics" "add")
        | SdkRef(modul, local) -> Morphir.IR.Classic.Value.Reference(a, TypeCases.sdk modul local)
        | Apply(f, x) -> Morphir.IR.Classic.Value.Apply(a, value f, value x)
        | Lambda(name, body) ->
            Morphir.IR.Classic.Value.Lambda(
                a,
                Morphir.IR.Classic.AsPattern(a, Morphir.IR.Classic.WildcardPattern a, n name),
                value body
            )
        | Let(name, inputs, output, body, rest) ->
            Morphir.IR.Classic.Value.LetDefinition(
                a,
                n name,
                definition inputs output body,
                value rest
            )

    and definition
        inputs
        output
        body
        : Morphir.IR.Classic.ValueDefinition<JsonElement, JsonElement> =
        {
            InputTypes =
                inputs
                |> List.map (fun (name, t) -> n name, a, typ t)
            OutputType = typ output
            Body = value body
        }

    let modul: Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement> = {
        Types = Map.empty
        Values =
            definitions
            |> List.map (fun d ->
                n d.Name,
                Morphir.IR.Classic.AccessControlled.public' (
                    Morphir.IR.Classic.Documented.WithoutDocumentation(
                        definition d.Inputs d.Output d.Body
                    )
                )
            )
            |> Map.ofList
        Doc = None
    }

    {
        SourceVersion = TypeCases.version "3"
        Distribution =
            DistributionModel.Classic(
                Morphir.IR.Classic.Distribution.Library(
                    pkg,
                    Map.empty,
                    {
                        Modules =
                            Map.ofList[path, Morphir.IR.Classic.AccessControlled.public' modul]
                    }
                )
            )
    }

let v4With definitions =
    let ta = Morphir.IR.V4.TypeAttributes.empty
    let va = Morphir.IR.V4.ValueAttributes.empty

    let rec typ =
        function
        | Int -> Morphir.IR.V4.Type.Reference(ta, TypeCases.sdk "basics" "int", [])
        | Bool -> Morphir.IR.V4.Type.Reference(ta, TypeCases.sdk "basics" "bool", [])
        | Text -> Morphir.IR.V4.Type.Reference(ta, TypeCases.sdk "string" "string", [])
        | UnitTy -> Morphir.IR.V4.Type.Unit ta
        | TupleTy items -> Morphir.IR.V4.Type.Tuple(ta, List.map typ items)
        | ListTy item -> Morphir.IR.V4.Type.Reference(ta, TypeCases.sdk "list" "list", [ typ item ])
        | RecordTy fields ->
            Morphir.IR.V4.Type.Record(
                ta,
                fields
                |> List.map (fun (name, t) -> n name, typ t)
            )
        | Own(modul, local, args) ->
            Morphir.IR.V4.Type.Reference(
                ta,
                FQName.fqName pkg (ModulePath.modulePathFromString modul) (n local),
                List.map typ args
            )
        | SdkTy(modul, local, args) ->
            Morphir.IR.V4.Type.Reference(ta, TypeCases.sdk modul local, List.map typ args)
        | Generic name -> Morphir.IR.V4.Type.Variable(ta, n name)
        | RawGeneric name -> Morphir.IR.V4.Type.Variable(ta, name)
        | Fn(input, output) -> Morphir.IR.V4.Type.Function(ta, typ input, typ output)

    let rec pattern =
        function
        | PWild -> Morphir.IR.V4.WildcardPattern va
        | PBind name -> Morphir.IR.V4.AsPattern(va, Morphir.IR.V4.WildcardPattern va, n name)
        | PAs(inner, name) -> Morphir.IR.V4.AsPattern(va, pattern inner, n name)
        | PTuple items -> Morphir.IR.V4.TuplePattern(va, List.map pattern items)
        | PCtor(modul, local, args) ->
            Morphir.IR.V4.ConstructorPattern(va, constructorName modul local, List.map pattern args)
        | PEmptyList -> Morphir.IR.V4.EmptyListPattern va
        | PHeadTail(head, tail) -> Morphir.IR.V4.HeadTailPattern(va, pattern head, pattern tail)
        | PInt x -> Morphir.IR.V4.LiteralPattern(va, Morphir.IR.V4.IntegerLiteral(bigint x))
        | PBool x -> Morphir.IR.V4.LiteralPattern(va, Morphir.IR.V4.BoolLiteral x)
        | PText x -> Morphir.IR.V4.LiteralPattern(va, Morphir.IR.V4.StringLiteral x)
        | PUnit -> Morphir.IR.V4.UnitPattern va

    let rec value =
        function
        | LetRec(name, output, body, rest) ->
            Morphir.IR.V4.Value.LetRecursion(
                va,
                TypeCases.ordered [
                    n name,
                    {
                        Doc = None
                        Value = {
                            InputTypes = []
                            Body = Morphir.IR.V4.ExpressionBody(typ output, value body)
                        }
                    }
                ],
                value rest
            )
        | Boolean x -> Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.BoolLiteral x)
        | StringValue x -> Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.StringLiteral x)
        | UnitValue -> Morphir.IR.V4.Value.Unit va
        | TupleValue items -> Morphir.IR.V4.Value.Tuple(va, List.map value items)
        | ListValue items -> Morphir.IR.V4.Value.List(va, List.map value items)
        | RecordValue fields ->
            Morphir.IR.V4.Value.Record(
                va,
                fields
                |> List.map (fun (name, x) -> n name, value x)
            )
        | Ctor(modul, local) -> Morphir.IR.V4.Value.Constructor(va, constructorName modul local)
        | Field(target, name) -> Morphir.IR.V4.Value.Field(va, value target, n name)
        | FieldFunction name -> Morphir.IR.V4.Value.FieldFunction(va, n name)
        | Update(target, fields) ->
            Morphir.IR.V4.Value.UpdateRecord(
                va,
                value target,
                fields
                |> List.map (fun (name, x) -> n name, value x)
            )
        | If(condition, yes, no) ->
            Morphir.IR.V4.Value.IfThenElse(va, value condition, value yes, value no)
        | Match(target, cases) ->
            Morphir.IR.V4.Value.PatternMatch(
                va,
                value target,
                cases
                |> List.map (fun (p, body) -> pattern p, value body)
            )
        | Destructure(p, x, body) ->
            Morphir.IR.V4.Value.Destructure(va, pattern p, value x, value body)
        | PatternLambda(p, body) -> Morphir.IR.V4.Value.Lambda(va, pattern p, value body)
        | Number x -> Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.IntegerLiteral(bigint x))
        | Variable name -> Morphir.IR.V4.Value.Variable(va, n name)
        | Ref name -> Morphir.IR.V4.Value.Reference(va, own name)
        | Add -> Morphir.IR.V4.Value.Reference(va, TypeCases.sdk "basics" "add")
        | SdkRef(modul, local) -> Morphir.IR.V4.Value.Reference(va, TypeCases.sdk modul local)
        | Apply(f, x) -> Morphir.IR.V4.Value.Apply(va, value f, value x)
        | Lambda(name, body) ->
            Morphir.IR.V4.Value.Lambda(
                va,
                Morphir.IR.V4.AsPattern(va, Morphir.IR.V4.WildcardPattern va, n name),
                value body
            )
        | Let(name, inputs, output, body, rest) ->
            Morphir.IR.V4.Value.LetDefinition(
                va,
                n name,
                {
                    Doc = None
                    Value = definition inputs output body
                },
                value rest
            )

    and definition inputs output body : Morphir.IR.V4.ValueDefinition = {
        InputTypes =
            inputs
            |> List.map (fun (name, t) -> n name, typ t)
        Body = Morphir.IR.V4.ExpressionBody(typ output, value body)
    }

    let publicValue
        value
        : Morphir.IR.V4.AccessControlled<Morphir.IR.V4.Documented<Morphir.IR.V4.ValueDefinition>> =
        {
            Access = Morphir.IR.V4.Public
            Value = { Doc = None; Value = value }
        }

    let modul: Morphir.IR.V4.ModuleDefinition = {
        Types = Morphir.IR.V4.OrderedMap.empty
        Values =
            definitions
            |> List.map (fun d -> n d.Name, publicValue (definition d.Inputs d.Output d.Body))
            |> TypeCases.ordered
        Doc = None
    }

    {
        SourceVersion = TypeCases.version "4"
        Distribution =
            DistributionModel.V4(
                Morphir.IR.V4.Library(
                    pkg,
                    Morphir.IR.V4.OrderedMap.empty,
                    {
                        Modules =
                            TypeCases.ordered[path,
                                              {
                                                  Access = Morphir.IR.V4.Public
                                                  Value = modul
                                              }]
                    }
                )
            )
    }

let classic () = classicWith definitions
let v4 () = v4With definitions

let refusals = [
    "unbound-variable",
    {
        Name = "bad"
        Inputs = []
        Output = Int
        Body = Variable "missing"
    }
    "unresolved-reference",
    {
        Name = "bad"
        Inputs = []
        Output = Int
        Body = Ref "missing"
    }
    "unmapped-sdk",
    {
        Name = "bad"
        Inputs = []
        Output = Int
        Body = SdkRef("basics", "notInvented")
    }
    "type-mismatch",
    {
        Name = "bad"
        Inputs = []
        Output = Int
        Body = app (Number 3) [ Number 2 ]
    }
    "type-mismatch",
    {
        Name = "bad"
        Inputs = []
        Output = Fn(Int, Int)
        Body = Lambda("x", app (Variable "x") [ Variable "x" ])
    }
    "unsupported-numeric",
    {
        Name = "bad"
        Inputs = [ "x", Generic "a" ]
        Output = Generic "a"
        Body = add (Variable "x") (Variable "x")
    }
    "unbound-variable",
    {
        Name = "bad"
        Inputs = []
        Output = Int
        Body = Let("self", [], Int, Variable "self", Number 1)
    }
]

let consumer =
    """
public static class Observation {
    public static int Run() {
        var trace = new System.Collections.Generic.List<int>();
        var order=Example.FunctionsModule.Order(x => {trace.Add(x);return x;});
        if(order!=11 || trace.Count!=2 || trace[0]!=8 || trace[1]!=3) return 0;
        if(Example.FunctionsModule.Capture(7)!=10 || Example.FunctionsModule.Shadow(7)!=8) return 0;
        if(Example.FunctionsModule.Partial(7)!=12 || Example.FunctionsModule.Make(4)(5)!=9) return 0;
        if(Example.FunctionsModule.Identity("typed")!="typed" || Example.FunctionsModule.GenericUse()!=23) return 0;
        return 1;
    }
}
"""
