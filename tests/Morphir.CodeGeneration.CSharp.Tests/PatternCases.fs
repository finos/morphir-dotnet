module Morphir.CodeGeneration.CSharp.Tests.PatternCases

open Morphir.IR.Model
open Morphir.CodeGeneration.CSharp.Tests.FunctionCases

// One pattern and record corpus constructed directly in each typed family.
let person = Own("data", "person", [])
let choice = Own("data", "choice", [ Int ])

let pair =
    TupleTy[Int
            Int]

let fields =
    RecordTy["a", Int
             "b", Int]

let definitions = [
    {
        Name = "largeTuple"
        Inputs = [ "tuple", TupleTy(List.replicate 9 Int) ]
        Output = Int
        Body =
            Match(
                Variable "tuple",
                [
                    PTuple(
                        [ for _ in 1..8 -> PWild ]
                        @ [ PBind "last" ]
                    ),
                    Variable "last"
                ]
            )
    }
    {
        Name = "literalKinds"
        Inputs = [ "text", Text; "flag", Bool; "unit", UnitTy ]
        Output = Int
        Body =
            Match(
                Variable "text",
                [
                    PText "hello",
                    Match(
                        Variable "flag",
                        [ PBool true, Match(Variable "unit", [ PUnit, Number 7 ]); PWild, Number 3 ]
                    )
                    PWild, Number 2
                ]
            )
    }
    {
        Name = "conditionalOrder"
        Inputs = [ "mark", Fn(Int, Int); "flag", Bool ]
        Output = Int
        Body =
            If(
                Variable "flag",
                app (Variable "mark") [ Number 1 ],
                app (Variable "mark") [ Number 2 ]
            )
    }
    {
        Name = "literalFirst"
        Inputs = [ "x", Int ]
        Output = Int
        Body = Match(Variable "x", [ PInt 1, Number 10; PWild, Number 20; PInt 2, Number 999 ])
    }
    {
        Name = "choiceValue"
        Inputs = [ "x", choice ]
        Output = Int
        Body =
            Match(
                Variable "x",
                [
                    PCtor("data", "empty", []), Number 0
                    PCtor("data", "value", [ PBind "payload" ]), add (Variable "payload") (Number 1)
                ]
            )
    }
    {
        Name = "makeChoice"
        Inputs = [ "x", Int ]
        Output = choice
        Body = app (Ctor("data", "value")) [ Variable "x" ]
    }
    {
        Name = "choiceFactory"
        Inputs = []
        Output = Fn(Int, choice)
        Body = Ctor("data", "value")
    }
    {
        Name = "emptyChoice"
        Inputs = []
        Output = choice
        Body = Ctor("data", "empty")
    }
    {
        Name = "tupleAlias"
        Inputs = [ "x", pair ]
        Output = pair
        Body =
            Match(
                Variable "x",
                [
                    PAs(
                        PTuple[PInt 1
                               PWild],
                        "both"
                    ),
                    Variable "both"
                    PWild,
                    TupleValue[Number 0
                               Number 0]
                ]
            )
    }
    {
        Name = "pairSum"
        Inputs = []
        Output = Fn(pair, Int)
        Body =
            PatternLambda(
                PTuple[PBind "left"
                       PBind "right"],
                add (Variable "left") (Variable "right")
            )
    }
    {
        Name = "destructure"
        Inputs = []
        Output = Int
        Body =
            Destructure(
                PTuple[PBind "left"
                       PBind "right"],
                TupleValue[Number 4
                           Number 9],
                add (Variable "left") (Variable "right")
            )
    }
    {
        Name = "sum"
        Inputs = [ "values", ListTy Int ]
        Output = Int
        Body =
            Match(
                Variable "values",
                [
                    PEmptyList, Number 0
                    PHeadTail(PBind "head", PBind "tail"),
                    add (Variable "head") (app (Ref "sum") [ Variable "tail" ])
                ]
            )
    }
    {
        Name = "literalList"
        Inputs = []
        Output = ListTy Int
        Body =
            ListValue[Number 1
                      Number 2
                      Number 3]
    }
    {
        Name = "bump"
        Inputs = [ "person", person; "years", Int ]
        Output = person
        Body =
            Update(
                Variable "person",
                [ "age", add (Field(Variable "person", "age")) (Variable "years") ]
            )
    }
    {
        Name = "ageGetter"
        Inputs = []
        Output = Fn(person, Int)
        Body = FieldFunction "age"
    }
    {
        Name = "orderedFields"
        Inputs = [ "mark", Fn(Int, Int) ]
        Output = fields
        Body =
            RecordValue["a", app (Variable "mark") [ Number 1 ]
                        "b", app (Variable "mark") [ Number 2 ]]
    }
    {
        Name = "updateFields"
        Inputs = [ "source", Fn(Int, fields); "mark", Fn(Int, Int) ]
        Output = fields
        Body =
            Update(
                app (Variable "source") [ Number 0 ],
                [ "a", app (Variable "mark") [ Number 4 ]; "b", app (Variable "mark") [ Number 3 ] ]
            )
    }
    {
        Name = "choose"
        Inputs = [ "flag", Bool ]
        Output = Int
        Body = If(Variable "flag", Number 7, Number 11)
    }
    {
        Name = "unitValue"
        Inputs = []
        Output = UnitTy
        Body = UnitValue
    }
]

