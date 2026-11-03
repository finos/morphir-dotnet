module Morphir.CodeGeneration.CSharp.Tests.FunctionCases

open System.Text.Json
open Morphir.IR
open Morphir.IR.Model

// One authored behavior corpus, built directly into each typed input family.
type Ty =
    | Int
    | Generic of string
    | Fn of Ty * Ty

type Expr =
    | Number of int
    | Variable of string
    | Ref of string
    | SdkRef of string * string
    | Add
    | Apply of Expr * Expr
    | Lambda of string * Expr
    | Let of string * (string * Ty) list * Ty * Expr * Expr

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

let classicWith definitions =
    let rec typ =
        function
        | Int -> Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk "basics" "int", [])
        | Generic name -> Morphir.IR.Classic.Type.Variable(a, n name)
        | Fn(input, output) -> Morphir.IR.Classic.Type.Function(a, typ input, typ output)

    let rec value =
        function
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
        | Generic name -> Morphir.IR.V4.Type.Variable(ta, n name)
        | Fn(input, output) -> Morphir.IR.V4.Type.Function(ta, typ input, typ output)

    let rec value =
        function
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
