module Morphir.Lang.CSharp.Backend.Tests.SdkCases

open Morphir.Lang.CSharp.Backend.Tests.FunctionCases

// Shared typed SDK observations, independent of production binding code.
let maybe t = SdkTy("maybe", "maybe", [ t ])
let result e v = SdkTy("result", "result", [ e; v ])
let sdk modul local args = app (SdkRef(modul, local)) args

let refusals = [
    "constructor-arity",
    {
        Name = "badConstructorPattern"
        Inputs = [ "x", maybe Int ]
        Output = Int
        Body = Match(Variable "x", [ PCtor("maybe", "just", []), Number 1 ])
    }
    "unsupported-equality",
    {
        Name = "badFieldEquality"
        Inputs = [ "x", RecordTy [ "value", Fn(Int, Int) ] ]
        Output = Bool
        Body = sdk "basics" "equal" [ Field(Variable "x", "value"); Field(Variable "x", "value") ]
    }
    "unsupported-comparable",
    {
        Name = "badComparison"
        Inputs = [ "x", Bool ]
        Output = Bool
        Body = sdk "basics" "lessThan" [ Variable "x"; Variable "x" ]
    }
    "unsupported-equality",
    {
        Name = "badEquality"
        Inputs = [ "x", Fn(Int, Int) ]
        Output = Bool
        Body = sdk "basics" "equal" [ Variable "x"; Variable "x" ]
    }
]

let definitions = [
    {
        Name = "chooseFirstRecord"
        Inputs = [ "pair", RecordTy [ "left", Generic "a"; "right", Generic "a" ] ]
        Output = Generic "a"
        Body = Field(Variable "pair", "left")
    }
    {
        Name = "reorderedPair"
        Inputs = []
        Output =
            RecordTy [
                "left", RecordTy [ "a", Int; "b", Int ]
                "right", RecordTy [ "b", Int; "a", Int ]
            ]
        Body =
            RecordValue [
                "left", RecordValue [ "a", Number 1; "b", Number 2 ]
                "right", RecordValue [ "a", Number 3; "b", Number 4 ]
            ]
    }
    {
        Name = "referenceJust"
        Inputs = [ "x", Int ]
        Output = maybe Int
        Body = sdk "maybe" "Just" [ Variable "x" ]
    }
    {
        Name = "recordNumber"
        Inputs = []
        Output = RecordTy [ "value", Int ]
        Body = RecordValue [ "value", Number 7 ]
    }
    {
        Name = "ordering"
        Inputs = [ "x", Int; "y", Int ]
        Output = Int
        Body =
            Match(
                sdk "basics" "compare" [ Variable "x"; Variable "y" ],
                [
                    PCtor("basics", "LT", []), Number -1
                    PCtor("basics", "EQ", []), Number 0
                    PCtor("basics", "GT", []), Number 1
                ]
            )
    }
    {
        Name = "fieldComparison"
        Inputs = [ "x", RecordTy [ "value", Int ] ]
        Output = Bool
        Body =
            sdk "basics" "lessThan" [ Field(Variable "x", "value"); Field(Variable "x", "value") ]
    }
    {
        Name = "arithmetic"
        Inputs = [ "x", Int; "y", Int ]
        Output = Int
        Body =
            sdk "basics" "multiply" [
                sdk "basics" "subtract" [ Variable "x"; Variable "y" ]
                Number 3
            ]
    }
    {
        Name = "comparison"
        Inputs = [ "x", Int; "y", Int ]
        Output = Bool
        Body = sdk "basics" "lessThan" [ Variable "x"; Variable "y" ]
    }
    {
        Name = "equal"
        Inputs = [ "x", ListTy Int; "y", ListTy Int ]
        Output = Bool
        Body = sdk "basics" "equal" [ Variable "x"; Variable "y" ]
    }
    {
        Name = "mappedList"
        Inputs = [ "xs", ListTy Int ]
        Output = ListTy Int
        Body = sdk "list" "map" [ Lambda("x", add (Variable "x") (Number 1)); Variable "xs" ]
    }
    {
        Name = "mappedMaybe"
        Inputs = [ "x", maybe Int ]
        Output = maybe Int
        Body = sdk "maybe" "map" [ Lambda("v", add (Variable "v") (Number 1)); Variable "x" ]
    }
    {
        Name = "mappedResult"
        Inputs = [ "x", result Text Int ]
        Output = result Text Int
        Body = sdk "result" "map" [ Lambda("v", add (Variable "v") (Number 1)); Variable "x" ]
    }
    {
        Name = "just"
        Inputs = [ "x", Int ]
        Output = maybe Int
        Body = app (Ctor("maybe", "just")) [ Variable "x" ]
    }
    {
        Name = "nothing"
        Inputs = []
        Output = maybe Int
        Body = Ctor("maybe", "nothing")
    }
    {
        Name = "ok"
        Inputs = [ "x", Int ]
        Output = result Text Int
        Body = app (Ctor("result", "ok")) [ Variable "x" ]
    }
    {
        Name = "err"
        Inputs = [ "x", Text ]
        Output = result Text Int
        Body = app (Ctor("result", "err")) [ Variable "x" ]
    }
    {
        Name = "unwrapMaybe"
        Inputs = [ "x", maybe Int ]
        Output = Int
        Body =
            Match(
                Variable "x",
                [
                    PCtor("maybe", "just", [ PBind "v" ]), Variable "v"
                    PCtor("maybe", "nothing", []), Number 0
                ]
            )
    }
    {
        Name = "unwrapResult"
        Inputs = [ "x", result Text Int ]
        Output = Int
        Body =
            Match(
                Variable "x",
                [
                    PCtor("result", "ok", [ PBind "v" ]), Variable "v"
                    PCtor("result", "err", [ PWild ]), Number 0
                ]
            )
    }
]

let consumer =
    """
public static class Observation {
  public static int Run() {
    if(Example.Functions.ChooseFirstRecord(Example.Functions.ReorderedPair()).A!=1) return 0;
    if(Example.Functions.FieldComparison(Example.Functions.RecordNumber())) return 0;
    if(Example.Functions.UnwrapMaybe(Example.Functions.ReferenceJust(7))!=7) return 0;
    if(Example.Functions.Ordering(1,2)!=-1 || Example.Functions.Ordering(2,2)!=0 || Example.Functions.Ordering(3,2)!=1) return 0;
    if(Example.Functions.Arithmetic(7,3)!=12 || !Example.Functions.Comparison(3,7)) return 0;
    var xs=Morphir.SDK.CSharp.List<int>.FromEnumerable(new[]{1,2});
    var mapped=Example.Functions.MappedList(xs);
    if(mapped[0]!=2 || mapped[1]!=3 || xs[0]!=1 || !Example.Functions.Equal(xs,Morphir.SDK.CSharp.List<int>.FromEnumerable(new[]{1,2}))) return 0;
    if(Example.Functions.UnwrapMaybe(Example.Functions.MappedMaybe(Example.Functions.Just(7)))!=8) return 0;
    if(Example.Functions.UnwrapMaybe(Example.Functions.MappedMaybe(Example.Functions.Nothing()))!=0) return 0;
    if(Example.Functions.UnwrapResult(Example.Functions.MappedResult(Example.Functions.Ok(7)))!=8) return 0;
    var err=Example.Functions.MappedResult(Example.Functions.Err("negative"));
    if(err.IsOk || err.Match(e=>e,v=>"wrong")!="negative" || Example.Functions.UnwrapResult(err)!=0) return 0;
    return 1;
  }
}
"""