let classic () =
    let types = TypeCases.classic ()

    let functions =
        classicWith (
            FunctionCases.definitions
            @ definitions
        )

    match types.Distribution, functions.Distribution with
    | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, deps, types)),
      DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(_, _, functions)) ->
        { TypeCases.classic () with
            Distribution =
                DistributionModel.Classic(
                    Morphir.IR.Classic.Distribution.Library(
                        pkg,
                        deps,
                        {
                            Modules =
                                Map.fold
                                    (fun modules name value -> Map.add name value modules)
                                    types.Modules
                                    functions.Modules
                        }
                    )
                )
        }
    | _ -> failwith "Classic typed corpus required."

let v4WithDefinitions extra =
    let types = TypeCases.v4 ()

    let functions =
        v4With (
            FunctionCases.definitions
            @ extra
        )

    match types.Distribution, functions.Distribution with
    | DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, types)),
      DistributionModel.V4(Morphir.IR.V4.Library(_, _, functions)) ->
        { TypeCases.v4 () with
            Distribution =
                DistributionModel.V4(
                    Morphir.IR.V4.Library(
                        pkg,
                        deps,
                        {
                            Modules =
                                TypeCases.ordered (
                                    Morphir.IR.V4.OrderedMap.toList types.Modules
                                    @ Morphir.IR.V4.OrderedMap.toList functions.Modules
                                )
                        }
                    )
                )
        }
    | _ -> failwith "V4 typed corpus required."

let v4 () = v4WithDefinitions definitions

let v4Reversed () =
    definitions
    |> List.map (fun definition ->
        match definition.Body with
        | RecordValue fields ->
            { definition with
                Body = RecordValue(List.rev fields)
            }
        | Update(target, fields) ->
            { definition with
                Body = Update(target, List.rev fields)
            }
        | _ -> definition
    )
    |> v4WithDefinitions

let refusals = [
    "local-recursion",
    {
        Name = "localRec"
        Inputs = []
        Output = Int
        Body = LetRec("loop", Int, Variable "loop", Number 1)
    }
    "duplicate-pattern-binding",
    {
        Name = "duplicate"
        Inputs = [ "x", pair ]
        Output = Int
        Body = Match(Variable "x", [ PTuple [ PBind "y"; PBind "y" ], Variable "y" ])
    }
    "type-mismatch",
    {
        Name = "wrongPattern"
        Inputs = [ "x", Int ]
        Output = Int
        Body = Match(Variable "x", [ PEmptyList, Number 1 ])
    }
    "empty-match",
    {
        Name = "emptyMatch"
        Inputs = [ "x", Int ]
        Output = Int
        Body = Match(Variable "x", [])
    }
    "type-mismatch",
    {
        Name = "wrongBranch"
        Inputs = [ "x", Int ]
        Output = Int
        Body = Match(Variable "x", [ PInt 1, Boolean true; PWild, Number 2 ])
    }
    "unknown-field",
    {
        Name = "missingField"
        Inputs = [ "x", fields ]
        Output = Int
        Body = Field(Variable "x", "missing")
    }
    "unknown-field",
    {
        Name = "missingUpdate"
        Inputs = [ "x", fields ]
        Output = fields
        Body = Update(Variable "x", [ "missing", Number 1 ])
    }
    "type-mismatch",
    {
        Name = "wrongUpdate"
        Inputs = [ "x", fields ]
        Output = fields
        Body = Update(Variable "x", [ "a", Boolean true ])
    }
    "unresolved-constructor",
    {
        Name = "missingConstructor"
        Inputs = []
        Output = Int
        Body = Ctor("missing", "unknown")
    }
]

let consumer =
    """
public static class Observation {
  public static int Run() {
    if(Example.FunctionsModule.LargeTuple((1,2,3,4,5,6,7,8,9))!=9) return 0;
    if(Example.FunctionsModule.LiteralKinds("hello",true,Morphir.SDK.CSharp.Unit.Value)!=7 || Example.FunctionsModule.LiteralKinds("hello",false,Morphir.SDK.CSharp.Unit.Value)!=3 || Example.FunctionsModule.LiteralKinds("bye",true,Morphir.SDK.CSharp.Unit.Value)!=2) return 0;
    int calls=0;
    if(Example.FunctionsModule.ConditionalOrder(x=>{calls++;return x;},true)!=1 || calls!=1) return 0;
    if(Example.FunctionsModule.LiteralFirst(1)!=10 || Example.FunctionsModule.LiteralFirst(2)!=20) return 0;
    if(Example.FunctionsModule.ChoiceValue(Example.FunctionsModule.MakeChoice(9))!=10) return 0;
    if(Example.FunctionsModule.ChoiceValue(Example.FunctionsModule.EmptyChoice())!=0) return 0;
    if(Example.FunctionsModule.ChoiceValue(Example.FunctionsModule.ChoiceFactory().Invoke(4))!=5) return 0;
    if(Example.FunctionsModule.TupleAlias((1,8))!=(1,8) || Example.FunctionsModule.TupleAlias((2,8))!=(0,0)) return 0;
    if(Example.FunctionsModule.PairSum().Invoke((4,5))!=9 || Example.FunctionsModule.Destructure()!=13) return 0;
    if(Example.FunctionsModule.Sum(Example.FunctionsModule.LiteralList())!=6) return 0;
    var person=new Example.DataModule.Person(21,"Ada");
    var older=Example.FunctionsModule.Bump(person,3);
    if(person.Age!=21 || older.Age!=24 || older.Name!="Ada" || Example.FunctionsModule.AgeGetter().Invoke(person)!=21) return 0;
    var trace=new System.Collections.Generic.List<int>();
    var initial=Example.FunctionsModule.OrderedFields(x=>{trace.Add(x);return x;});
    if(initial.A!=1 || initial.B!=2 || trace.Count!=2 || trace[0]!=1 || trace[1]!=2) return 0;
    trace.Clear();
    var updated=Example.FunctionsModule.UpdateFields(x=>{trace.Add(x);return initial;},x=>{trace.Add(x);return x;});
    if(updated.A!=4 || updated.B!=3 || initial.A!=1 || initial.B!=2 || trace.Count!=3 || trace[0]!=0 || trace[1]!=4 || trace[2]!=3) return 0;
    if(Example.FunctionsModule.Choose(true)!=7 || Example.FunctionsModule.Choose(false)!=11) return 0;
    if(Example.FunctionsModule.UnitValue()!=Morphir.SDK.CSharp.Unit.Value) return 0;
    return 1;
  }
}
"""
